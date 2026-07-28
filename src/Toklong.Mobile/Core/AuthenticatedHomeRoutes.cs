namespace Toklong.Mobile.Core;

public enum TransactionRoleRoute
{
    Buying,
    Selling
}

public static class AuthenticatedHomeRoutes
{
    public const string Home = "//home";

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
}
