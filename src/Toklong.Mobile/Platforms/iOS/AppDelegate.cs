using Foundation;
using Toklong.Mobile.Core;
using UIKit;
using UserNotifications;

namespace Toklong.Mobile;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    private static readonly ToklongNotificationDelegate
        NotificationDelegate = new();

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override bool FinishedLaunching(
        UIApplication application,
        NSDictionary? launchOptions)
    {
        var finished = base.FinishedLaunching(
            application,
            launchOptions);
        UNUserNotificationCenter.Current.Delegate =
            NotificationDelegate;
        return finished;
    }

    [Export(
        "application:didRegisterForRemoteNotificationsWithDeviceToken:")]
    public void DidRegisterForRemoteNotifications(
        UIApplication application,
        NSData deviceToken)
    {
        var token = Convert.ToHexString(
                deviceToken.ToArray())
            .ToLowerInvariant();
        var registration =
            IPlatformApplication.Current?.Services
                .GetService<IPushRegistrationService>();
        if (registration is not null)
            _ = UploadTokenSafelyAsync(registration, token);
    }

    public override bool OpenUrl(
        UIApplication application,
        NSUrl url,
        NSDictionary options) =>
        HandleDeepLink(url) ||
        base.OpenUrl(application, url, options);

    public override bool ContinueUserActivity(
        UIApplication application,
        NSUserActivity userActivity,
        UIApplicationRestorationHandler completionHandler) =>
        HandleDeepLink(userActivity.WebPageUrl) ||
        base.ContinueUserActivity(
            application,
            userActivity,
            completionHandler);

    private static bool HandleDeepLink(NSUrl? url)
    {
        if (url is null ||
            !Uri.TryCreate(
                url.AbsoluteString,
                UriKind.Absolute,
                out var uri))
            return false;
        var coordinator = IPlatformApplication.Current?.Services
            .GetService<IDeepLinkCoordinator>();
        if (coordinator is null)
            return false;
        _ = coordinator.HandleAsync(uri);
        return true;
    }

    private static async Task UploadTokenSafelyAsync(
        IPushRegistrationService registration,
        string token)
    {
        try
        {
            await registration.UploadTokenAsync(token);
        }
        catch
        {
            // The in-app inbox remains available. A later app launch asks
            // APNs for registration again without logging the device token.
        }
    }
}

internal sealed class ToklongNotificationDelegate
    : UNUserNotificationCenterDelegate
{
    public override void WillPresentNotification(
        UNUserNotificationCenter center,
        UNNotification notification,
        Action<UNNotificationPresentationOptions>
            completionHandler) =>
        completionHandler(
            UNNotificationPresentationOptions.Banner |
            UNNotificationPresentationOptions.Sound |
            UNNotificationPresentationOptions.Badge);

    public override void DidReceiveNotificationResponse(
        UNUserNotificationCenter center,
        UNNotificationResponse response,
        Action completionHandler)
    {
        try
        {
            var value = response.Notification.Request
                .Content.UserInfo[
                    new NSString("deepLink")]
                ?.ToString();
            if (Uri.TryCreate(
                    value,
                    UriKind.Absolute,
                    out var uri))
            {
                var coordinator =
                    IPlatformApplication.Current?.Services
                        .GetService<IDeepLinkCoordinator>();
                if (coordinator is not null)
                    _ = coordinator.HandleAsync(uri);
            }
        }
        finally
        {
            completionHandler();
        }
    }
}
