using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class AuthenticatedHomeRoutesTests
{
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
}
