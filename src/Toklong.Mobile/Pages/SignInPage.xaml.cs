using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Pages;

public partial class SignInPage : ContentPage
{
    public SignInPage(SignInViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private async void OnBackClicked(
        object? sender,
        EventArgs eventArgs) =>
        await Shell.Current.GoToAsync("//welcome");
}
