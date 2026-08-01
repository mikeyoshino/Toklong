namespace Toklong.Mobile.Core;

public interface IWorkspaceRolePreference
{
    TransactionRoleRoute GetPreferredRole();
    void SavePreferredRole(TransactionRoleRoute role);
    void Clear();
}

public sealed class WorkspaceRolePreference : IWorkspaceRolePreference
{
    private const string Key = "workspace.preferred-role";

    public WorkspaceRolePreference(AuthenticatedSessionBoundary session) =>
        session.ResetRequested += (_, _) => Clear();

    public TransactionRoleRoute GetPreferredRole() =>
        Preferences.Default.Get(Key, "buying") == "selling"
            ? TransactionRoleRoute.Selling
            : TransactionRoleRoute.Buying;

    public void SavePreferredRole(TransactionRoleRoute role) =>
        Preferences.Default.Set(Key, role switch
        {
            TransactionRoleRoute.Buying => "buying",
            TransactionRoleRoute.Selling => "selling",
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        });

    public void Clear() => Preferences.Default.Remove(Key);
}
