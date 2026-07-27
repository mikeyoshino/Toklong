using Toklong.Mobile.Core;

namespace Toklong.Mobile;

public partial class App : Application
{
    private readonly AppShell shell;
    private readonly IAuthenticationService authentication;
    private readonly IDeepLinkCoordinator deepLinks;
    private readonly IPushRegistrationService pushRegistration;

    public App(
        AppShell shell,
        IAuthenticationService authentication,
        IDeepLinkCoordinator deepLinks,
        IPushRegistrationService pushRegistration)
    {
        InitializeComponent();
        this.shell = shell;
        this.authentication = authentication;
        this.deepLinks = deepLinks;
        this.pushRegistration = pushRegistration;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(shell);
#if MACCATALYST
        // Keep the debug desktop preview close to a real phone viewport so
        // visual reviews do not accidentally optimize for a wide Mac window.
        window.Width = 440;
        window.Height = 790;
        window.X = 280;
        window.Y = 35;
#endif
        window.Created += async (_, _) => await OpenInitialRouteAsync();
        return window;
    }

    private async Task OpenInitialRouteAsync()
    {
        if (await authentication.HasSessionAsync())
        {
            await Shell.Current.GoToAsync("//transactions");
            await pushRegistration.InitializeAsync();
            await deepLinks.ResumePendingAsync();
        }
    }
}
