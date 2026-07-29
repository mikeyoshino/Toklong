using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Pages;

public partial class VerifyEmailChangePage :
    ContentPage,
    IQueryAttributable
{
    private readonly VerifyEmailChangeViewModel viewModel;

    public VerifyEmailChangePage(
        VerifyEmailChangeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }

    public void ApplyQueryAttributes(
        IDictionary<string, object> query)
    {
        if (query.TryGetValue("Pending", out var value) &&
            value is Core.PendingEmailChange pending)
            viewModel.Apply(pending);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.ErrorPresented += OnErrorPresented;
        viewModel.Activate();
        Dispatcher.DispatchDelayed(
            TimeSpan.FromMilliseconds(250),
            OtpInput.FocusInput);
    }

    protected override void OnDisappearing()
    {
        viewModel.ErrorPresented -= OnErrorPresented;
        viewModel.Deactivate();
        base.OnDisappearing();
    }

    private void OnErrorPresented(
        object? sender,
        Core.EmailChangeErrorNotice notice) =>
        Dispatcher.Dispatch(
            async () =>
                await PresentErrorAsync(notice));

    private async Task PresentErrorAsync(
        Core.EmailChangeErrorNotice notice)
    {
        SemanticScreenReader.Announce(notice.Message);
        var target = notice.Target switch
        {
            Core.EmailChangeErrorTarget.ResendAction =>
                (VisualElement)ResendButton,
            Core.EmailChangeErrorTarget.NewRequestAction =>
                NewRequestButton,
            Core.EmailChangeErrorTarget.AccountReturnAction =>
                ReturnToAccountButton,
            _ => OtpInput
        };
        await EmailChangeScroll.ScrollToAsync(
            target,
            ScrollToPosition.Center,
            true);
        if (notice.Target ==
            Core.EmailChangeErrorTarget.CodeInput)
        {
            OtpInput.FocusInput();
        }
        else
        {
            target.Focus();
        }
    }
}
