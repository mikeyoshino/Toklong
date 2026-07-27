using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Pages;

public partial class PayoutSettingsPage : ContentPage
{
    private readonly PayoutSettingsViewModel viewModel;

    public PayoutSettingsPage(
        PayoutSettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.LoadAsync();
    }
}
