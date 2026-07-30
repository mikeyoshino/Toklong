using System.Text.RegularExpressions;
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
        var idempotencyKey = request.IdempotencyKey ?? "";
        if (!IdempotencyKeyPattern().IsMatch(idempotencyKey))
            throw new DomainException("รหัสป้องกันการทำซ้ำไม่ถูกต้อง");
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

        if (transaction.ShippingQuoteExpiresAt <= clock.UtcNow)
            throw new DomainException("ราคาค่าจัดส่งหมดอายุ กรุณาดูราคาใหม่");
        var quoteRequest = ParcelProtectionCheckout.BuildProtectionRequest(transaction);
        var availability = await protectionQuotes.GetAvailabilityAsync(
            quoteRequest, cancellationToken);
        var resolved = await ResolveSelectionAsync(
            transaction, request, quoteRequest, availability, cancellationToken);
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
            throw new DomainException("ราคาหรือเงื่อนไขความคุ้มครองพัสดุเปลี่ยน กรุณาตรวจสอบใหม่");
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

    [GeneratedRegex("^[A-Za-z0-9:_-]{16,80}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdempotencyKeyPattern();

    private sealed record ResolvedSelection(
        ParcelProtectionSelection Selection,
        string? InsuranceCode);
}
