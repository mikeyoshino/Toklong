namespace Toklong.Mobile.Core;

public enum TransactionRoleRoute
{
    Buying,
    Selling
}

public static class AuthenticatedHomeRoutes
{
    public const string Buying = "//buying";
    public const string Selling = "//selling";
    public const string Default = Buying;

    public static string Root(TransactionRoleRoute role) =>
        role switch
        {
            TransactionRoleRoute.Buying => Buying,
            TransactionRoleRoute.Selling => Selling,
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        };

    public static bool TryParseRoot(
        string? route,
        out TransactionRoleRoute role)
    {
        var value = route?.Trim();
        if (!string.IsNullOrEmpty(value))
        {
            var suffixIndex = value.IndexOfAny(['?', '#']);
            var path = suffixIndex < 0
                ? value
                : value[..suffixIndex];
            var segments = path.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 1)
            {
                if (string.Equals(
                        segments[0],
                        "selling",
                        StringComparison.Ordinal))
                {
                    role = TransactionRoleRoute.Selling;
                    return true;
                }

                if (string.Equals(
                        segments[0],
                        "buying",
                        StringComparison.Ordinal))
                {
                    role = TransactionRoleRoute.Buying;
                    return true;
                }
            }
        }

        role = default;
        return false;
    }

    public static bool IsAuthenticatedRoot(string? route) =>
        TryParseRoot(route, out _);

    public static string RootOrDefault(string? route) =>
        TryParseRoot(route, out var role)
            ? Root(role)
            : Default;

}
