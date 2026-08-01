using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class AuthenticatedHomeRoutesTests
{
    [Theory]
    [InlineData(TransactionRoleRoute.Buying, "//main/buying")]
    [InlineData(TransactionRoleRoute.Selling, "//main/selling")]
    public void Root_returns_native_tab_route(
        TransactionRoleRoute role,
        string expected) =>
        Assert.Equal(expected, AuthenticatedHomeRoutes.Root(role));

    [Theory]
    [InlineData("//main/buying", TransactionRoleRoute.Buying)]
    [InlineData("//main/selling", TransactionRoleRoute.Selling)]
    [InlineData("main/selling/TransactionDetailPage", TransactionRoleRoute.Selling)]
    public void TryParseRoot_recognizes_role_root(
        string route,
        TransactionRoleRoute expected)
    {
        Assert.True(AuthenticatedHomeRoutes.TryParseRoot(route, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("//main/account")]
    [InlineData("ActivityPage")]
    [InlineData(null)]
    public void TryParseRoot_ignores_non_role_destinations(string? route) =>
        Assert.False(AuthenticatedHomeRoutes.TryParseRoot(route, out _));

}
