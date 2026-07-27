using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Pages;

public partial class SignUpPage : ContentPage
{
    public SignUpPage(SignUpViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private async void OnBackClicked(
        object? sender,
        EventArgs eventArgs) =>
        await Shell.Current.GoToAsync("..");

    private async void OnOpenSignInClicked(
        object? sender,
        EventArgs eventArgs) =>
        await Shell.Current.GoToAsync("//signin");
}
