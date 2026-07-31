using Toklong.Mobile.Core;
using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Pages;

public partial class ChangeNamePage : ContentPage, IQueryAttributable
{
    private readonly ChangeNameViewModel viewModel;
    private bool wasParented;

    public ChangeNamePage(ChangeNameViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        query.TryGetValue("FirstName", out var firstName);
        query.TryGetValue("LastName", out var lastName);
        query.TryGetValue("SessionGeneration", out var generation);
        if (firstName is string || lastName is string)
        {
            viewModel.ApplyRouteName(
                firstName as string,
                lastName as string,
                generation is long routeGeneration
                    ? routeGeneration
                    : long.MinValue);
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        viewModel.ErrorPresented += OnErrorPresented;
        viewModel.NameChangeBlocked += OnNameChangeBlocked;
        viewModel.ActionBlocked += OnActionBlocked;
        viewModel.Activate();
        await viewModel.LoadCurrentNameAsync();
    }

    protected override void OnDisappearing()
    {
        viewModel.ErrorPresented -= OnErrorPresented;
        viewModel.NameChangeBlocked -= OnNameChangeBlocked;
        viewModel.ActionBlocked -= OnActionBlocked;
        viewModel.Deactivate();
        base.OnDisappearing();
    }

    protected override void OnParentSet()
    {
        base.OnParentSet();
        if (Parent is not null)
        {
            wasParented = true;
            return;
        }

        if (wasParented)
            viewModel.Dispose();
    }

    private void OnNameChangeBlocked(
        object? sender,
        AccountNameChangeBlockedNotice notice) =>
        PresentModal(AccountNameChangeModalPresenter.Cooldown(notice));

    private void OnActionBlocked(
        object? sender,
        AccountNameChangeModalNotice notice) =>
        PresentModal(notice);

    private void PresentModal(AccountNameChangeModalNotice notice) =>
        Dispatcher.Dispatch(async () =>
        {
            SemanticScreenReader.Announce(notice.Message);
            await DisplayAlertAsync(
                notice.Title,
                notice.Message,
                notice.AcceptText);
        });

    private void OnErrorPresented(
        object? sender,
        AccountNameChangeErrorNotice notice) =>
        Dispatcher.Dispatch(async () =>
            await PresentErrorAsync(notice));

    private async Task PresentErrorAsync(
        AccountNameChangeErrorNotice notice)
    {
        SemanticScreenReader.Announce(notice.Message);
        var target = notice.Target switch
        {
            AccountNameChangeErrorTarget.LastNameInput =>
                (VisualElement)LastNameEntry,
            AccountNameChangeErrorTarget.VerificationAction =>
                SubmitButton,
            _ => FirstNameEntry
        };
        await NameChangeScroll.ScrollToAsync(
            target,
            ScrollToPosition.Center,
            true);
        target.Focus();
    }
}
