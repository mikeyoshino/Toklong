using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class WorkspaceRolePreferenceTests : IDisposable
{
    private readonly AuthenticatedSessionBoundary session = new();

    public WorkspaceRolePreferenceTests() => Preferences.Default.Clear();

    [Fact]
    public void Missing_or_invalid_value_falls_back_to_buying()
    {
        var preference = new WorkspaceRolePreference(session);
        Assert.Equal(
            TransactionRoleRoute.Buying,
            preference.GetPreferredRole());

        Preferences.Default.Set("workspace.preferred-role", "invalid");
        Assert.Equal(
            TransactionRoleRoute.Buying,
            preference.GetPreferredRole());
    }

    [Fact]
    public void Save_and_clear_round_trip_only_supported_roles()
    {
        var preference = new WorkspaceRolePreference(session);
        preference.SavePreferredRole(TransactionRoleRoute.Selling);
        Assert.Equal(
            TransactionRoleRoute.Selling,
            preference.GetPreferredRole());

        session.Reset();
        Assert.Equal(
            TransactionRoleRoute.Buying,
            preference.GetPreferredRole());
    }

    public void Dispose() => Preferences.Default.Clear();
}
