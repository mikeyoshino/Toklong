namespace Toklong.Mobile.Core;

public enum WorkspaceNavigationSource
{
    Startup,
    BottomAction,
    DeepLink
}

public static class WorkspaceNavigationAnalytics
{
    public static MobileAnalyticsEvent Opened(
        TransactionRoleRoute role,
        WorkspaceNavigationSource source) =>
        new(
            "workspace_opened",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["role"] = role switch
                {
                    TransactionRoleRoute.Buying => "buying",
                    TransactionRoleRoute.Selling => "selling",
                    _ => throw new ArgumentOutOfRangeException(nameof(role))
                },
                ["source"] = source switch
                {
                    WorkspaceNavigationSource.Startup => "startup",
                    WorkspaceNavigationSource.BottomAction => "bottom_action",
                    WorkspaceNavigationSource.DeepLink => "deep_link",
                    _ => throw new ArgumentOutOfRangeException(nameof(source))
                }
            });

    public static MobileAnalyticsEvent CreateOfferStarted(RoleFilter role) =>
        new(
            "create_offer_started",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["source_role"] = role switch
                {
                    RoleFilter.Buying => "buying",
                    RoleFilter.Selling => "selling",
                    _ => throw new ArgumentOutOfRangeException(nameof(role))
                }
            });
}
