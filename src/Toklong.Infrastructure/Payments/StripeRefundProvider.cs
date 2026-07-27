using Stripe;
using Toklong.Application.Abstractions;

namespace Toklong.Infrastructure.Payments;

public sealed class StripeRefundProvider(
    StripePaymentOptions options,
    IClock clock,
    IStripeClient? configuredClient = null)
    : IRefundProvider, IRefundReconciliationProvider
{
    public async Task<RefundPreparation> CreateFullRefundAsync(
        Guid transactionId,
        string paymentReference,
        long amountSatang,
        string currency,
        string? existingRefundReference,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var client = configuredClient ??
                     new StripeClient(options.SecretKey);
        var paymentIntent = await new PaymentIntentService(client)
            .GetAsync(
                paymentReference,
                new PaymentIntentGetOptions
                {
                    Expand = ["latest_charge"]
                },
                cancellationToken: cancellationToken);
        if (paymentIntent.Amount != amountSatang ||
            !string.Equals(
                paymentIntent.Currency,
                currency,
                StringComparison.OrdinalIgnoreCase) ||
            paymentIntent.Livemode != options.LiveMode ||
            string.IsNullOrWhiteSpace(
                paymentIntent.ReceiptEmail) ||
            !paymentIntent.Metadata.TryGetValue(
                "toklong_transaction_id",
                out var paymentTransactionId) ||
            !string.Equals(
                paymentTransactionId,
                transactionId.ToString("N"),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Stripe PaymentIntent สำหรับคืนเงินไม่ตรงกับรายการ");
        var requiresInstructionsEmail = string.Equals(
            paymentIntent.LatestCharge?
                .PaymentMethodDetails?
                .Type,
            "promptpay",
            StringComparison.OrdinalIgnoreCase);
        var service = new RefundService(client);
        Refund refund;
        if (!string.IsNullOrWhiteSpace(existingRefundReference))
        {
            refund = await service.GetAsync(
                existingRefundReference,
                cancellationToken: cancellationToken);
        }
        else
        {
            refund = await service.CreateAsync(
                new RefundCreateOptions
                {
                    PaymentIntent = paymentReference,
                    Amount = amountSatang,
                    InstructionsEmail = requiresInstructionsEmail
                        ? paymentIntent.ReceiptEmail
                        : null,
                    Metadata = new Dictionary<string, string>
                    {
                        ["toklong_transaction_id"] =
                            transactionId.ToString("N"),
                        ["toklong_currency"] =
                            currency.ToLowerInvariant()
                    }
                },
                new RequestOptions
                {
                    IdempotencyKey =
                        $"toklong-refund-{transactionId:N}"
                },
                cancellationToken);
        }

        if (refund.Amount != amountSatang ||
            !string.Equals(
                refund.Currency,
                currency,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                refund.PaymentIntentId,
                paymentReference,
                StringComparison.Ordinal) ||
            !IsLinkedToTransaction(refund, transactionId))
            throw new InvalidOperationException(
                "Stripe คืนข้อมูลคำขอคืนเงินไม่ตรงกับรายการ");
        var details = refund.NextAction?.DisplayDetails;
        return new RefundPreparation(
            refund.Id,
            NormalizeStatus(refund.Status),
            details?.ExpiresAt,
            details?.EmailSent?.EmailSentAt);
    }

    public async Task<RefundReconciliationResult> ReconcileAsync(
        Guid transactionId,
        string refundReference,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var refund = await new RefundService(
                configuredClient ??
                new StripeClient(options.SecretKey))
            .GetAsync(
                refundReference,
                cancellationToken: cancellationToken);
        if (!string.Equals(
                refund.Id,
                refundReference,
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(refund.PaymentIntentId) ||
            !IsLinkedToTransaction(refund, transactionId))
            throw new InvalidOperationException(
                "Stripe refund reconciliation ไม่ตรงกับรายการ");
        var status = refund.Status?.Trim().ToLowerInvariant() ??
                     "unknown";
        var details = refund.NextAction?.DisplayDetails;
        return new RefundReconciliationResult(
            string.Equals(
                status,
                "succeeded",
                StringComparison.Ordinal),
            $"stripe-refund-reconcile:{refund.Id}:{status}",
            refund.Id,
            refund.PaymentIntentId,
            refund.Amount,
            refund.Currency,
            clock.UtcNow,
            status,
            details?.ExpiresAt,
            details?.EmailSent?.EmailSentAt);
    }

    private static bool IsLinkedToTransaction(
        Refund refund,
        Guid transactionId) =>
        refund.Metadata.TryGetValue(
            "toklong_transaction_id",
            out var linkedTransactionId) &&
        string.Equals(
            linkedTransactionId,
            transactionId.ToString("N"),
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeStatus(string? value) =>
        value?.Trim().ToLowerInvariant() ?? "unknown";

    private void EnsureConfigured()
    {
        options.EnsureServerApiConfigured();
    }
}
