using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Pages;

public partial class VerifyCodePage : ContentPage, IQueryAttributable
{
    private readonly VerifyCodeViewModel viewModel;

    public VerifyCodePage(VerifyCodeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("Request", out var value) &&
            value is VerificationRequest request)
            viewModel.Apply(request);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Dispatcher.DispatchDelayed(
            TimeSpan.FromMilliseconds(250),
            OtpInput.FocusInput);
    }

    private async void OnBackClicked(
        object? sender,
        EventArgs eventArgs) =>
        await Shell.Current.GoToAsync("..");
}
