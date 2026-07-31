using Toklong.Mobile.Core;
using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Pages;

public partial class VerifyNameChangePage :
    ContentPage,
    IQueryAttributable
{
    private readonly VerifyNameChangeViewModel viewModel;
    private bool wasParented;

    public VerifyNameChangePage(VerifyNameChangeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("Pending", out var value) &&
            value is PendingAccountNameChange pending)
        {
            viewModel.ApplyRoutePending(
                pending,
                query.TryGetValue("SessionGeneration", out var generation) &&
                generation is long routeGeneration
                    ? routeGeneration
                    : long.MinValue);
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        viewModel.ErrorPresented += OnErrorPresented;
        viewModel.ActionBlocked += OnActionBlocked;
        viewModel.Activate();
        await viewModel.LoadPendingAfterResetAsync();
        if (viewModel.CanUseChallenge)
        {
            Dispatcher.DispatchDelayed(
                TimeSpan.FromMilliseconds(250),
                () =>
                {
                    if (viewModel.CanUseChallenge)
                        OtpForm.FocusInput();
                });
        }
    }

    protected override void OnDisappearing()
    {
        viewModel.ErrorPresented -= OnErrorPresented;
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

    private void OnActionBlocked(
        object? sender,
        AccountNameChangeModalNotice notice) =>
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
            AccountNameChangeErrorTarget.ResendAction =>
                (VisualElement)ResendButton,
            AccountNameChangeErrorTarget.NewRequestAction =>
                NewRequestButton,
            AccountNameChangeErrorTarget.AccountReturnAction =>
                ReturnToAccountButton,
            _ => OtpForm
        };
        await NameVerificationScroll.ScrollToAsync(
            target,
            ScrollToPosition.Center,
            true);
        if (notice.Target == AccountNameChangeErrorTarget.CodeInput)
            OtpForm.FocusInput();
        else
            target.Focus();
    }
}
