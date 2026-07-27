using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Payments;

namespace Toklong.Application.Tests.Payments;

public sealed class StripePaymentIntentProviderTests
{
    [Fact]
    public async Task Digital_payment_is_blocked_until_platform_policy_is_approved()
    {
        var provider = new StripePaymentIntentProvider(
            new StripePaymentOptions
            {
                Enabled = true,
                EnableDigitalGoods = false,
                PublishableKey = "pk_test_not_real",
                SecretKey = "sk_test_not_real"
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.PrepareAsync(
                Guid.NewGuid(),
                100_000,
                "THB",
                FulfillmentType.DigitalHandoff,
                "buyer@example.com",
                null,
                default));
    }

    [Fact]
    public async Task Test_mode_rejects_live_keys_before_calling_Stripe()
    {
        var provider = new StripePaymentIntentProvider(
            new StripePaymentOptions
            {
                Enabled = true,
                LiveMode = false,
                EnableDigitalGoods = true,
                PublishableKey = "pk_live_not_real",
                SecretKey = "sk_live_not_real"
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.PrepareAsync(
                Guid.NewGuid(),
                100_000,
                "THB",
                FulfillmentType.PhysicalShipment,
                "buyer@example.com",
                null,
                default));

        Assert.Contains("Secret Key", exception.Message);
    }

    [Fact]
    public async Task Mixed_test_and_live_keys_are_rejected_before_calling_Stripe()
    {
        var provider = new StripePaymentIntentProvider(
            new StripePaymentOptions
            {
                Enabled = true,
                LiveMode = false,
                EnableDigitalGoods = true,
                PublishableKey = "pk_live_not_real",
                SecretKey = "sk_test_not_real"
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.PrepareAsync(
                Guid.NewGuid(),
                100_000,
                "THB",
                FulfillmentType.PhysicalShipment,
                "buyer@example.com",
                null,
                default));

        Assert.Contains("Publishable Key", exception.Message);
    }
}
