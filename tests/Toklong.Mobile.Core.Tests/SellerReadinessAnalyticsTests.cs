using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class SellerReadinessAnalyticsTests
{
    [Theory]
    [InlineData(AppFulfillmentType.Physical, "physical")]
    [InlineData(AppFulfillmentType.Digital, "game_account")]
    public void Confirmed_records_only_safe_type(
        AppFulfillmentType type,
        string expected)
    {
        var value = SellerReadinessAnalytics.Confirmed(type);

        Assert.Equal("seller_readiness_confirmed", value.Name);
        Assert.Equal(expected, value.Properties["type"]);
        Assert.Single(value.Properties);
    }

    [Fact]
    public void Validation_failed_records_safe_enums_only()
    {
        var value = SellerReadinessAnalytics.ValidationFailed(
            AppFulfillmentType.Physical,
            SellerReadinessFailureReason.ShippingSelection);

        Assert.Equal("seller_readiness_validation_failed", value.Name);
        Assert.Equal("physical", value.Properties["type"]);
        Assert.Equal("shipping_selection", value.Properties["reason"]);
        Assert.Equal(2, value.Properties.Count);
    }
}
