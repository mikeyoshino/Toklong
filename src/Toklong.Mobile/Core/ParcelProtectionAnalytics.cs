namespace Toklong.Mobile.Core;

public static class ParcelProtectionAnalytics
{
    public static MobileAnalyticsEvent Offered() => Event("parcel_protection_offered");

    public static MobileAnalyticsEvent Accepted(long customerPriceSatang) =>
        Event(
            "parcel_protection_accepted",
            ("customer_price_satang", customerPriceSatang.ToString(
                System.Globalization.CultureInfo.InvariantCulture)));

    public static MobileAnalyticsEvent Declined() => Event("parcel_protection_declined");

    public static MobileAnalyticsEvent Unavailable() => Event("parcel_protection_unavailable");

    public static MobileAnalyticsEvent Changed() => Event("parcel_protection_changed");

    public static MobileAnalyticsEvent PriceChanged() => Event("parcel_protection_price_changed");

    public static MobileAnalyticsEvent CheckoutConverted() =>
        Event("parcel_protection_checkout_converted");

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
