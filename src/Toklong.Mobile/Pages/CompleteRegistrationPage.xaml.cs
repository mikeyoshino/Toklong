using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Pages;

public partial class CompleteRegistrationPage : ContentPage
{
    private readonly CompleteRegistrationViewModel viewModel;

    public CompleteRegistrationPage(
        CompleteRegistrationViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.InitializeAsync();
    }
}
