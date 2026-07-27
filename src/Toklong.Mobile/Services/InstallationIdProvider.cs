using Toklong.Mobile.Core;

namespace Toklong.Mobile.Services;

public sealed class InstallationIdProvider
    : IInstallationIdProvider
{
    private const string InstallationIdKey =
        "toklong.notification.installation-id";

    public string GetInstallationId()
    {
        var existing = Preferences.Default.Get(
            InstallationIdKey,
            "");
        if (Guid.TryParse(existing, out var id) &&
            id != Guid.Empty)
            return id.ToString("N");

        var created = Guid.NewGuid().ToString("N");
        Preferences.Default.Set(
            InstallationIdKey,
            created);
        return created;
    }
}
