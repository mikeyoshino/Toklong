using System.Net.Http.Json;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Services;

public sealed class ApiPushRegistrationClient(
    MobileApiClient api)
{
    private const string InstallationIdKey =
        "toklong.notification.installation-id";

    public async Task UploadAsync(
        string platform,
        string pushToken,
        CancellationToken cancellationToken)
    {
        using var response = await api.SendAuthenticatedAsync(
            () => new HttpRequestMessage(
                HttpMethod.Put,
                "api/mobile/notification-devices/current")
            {
                Content = JsonContent.Create(new
                {
                    InstallationId = GetInstallationId(),
                    Platform = platform,
                    PushToken = pushToken
                })
            },
            cancellationToken);
        await MobileApiClient.EnsureSuccessAsync(
            response,
            cancellationToken);
    }

    public async Task UnregisterAsync(
        CancellationToken cancellationToken)
    {
        if (!Preferences.Default.ContainsKey(
                InstallationIdKey))
            return;
        using var response = await api.SendAuthenticatedAsync(
            () => new HttpRequestMessage(
                HttpMethod.Delete,
                $"api/mobile/notification-devices/current/{GetInstallationId()}"),
            cancellationToken);
        await MobileApiClient.EnsureSuccessAsync(
            response,
            cancellationToken);
    }

    private static string GetInstallationId()
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
