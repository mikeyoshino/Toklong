namespace Toklong.Mobile.Core;

public enum TransactionRoleRoute
{
    Buying,
    Selling
}

public static class AuthenticatedHomeRoutes
{
    public const string Home = "//home";
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

    public static string Transactions(TransactionRoleRoute role) =>
        role switch
        {
            TransactionRoleRoute.Buying =>
                "//transactions?role=buying",
            TransactionRoleRoute.Selling =>
                "//transactions?role=selling",
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        };

    public static bool TryParseRole(
        string? value,
        out TransactionRoleRoute role)
    {
        role = value switch
        {
            "buying" => TransactionRoleRoute.Buying,
            "selling" => TransactionRoleRoute.Selling,
            _ => default
        };
        return value is "buying" or "selling";
    }

    public static RoleFilter ToRoleFilter(
        TransactionRoleRoute role) =>
        role switch
        {
            TransactionRoleRoute.Buying => RoleFilter.Buying,
            TransactionRoleRoute.Selling => RoleFilter.Selling,
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        };
}
