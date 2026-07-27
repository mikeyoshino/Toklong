namespace Toklong.Domain.Transactions;

public sealed class FinancialRetentionRecord
{
    private FinancialRetentionRecord() { }

    private FinancialRetentionRecord(
        SaleTransaction transaction,
        DateTimeOffset purgedAt)
    {
        TransactionId = transaction.Id;
        TerminalState = transaction.State;
        PriceSatang = transaction.PriceSatang;
        ShippingFeeSatang =
            transaction.ShippingFeeSatang;
        BuyerTotalSatang =
            transaction.BuyerTotalSatang;
        PlatformFeeSatang =
            transaction.PlatformFeeSatang;
        BuyerProtectionFeeSatang =
            transaction.BuyerProtectionFeeSatang;
        SellerExpectedNetSatang =
            transaction.SellerExpectedNetSatang;
        Currency = transaction.Currency;
        PaymentProvider =
            transaction.PaymentProvider;
        PaymentReference =
            transaction.PaymentReference;
        RefundReference =
            transaction.RefundReference;
        PayoutProvider =
            transaction.PayoutProvider;
        PayoutReference =
            transaction.PayoutReference;
        RetentionStartedAt =
            transaction.RetentionStartsAt ??
            throw new InvalidOperationException(
                "Transaction has no retention start.");
        EvidenceRetentionExpiredAt =
            transaction.RetentionExpiresAt ??
            throw new InvalidOperationException(
                "Transaction has no retention expiry.");
        FinancialRetentionExpiresAt =
            RetentionStartedAt.AddYears(
                SaleTransaction
                    .FinancialRetentionYears);
        PurgedAt = purgedAt;
    }

    public Guid TransactionId { get; private set; }
    public TransactionState TerminalState { get; private set; }
    public long PriceSatang { get; private set; }
    public long ShippingFeeSatang { get; private set; }
    public long BuyerTotalSatang { get; private set; }
    public long PlatformFeeSatang { get; private set; }
    public long BuyerProtectionFeeSatang { get; private set; }
    public long SellerExpectedNetSatang { get; private set; }
    public string Currency { get; private set; } = "";
    public string? PaymentProvider { get; private set; }
    public string? PaymentReference { get; private set; }
    public string? RefundReference { get; private set; }
    public string? PayoutProvider { get; private set; }
    public string? PayoutReference { get; private set; }
    public DateTimeOffset RetentionStartedAt { get; private set; }
    public DateTimeOffset EvidenceRetentionExpiredAt { get; private set; }
    public DateTimeOffset FinancialRetentionExpiresAt { get; private set; }
    public DateTimeOffset PurgedAt { get; private set; }

    public static FinancialRetentionRecord Create(
        SaleTransaction transaction,
        DateTimeOffset purgedAt)
    {
        if (!transaction.RetentionExpiresAt.HasValue ||
            transaction.RetentionExpiresAt.Value >
            purgedAt)
            throw new InvalidOperationException(
                "Transaction evidence is not due for purge.");
        if (transaction.HasActiveLegalHold)
            throw new InvalidOperationException(
                "Transaction has an active legal hold.");
        return new FinancialRetentionRecord(
            transaction,
            purgedAt);
    }
}
