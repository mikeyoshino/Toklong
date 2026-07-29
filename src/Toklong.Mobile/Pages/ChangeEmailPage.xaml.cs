using Toklong.Mobile.Core;
using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Pages;

public partial class ChangeEmailPage : ContentPage
{
    private readonly ChangeEmailViewModel viewModel;

    public ChangeEmailPage(ChangeEmailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.ErrorPresented += OnErrorPresented;
        viewModel.Activate();
    }

    protected override void OnDisappearing()
    {
        viewModel.ErrorPresented -= OnErrorPresented;
        viewModel.Deactivate();
        base.OnDisappearing();
    }

    private void OnErrorPresented(
        object? sender,
        EmailChangeErrorNotice notice) =>
        Dispatcher.Dispatch(
            async () =>
                await PresentErrorAsync(notice));

    private async Task PresentErrorAsync(
        EmailChangeErrorNotice notice)
    {
        SemanticScreenReader.Announce(notice.Message);
        await EmailChangeScroll.ScrollToAsync(
            NewEmailEntry,
            ScrollToPosition.Center,
            true);
        NewEmailEntry.Focus();
    }
}
