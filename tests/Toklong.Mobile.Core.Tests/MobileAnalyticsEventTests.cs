using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class MobileAnalyticsEventTests
{
    [Fact]
    public void Parcel_protection_analytics_has_only_the_customer_price()
    {
        var accepted = ParcelProtectionAnalytics.Accepted(6_000);

        Assert.Equal("parcel_protection_accepted", accepted.Name);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["customer_price_satang"] = "6000"
            },
            accepted.Properties);
        Assert.DoesNotContain(
            accepted.Properties.Keys,
            key => key.Contains("provider", StringComparison.OrdinalIgnoreCase) ||
                   key.Contains("address", StringComparison.OrdinalIgnoreCase) ||
                   key.Contains("phone", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Seller_events_contain_only_approved_aggregate_properties()
    {
        var filter = SellerWorkspaceAnalytics.FilterSelected(
            SellerWorkCategory.NewOffers,
            3);
        var spotlight = SellerWorkspaceAnalytics.SpotlightOpened(
            TransactionAction.ReviewSellerOffer,
            "AwaitingSellerAcceptance");
        var problem = SellerWorkspaceAnalytics.ProblemBannerOpened(2);
        var home = SellerWorkspaceAnalytics.HomeOpened(3, 4);
        var unknownState = SellerWorkspaceAnalytics.SpotlightOpened(
            TransactionAction.AddTracking,
            "unexpected product text");

        Assert.Equal("seller_summary_filter_selected", filter.Name);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["category"] = "NewOffers",
                ["visible_count"] = "3"
            },
            filter.Properties);
        Assert.Equal("seller_spotlight_opened", spotlight.Name);
        Assert.Equal("ReviewSellerOffer", spotlight.Properties["action"]);
        Assert.Equal(
            "AwaitingSellerAcceptance",
            spotlight.Properties["state"]);
        Assert.Equal("2", problem.Properties["visible_count"]);
        Assert.Equal("3", home.Properties["new_offer_count"]);
        Assert.Equal("4", home.Properties["actionable_count"]);
        Assert.Equal("Unknown", unknownState.Properties["state"]);
    }
}
