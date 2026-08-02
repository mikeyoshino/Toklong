using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class CreateOfferAnalyticsTests
{
    [Fact]
    public void TypeSelectionOpened_has_no_product_or_identity_data()
    {
        var value = CreateOfferAnalytics.TypeSelectionOpened();

        Assert.Equal("buyer_offer_type_selection_opened", value.Name);
        Assert.Empty(value.Properties);
    }

    [Theory]
    [InlineData(AppFulfillmentType.Physical, "physical")]
    [InlineData(AppFulfillmentType.Digital, "game_account")]
    public void TypeSelected_records_only_the_safe_type_dimension(
        AppFulfillmentType type,
        string expected)
    {
        var value = CreateOfferAnalytics.TypeSelected(type);

        Assert.Equal("buyer_offer_type_selected", value.Name);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["type"] = expected
            },
            value.Properties);
    }
}
