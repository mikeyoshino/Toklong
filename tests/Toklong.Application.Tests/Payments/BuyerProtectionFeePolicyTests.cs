using Toklong.Infrastructure.Pricing;

namespace Toklong.Application.Tests.Payments;

public sealed class BuyerProtectionFeePolicyTests
{
    [Theory]
    [InlineData(100_000, 5_900)]
    [InlineData(300_000, 12_000)]
    [InlineData(500_000, 20_000)]
    [InlineData(1_000_000, 37_500)]
    [InlineData(1_500_000, 55_000)]
    [InlineData(2_000_000, 70_000)]
    [InlineData(3_000_000, 100_000)]
    public void Marginal_tiers_use_integer_satang_and_buyer_funding(
        long itemPriceSatang,
        long expectedBuyerFeeSatang)
    {
        var fees = Policy().Calculate(itemPriceSatang);

        Assert.Equal(
            expectedBuyerFeeSatang,
            fees.BuyerProtectionFeeSatang);
        Assert.Equal(0, fees.PlatformFeeSatang);
        Assert.Equal(
            itemPriceSatang,
            fees.SellerExpectedNetSatang);
        Assert.Equal(
            "buyer-protection-v2",
            fees.PolicyVersion);
    }

    [Fact]
    public void Weighted_tiers_are_rounded_up_once_to_whole_satang()
    {
        var fees = Policy().Calculate(500_001);

        Assert.Equal(20_001, fees.BuyerProtectionFeeSatang);
    }

    [Theory]
    [InlineData(499_999, 20_000)]
    [InlineData(500_000, 20_000)]
    [InlineData(500_001, 20_001)]
    [InlineData(1_499_999, 55_000)]
    [InlineData(1_500_000, 55_000)]
    [InlineData(1_500_001, 55_001)]
    public void Marginal_tier_boundaries_do_not_reprice_lower_portions(
        long itemPriceSatang,
        long expectedBuyerFeeSatang)
    {
        var fees = Policy().Calculate(itemPriceSatang);

        Assert.Equal(
            expectedBuyerFeeSatang,
            fees.BuyerProtectionFeeSatang);
    }

    [Theory]
    [InlineData(99_999)]
    [InlineData(3_000_001)]
    public void Price_outside_active_pilot_range_is_rejected(
        long itemPriceSatang)
    {
        Assert.Throws<Toklong.Domain.Common.DomainException>(
            () => Policy().Calculate(itemPriceSatang));
    }

    [Fact]
    public void Active_range_must_be_covered_by_configured_tiers()
    {
        var options = new BuyerProtectionFeeOptions
        {
            Enabled = true,
            MinimumItemPriceSatang = 100_000,
            MaximumItemPriceSatang = 3_000_001,
            PolicyVersion = "buyer-protection-invalid",
            Tiers =
            [
                new BuyerProtectionFeeTierOptions
                {
                    UpToItemPriceSatang = 3_000_000,
                    RateBasisPoints = 300
                }
            ]
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Rates_must_not_increase_for_higher_tiers()
    {
        var options = new BuyerProtectionFeeOptions
        {
            Enabled = true,
            MinimumItemPriceSatang = 100_000,
            MaximumItemPriceSatang = 3_000_000,
            PolicyVersion = "buyer-protection-invalid",
            Tiers =
            [
                new BuyerProtectionFeeTierOptions
                {
                    UpToItemPriceSatang = 500_000,
                    RateBasisPoints = 300
                },
                new BuyerProtectionFeeTierOptions
                {
                    UpToItemPriceSatang = 3_000_000,
                    RateBasisPoints = 400
                }
            ]
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Tier_upper_bounds_must_be_strictly_ordered()
    {
        var options = new BuyerProtectionFeeOptions
        {
            Enabled = true,
            MinimumItemPriceSatang = 100_000,
            MaximumItemPriceSatang = 3_000_000,
            PolicyVersion = "buyer-protection-invalid",
            Tiers =
            [
                new BuyerProtectionFeeTierOptions
                {
                    UpToItemPriceSatang = 1_500_000,
                    RateBasisPoints = 400
                },
                new BuyerProtectionFeeTierOptions
                {
                    UpToItemPriceSatang = 500_000,
                    RateBasisPoints = 350
                }
            ]
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Active_limit_cannot_exceed_domain_technical_maximum()
    {
        var options = new BuyerProtectionFeeOptions
        {
            Enabled = true,
            MinimumItemPriceSatang = 100_000,
            MaximumItemPriceSatang =
                Toklong.Domain.Transactions.SaleTransaction
                    .MaximumProtectedItemPriceSatang + 1,
            PolicyVersion = "buyer-protection-invalid",
            Tiers =
            [
                new BuyerProtectionFeeTierOptions
                {
                    UpToItemPriceSatang =
                        Toklong.Domain.Transactions.SaleTransaction
                            .MaximumProtectedItemPriceSatang,
                    RateBasisPoints = 300
                }
            ]
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    private static ConfiguredBuyerProtectionFeePolicy Policy(
        long minimumItemPriceSatang = 100_000,
        long maximumItemPriceSatang = 3_000_000) =>
        new(
            new BuyerProtectionFeeOptions
            {
                Enabled = true,
                MinimumFeeSatang = 5_900,
                MinimumItemPriceSatang =
                    minimumItemPriceSatang,
                MaximumItemPriceSatang =
                    maximumItemPriceSatang,
                PolicyVersion =
                    "buyer-protection-v2"
            });
}
