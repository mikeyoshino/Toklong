namespace Toklong.Shippop.Certification;

internal static class CertificationEndpointGuard
{
    private const string Approved =
        "https://mkpservice.shippop.dev";

    public static void EnsureApproved(string baseUrl)
    {
        var clean = baseUrl.Trim();
        if (clean.EndsWith("/", StringComparison.Ordinal))
            clean = clean[..^1];
        if (!string.Equals(
                clean,
                Approved,
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "SHIPPOP certification endpoint is not approved.");
    }
}
