# Mobile routes

Framework routing: .NET MAUI Shell. Routes are declared in `AppShell.xaml` and registered for pushed pages in `AppShell.xaml.cs`.

| Route | Page | Layout |
|---|---|---|
| `welcome` | `Pages/WelcomePage.xaml` | full-screen, no nav |
| `signin` | `Pages/SignInPage.xaml` | full-screen, no nav |
| `signup` | `Pages/SignUpPage.xaml` | pushed auth page |
| `verify-code` | `Pages/VerifyCodePage.xaml` | pushed auth page |
| `//main/transactions` | `Pages/TransactionsPage.xaml` | bottom tab shell |
| `//main/activity` | `Pages/ActivityPage.xaml` | bottom tab shell |
| `//main/account` | `Pages/AccountPage.xaml` | bottom tab shell |
| `create-offer` | `Pages/CreateOfferPage.xaml` | pushed screen, bottom bar hidden |
| `seller-offer` | `Pages/SellerOfferPage.xaml` | pushed screen |
| `transaction-detail` | `Pages/TransactionDetailPage.xaml` | pushed screen |
| `payout-settings` | `Pages/PayoutSettingsPage.xaml` | pushed screen |
| `shipping-label` | `Pages/ShippingLabelPage.xaml` | pushed screen |

## Create Offer

Entry: `src/Toklong.Mobile/Pages/CreateOfferPage.xaml`

This is a buyer-first mobile form. It keeps physical/digital product switching, seller phone, item name, optional product photo, optional agreement details, integer-satang item pricing, physical delivery address, AI-assisted drafting, validation, review, and final submission. The current review layer also asks for condition and known defects.
