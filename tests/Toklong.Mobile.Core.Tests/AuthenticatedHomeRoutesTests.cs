using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class AuthenticatedHomeRoutesTests
{
    [Fact]
    public void Default_opens_buyer_workspace() =>
        Assert.Equal(
            AuthenticatedHomeRoutes.Buying,
            AuthenticatedHomeRoutes.Default);

    [Theory]
    [InlineData(TransactionRoleRoute.Buying, "//buying")]
    [InlineData(TransactionRoleRoute.Selling, "//selling")]
    public void Root_returns_hidden_shell_root(
        TransactionRoleRoute role,
        string expected) =>
        Assert.Equal(expected, AuthenticatedHomeRoutes.Root(role));

    [Theory]
    [InlineData("//buying", TransactionRoleRoute.Buying)]
    [InlineData("//selling", TransactionRoleRoute.Selling)]
    [InlineData("selling/TransactionDetailPage", TransactionRoleRoute.Selling)]
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
    [InlineData("//main/account/ActivityPage?next=/selling")]
    [InlineData("//main/buying-tools")]
    [InlineData("//main/reselling")]
    [InlineData("//main/account#return=/buying")]
    [InlineData("//main/buying")]
    [InlineData("//main/selling")]
    [InlineData(null)]
    public void TryParseRoot_ignores_non_role_destinations(string? route) =>
        Assert.False(AuthenticatedHomeRoutes.TryParseRoot(route, out _));

}
