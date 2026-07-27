using Toklong.Mobile.Pages;

namespace Toklong.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(SignUpPage), typeof(SignUpPage));
        Routing.RegisterRoute(nameof(VerifyCodePage), typeof(VerifyCodePage));
        Routing.RegisterRoute(nameof(TransactionDetailPage), typeof(TransactionDetailPage));
        Routing.RegisterRoute(nameof(ShippingLabelPage), typeof(ShippingLabelPage));
        Routing.RegisterRoute(nameof(CreateOfferPage), typeof(CreateOfferPage));
        Routing.RegisterRoute(nameof(SellerOfferPage), typeof(SellerOfferPage));
        Routing.RegisterRoute(
            nameof(PayoutSettingsPage),
            typeof(PayoutSettingsPage));
    }
}
