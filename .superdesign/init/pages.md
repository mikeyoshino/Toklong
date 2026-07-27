# Mobile page dependency trees

## Create Offer

Entry: `src/Toklong.Mobile/Pages/CreateOfferPage.xaml`

Dependencies:
- `src/Toklong.Mobile/App.xaml`
- `src/Toklong.Mobile/AppShell.xaml`
- `src/Toklong.Mobile/Pages/CreateOfferPage.xaml.cs`
  - `src/Toklong.Mobile/ViewModels/CreateOfferViewModel.cs`
    - `src/Toklong.Mobile/Core/BuyerCostPreview.cs`
    - `src/Toklong.Mobile/Core/ISellerOfferService.cs`
    - `src/Toklong.Mobile/Core/IAddressService.cs`
    - `src/Toklong.Mobile/Core/IAgreementDraftService.cs`
    - `src/Toklong.Mobile/Core/IDraftPhotoStore.cs`
    - `src/Toklong.Mobile/Core/QuickDealSnapshotFields.cs`
- `src/Toklong.Mobile/Controls/FormLabelView.xaml`
- `src/Toklong.Mobile/Controls/ThaiMobilePhoneEntry.cs`
- `src/Toklong.Mobile/Resources/Images/ui_offer.svg`
- `src/Toklong.Mobile/Resources/Images/ui_ai_assist.svg`
- `src/Toklong.Mobile/Resources/Images/ui_camera.svg`
- `src/Toklong.Mobile/Resources/Images/ui_note.svg`
- `src/Toklong.Mobile/Resources/Images/ui_money.svg`
- `src/Toklong.Mobile/Resources/Images/ui_location.svg`

## Transactions

Entry: `src/Toklong.Mobile/Pages/TransactionsPage.xaml`

Dependencies:
- `src/Toklong.Mobile/App.xaml`
- `src/Toklong.Mobile/AppShell.xaml`
- `src/Toklong.Mobile/Pages/TransactionsPage.xaml.cs`
- `src/Toklong.Mobile/ViewModels/TransactionsViewModel.cs`
- `src/Toklong.Mobile/Core/TransactionStatePresenter.cs`
- `src/Toklong.Mobile/Core/TransactionFilter.cs`

## Transaction Detail

Entry: `src/Toklong.Mobile/Pages/TransactionDetailPage.xaml`

Dependencies:
- `src/Toklong.Mobile/App.xaml`
- `src/Toklong.Mobile/Pages/TransactionDetailPage.xaml.cs`
- `src/Toklong.Mobile/ViewModels/TransactionDetailViewModel.cs`
- `src/Toklong.Mobile/Core/AppTransaction.cs`
- shared transaction and fulfillment image assets

## Seller Offer

Entry: `src/Toklong.Mobile/Pages/SellerOfferPage.xaml`

Dependencies:
- `src/Toklong.Mobile/App.xaml`
- `src/Toklong.Mobile/Pages/SellerOfferPage.xaml.cs`
- `src/Toklong.Mobile/ViewModels/SellerOfferViewModel.cs`
- `src/Toklong.Mobile/Core/SellerOfferLink.cs`

## Authentication

Entries:
- `src/Toklong.Mobile/Pages/WelcomePage.xaml`
- `src/Toklong.Mobile/Pages/SignInPage.xaml`
- `src/Toklong.Mobile/Pages/SignUpPage.xaml`
- `src/Toklong.Mobile/Pages/VerifyCodePage.xaml`

Dependencies:
- `src/Toklong.Mobile/App.xaml`
- `src/Toklong.Mobile/Controls/BrandLockupView.xaml`
- `src/Toklong.Mobile/Controls/FormLabelView.xaml`
- `src/Toklong.Mobile/Controls/OtpCodeInput.xaml`
- corresponding page code-behind and ViewModel files
