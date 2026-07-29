using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Domain.Transactions;

namespace Toklong.Infrastructure.Persistence;

public sealed class TransactionRepository(ToklongDbContext dbContext) : ITransactionRepository
{
    public Task AddAsync(SaleTransaction transaction, CancellationToken cancellationToken) =>
        dbContext.Transactions.AddAsync(transaction, cancellationToken).AsTask();

    public Task AddDisputeEvidenceAsync(
        DisputeEvidence evidence,
        CancellationToken cancellationToken) =>
        dbContext.DisputeEvidence
            .AddAsync(evidence, cancellationToken)
            .AsTask();

    public Task AddRiskEventAsync(ActivationRiskEvent riskEvent, CancellationToken cancellationToken) =>
        dbContext.ActivationRiskEvents.AddAsync(riskEvent, cancellationToken).AsTask();

    public Task<SaleTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Query().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<SaleTransaction?> GetByPublicTokenAsync(string publicToken, CancellationToken cancellationToken) =>
        Query().SingleOrDefaultAsync(x => x.PublicToken == publicToken, cancellationToken);

    public Task<SaleTransaction?> GetBySellerTokenAsync(string sellerToken, CancellationToken cancellationToken) =>
        Query().SingleOrDefaultAsync(x => x.SellerAccessToken == sellerToken, cancellationToken);

    public Task<SaleTransaction?> GetByBuyerTokenAsync(string buyerToken, CancellationToken cancellationToken) =>
        Query().SingleOrDefaultAsync(x => x.BuyerAccessToken == buyerToken, cancellationToken);

