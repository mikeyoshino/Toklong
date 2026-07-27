using Microsoft.Extensions.Logging;
using Microsoft.Maui.Handlers;
using Toklong.Mobile.Controls;
using Toklong.Mobile.Core;
using Toklong.Mobile.Pages;
using Toklong.Mobile.Services;
using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("NotoSansThai.ttf", "NotoSansThai");
                fonts.AddFont(
                    "NotoSansThai-Medium.ttf",
                    "NotoSansThaiMedium");
            })
            .ConfigureMauiHandlers(handlers =>
            {
                EntryHandler.Mapper.AppendToMapping(
                    "ToklongBorderlessEntry",
                    static (handler, view) =>
                    {
#if IOS || MACCATALYST
                        handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
                        handler.PlatformView.BackgroundColor = UIKit.UIColor.Clear;
                        if (view is ThaiMobilePhoneEntry phoneEntry)
                        {
                            handler.PlatformView.InputAccessoryView =
                                new UIKit.UIView(CoreGraphics.CGRect.Empty);
                            handler.PlatformView.EditingChanged +=
                                (_, _) =>
                                {
                                    var textField = handler.PlatformView;
                                    var formatted =
                                        phoneEntry.ApplyNativeInput(
                                            textField.Text);

                                    if (!string.Equals(
                                            textField.Text,
                                            formatted,
                                            StringComparison.Ordinal))
                                        textField.Text = formatted;
                                    var end = textField.EndOfDocument;
                                    textField.SelectedTextRange =
                                        textField.GetTextRange(end, end);
                                };
                        }
#elif ANDROID
                        handler.PlatformView.BackgroundTintList =
                            Android.Content.Res.ColorStateList.ValueOf(
                                Android.Graphics.Color.Transparent);
#endif
                    });
                PickerHandler.Mapper.AppendToMapping(
                    "ToklongBorderlessPicker",
                    static (handler, _) =>
                    {
#if IOS || MACCATALYST
                        handler.PlatformView.BorderStyle =
                            UIKit.UITextBorderStyle.None;
                        handler.PlatformView.BackgroundColor =
                            UIKit.UIColor.Clear;
#elif ANDROID
                        handler.PlatformView.BackgroundTintList =
                            Android.Content.Res.ColorStateList.ValueOf(
                                Android.Graphics.Color.Transparent);
#endif
                    });
                WebViewHandler.Mapper.AppendToMapping(
                    "ToklongShippingLabelViewer",
                    static (handler, _) =>
                    {
#if IOS || MACCATALYST
                        if (handler.PlatformView.Configuration
                                .DefaultWebpagePreferences is
                            { } preferences)
                            preferences
                                .AllowsContentJavaScript = false;
                        handler.PlatformView.ScrollView
                            .MinimumZoomScale =
                            (System.Runtime.InteropServices.NFloat)0.5;
                        handler.PlatformView.ScrollView
                            .MaximumZoomScale =
                            (System.Runtime.InteropServices.NFloat)5;
#elif ANDROID
                        handler.PlatformView.Settings
                            .JavaScriptEnabled = false;
                        handler.PlatformView.Settings
                            .SetSupportZoom(true);
                        handler.PlatformView.Settings
                            .BuiltInZoomControls = true;
                        handler.PlatformView.Settings
                            .DisplayZoomControls = false;
#endif
                    });
            });

        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddSingleton<IStartupMotionPreference, StartupMotionPreference>();
        builder.Services.AddSingleton<StartupCoordinator>();
        builder.Services.AddSingleton<StartupLogoPage>();
#if DEBUG && ANDROID
        var apiOptions = new MobileApiOptions(
            new Uri("http://10.0.2.2:5181/"));
#elif DEBUG
        var apiOptions = new MobileApiOptions(
            new Uri("http://localhost:5181/"));
#else
        var apiOptions = new MobileApiOptions(
            new Uri("https://api.toklong.co.th/"));
#endif
        builder.Services.AddSingleton(apiOptions);
        builder.Services.AddHttpClient(
            "ToklongApi",
            client =>
            {
                client.BaseAddress = apiOptions.BaseUri;
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Toklong-Mobile/0.1");
            });
#if DEBUG && IOS
        if (DeviceInfo.DeviceType == DeviceType.Virtual)
        {
            // Xcode 26 simulators without an Apple signing identity cannot
            // persist Keychain values. Keep test credentials in memory only.
            builder.Services.AddSingleton<
                IMobileSessionStore,
                InMemoryMobileSessionStore>();
        }
        else
        {
            builder.Services.AddSingleton<
                IMobileSessionStore,
                SecureMobileSessionStore>();
        }
#else
        builder.Services.AddSingleton<
            IMobileSessionStore,
            SecureMobileSessionStore>();
#endif
        builder.Services.AddSingleton<IDraftPhotoStore>(
            new DraftPhotoStore(FileSystem.AppDataDirectory));
        builder.Services.AddSingleton<MobileApiClient>();
        builder.Services.AddSingleton<ApiPushRegistrationClient>();
#if IOS
        builder.Services.AddSingleton<
            IPushRegistrationService,
            IosPushRegistrationService>();
#else
        builder.Services.AddSingleton<
            IPushRegistrationService,
            DisabledPushRegistrationService>();
#endif
        builder.Services.AddSingleton<IAuthenticationService, MobileAuthenticationService>();
        builder.Services.AddSingleton<ITransactionService, ApiTransactionService>();
        builder.Services.AddSingleton<
            IAgreementDraftService,
            ApiAgreementDraftService>();
        builder.Services.AddSingleton<ISellerOfferService, ApiSellerOfferService>();
        builder.Services.AddSingleton<IPendingSellerOfferStore, PendingSellerOfferStore>();
        builder.Services.AddSingleton<IDeepLinkCoordinator, DeepLinkCoordinator>();
        builder.Services.AddSingleton<IAddressService, ApiAddressService>();
        builder.Services.AddSingleton<
            INotificationService,
            ApiNotificationService>();
        builder.Services.AddSingleton<IStripePaymentSheetService, StripePaymentSheetService>();

        builder.Services.AddSingleton<TransactionsViewModel>();
        builder.Services.AddSingleton<ActivityViewModel>();
        builder.Services.AddTransient<TransactionDetailViewModel>();
        builder.Services.AddTransient<ShippingLabelViewModel>();
        builder.Services.AddTransient<CreateOfferViewModel>();
        builder.Services.AddTransient<SellerOfferViewModel>();
        builder.Services.AddTransient<PayoutSettingsViewModel>();
        builder.Services.AddTransient<SignInViewModel>();
        builder.Services.AddTransient<SignUpViewModel>();
        builder.Services.AddTransient<VerifyCodeViewModel>();
        builder.Services.AddSingleton<AccountViewModel>();

        builder.Services.AddSingleton<WelcomePage>();
        builder.Services.AddTransient<SignInPage>();
        builder.Services.AddTransient<SignUpPage>();
        builder.Services.AddTransient<VerifyCodePage>();
        builder.Services.AddSingleton<TransactionsPage>();
        builder.Services.AddTransient<TransactionDetailPage>();
        builder.Services.AddTransient<ShippingLabelPage>();
        builder.Services.AddTransient<CreateOfferPage>();
        builder.Services.AddTransient<SellerOfferPage>();
        builder.Services.AddTransient<PayoutSettingsPage>();
        builder.Services.AddSingleton<ActivityPage>();
        builder.Services.AddSingleton<AccountPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
