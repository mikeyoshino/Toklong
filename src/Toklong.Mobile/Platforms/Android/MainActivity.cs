using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Toklong.Mobile.Core;

namespace Toklong.Mobile;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges =
        ConfigChanges.ScreenSize |
        ConfigChanges.Orientation |
        ConfigChanges.UiMode |
        ConfigChanges.ScreenLayout |
        ConfigChanges.SmallestScreenSize |
        ConfigChanges.Density)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "toklong",
    DataHost = "checkout-return")]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "https",
    DataHost = "app.toklong.co.th",
    DataPathPrefix = "/mobile/stripe-return",
    AutoVerify = true)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "toklong",
    DataHost = "offer")]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "https",
    DataHost = "app.toklong.co.th",
    DataPathPrefix = "/offer/",
    AutoVerify = true)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "https",
    DataHost = "toklong.co.th",
    DataPathPrefix = "/offer/",
    AutoVerify = true)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "https",
    DataHost = "www.toklong.co.th",
    DataPathPrefix = "/offer/",
    AutoVerify = true)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        HandleDeepLink(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;
        HandleDeepLink(intent);
    }

    private static void HandleDeepLink(Intent? intent)
    {
        if (!Uri.TryCreate(
                intent?.DataString,
                UriKind.Absolute,
                out var uri))
            return;
        var coordinator = IPlatformApplication.Current?.Services
            .GetService<IDeepLinkCoordinator>();
        if (coordinator is not null)
            _ = coordinator.HandleAsync(uri);
    }
}
