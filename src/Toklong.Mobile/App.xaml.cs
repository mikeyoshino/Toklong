using Microsoft.Extensions.Logging;
using Toklong.Mobile.Core;
using Toklong.Mobile.Pages;

namespace Toklong.Mobile;

public partial class App : Application
{
    private readonly AppShell shell;
    private readonly IDeepLinkCoordinator deepLinks;
    private readonly IPushRegistrationService pushRegistration;
    private readonly StartupLogoPage startupPage;
    private readonly StartupCoordinator startupCoordinator;
    private readonly ILogger<App> logger;
    private readonly CancellationTokenSource startupCancellation = new();
    private int startupStarted;

    public App(
        AppShell shell,
        IDeepLinkCoordinator deepLinks,
        IPushRegistrationService pushRegistration,
        StartupLogoPage startupPage,
        StartupCoordinator startupCoordinator,
        ILogger<App> logger)
    {
        InitializeComponent();
        this.shell = shell;
        this.deepLinks = deepLinks;
        this.pushRegistration = pushRegistration;
        this.startupPage = startupPage;
        this.startupCoordinator = startupCoordinator;
        this.logger = logger;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(startupPage);
#if MACCATALYST
        // Keep the debug desktop preview close to a real phone viewport so
        // visual reviews do not accidentally optimize for a wide Mac window.
        window.Width = 440;
        window.Height = 790;
        window.X = 280;
        window.Y = 35;
#endif
        window.Created += async (_, _) =>
            await OpenInitialRouteAsync(window);
        window.Destroying += (_, _) =>
        {
            startupCancellation.Cancel();
            startupPage.CancelMotion();
        };
        return window;
    }

    private async Task OpenInitialRouteAsync(Window window)
    {
        if (Interlocked.Exchange(ref startupStarted, 1) != 0)
            return;

        try
        {
            var result = await startupCoordinator.StartAsync(
                startupPage.PlayAsync,
                startupCancellation.Token);
            if (result.SessionError is not null)
            {
                logger.LogWarning(
                    result.SessionError,
                    "Mobile session lookup failed during startup.");
            }
            if (result.PendingRegistrationError is not null)
            {
                logger.LogWarning(
                    result.PendingRegistrationError,
                    "Pending mobile registration lookup failed during startup.");
            }

            window.Page = shell;
            await shell.GoToAsync(result.Route, false);
            if (result.Route == "//transactions")
                _ = InitializeAuthenticatedServicesAsync();
        }
        catch (OperationCanceledException)
        {
            // Window destruction cancels startup without installing another root.
        }
    }

    private async Task InitializeAuthenticatedServicesAsync()
    {
        try
        {
            await pushRegistration.InitializeAsync();
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Push registration did not complete during startup.");
        }

        try
        {
            await deepLinks.ResumePendingAsync();
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Pending deep-link navigation did not complete during startup.");
        }
    }
}
