using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Toklong.Application.Abstractions;
using Toklong.Application.Pricing;
using Toklong.Infrastructure.Email;
using Toklong.Infrastructure.Persistence;
using Toklong.Infrastructure.Payments;
using Toklong.Infrastructure.Pricing;
using Toklong.Infrastructure.Security;
using Toklong.Infrastructure.Services;

namespace Toklong.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ToklongDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("ToklongDatabase"),
                npgsql => npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<
            IShippingOperationRepository,
            ShippingOperationRepository>();
        services.AddScoped<IRetentionRepository, RetentionRepository>();
        services.AddScoped<IBuyerRepository, BuyerRepository>();
        services.AddScoped<
            IBuyerEmailChangeRepository,
            BuyerEmailChangeRepository>();
        services.AddScoped<ISellerRepository, SellerRepository>();
        services.AddScoped<IMobileSessionRepository, MobileSessionRepository>();
        services.AddScoped<
            IPendingMobileRegistrationRepository,
            PendingMobileRegistrationRepository>();
        services.AddSingleton<
            IRegistrationTicketService,
            RegistrationTicketService>();
        services.AddScoped<
            INotificationInboxRepository,
            NotificationInboxRepository>();
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<ToklongDbContext>());
        services.AddSingleton<IThaiAddressCatalog, BundledThaiAddressCatalog>();
        services.AddHttpClient<IListingImportService, ListingImportService>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(12);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "ToklongListingImporter/1.0 (+https://toklong.local)");
            })
            .ConfigurePrimaryHttpMessageHandler(ListingImportService.CreateSafeHandler);
        services.AddSingleton(new ListingAiOptions
        {
            ApiKey = configuration[$"{ListingAiOptions.SectionName}:ApiKey"] ?? "",
            Model = configuration[$"{ListingAiOptions.SectionName}:Model"] ?? "gpt-5.6-luna"
        });
        services.AddSingleton<IListingImageAnalysisService, OpenAiListingImageAnalysisService>();
        services.AddSingleton<
            IAgreementDraftExtractionService,
            OpenAiAgreementDraftExtractionService>();
        services.AddSingleton<IImportedProductImageStore, ImportedProductImageStore>();
        services.AddSingleton<
            IDisputeEvidenceStore,
            EncryptedDisputeEvidenceStore>();
        var emailVerificationOptions =
            EmailVerificationOptions.From(configuration);
        services.AddSingleton(emailVerificationOptions);
        services.AddSingleton<
            IEmailVerificationTemplate,
            ToklongEmailVerificationTemplate>();
        if (string.Equals(
                emailVerificationOptions.Provider,
                "Development",
                StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<
                IEmailVerificationCodeService,
                DevelopmentEmailVerificationCodeService>();
            services.AddSingleton<
                DevelopmentTransactionalEmailSender>();
            services.AddSingleton<ITransactionalEmailSender>(
                provider => provider.GetRequiredService<
                    DevelopmentTransactionalEmailSender>());
            services.AddSingleton<IDevelopmentEmailInbox>(
                provider => provider.GetRequiredService<
                    DevelopmentTransactionalEmailSender>());
        }
        else
        {
            services.AddSingleton<
                IEmailVerificationCodeService,
                HmacEmailVerificationCodeService>();
            services.AddSingleton<
                ITransactionalEmailSender,
                UnavailableTransactionalEmailSender>();
        }
        var otpOptions = OtpProviderOptions.From(configuration);
        services.AddSingleton(otpOptions);
        if (string.Equals(
                otpOptions.Provider,
                "ThaiBulkSms",
                StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<
                    IOtpVerificationProvider,
                    ThaiBulkSmsOtpVerificationProvider>(client =>
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(
                        "ToklongOtp/1.0");
                });
        }
        else if (string.Equals(
                otpOptions.Provider,
                "Http",
                StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<
                    IOtpVerificationProvider,
                    HttpOtpVerificationProvider>(client =>
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(
                        "ToklongOtp/1.0");
                });
        }
        else
        {
            services.AddSingleton<
                IOtpVerificationProvider,
                DevelopmentOtpVerificationProvider>();
        }
        services.AddSingleton<IClock, SystemClock>();
        var shippingProvider =
            configuration["ShippingQuotes:Provider"];
        if (string.Equals(
                shippingProvider,
                "Development",
                StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<
                DevelopmentShippingQuoteProvider>();
            services.AddSingleton<IShippingQuoteProvider>(
                provider => provider.GetRequiredService<
                    DevelopmentShippingQuoteProvider>());
            services.AddSingleton<IShipmentProvider>(
                provider => provider.GetRequiredService<
                    DevelopmentShippingQuoteProvider>());
        }
        else if (string.Equals(
                     shippingProvider,
                     "Shippop",
                     StringComparison.OrdinalIgnoreCase))
        {
            var shippop = ShippopShippingOptions.From(
                configuration);
            services.AddSingleton(shippop);
            services.AddHttpClient<ShippopShippingProvider>(
                client =>
                {
                    client.BaseAddress = new Uri(
                        shippop.BaseUrl.EndsWith(
                            "/",
                            StringComparison.Ordinal)
                            ? shippop.BaseUrl
                            : $"{shippop.BaseUrl}/");
                    client.Timeout =
                        TimeSpan.FromSeconds(20);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(
                        "ToklongShipping/1.0");
                });
            services.AddTransient<IShippingQuoteProvider>(
                provider => provider.GetRequiredService<
                    ShippopShippingProvider>());
            services.AddTransient<IShipmentProvider>(
                provider => provider.GetRequiredService<
                    ShippopShippingProvider>());
        }
        else
        {
            services.AddSingleton<
                UnavailableShippingQuoteProvider>();
            services.AddSingleton<IShippingQuoteProvider>(
                provider => provider.GetRequiredService<
                    UnavailableShippingQuoteProvider>());
            services.AddSingleton<IShipmentProvider>(
                provider => provider.GetRequiredService<
                    UnavailableShippingQuoteProvider>());
        }
        var payoutOptions = BankPayoutOptions.From(configuration);
        services.AddSingleton(payoutOptions);
        if (string.Equals(
                payoutOptions.Provider,
                "Manual",
                StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<
                IPayoutProvider,
                ManualPayoutProvider>();
        }
        else
        {
            services.AddHttpClient<
                    IPayoutProvider,
                    HttpBankPayoutProvider>(client =>
                {
                    client.Timeout = TimeSpan.FromSeconds(15);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(
                        "ToklongPayout/1.0");
                });
        }
        services.AddSingleton(new ReconciliationOptions
        {
            SigningSecret = configuration[$"{ReconciliationOptions.SectionName}:SigningSecret"] ?? ""
        });
        services.AddSingleton<IWebhookSignatureVerifier, HmacWebhookSignatureVerifier>();
        var stripeOptions = StripePaymentOptions.From(configuration);
        services.AddSingleton(stripeOptions);
        services.AddSingleton(
            BuyerProtectionFeeOptions.From(configuration));
        services.AddSingleton<
            IPaymentFeePolicy,
            ConfiguredBuyerProtectionFeePolicy>();
        services.AddSingleton<IPaymentIntentProvider, StripePaymentIntentProvider>();
        services.AddSingleton<
            IPaymentReconciliationProvider,
            StripePaymentReconciliationProvider>();
        services.AddSingleton<StripeRefundProvider>();
        services.AddSingleton<IRefundProvider>(
            provider => provider.GetRequiredService<
                StripeRefundProvider>());
        services.AddSingleton<IRefundReconciliationProvider>(
            provider => provider.GetRequiredService<
                StripeRefundProvider>());
        var notificationOptions =
            NotificationProviderOptions.From(configuration);
        services.AddSingleton(notificationOptions);
        if (notificationOptions.Enabled)
        {
            services.AddHttpClient<
                    HttpNotificationProvider>(client =>
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(
                        "ToklongNotifications/1.0");
                });
            services.AddSingleton<INotificationProvider>(
                provider => provider.GetRequiredService<
                    HttpNotificationProvider>());
            services.AddSingleton<
                IDeviceNotificationRegistrationProvider>(
                provider => provider.GetRequiredService<
                    HttpNotificationProvider>());
        }
        else
        {
            services.AddSingleton<DisabledNotificationProvider>();
            services.AddSingleton<INotificationProvider>(
                provider => provider.GetRequiredService<
                    DisabledNotificationProvider>());
            services.AddSingleton<
                IDeviceNotificationRegistrationProvider>(
                provider => provider.GetRequiredService<
                    DisabledNotificationProvider>());
        }
        return services;
    }
}
