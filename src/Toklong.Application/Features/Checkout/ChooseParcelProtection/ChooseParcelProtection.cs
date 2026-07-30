using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Features.Checkout.GetParcelProtection;
using Toklong.Application.Features.Shipping;
using Toklong.Application.Pricing;
using Toklong.Application.Transactions;
using Toklong.Domain.Common;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.Checkout.ChooseParcelProtection;

public sealed record ChooseParcelProtectionCommand(
    Guid TransactionId,
    Guid BuyerId,
    bool AddProtection,
    string? OptionReference,
    long? DisclosedCustomerPriceSatang,
    string IdempotencyKey) : IRequest<ChooseParcelProtectionResult>;

public sealed record ChooseParcelProtectionResult(
    TransactionView Transaction, string BookingStatus);

public sealed partial class ChooseParcelProtectionHandler(
    ITransactionRepository repository,
    IParcelProtectionQuoteProvider protectionQuotes,
    IParcelProtectionPricingPolicy pricing,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<ChooseParcelProtectionCommand, ChooseParcelProtectionResult>
{
    public async Task<ChooseParcelProtectionResult> Handle(
        ChooseParcelProtectionCommand request, CancellationToken cancellationToken)
    {
        var idempotencyKey = ParcelProtectionCheckout.RequireSafeIdempotencyKey(
            request.IdempotencyKey);
        var transaction = await repository.GetByIdAsync(
            request.TransactionId, cancellationToken)
            ?? throw new NotFoundException("ไม่พบรายการ");
        ParcelProtectionCheckout.RequireBuyer(transaction, request.BuyerId);
        if (transaction.BuyerPaymentDeadlineAt <= clock.UtcNow)
            throw new DomainException("หมดเวลาชำระแล้ว กรุณาส่งข้อเสนอใหม่ให้ผู้ขายยืนยัน");
        if (transaction.FulfillmentType == FulfillmentType.DigitalHandoff)
            return new ChooseParcelProtectionResult(
                TransactionView.From(transaction), "not_applicable");
        if (transaction.State != TransactionState.SellerAcceptedAwaitingPayment)
            throw new DomainException("รายการนี้ยังเลือกความคุ้มครองพัสดุไม่ได้");

        var priorChange = transaction.ParcelProtectionChangeRequests.SingleOrDefault(
            change => string.Equals(change.IdempotencyKey, idempotencyKey,
                StringComparison.Ordinal));
        if (priorChange is not null)
        {
            if (!MatchesPriorChangeElection(priorChange, request))
                throw new DomainException("รหัสป้องกันการทำซ้ำถูกใช้กับตัวเลือกอื่นแล้ว");
            return new ChooseParcelProtectionResult(TransactionView.From(transaction),
                priorChange.Status switch
                {
                    ParcelProtectionChangeStatus.AwaitingCancellation =>
                        "cancelling_shipping",
                    ParcelProtectionChangeStatus.Completed when
                        transaction.ParcelProtectionBookingReady => "booking_ready",
                    _ => "preparing_shipping"
                });
        }

        var bookingKey = $"book-outbound:{transaction.Id:N}:{idempotencyKey}";
        var priorBooking = transaction.ShippingOperations.SingleOrDefault(
            operation => string.Equals(operation.IdempotencyKey, bookingKey,
                StringComparison.Ordinal));
        if (priorBooking is not null)
        {
            if (!MatchesPriorElection(transaction, request))
                throw new DomainException("รหัสป้องกันการทำซ้ำถูกใช้กับตัวเลือกอื่นแล้ว");
            return new ChooseParcelProtectionResult(TransactionView.From(transaction),
                transaction.ParcelProtectionBookingReady
                    ? "booking_ready"
                    : "preparing_shipping");
        }

        if (transaction.ShippingQuoteExpiresAt <= clock.UtcNow)
            throw new DomainException("ราคาค่าจัดส่งหมดอายุ กรุณาดูราคาใหม่");
        var quoteRequest = ParcelProtectionCheckout.BuildProtectionRequest(transaction);
        var availability = await protectionQuotes.GetAvailabilityAsync(
            quoteRequest, cancellationToken);
        var resolved = await ResolveSelectionAsync(
            transaction, request, quoteRequest, availability, cancellationToken);
        var currentShipment = transaction.CurrentOutboundShipment;
        if (currentShipment is not null)
        {
            if (!string.IsNullOrWhiteSpace(transaction.PaymentReference) ||
                transaction.State == TransactionState.PaymentPending)
                throw new DomainException("เปลี่ยนตัวเลือกความคุ้มครองพัสดุหลังเริ่มชำระเงินไม่ได้");
            var currentBooking = transaction.ShippingOperations
                .Where(operation =>
                    operation.ManagedShipmentId == currentShipment.Id &&
                    operation.OperationType == ShippingOperationType.BookOutbound)
                .OrderByDescending(operation => operation.CreatedAt)
                .FirstOrDefault();
            if (currentBooking?.Status is ShippingOperationStatus.Pending or
                ShippingOperationStatus.RetryScheduled)
            {
                currentBooking.SupersedeBeforeMutation(
                    request.BuyerId.ToString("N"), "buyer-selection-changed",
                    clock.UtcNow);
                currentShipment.RecordCancellation(clock.UtcNow);
                transaction.InvalidateParcelProtectionElection(
                    "buyer-selection-changed", clock.UtcNow);
                transaction.RecordParcelProtectionElection(
                    request.BuyerId, resolved.Selection, clock.UtcNow);
                return await QueueBookingAsync(
                    transaction, request, resolved, cancellationToken,
                    selectionAlreadyRecorded: true);
            }
            if (currentBooking?.Status is ShippingOperationStatus.Processing or
                ShippingOperationStatus.OutcomeUnknown or
                ShippingOperationStatus.NeedsReview)
                throw new DomainException(
                    "กำลังตรวจสอบรายการจัดส่ง กรุณารอผลก่อนเปลี่ยนตัวเลือก");
            if (currentShipment.Status == ManagedShipmentStatus.Reserved)
            {
                var change = transaction.RequestParcelProtectionChange(
                    request.BuyerId, currentShipment, resolved.Selection,
                    resolved.InsuranceCode, idempotencyKey, clock.UtcNow);
                if (!transaction.ShippingOperations.Any(operation =>
                        operation.ManagedShipmentId == currentShipment.Id &&
                        operation.OperationType == ShippingOperationType.CancelOutbound &&
                        operation.Status is not ShippingOperationStatus.Superseded))
                {
                    var fingerprint = ManagedShippingOperationQueue
                        .CancellationFingerprint(transaction, currentShipment);
                    transaction.QueueShippingOperation(
                        ShippingOperation.Queue(transaction.Id, currentShipment.Id,
                            ShippingOperationType.CancelOutbound,
                            $"cancel-outbound-change:{change.Id:N}", fingerprint,
                            clock.UtcNow),
                        ActorRole.System, "parcel-protection-change", clock.UtcNow);
                }
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return new ChooseParcelProtectionResult(
                    TransactionView.From(transaction), "cancelling_shipping");
            }
            if (currentBooking?.Status == ShippingOperationStatus.Superseded &&
                currentShipment.Status == ManagedShipmentStatus.PendingBooking)
            {
                currentShipment.RecordCancellation(clock.UtcNow);
                return await QueueBookingAsync(
                    transaction, request, resolved, cancellationToken);
            }
            throw new DomainException(
                "กำลังตรวจสอบรายการจัดส่ง กรุณารอผลก่อนเปลี่ยนตัวเลือก");
        }

        return await QueueBookingAsync(
            transaction, request, resolved, cancellationToken);
    }

    private async Task<ChooseParcelProtectionResult> QueueBookingAsync(
        SaleTransaction transaction,
        ChooseParcelProtectionCommand request,
        ResolvedSelection resolved,
        CancellationToken cancellationToken,
        bool selectionAlreadyRecorded = false)
    {
        var idempotencyKey = request.IdempotencyKey;
        var draft = BuildDraft(transaction, resolved.Selection, resolved.InsuranceCode);
        var shipment = ManagedShipment.CreateOutbound(transaction.Id, draft, clock.UtcNow);
        var fingerprint = ManagedShippingOperationQueue.BookingFingerprint(shipment);
        var bookingKey = $"book-outbound:{transaction.Id:N}:{idempotencyKey}";
        var duplicate = transaction.ShippingOperations.SingleOrDefault(operation =>
            string.Equals(operation.IdempotencyKey, bookingKey,
                StringComparison.Ordinal));
        if (duplicate is not null)
        {
            if (!string.Equals(duplicate.RequestFingerprint, fingerprint,
                    StringComparison.Ordinal))
                throw new DomainException("รหัสป้องกันการทำซ้ำถูกใช้กับตัวเลือกอื่นแล้ว");
            return new ChooseParcelProtectionResult(
                TransactionView.From(transaction), "preparing_shipping");
        }

        if (!selectionAlreadyRecorded)
            transaction.RecordParcelProtectionElection(
                request.BuyerId, resolved.Selection, clock.UtcNow);
        var operation = ShippingOperation.Queue(transaction.Id, shipment.Id,
            ShippingOperationType.BookOutbound, bookingKey, fingerprint, clock.UtcNow);
        transaction.QueueManagedShipment(shipment, operation, ActorRole.System,
            "parcel-protection-checkout", clock.UtcNow);
        transaction.RecordParcelProtectionBookingIntent(shipment, request.BuyerId,
            idempotencyKey, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new ChooseParcelProtectionResult(TransactionView.From(transaction),
            "preparing_shipping");
    }

    private static bool MatchesPriorElection(
        SaleTransaction transaction,
        ChooseParcelProtectionCommand request) => request.AddProtection
        ? transaction.ParcelProtectionElection == ParcelProtectionElectionStatus.Accepted &&
          request.DisclosedCustomerPriceSatang == transaction.ParcelInsuranceFeeSatang &&
          string.Equals(request.OptionReference, transaction.ParcelProtectionOptionReference,
              StringComparison.Ordinal)
        : (transaction.ParcelProtectionElection is
            ParcelProtectionElectionStatus.Declined or
            ParcelProtectionElectionStatus.Unavailable) &&
          request.OptionReference is null &&
          request.DisclosedCustomerPriceSatang is null;

    private static bool MatchesPriorChangeElection(
        ParcelProtectionChangeRequest change,
        ChooseParcelProtectionCommand request) => request.AddProtection
        ? change.DesiredElection == ParcelProtectionElectionStatus.Accepted &&
          request.DisclosedCustomerPriceSatang == change.DesiredCustomerPriceSatang &&
          string.Equals(request.OptionReference, change.DesiredOptionReference,
              StringComparison.Ordinal)
        : change.DesiredElection is ParcelProtectionElectionStatus.Declined or
            ParcelProtectionElectionStatus.Unavailable &&
          request.OptionReference is null &&
          request.DisclosedCustomerPriceSatang is null;

    private async Task<ResolvedSelection> ResolveSelectionAsync(
        SaleTransaction transaction, ChooseParcelProtectionCommand request,
        ParcelProtectionQuoteRequest quoteRequest,
        ParcelProtectionAvailability availability,
        CancellationToken cancellationToken)
    {
        if (!request.AddProtection)
        {
            if (request.OptionReference is not null ||
                request.DisclosedCustomerPriceSatang is not null)
                throw new DomainException("การไม่ซื้อความคุ้มครองพัสดุต้องไม่มีราคาอ้างอิง");
            var unavailable = transaction.PriceSatang >
                availability.IncludedCoverageLimitSatang &&
                (availability.AddOn is null || !availability.ProviderCapabilityCertified);
            return new ResolvedSelection(new ParcelProtectionSelection(
                unavailable ? ParcelProtectionElectionStatus.Unavailable : ParcelProtectionElectionStatus.Declined,
                0, 0, 0, availability.IncludedCoverageLimitSatang,
                availability.IncludedCoverageLimitSatang,
                ParcelProtectionCheckout.IncludedTermsVersion, null, clock.UtcNow,
                transaction.BuyerPaymentDeadlineAt!.Value), null);
        }

        if (!availability.ProviderCapabilityCertified || availability.AddOn is null ||
            string.IsNullOrWhiteSpace(request.OptionReference))
            throw new DomainException("ความคุ้มครองพัสดุเพิ่มเติมยังไม่พร้อมใช้งาน");
        var option = await protectionQuotes.ValidateOptionAsync(quoteRequest,
            request.OptionReference, cancellationToken);
        var price = pricing.Price(option.ProviderCostSatang);
        if (request.DisclosedCustomerPriceSatang != price.CustomerPriceSatang ||
            option.ExpiresAt <= clock.UtcNow || option.QuotedAt > clock.UtcNow ||
            option.ExpiresAt > transaction.BuyerPaymentDeadlineAt ||
            !string.Equals(option.OptionReference, availability.AddOn.OptionReference,
                StringComparison.Ordinal) ||
            !string.Equals(option.TermsVersion, availability.AddOn.TermsVersion,
                StringComparison.Ordinal) ||
            option.IncludedCoverageLimitSatang != availability.AddOn.IncludedCoverageLimitSatang ||
            option.SelectedCoverageLimitSatang != availability.AddOn.SelectedCoverageLimitSatang ||
            option.SelectedCoverageLimitSatang < transaction.PriceSatang ||
            !string.Equals(option.Provider, transaction.ShippingQuoteProvider,
                StringComparison.Ordinal))
            throw new ParcelProtectionOptionChangedException(
                "parcel-protection-option-changed");
        return new ResolvedSelection(new ParcelProtectionSelection(ParcelProtectionElectionStatus.Accepted,
            price.CustomerPriceSatang, option.ProviderCostSatang,
            price.ToklongServiceFeeSatang, option.IncludedCoverageLimitSatang,
            option.SelectedCoverageLimitSatang, option.TermsVersion,
            option.OptionReference, option.QuotedAt, option.ExpiresAt), option.InsuranceCode);
    }

    private static ManagedShipmentDraft BuildDraft(SaleTransaction transaction,
        ParcelProtectionSelection selection, string? insuranceCode) => new(
            transaction.ShippingQuoteProvider ?? throw new DomainException("ไม่พบผู้ให้บริการขนส่งที่เลือก"),
            $"origin:{transaction.Id:N}", $"destination:{transaction.Id:N}",
            transaction.ProductName,
            transaction.PackageWeightGrams!.Value,
            transaction.PackageWidthCentimeters!.Value,
            transaction.PackageLengthCentimeters!.Value,
            transaction.PackageHeightCentimeters!.Value,
            transaction.CarrierCode ?? throw new DomainException("ไม่พบผู้ให้บริการขนส่งที่เลือก"),
            transaction.ShippingServiceCode ?? throw new DomainException("ไม่พบบริการขนส่งที่เลือก"),
            transaction.ShippingServiceName ?? throw new DomainException("ไม่พบชื่อบริการขนส่ง"),
            transaction.ShippingFeeSatang, selection.ProviderCostSatang,
            selection.Election == ParcelProtectionElectionStatus.Accepted
                ? selection.SelectedCoverageLimitSatang : 0,
            selection.Election == ParcelProtectionElectionStatus.Accepted
                ? insuranceCode : null,
            transaction.ShippingQuoteReference ?? throw new DomainException("ไม่พบราคาอ้างอิงการจัดส่ง"),
            transaction.ShippingQuoteExpiresAt ?? throw new DomainException("ราคาค่าจัดส่งหมดอายุ"),
            selection.TermsVersion, selection.ProviderOptionReference, selection.Election,
            selection.ProviderCostSatang, selection.IncludedCoverageLimitSatang,
            selection.SelectedCoverageLimitSatang);

    private sealed record ResolvedSelection(
        ParcelProtectionSelection Selection,
        string? InsuranceCode);
}
