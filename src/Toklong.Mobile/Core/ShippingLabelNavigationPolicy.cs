namespace Toklong.Mobile.Core;

public static class ShippingLabelNavigationPolicy
{
    private static readonly HashSet<string> InternalSchemes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "about",
            "data",
            "file"
        };

    public static bool ShouldCancel(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(
                url,
                UriKind.Absolute,
                out var uri))
            return false;

        return !InternalSchemes.Contains(uri.Scheme);
    }
}
