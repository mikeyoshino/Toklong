namespace Toklong.Mobile.Core;

public sealed record MobileAnalyticsEvent(
    string Name,
    IReadOnlyDictionary<string, string> Properties);

public interface IMobileAnalytics
{
    void Track(MobileAnalyticsEvent value);
}

public static class SellerWorkspaceAnalytics
{
    public static MobileAnalyticsEvent FilterSelected(
        SellerWorkCategory category,
        int visibleCount) =>
        Event(
            "seller_summary_filter_selected",
            ("category", category.ToString()),
            ("visible_count", visibleCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture)));

    public static MobileAnalyticsEvent SpotlightOpened(
        TransactionAction action,
        string state) =>
        Event(
            "seller_spotlight_opened",
            ("action", action.ToString()),
            ("state", SafeSpotlightState(state)));

    public static MobileAnalyticsEvent ProblemBannerOpened(int count) =>
        Event(
            "seller_problem_banner_opened",
            ("visible_count", count.ToString(
                System.Globalization.CultureInfo.InvariantCulture)));

    public static MobileAnalyticsEvent HomeOpened(
        int newOfferCount,
        int actionableCount) =>
        Event(
            "seller_home_opened",
            ("new_offer_count", newOfferCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture)),
            ("actionable_count", actionableCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture)));

    private static MobileAnalyticsEvent Event(
        string name,
        params (string Key, string Value)[] properties) =>
        new(
            name,
            properties.ToDictionary(
                property => property.Key,
                property => property.Value,
                StringComparer.Ordinal));

    private static string SafeSpotlightState(string state) =>
        state switch
        {
            "AwaitingSellerAcceptance" or
            "PaidAwaitingShipment" or
            "PaidAwaitingDigitalDelivery" or
            "TrackingUnverified" or
            "ShipmentOverdue" => state,
            _ => "Unknown"
        };
}
