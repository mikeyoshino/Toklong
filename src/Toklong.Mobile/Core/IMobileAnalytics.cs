namespace Toklong.Mobile.Core;

public sealed record MobileAnalyticsEvent(
    string Name,
    IReadOnlyDictionary<string, string> Properties);

public interface IMobileAnalytics
{
    void Track(MobileAnalyticsEvent value);
}

public enum AccountEmailChangeFailureReason
{
    Invalid,
    Expired,
    Locked,
    Network,
    Sender
}

public static class AccountEmailChangeAnalytics
{
    public static MobileAnalyticsEvent Started() =>
        Event("account_email_change_started");

    public static MobileAnalyticsEvent CodeResent() =>
        Event("account_email_change_code_resent");

    public static MobileAnalyticsEvent Verified() =>
        Event("account_email_change_verified");

    public static MobileAnalyticsEvent Failed(
        AccountEmailChangeFailureReason reason) =>
        Event(
            "account_email_change_failed",
            ("reason", reason switch
            {
                AccountEmailChangeFailureReason.Invalid => "invalid",
                AccountEmailChangeFailureReason.Expired => "expired",
                AccountEmailChangeFailureReason.Locked => "locked",
                AccountEmailChangeFailureReason.Network => "network",
                AccountEmailChangeFailureReason.Sender => "sender",
                _ => "invalid"
            }));

    private static MobileAnalyticsEvent Event(
        string name,
        params (string Key, string Value)[] properties) =>
        new(
            name,
            properties.ToDictionary(
                property => property.Key,
                property => property.Value,
                StringComparer.Ordinal));
}

public enum AccountNameChangeBlockReason
{
    Cooldown
}

public enum AccountNameChangeFailureReason
{
    Invalid,
    Expired,
    Locked,
    Network,
    Provider
}

public static class AccountNameChangeAnalytics
{
    public static MobileAnalyticsEvent Started() => Event("account_name_change_started");

    public static MobileAnalyticsEvent CodeResent() => Event("account_name_change_code_resent");

    public static MobileAnalyticsEvent Verified() => Event("account_name_change_verified");

    public static MobileAnalyticsEvent Blocked(AccountNameChangeBlockReason reason) =>
        Event("account_name_change_blocked", ("reason", reason switch
        {
            AccountNameChangeBlockReason.Cooldown => "cooldown",
            _ => "cooldown"
        }));

    public static MobileAnalyticsEvent Failed(AccountNameChangeFailureReason reason) =>
        Event("account_name_change_failed", ("reason", reason switch
        {
            AccountNameChangeFailureReason.Invalid => "invalid",
            AccountNameChangeFailureReason.Expired => "expired",
            AccountNameChangeFailureReason.Locked => "locked",
            AccountNameChangeFailureReason.Network => "network",
            AccountNameChangeFailureReason.Provider => "provider",
            _ => "invalid"
        }));

    private static MobileAnalyticsEvent Event(
        string name,
        params (string Key, string Value)[] properties) =>
        new(
            name,
            properties.ToDictionary(
                property => property.Key,
                property => property.Value,
                StringComparer.Ordinal));
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
