using Toklong.Mobile.Core;
using Toklong.Mobile.Services;
using UIKit;
using UserNotifications;

namespace Toklong.Mobile;

public sealed class IosPushRegistrationService(
    ApiPushRegistrationClient api)
    : IPushRegistrationService
{
    public Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        if (DeviceInfo.DeviceType == DeviceType.Virtual)
            return Task.CompletedTask;

        var completion =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        UNUserNotificationCenter.Current.RequestAuthorization(
            UNAuthorizationOptions.Alert |
            UNAuthorizationOptions.Badge |
            UNAuthorizationOptions.Sound,
            (approved, _) =>
            {
                if (approved)
                    MainThread.BeginInvokeOnMainThread(
                        UIApplication.SharedApplication
                            .RegisterForRemoteNotifications);
                completion.TrySetResult();
            });
        cancellationToken.Register(
            () => completion.TrySetCanceled(
                cancellationToken));
        return completion.Task;
    }

    public Task UploadTokenAsync(
        string pushToken,
        CancellationToken cancellationToken = default) =>
        api.UploadAsync(
            "ios",
            pushToken,
            cancellationToken);

    public Task UnregisterAsync(
        CancellationToken cancellationToken = default) =>
        api.UnregisterAsync(cancellationToken);
}
