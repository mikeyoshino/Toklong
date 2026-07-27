using Stripe;
using Toklong.Application.Abstractions;

namespace Toklong.Infrastructure.Payments;

public sealed class StripePaymentReconciliationProvider(
    StripePaymentOptions options)
    : IPaymentReconciliationProvider
{
    public async Task<PaymentReconciliationResult> ReconcileAsync(
        Guid transactionId,
        string paymentReference,
        CancellationToken cancellationToken)
    {
        options.EnsureServerApiConfigured();
        var client = new StripeClient(options.SecretKey);
        var intent = await new PaymentIntentService(client)
            .GetAsync(
                paymentReference,
                cancellationToken: cancellationToken);
        if (!string.Equals(
                intent.Id,
                paymentReference,
                StringComparison.Ordinal) ||
            intent.Livemode != options.LiveMode ||
            !intent.Metadata.TryGetValue(
                "toklong_transaction_id",
                out var linkedTransactionId) ||
            !string.Equals(
                linkedTransactionId,
                transactionId.ToString("N"),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Stripe reconciliation ไม่ตรงกับรายการ");
        if (!string.Equals(
                intent.Status,
                "succeeded",
                StringComparison.OrdinalIgnoreCase))
            return new PaymentReconciliationResult(
                false,
                $"stripe-reconcile:{intent.Id}:{intent.Status}",
                intent.Amount,
                intent.Currency,
                DateTimeOffset.UtcNow);
        if (string.IsNullOrWhiteSpace(intent.LatestChargeId))
            throw new InvalidOperationException(
                "Stripe payment สำเร็จแต่ไม่มี charge สำหรับยืนยันเวลา");
        var charge = await new ChargeService(client).GetAsync(
            intent.LatestChargeId,
            cancellationToken: cancellationToken);
        if (!charge.Paid ||
            charge.Amount != intent.Amount ||
            !string.Equals(
                charge.Currency,
                intent.Currency,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Stripe charge ไม่ตรงกับ PaymentIntent");
        return new PaymentReconciliationResult(
            true,
            $"stripe-reconcile:{intent.Id}:{charge.Id}",
            charge.Amount,
            charge.Currency,
            charge.Created);
    }
}
