using Toklong.Mobile.Core;

namespace Toklong.Mobile.Pages;

public partial class StartupLogoPage : ContentPage
{
    public StartupLogoPage(
        IStartupMotionPreference motionPreference)
    {
        InitializeComponent();

        if (motionPreference.IsReducedMotionEnabled)
            Mark.ShowCompletedState();
        else
            Mark.ShowInitialState();
    }

    public Task PlayAsync(
        CancellationToken cancellationToken = default) =>
        Mark.PlayAsync(cancellationToken);

    public void ShowCompletedState() =>
        Mark.ShowCompletedState();

    public void CancelMotion() =>
        Mark.CancelMotion();
}
