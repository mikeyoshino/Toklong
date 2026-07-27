using Toklong.Domain.Transactions;

namespace Toklong.Application.Abstractions;

public interface ITransactionRepository
{
    Task AddAsync(SaleTransaction transaction, CancellationToken cancellationToken);
    Task AddDisputeEvidenceAsync(
        DisputeEvidence evidence,
        CancellationToken cancellationToken);
    Task AddRiskEventAsync(ActivationRiskEvent riskEvent, CancellationToken cancellationToken);
    Task<SaleTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<SaleTransaction?> GetByPublicTokenAsync(string publicToken, CancellationToken cancellationToken);
    Task<SaleTransaction?> GetBySellerTokenAsync(string sellerToken, CancellationToken cancellationToken);
    Task<SaleTransaction?> GetByBuyerTokenAsync(string buyerToken, CancellationToken cancellationToken);
    Task<IReadOnlyList<SaleTransaction>> GetForPartiesAsync(
        Guid? buyerId,
        Guid? sellerId,
        string? intendedSellerPhoneNumber,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<SaleTransaction>> GetDueForReleaseAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<IReadOnlyList<SaleTransaction>> GetDueForExpirationAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<SaleTransaction>> GetDueForShipmentDeadlineAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<SaleTransaction>> GetPendingRefundsAsync(
        CancellationToken cancellationToken);
    Task<IReadOnlyList<SaleTransaction>> GetOpenDisputesAsync(
        CancellationToken cancellationToken);
    Task<IReadOnlyList<SaleTransaction>>
        GetPendingProviderPaymentsAsync(
            CancellationToken cancellationToken);
    Task<IReadOnlyList<SaleTransaction>>
        GetShipmentsAwaitingProviderConfirmationAsync(
            CancellationToken cancellationToken);
    Task<IReadOnlyList<SaleTransaction>>
        GetProviderShipmentsForReconciliationAsync(
            CancellationToken cancellationToken);
    Task<IReadOnlyList<SaleTransaction>>
        GetShipmentsPendingCancellationAsync(
            CancellationToken cancellationToken);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed record PayoutInstructionPreparation(
    string Provider,
    string ProviderReference,
    string Status);

public interface IPayoutProvider
{
    Task<PayoutInstructionPreparation> CreateInstructionAsync(
        Guid transactionId,
        long amountSatang,
        string currency,
        string bankCode,
        string accountName,
        string accountNumber,
        CancellationToken cancellationToken);
}

public sealed record RefundPreparation(
    string ProviderReference,
    string Status,
    DateTimeOffset? ActionExpiresAt = null,
    DateTimeOffset? InstructionsSentAt = null);

public interface IRefundProvider
{
    Task<RefundPreparation> CreateFullRefundAsync(
        Guid transactionId,
        string paymentReference,
        long amountSatang,
        string currency,
        string? existingRefundReference,
        CancellationToken cancellationToken);
}

public sealed record RefundReconciliationResult(
    bool Succeeded,
    string EventId,
    string RefundReference,
    string PaymentReference,
    long AmountSatang,
    string Currency,
    DateTimeOffset OccurredAt,
    string Status,
    DateTimeOffset? ActionExpiresAt = null,
    DateTimeOffset? InstructionsSentAt = null);

public interface IRefundReconciliationProvider
{
    Task<RefundReconciliationResult> ReconcileAsync(
        Guid transactionId,
        string refundReference,
        CancellationToken cancellationToken);
}

public interface IWebhookSignatureVerifier
{
    bool Verify(string payload, string signature);
}

public sealed record PaymentIntentPreparation(
    string ProviderReference,
    string ClientSecret,
    string PublishableKey);

public interface IPaymentIntentProvider
{
    Task<PaymentIntentPreparation> PrepareAsync(
        Guid transactionId,
        long amountSatang,
        string currency,
        FulfillmentType fulfillmentType,
        string receiptEmail,
        string? existingProviderReference,
        CancellationToken cancellationToken);
}

public sealed record PaymentReconciliationResult(
    bool Succeeded,
    string EventId,
    long AmountSatang,
    string Currency,
    DateTimeOffset OccurredAt);

public interface IPaymentReconciliationProvider
{
    Task<PaymentReconciliationResult> ReconcileAsync(
        Guid transactionId,
        string paymentReference,
        CancellationToken cancellationToken);
}

public sealed record NotificationDeliveryResult(
    string ProviderReference);

public interface INotificationProvider
{
    Task<NotificationDeliveryResult> SendAsync(
        Guid notificationId,
        string recipient,
        string template,
        Guid transactionId,
        string title,
        string body,
        string deepLink,
        CancellationToken cancellationToken);
}
