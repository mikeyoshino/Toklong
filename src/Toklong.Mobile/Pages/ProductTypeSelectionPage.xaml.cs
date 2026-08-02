using Toklong.Mobile.Core;

namespace Toklong.Mobile.Pages;

public partial class ProductTypeSelectionPage : ContentPage
{
    private readonly IMobileAnalytics analytics;
    private bool isNavigating;

    public ProductTypeSelectionPage(IMobileAnalytics analytics)
    {
        InitializeComponent();
        this.analytics = analytics;
    }

    private async void OnBackClicked(
        object? sender,
        EventArgs eventArgs) =>
        await Shell.Current.GoToAsync("..");

    private async void OnPhysicalClicked(
        object? sender,
        EventArgs eventArgs) =>
        await OpenCreateOfferAsync(AppFulfillmentType.Physical);

    private async void OnGameAccountClicked(
        object? sender,
        EventArgs eventArgs) =>
        await OpenCreateOfferAsync(AppFulfillmentType.Digital);

    private async Task OpenCreateOfferAsync(AppFulfillmentType type)
    {
        if (isNavigating)
            return;

        isNavigating = true;
        try
        {
            analytics.Track(CreateOfferAnalytics.TypeSelected(type));
            await Shell.Current.GoToAsync(
                nameof(CreateOfferPage),
                new Dictionary<string, object>
                {
                    [CreateOfferPage.FulfillmentTypeQueryKey] = type
                });
        }
        finally
        {
            isNavigating = false;
        }
    }
}
