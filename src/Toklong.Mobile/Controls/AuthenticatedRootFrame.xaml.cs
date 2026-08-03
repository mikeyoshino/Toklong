using System.Windows.Input;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Controls;

public partial class AuthenticatedRootFrame : ContentView
{
    public static readonly BindableProperty BodyProperty =
        BindableProperty.Create(
            nameof(Body),
            typeof(View),
            typeof(AuthenticatedRootFrame));

    public static readonly BindableProperty SelectedRoleProperty =
        BindableProperty.Create(
            nameof(SelectedRole),
            typeof(RoleFilter),
            typeof(AuthenticatedRootFrame),
            RoleFilter.Buying,
            propertyChanged: static (bindable, _, _) =>
                ((AuthenticatedRootFrame)bindable).UpdateSelectedState());

    public static readonly BindableProperty OpenBuyingCommandProperty =
        BindableProperty.Create(
            nameof(OpenBuyingCommand),
            typeof(ICommand),
            typeof(AuthenticatedRootFrame));

    public static readonly BindableProperty CreateOfferCommandProperty =
        BindableProperty.Create(
            nameof(CreateOfferCommand),
            typeof(ICommand),
            typeof(AuthenticatedRootFrame));

    public static readonly BindableProperty OpenSellingCommandProperty =
        BindableProperty.Create(
            nameof(OpenSellingCommand),
            typeof(ICommand),
            typeof(AuthenticatedRootFrame));

    private bool reducedMotionEnabled;

    public AuthenticatedRootFrame()
    {
        InitializeComponent();
        UpdateSelectedState();
    }

    public View? Body
    {
        get => (View?)GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    public RoleFilter SelectedRole
    {
        get => (RoleFilter)GetValue(SelectedRoleProperty);
        set => SetValue(SelectedRoleProperty, value);
    }

    public ICommand? OpenBuyingCommand
    {
        get => (ICommand?)GetValue(OpenBuyingCommandProperty);
        set => SetValue(OpenBuyingCommandProperty, value);
    }

    public ICommand? CreateOfferCommand
    {
        get => (ICommand?)GetValue(CreateOfferCommandProperty);
        set => SetValue(CreateOfferCommandProperty, value);
    }

    public ICommand? OpenSellingCommand
    {
        get => (ICommand?)GetValue(OpenSellingCommandProperty);
        set => SetValue(OpenSellingCommandProperty, value);
    }

    public async Task RevealAsync(bool reducedMotion)
    {
        reducedMotionEnabled = reducedMotion;
        if (Body is null)
            return;

        if (reducedMotion)
        {
            Body.Opacity = 1;
            Body.TranslationY = 0;
            return;
        }

        Body.Opacity = 0;
        Body.TranslationY = 6;
        await Task.WhenAll(
            Body.FadeToAsync(1, 180),
            Body.TranslateToAsync(0, 0, 180, Easing.CubicOut));
    }

    private async void OnCreatePressed(object? sender, EventArgs args)
    {
        if (!reducedMotionEnabled)
            await CreateActionVisual.ScaleToAsync(0.94, 100, Easing.CubicOut);
    }

    private async void OnCreateReleased(object? sender, EventArgs args)
    {
        if (!reducedMotionEnabled)
            await CreateActionVisual.ScaleToAsync(1.0, 100, Easing.CubicOut);
    }

    private void UpdateSelectedState()
    {
        var buyingSelected = SelectedRole == RoleFilter.Buying;
        var sellingSelected = SelectedRole == RoleFilter.Selling;

        SemanticProperties.SetDescription(
            BuyButton,
            buyingSelected ? "ซื้อ เลือกอยู่" : "ซื้อ");
        SemanticProperties.SetDescription(
            SellButton,
            sellingSelected ? "ขาย เลือกอยู่" : "ขาย");

        BuyButton.BackgroundColor = Colors.Transparent;
        BuyButton.ImageSource = buyingSelected
            ? "nav_buy_active.png"
            : "nav_buy.png";
        BuyButton.TextColor = Color.FromArgb(
            buyingSelected
                ? CleanLedgerPalette.BuyerBlue
                : CleanLedgerPalette.MutedInk);
        SellButton.BackgroundColor = Colors.Transparent;
        SellButton.ImageSource = sellingSelected
            ? "nav_sell_active.png"
            : "nav_sell.png";
        SellButton.TextColor = Color.FromArgb(
            sellingSelected
                ? CleanLedgerPalette.SellerIndigo
                : CleanLedgerPalette.MutedInk);
    }
}
