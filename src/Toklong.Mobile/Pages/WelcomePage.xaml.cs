namespace Toklong.Mobile.Pages;

public partial class WelcomePage : ContentPage
{
    public WelcomePage()
    {
        InitializeComponent();
    }

    private async void OnSignInClicked(
        object? sender,
        EventArgs eventArgs) =>
        await Shell.Current.GoToAsync("//signin");

    private async void OnSignUpClicked(
        object? sender,
        EventArgs eventArgs) =>
        await Shell.Current.GoToAsync(nameof(SignUpPage));
}
