namespace Toklong.Mobile.Core;

public enum SellerReadinessFailureReason
{
    PayoutAccount,
    Confirmations,
    ShippingSelection
}

public static class SellerReadinessAnalytics
{
    public static MobileAnalyticsEvent Confirmed(AppFulfillmentType type) =>
        Event("seller_readiness_confirmed", type);

    public static MobileAnalyticsEvent Declined(AppFulfillmentType type) =>
        Event("seller_readiness_declined", type);

    public static MobileAnalyticsEvent ValidationFailed(
        AppFulfillmentType type,
        SellerReadinessFailureReason reason) =>
        Event(
            "seller_readiness_validation_failed",
            type,
            ("reason", reason switch
            {
                SellerReadinessFailureReason.PayoutAccount =>
                    "payout_account",
                SellerReadinessFailureReason.Confirmations =>
                    "confirmations",
                SellerReadinessFailureReason.ShippingSelection =>
                    "shipping_selection",
                _ => throw new ArgumentOutOfRangeException(nameof(reason))
            }));

    private static MobileAnalyticsEvent Event(
        string name,
        AppFulfillmentType type,
        params (string Key, string Value)[] properties) =>
        new(
            name,
            new[]
            {
                ("type", type switch
                {
                    AppFulfillmentType.Physical => "physical",
                    AppFulfillmentType.Digital => "game_account",
                    _ => throw new ArgumentOutOfRangeException(nameof(type))
                })
            }
            .Concat(properties)
            .ToDictionary(
                property => property.Item1,
                property => property.Item2,
                StringComparer.Ordinal));
}
