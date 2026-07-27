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
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName ==
                nameof(CreateOfferViewModel.IsCostPreviewSheetOpen))
            {
                if (viewModel.IsCostPreviewSheetOpen)
                    Dispatcher.Dispatch(() =>
                    {
                        AmountEntry.Unfocus();
                        CostPreviewCloseButton.Focus();
                    });
                else if (viewModel.HasCostPreview)
                    Dispatcher.Dispatch(() =>
                        CostPreviewSummaryBar.Focus());
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.LoadAsync();
        viewModel.ResumeCostPreview();
    }

    protected override void OnDisappearing()
    {
        viewModel.CancelCostPreview();
        viewModel.DiscardAiSource();
        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed()
    {
        if (!viewModel.IsCostPreviewSheetOpen)
            return base.OnBackButtonPressed();

        viewModel.CloseCostPreviewCommand.Execute(null);
        return true;
    }
}
