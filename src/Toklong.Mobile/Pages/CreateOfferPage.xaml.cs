using Toklong.Mobile.Core;
using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Pages;

public partial class CreateOfferPage : ContentPage
{
    private readonly CreateOfferViewModel viewModel;
    private bool isHandlingBack;

    public CreateOfferPage(CreateOfferViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        viewModel.ValidationFailed += OnValidationFailed;
        await viewModel.LoadAsync();
    }

    protected override void OnDisappearing()
    {
        viewModel.ValidationFailed -= OnValidationFailed;
        viewModel.CancelReviewPricing();
        viewModel.DiscardAiSource();
        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed()
    {
        Dispatcher.Dispatch(
            async () => await HandleBackAsync());
        return true;
    }

    private async void OnBackClicked(
        object? sender,
        EventArgs eventArgs)
        => await HandleBackAsync();

    private async Task HandleBackAsync()
    {
        if (isHandlingBack)
            return;

        isHandlingBack = true;
        try
        {
            if (viewModel.IsAiSheetOpen)
            {
                viewModel.CloseAiSheetCommand.Execute(null);
                return;
            }

            if (viewModel.CurrentStep != CreateOfferStep.Deal)
            {
                viewModel.PreviousStepCommand.Execute(null);
                return;
            }

            if (!viewModel.IsWizardDirty)
            {
                await Shell.Current.GoToAsync("..");
                return;
            }

            var keepEditing = await DisplayAlertAsync(
                CreateOfferExitPrompt.Title,
                CreateOfferExitPrompt.Message,
                CreateOfferExitPrompt.KeepEditing,
                CreateOfferExitPrompt.Discard);
            if (!keepEditing)
            {
                viewModel.DiscardDraft();
                await Shell.Current.GoToAsync("..");
            }
        }
        finally
        {
            isHandlingBack = false;
        }
    }

    private void OnValidationFailed(
        object? sender,
        CreateOfferValidationTarget target) =>
        Dispatcher.Dispatch(
            async () => await FocusValidationTargetAsync(target));

    private async Task FocusValidationTargetAsync(
        CreateOfferValidationTarget target)
    {
        (ScrollView scroll, VisualElement element, string message) =
            target switch
        {
            CreateOfferValidationTarget.SellerPhone =>
                (DealStepScroll, (VisualElement)SellerPhoneEntry,
                    viewModel.SellerPhoneError),
            CreateOfferValidationTarget.ProductName =>
                (DealStepScroll, (VisualElement)ProductNameEntry,
                    viewModel.ProductNameError),
            CreateOfferValidationTarget.ProductPhoto =>
                (DealStepScroll, (VisualElement)ProductPhotoButton,
                    viewModel.ProductPhotoError),
            CreateOfferValidationTarget.Amount =>
                (DealStepScroll, (VisualElement)AmountEntry,
                    viewModel.AmountError),
            CreateOfferValidationTarget.DeliveryAddress =>
                (FulfillmentStepScroll, (VisualElement)AddressLineEntry,
                    viewModel.DeliveryAddressError),
            CreateOfferValidationTarget.Condition =>
                (ReviewStepScroll, (VisualElement)NewConditionButton,
                    viewModel.ConditionError),
            CreateOfferValidationTarget.KnownDefects =>
                (ReviewStepScroll, (VisualElement)KnownDefectsEditor,
                    viewModel.KnownDefectsError),
            _ =>
                (ReviewStepScroll, (VisualElement)SubmitReviewedOfferButton,
                    viewModel.Message)
        };

        if (!string.IsNullOrWhiteSpace(message))
            SemanticScreenReader.Announce(message);
        await scroll.ScrollToAsync(
            element,
            ScrollToPosition.Center,
            true);
        element.Focus();
    }
}
