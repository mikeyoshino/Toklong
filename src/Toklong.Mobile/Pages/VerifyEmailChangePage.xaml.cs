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
        viewModel.Activate();
        Dispatcher.DispatchDelayed(
            TimeSpan.FromMilliseconds(250),
            OtpInput.FocusInput);
    }

    protected override void OnDisappearing()
    {
        viewModel.Deactivate();
        base.OnDisappearing();
    }
}
