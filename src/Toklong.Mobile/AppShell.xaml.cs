using Toklong.Mobile.Pages;
using Toklong.Mobile.Core;

namespace Toklong.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(ActivityPage), typeof(ActivityPage));
        Routing.RegisterRoute(nameof(AccountPage), typeof(AccountPage));
        Routing.RegisterRoute(nameof(SignUpPage), typeof(SignUpPage));
        Routing.RegisterRoute(nameof(VerifyCodePage), typeof(VerifyCodePage));
        Routing.RegisterRoute(
            AuthenticationRoutes.CompleteRegistration,
            typeof(CompleteRegistrationPage));
        Routing.RegisterRoute(nameof(TransactionDetailPage), typeof(TransactionDetailPage));
        Routing.RegisterRoute(nameof(ShippingLabelPage), typeof(ShippingLabelPage));
        Routing.RegisterRoute(nameof(CounterQrPage), typeof(CounterQrPage));
        Routing.RegisterRoute(
            nameof(ProductTypeSelectionPage),
            typeof(ProductTypeSelectionPage));
        Routing.RegisterRoute(nameof(CreateOfferPage), typeof(CreateOfferPage));
        Routing.RegisterRoute(nameof(SellerOfferPage), typeof(SellerOfferPage));
        Routing.RegisterRoute(
            nameof(PayoutSettingsPage),
            typeof(PayoutSettingsPage));
        Routing.RegisterRoute(
            nameof(ChangeEmailPage),
            typeof(ChangeEmailPage));
        Routing.RegisterRoute(
            nameof(VerifyEmailChangePage),
            typeof(VerifyEmailChangePage));
        Routing.RegisterRoute(
            nameof(ChangeNamePage),
            typeof(ChangeNamePage));
        Routing.RegisterRoute(
            nameof(VerifyNameChangePage),
            typeof(VerifyNameChangePage));
    }
}
