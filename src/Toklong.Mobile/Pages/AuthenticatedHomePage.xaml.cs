using Toklong.Mobile.Core;

namespace Toklong.Mobile.Pages;

public partial class AuthenticatedHomePage : ContentPage
{
    public AuthenticatedHomePage() => InitializeComponent();

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
