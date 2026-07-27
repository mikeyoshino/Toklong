using System.Net.Http.Json;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Services;

public sealed class ApiNotificationService(
    MobileApiClient api) : INotificationService
{
    public async Task<IReadOnlyList<AppNotification>>
        GetNotificationsAsync(
            CancellationToken cancellationToken = default)
    {
        using var response = await api.SendAuthenticatedAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                "api/mobile/notifications"),
            cancellationToken);
        await MobileApiClient.EnsureSuccessAsync(
            response,
            cancellationToken);
        return await response.Content
                   .ReadFromJsonAsync<
                       IReadOnlyList<AppNotification>>(
                       cancellationToken: cancellationToken) ??
               [];
    }
}
