using Toklong.Mobile.ViewModels;
using Toklong.Mobile.Core;

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
        viewModel.NameChangeBlocked += OnNameChangeBlocked;
        await viewModel.LoadAsync();
    }

    protected override void OnDisappearing()
    {
        viewModel.NameChangeBlocked -= OnNameChangeBlocked;
        viewModel.DismissSuccessMessage();
        base.OnDisappearing();
    }

    private void OnNameChangeBlocked(
        object? sender,
        AccountNameChangeBlockedNotice notice) =>
        Dispatcher.Dispatch(async () =>
        {
            var modal = AccountNameChangeModalPresenter.Cooldown(notice);
            SemanticScreenReader.Announce(modal.Message);
            await DisplayAlertAsync(
                modal.Title,
                modal.Message,
                modal.AcceptText);
        });

}
