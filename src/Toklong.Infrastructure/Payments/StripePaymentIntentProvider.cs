using Microsoft.Extensions.Configuration;
using Stripe;
using Toklong.Application.Abstractions;
using Toklong.Domain.Transactions;

namespace Toklong.Infrastructure.Payments;

public sealed class StripePaymentOptions
{
    public const string SectionName = "Stripe";

    public bool Enabled { get; init; }
    public bool LiveMode { get; init; }
    public bool EnableDigitalGoods { get; init; }
    public string PublishableKey { get; init; } = "";
    public string SecretKey { get; init; } = "";
    public string WebhookSecret { get; init; } = "";

    public static StripePaymentOptions From(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        return new StripePaymentOptions
        {
            Enabled = section.GetValue<bool>("Enabled"),
            LiveMode = section.GetValue<bool>("LiveMode"),
            EnableDigitalGoods =
                section.GetValue<bool>("EnableDigitalGoods"),
            PublishableKey = section["PublishableKey"] ?? "",
            SecretKey = section["SecretKey"] ?? "",
            WebhookSecret = section["WebhookSecret"] ?? ""
        };
    }

    public void EnsurePaymentApiConfigured()
    {
        if (!Enabled ||
            string.IsNullOrWhiteSpace(SecretKey) ||
            string.IsNullOrWhiteSpace(PublishableKey))
            throw new InvalidOperationException(
                "ยังไม่ได้เปิดระบบรับชำระเงิน Stripe");
        EnsureSecretKeyMatchesMode();
        var expectedPublishablePrefix =
            LiveMode ? "pk_live_" : "pk_test_";
        if (!PublishableKey.StartsWith(
                expectedPublishablePrefix,
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "โหมดและ Publishable Key ของ Stripe ไม่ตรงกัน");
    }

    public void EnsureServerApiConfigured()
    {
        if (!Enabled || string.IsNullOrWhiteSpace(SecretKey))
            throw new InvalidOperationException(
                "ยังไม่ได้ตั้งค่า Stripe ฝั่งเซิร์ฟเวอร์");
        EnsureSecretKeyMatchesMode();
    }

    public bool ApiKeyModesMatch() =>
        SecretKey.StartsWith(
            LiveMode ? "sk_live_" : "sk_test_",
            StringComparison.Ordinal) &&
        PublishableKey.StartsWith(
            LiveMode ? "pk_live_" : "pk_test_",
            StringComparison.Ordinal);

    private void EnsureSecretKeyMatchesMode()
    {
        var expectedSecretPrefix =
            LiveMode ? "sk_live_" : "sk_test_";
        if (!SecretKey.StartsWith(
                expectedSecretPrefix,
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "โหมดและ Secret Key ของ Stripe ไม่ตรงกัน");
    }
}

public sealed class StripePaymentIntentProvider(StripePaymentOptions options)
    : IPaymentIntentProvider
{
    public async Task<PaymentIntentPreparation> PrepareAsync(
        Guid transactionId,
        long amountSatang,
        string currency,
        FulfillmentType fulfillmentType,
        string receiptEmail,
        string? existingProviderReference,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (fulfillmentType == FulfillmentType.DigitalHandoff &&
            !options.EnableDigitalGoods)
            throw new InvalidOperationException(
                "ยังไม่เปิดชำระสินค้าดิจิทัลในแอป เพราะรออนุมัตินโยบายแพลตฟอร์ม");
        var service = new PaymentIntentService(
            new StripeClient(options.SecretKey));
        PaymentIntent intent;
        if (!string.IsNullOrWhiteSpace(existingProviderReference))
        {
            intent = await service.GetAsync(
                existingProviderReference,
                cancellationToken: cancellationToken);
        }
        else
        {
            intent = await service.CreateAsync(
                new PaymentIntentCreateOptions
                {
                    Amount = amountSatang,
                    Currency = currency.ToLowerInvariant(),
                    ReceiptEmail = receiptEmail,
                    AutomaticPaymentMethods =
                        new PaymentIntentAutomaticPaymentMethodsOptions
                        {
                            Enabled = true
                        },
                    Metadata = new Dictionary<string, string>
                    {
                        ["toklong_transaction_id"] =
                            transactionId.ToString("N")
                    }
                },
                new RequestOptions
                {
                    IdempotencyKey =
                        $"toklong-payment-intent-{transactionId:N}"
                },
                cancellationToken);
        }

        if (intent.Amount != amountSatang ||
            !string.Equals(
                intent.Currency,
                currency,
                StringComparison.OrdinalIgnoreCase) ||
            intent.Livemode != options.LiveMode ||
            !intent.Metadata.TryGetValue(
                "toklong_transaction_id",
                out var linkedTransactionId) ||
            !string.Equals(
                linkedTransactionId,
                transactionId.ToString("N"),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "ข้อมูล PaymentIntent จาก Stripe ไม่ตรงกับรายการ");
        if (string.IsNullOrWhiteSpace(intent.ClientSecret))
            throw new InvalidOperationException(
                "Stripe ไม่ได้ส่งข้อมูลสำหรับเปิดหน้าจ่ายเงิน");
        return new PaymentIntentPreparation(
            intent.Id,
            intent.ClientSecret,
            options.PublishableKey);
    }

    private void EnsureConfigured()
    {
        options.EnsurePaymentApiConfigured();
    }
}
