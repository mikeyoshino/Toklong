using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Features.Checkout.GetParcelProtection;
using Toklong.Application.Pricing;
using Toklong.Domain.Common;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.Checkout.PrepareParcelProtection;

public sealed record PrepareParcelProtectionCommand(
    Guid TransactionId, Guid BuyerId, string IdempotencyKey)
    : IRequest<BuyerParcelProtectionView>;

public sealed class PrepareParcelProtectionHandler(
    ITransactionRepository repository,
    IParcelProtectionQuoteProvider protectionQuotes,
    IParcelProtectionPricingPolicy pricing,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<PrepareParcelProtectionCommand, BuyerParcelProtectionView>
{
    public async Task<BuyerParcelProtectionView> Handle(
        PrepareParcelProtectionCommand request, CancellationToken cancellationToken)
    {
        var idempotencyKey = ParcelProtectionCheckout.RequireSafeIdempotencyKey(
            request.IdempotencyKey);
        var transaction = await repository.GetByIdAsync(
            request.TransactionId, cancellationToken)
            ?? throw new NotFoundException("ไม่พบรายการ");
        ParcelProtectionCheckout.RequireBuyer(transaction, request.BuyerId);
        if (transaction.FulfillmentType == FulfillmentType.DigitalHandoff)
            return ParcelProtectionCheckout.FromStored(transaction);
        if (transaction.State != TransactionState.SellerAcceptedAwaitingPayment)
            throw new DomainException("รายการนี้ยังเลือกความคุ้มครองพัสดุไม่ได้");
        if (transaction.BuyerPaymentDeadlineAt <= clock.UtcNow)
            throw new DomainException("หมดเวลาชำระแล้ว กรุณาส่งข้อเสนอใหม่ให้ผู้ขายยืนยัน");

        var availability = await protectionQuotes.GetAvailabilityAsync(
            ParcelProtectionCheckout.BuildProtectionRequest(transaction), cancellationToken);
        var addOnAvailable = availability.AddOn is not null &&
            availability.ProviderCapabilityCertified;
        var requiresChoice = transaction.PriceSatang >
            availability.IncludedCoverageLimitSatang && addOnAvailable;
        long? customerPrice = null;
        if (availability.AddOn is not null)
            customerPrice = pricing.Price(availability.AddOn.ProviderCostSatang)
                .CustomerPriceSatang;

        transaction.RecordParcelProtectionAvailabilityPresented(
            request.BuyerId,
            new ParcelProtectionPreparedOffer(
                requiresChoice,
                addOnAvailable,
                availability.IncludedCoverageLimitSatang,
                availability.AddOn?.SelectedCoverageLimitSatang,
                customerPrice,
                availability.AddOn?.OptionReference,
                availability.AddOn?.TermsVersion ??
                    ParcelProtectionCheckout.IncludedTermsVersion,
                availability.AddOn?.ExpiresAt),
            idempotencyKey,
            clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new BuyerParcelProtectionView(
            requiresChoice,
            addOnAvailable,
            availability.IncludedCoverageLimitSatang,
            availability.AddOn?.SelectedCoverageLimitSatang,
            customerPrice,
            availability.AddOn?.OptionReference,
            availability.AddOn?.TermsVersion ?? ParcelProtectionCheckout.IncludedTermsVersion,
            availability.AddOn?.ExpiresAt,
            transaction.PriceSatang > availability.IncludedCoverageLimitSatang &&
            !addOnAvailable
                ? ParcelProtectionElectionStatus.Unavailable.ToString()
                : transaction.ParcelProtectionElection.ToString(),
            transaction.ParcelProtectionBookingReady,
            transaction.ParcelProtectionElection ==
                ParcelProtectionElectionStatus.ReconfirmationRequired);
    }
}
