using Toklong.Application.Pricing;
using Toklong.Domain.Common;

namespace Toklong.Application.Tests.Pricing;

public sealed class ParcelProtectionPricingPolicyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Price_rejects_non_positive_provider_cost(long providerCostSatang)
    {
        var policy = new ParcelProtectionPricingPolicy();

        Assert.Throws<DomainException>(() =>
            policy.Price(providerCostSatang));
    }

    [Fact]
    public void Price_rejects_customer_price_overflow()
    {
        var policy = new ParcelProtectionPricingPolicy();

        Assert.Throws<OverflowException>(() =>
            policy.Price(long.MaxValue));
    }
}
