using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Pages;

public partial class AccountPage : ContentPage
{
    private readonly AccountViewModel viewModel;

    public AccountPage(AccountViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.LoadAsync();
    }

    protected override void OnDisappearing()
    {
        viewModel.DismissSuccessMessage();
        base.OnDisappearing();
    }

}
