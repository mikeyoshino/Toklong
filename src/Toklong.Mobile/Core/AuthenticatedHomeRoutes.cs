namespace Toklong.Mobile.Core;

public enum TransactionRoleRoute
{
    Buying,
    Selling
}

public static class AuthenticatedHomeRoutes
{
    public const string Buying = "//main/buying";
    public const string Selling = "//main/selling";

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
        var value = route?.TrimEnd('/');
        if (value?.Contains("/selling", StringComparison.Ordinal) == true)
        {
            role = TransactionRoleRoute.Selling;
            return true;
        }

        if (value?.Contains("/buying", StringComparison.Ordinal) == true)
        {
            role = TransactionRoleRoute.Buying;
            return true;
        }

        role = default;
        return false;
    }

    public static bool IsAuthenticatedRoot(string? route) =>
        TryParseRoot(route, out _);

}
