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

    [Theory]
    [InlineData(TransactionRoleRoute.Buying, "//transactions?role=buying")]
    [InlineData(TransactionRoleRoute.Selling, "//transactions?role=selling")]
    public void Transactions_builds_explicit_role_route(
        TransactionRoleRoute role,
        string expected) =>
        Assert.Equal(expected, AuthenticatedHomeRoutes.Transactions(role));

    [Theory]
    [InlineData("buying", TransactionRoleRoute.Buying)]
    [InlineData("selling", TransactionRoleRoute.Selling)]
    public void TryParseRole_accepts_only_canonical_values(
        string value,
        TransactionRoleRoute expected)
    {
        Assert.True(
            AuthenticatedHomeRoutes.TryParseRole(value, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("buyer")]
    [InlineData("BUYING")]
    public void TryParseRole_rejects_missing_or_noncanonical_values(
        string? value) =>
        Assert.False(
            AuthenticatedHomeRoutes.TryParseRole(value, out _));

    [Theory]
    [InlineData(TransactionRoleRoute.Buying, RoleFilter.Buying)]
    [InlineData(TransactionRoleRoute.Selling, RoleFilter.Selling)]
    public void TransactionFilter_maps_navigation_role_to_visible_mode(
        TransactionRoleRoute route,
        RoleFilter expected) =>
        Assert.Equal(
            expected,
            AuthenticatedHomeRoutes.ToRoleFilter(route));
}
