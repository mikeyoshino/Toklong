using System.Net.Http.Json;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Services;

public sealed class ApiPushRegistrationClient(
    MobileApiClient api,
    IInstallationIdProvider installationIds)
{
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
                    InstallationId =
                        installationIds.GetInstallationId(),
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
        using var response = await api.SendAuthenticatedAsync(
            () => new HttpRequestMessage(
                HttpMethod.Delete,
                $"api/mobile/notification-devices/current/{installationIds.GetInstallationId()}"),
            cancellationToken);
        await MobileApiClient.EnsureSuccessAsync(
            response,
            cancellationToken);
    }

}
