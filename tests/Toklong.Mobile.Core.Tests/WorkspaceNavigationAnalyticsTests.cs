using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class WorkspaceNavigationAnalyticsTests
{
    [Theory]
    [InlineData(
        TransactionRoleRoute.Buying,
        WorkspaceNavigationSource.Startup,
        "buying",
        "startup")]
    [InlineData(
        TransactionRoleRoute.Selling,
        WorkspaceNavigationSource.BottomAction,
        "selling",
        "bottom_action")]
    [InlineData(
        TransactionRoleRoute.Buying,
        WorkspaceNavigationSource.DeepLink,
        "buying",
        "deep_link")]
    public void Opened_contains_only_the_role_and_approved_source(
        TransactionRoleRoute role,
        WorkspaceNavigationSource source,
        string expectedRole,
        string expectedSource)
    {
        var value = WorkspaceNavigationAnalytics.Opened(role, source);

        Assert.Equal("workspace_opened", value.Name);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["role"] = expectedRole,
                ["source"] = expectedSource
            },
            value.Properties);
    }

    [Theory]
    [InlineData(RoleFilter.Buying, "buying")]
    [InlineData(RoleFilter.Selling, "selling")]
    public void Create_offer_started_contains_only_the_source_role(
        RoleFilter role,
        string expectedRole)
    {
        var value = WorkspaceNavigationAnalytics.CreateOfferStarted(role);

        Assert.Equal("create_offer_started", value.Name);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["source_role"] = expectedRole
            },
            value.Properties);
    }
}
