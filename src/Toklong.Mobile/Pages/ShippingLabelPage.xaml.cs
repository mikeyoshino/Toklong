using Toklong.Mobile.Core;
using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Pages;

public partial class ShippingLabelPage :
    ContentPage,
    IQueryAttributable
{
    private readonly ShippingLabelViewModel viewModel;
    private bool previousKeepScreenOn;
    private bool keepScreenStateCaptured;

    public ShippingLabelPage(
        ShippingLabelViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }

    public async void ApplyQueryAttributes(
        IDictionary<string, object> query)
    {
        if (query.TryGetValue(
                "TransactionId",
                out var rawId) &&
            Guid.TryParse(
                rawId?.ToString(),
                out var transactionId))
        {
            var isReturn =
                query.TryGetValue(
                    "IsReturn",
                    out var rawReturn) &&
                bool.TryParse(
                    rawReturn?.ToString(),
                    out var parsedReturn) &&
                parsedReturn;
            await viewModel.LoadAsync(
                transactionId,
                isReturn);
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            previousKeepScreenOn =
                DeviceDisplay.Current.KeepScreenOn;
            keepScreenStateCaptured = true;
            DeviceDisplay.Current.KeepScreenOn = true;
        }
        catch (NotSupportedException)
        {
        }
    }

    protected override void OnDisappearing()
    {
        if (keepScreenStateCaptured)
        {
            try
            {
                DeviceDisplay.Current.KeepScreenOn =
                    previousKeepScreenOn;
            }
            catch (NotSupportedException)
            {
            }
            keepScreenStateCaptured = false;
        }
        base.OnDisappearing();
    }

    private void OnLabelNavigating(
        object? sender,
        WebNavigatingEventArgs eventArgs)
    {
        eventArgs.Cancel =
            ShippingLabelNavigationPolicy.ShouldCancel(
                eventArgs.Url);
    }
}
