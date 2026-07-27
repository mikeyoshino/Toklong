using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Pages;

public partial class SellerOfferPage : ContentPage, IQueryAttributable
{
    private readonly SellerOfferViewModel viewModel;

    public SellerOfferPage(SellerOfferViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }

    public async void ApplyQueryAttributes(
        IDictionary<string, object> query)
    {
        if (query.TryGetValue("PublicToken", out var rawToken))
            await viewModel.LoadAsync(rawToken?.ToString() ?? "");
    }
}
