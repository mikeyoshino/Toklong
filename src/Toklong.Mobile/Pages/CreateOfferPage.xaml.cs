using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Pages;

public partial class CreateOfferPage : ContentPage
{
    private readonly CreateOfferViewModel viewModel;

    public CreateOfferPage(CreateOfferViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.LoadAsync();
    }

    protected override void OnDisappearing()
    {
        viewModel.CancelReviewPricing();
        viewModel.DiscardAiSource();
        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed()
    {
        if (viewModel.IsReviewSheetOpen)
        {
            viewModel.CloseReviewCommand.Execute(null);
            return true;
        }

        if (viewModel.IsAiSheetOpen)
        {
            viewModel.CloseAiSheetCommand.Execute(null);
            return true;
        }

        return base.OnBackButtonPressed();
    }

    private async void OnBackClicked(
        object? sender,
        EventArgs eventArgs)
    {
        if (OnBackButtonPressed())
            return;

        await Shell.Current.GoToAsync("..");
    }
}