    public async Task<IReadOnlyList<SaleTransaction>> GetForPartiesAsync(
        Guid? buyerId,
        Guid? sellerId,
        string? intendedSellerPhoneNumber,
        CancellationToken cancellationToken)
    {
        if (!buyerId.HasValue &&
            !sellerId.HasValue &&
            string.IsNullOrWhiteSpace(
                intendedSellerPhoneNumber))
            return [];

        return await Query()
            .Where(transaction =>
                (buyerId.HasValue && transaction.BuyerId == buyerId) ||
                (sellerId.HasValue &&
                 transaction.SellerId == sellerId) ||
                (!string.IsNullOrWhiteSpace(
                     intendedSellerPhoneNumber) &&
                 transaction.SellerContact ==
                 intendedSellerPhoneNumber))
            .OrderByDescending(transaction => transaction.CreatedAt)
            .ThenByDescending(transaction => transaction.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SaleTransaction>> GetDueForReleaseAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        await Query()
            .Where(x =>
                x.State == TransactionState.DeliveredDisputeWindow &&
                x.DisputeWindowEndsAt <= now &&
                !x.ShippingOperations.Any(operation =>
                    operation.Status ==
                        ShippingOperationStatus.OutcomeUnknown ||
                    operation.Status ==
                        ShippingOperationStatus.NeedsReview) &&
                !x.ShippingInsuranceCases.Any(insuranceCase =>
                    insuranceCase.Status ==
                        ShippingInsuranceCaseStatus.Open) &&
                !x.ManagedShipments.Any(shipment =>
                    (shipment.Status ==
                         ManagedShipmentStatus.TrackingUnverified ||
                     shipment.Status ==
                         ManagedShipmentStatus.CarrierException) &&
                    shipment.ExceptionResolvedAt == null) &&
                !x.ProviderShippingAdjustments.Any(
                    adjustment =>
                        adjustment.ResolvedAt == null))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SaleTransaction>> GetDueForExpirationAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await Query()
            .Where(transaction =>
                (transaction.State ==
                     TransactionState.AwaitingSellerAcceptance &&
                 transaction.SellerAcceptanceDeadlineAt <= now) ||
                (transaction.State ==
                     TransactionState.SellerAcceptedAwaitingPayment ||
                 transaction.State ==
                     TransactionState.CheckoutStarted ||
                 transaction.State ==
                     TransactionState.PaymentPending) &&
                transaction.BuyerPaymentDeadlineAt <= now)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SaleTransaction>>
        GetDueForShipmentDeadlineAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
        await Query()
            .Where(transaction =>
                (transaction.State ==
                     TransactionState.PaidAwaitingShipment ||
                 transaction.State ==
                     TransactionState.PaidAwaitingDigitalDelivery ||
                 transaction.ShippingProviderTrackingCode != null &&
                 transaction.FirstCarrierScanAt == null &&
                 (transaction.State ==
                      TransactionState.TrackingSubmitted ||
                  transaction.State ==
                      TransactionState.TrackingUnverified)) &&
                transaction.ShipByAt <= now)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SaleTransaction>>
        GetPendingRefundsAsync(
            CancellationToken cancellationToken) =>
        await Query()
            .Where(transaction =>
                transaction.State == TransactionState.RefundPending &&
                transaction.PaymentProvider == "stripe" &&
                !transaction.ShippingOperations.Any(operation =>
                    operation.Status ==
                        ShippingOperationStatus.OutcomeUnknown ||
                    operation.Status ==
                        ShippingOperationStatus.NeedsReview) &&
                !transaction.ShippingInsuranceCases.Any(insuranceCase =>
                    insuranceCase.Status ==
                        ShippingInsuranceCaseStatus.Open) &&
                !transaction.ManagedShipments.Any(shipment =>
                    (shipment.Status ==
                         ManagedShipmentStatus.TrackingUnverified ||
                     shipment.Status ==
                         ManagedShipmentStatus.CarrierException) &&
                    shipment.ExceptionResolvedAt == null) &&
                !transaction.ProviderShippingAdjustments.Any(
                    adjustment =>
                        adjustment.ResolvedAt == null) &&
                (!transaction.ReturnRequired ||
                 transaction.ReturnDeliveredAt != null ||
                 transaction.ManualReturnResolutionReference != null) &&
                (transaction.ShippingPurchaseReference == null ||
                 transaction.FirstCarrierScanAt != null ||
                 transaction.ShippingCancelledAt != null))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SaleTransaction>>
        GetOpenDisputesAsync(
            CancellationToken cancellationToken) =>
        await Query()
            .Where(transaction =>
                transaction.State == TransactionState.Disputed ||
                transaction.State ==
                    TransactionState.ResolutionPending)
            .OrderBy(transaction => transaction.DisputeOpenedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SaleTransaction>>
        GetPendingProviderPaymentsAsync(
            CancellationToken cancellationToken) =>
        await Query()
            .Where(transaction =>
                (transaction.State ==
                     TransactionState.PaymentPending ||
                 transaction.State ==
                     TransactionState.Expired &&
                 transaction.ExpirationReason ==
                     TransactionExpirationReason.BuyerDidNotPay) &&
                transaction.PaymentProvider == "stripe")
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SaleTransaction>>
        GetShipmentsAwaitingProviderConfirmationAsync(
            CancellationToken cancellationToken) =>
        await Query()
            .Where(transaction =>
                transaction.State ==
                    TransactionState.PaidAwaitingShipment &&
                transaction.ShippingPurchaseReference != null &&
                transaction.ShippingProviderTrackingCode != null &&
                transaction.ShippingConfirmedAt == null &&
                transaction.ShippingCancelledAt == null &&
                !transaction.ManagedShipments.Any())
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SaleTransaction>>
        GetProviderShipmentsForReconciliationAsync(
            CancellationToken cancellationToken) =>
        await Query()
            .Where(transaction =>
                ((transaction.State ==
                      TransactionState.TrackingSubmitted ||
                  transaction.State ==
                      TransactionState.TrackingUnverified ||
                  transaction.State ==
                      TransactionState.InTransit) &&
                 transaction.ShippingPurchaseReference != null &&
                 transaction.ShippingProviderTrackingCode != null &&
                 transaction.ShippingCancelledAt == null) ||
                transaction.ManagedShipments.Any(shipment =>
                    shipment.Status ==
                        ManagedShipmentStatus.Confirmed ||
                    shipment.Status ==
                        ManagedShipmentStatus.CarrierAccepted ||
                    shipment.Status ==
                        ManagedShipmentStatus.InTransit ||
                    shipment.Status ==
                        ManagedShipmentStatus.TrackingUnverified))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SaleTransaction>>
        GetShipmentsPendingCancellationAsync(
            CancellationToken cancellationToken) =>
        await Query()
            .Where(transaction =>
                transaction.State ==
                    TransactionState.RefundPending &&
                transaction.ShippingPurchaseReference != null &&
                transaction.ShippingProviderTrackingCode != null &&
                transaction.ShippingCancelledAt == null &&
                transaction.FirstCarrierScanAt == null &&
                !transaction.ManagedShipments.Any())
            .ToListAsync(cancellationToken);

    private IQueryable<SaleTransaction> Query() =>
        dbContext.Transactions
            .Include(x => x.AuditEvents)
            .Include(x => x.AgreementAcceptances)
            .Include(x => x.ExternalEvents)
            .Include(x => x.Notifications)
            .Include(x => x.DisputeEvidence)
            .Include(x => x.ManagedShipments)
            .Include(x => x.ShippingOperations)
            .Include(x => x.ProviderShippingAdjustments)
            .Include(x => x.ShippingInsuranceCases);
}
