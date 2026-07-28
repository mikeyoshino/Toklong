using Toklong.Mobile.Core;
using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Pages;

public partial class AuthenticatedHomePage : ContentPage
{
    private readonly AuthenticatedHomeViewModel viewModel;

    public AuthenticatedHomePage(
        AuthenticatedHomeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.LoadAsync();
    }

    private async void OnBuyingClicked(
        object? sender,
        EventArgs eventArgs) =>
        await Shell.Current.GoToAsync(
            AuthenticatedHomeRoutes.Transactions(
                TransactionRoleRoute.Buying));

    private async void OnSellingClicked(
        object? sender,
        EventArgs eventArgs) =>
        await Shell.Current.GoToAsync(
            AuthenticatedHomeRoutes.Transactions(
                TransactionRoleRoute.Selling));

    private async void OnTransactionsClicked(
        object? sender,
        EventArgs eventArgs) =>
        await Shell.Current.GoToAsync("//transactions");
}
