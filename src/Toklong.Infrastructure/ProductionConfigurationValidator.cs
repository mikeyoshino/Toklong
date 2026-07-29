using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Toklong.Infrastructure.Email;
using Toklong.Infrastructure.Payments;
using Toklong.Infrastructure.Pricing;
using Toklong.Infrastructure.Services;
using Toklong.Domain.Transactions;

namespace Toklong.Infrastructure;

public static class ProductionConfigurationValidator
{
    public static void Validate(
        IConfiguration configuration,
        IHostEnvironment environment,
        bool requireMobileLinks,
        bool requirePersistentStorage)
    {
        if (environment.IsDevelopment() ||
            environment.IsEnvironment("Testing"))
            return;

        var errors = new List<string>();
        var emailVerification =
            EmailVerificationOptions.From(configuration);
        if (string.Equals(
                emailVerification.Provider,
                "Development",
                StringComparison.OrdinalIgnoreCase))
            errors.Add(
                "EmailVerification:Provider must not be Development outside Development or Testing");
        if (emailVerification.DigestKey.Length < 32)
            errors.Add(
                "EmailVerification:DigestKey must be at least 32 characters");

        var otp = OtpProviderOptions.From(configuration);
        var usesThaiBulkSms = string.Equals(
            otp.Provider,
            "ThaiBulkSms",
            StringComparison.OrdinalIgnoreCase);
        if (!usesThaiBulkSms &&
            !string.Equals(
                otp.Provider,
                "Http",
                StringComparison.OrdinalIgnoreCase))
            errors.Add(
                "Otp:Provider must be ThaiBulkSms or Http");
        ValidateHttpsSecret(
            otp.BaseUrl,
            otp.ApiKey,
            "Otp",
            errors);
        if (usesThaiBulkSms &&
            otp.ApiSecret.Length < 16)
            errors.Add(
                "Otp:ApiSecret must be supplied from secret storage for ThaiBulkSms");

        var reconciliationSecret =
            configuration["Reconciliation:SigningSecret"] ?? "";
        if (reconciliationSecret.Length < 32)
            errors.Add(
                "Reconciliation:SigningSecret must be at least 32 characters");

        var notifications =
            NotificationProviderOptions.From(configuration);
        if (!notifications.Enabled)
            errors.Add("Notifications must be enabled");
        ValidateHttpsSecret(
            notifications.BaseUrl,
            notifications.ApiKey,
            "Notifications",
            errors);

        if (!string.Equals(
                configuration["ShippingQuotes:Provider"],
                "Shippop",
                StringComparison.OrdinalIgnoreCase))
            errors.Add(
                "ShippingQuotes:Provider must be Shippop outside Development");
        else
        {
            var shippop = ShippopShippingOptions.From(
                configuration);
            ValidateHttpsSecret(
                shippop.BaseUrl,
                shippop.ApiKey,
                "Shippop",
                errors);
            if (shippop.AccountEmail.Length is < 3 or > 254 ||
                !shippop.AccountEmail.Contains(
                    '@',
                    StringComparison.Ordinal))
                errors.Add(
                    "Shippop:AccountEmail is required");
            if (shippop.QuoteSigningSecret.Length < 32)
                errors.Add(
                    "Shippop:QuoteSigningSecret must be at least 32 characters");
            if (string.Equals(
                    shippop.ApiKey,
                    shippop.QuoteSigningSecret,
                    StringComparison.Ordinal))
                errors.Add(
                    "Shippop API key and quote signing secret must be different");
            if (shippop.ServiceCodes.Count == 0 ||
                shippop.ServiceCodes.Any(
                    code =>
                        !ShippopShippingOptions
                            .SupportedServiceCodes.Contains(code)))
                errors.Add(
                    "Shippop:ServiceCodes contains an unsupported service");
            foreach (var serviceCode in shippop.ServiceCodes)
            {
                var profile = shippop.Profile(serviceCode);
                if (profile is null)
                {
                    errors.Add(
                        $"Shippop:Services:{serviceCode} profile is required");
                    continue;
                }
                var anyCapability =
                    profile.QuoteEnabled ||
                    profile.BookOutboundEnabled ||
                    profile.ConfirmEnabled ||
                    profile.ReturnEnabled ||
                    profile.InsuranceEnabled;
                if (anyCapability &&
                    string.IsNullOrWhiteSpace(
                        profile.CertificationReference))
                    errors.Add(
                        $"Shippop service {serviceCode} requires a certification reference");
                if (!string.Equals(
                        profile.HandoffMode,
                        "DropOff",
                        StringComparison.Ordinal))
                    errors.Add(
                        $"Shippop service {serviceCode} must use DropOff handoff");
                if (anyCapability &&
                    profile.MaximumCoverageSatang <
                    SaleTransaction
                        .MaximumProtectedItemPriceSatang)
                    errors.Add(
                        $"Shippop service {serviceCode} coverage is below the supported maximum");
                if (profile.BookOutboundEnabled &&
                    !profile.OperationLookupEnabled)
                    errors.Add(
                        $"Shippop service {serviceCode} booking requires operation lookup");
                if (profile.BookOutboundEnabled &&
                    !profile.InsuranceEnabled)
                    errors.Add(
                        $"Shippop service {serviceCode} booking requires full-value insurance");
            }
        }

        var payout = BankPayoutOptions.From(configuration);
        if (string.Equals(
                payout.Provider,
                "Manual",
                StringComparison.OrdinalIgnoreCase))
        {
            if (!configuration.GetValue<bool>(
                    "BankPayout:AllowManualInProduction"))
                errors.Add(
                    "Manual payout requires explicit BankPayout:AllowManualInProduction");
        }
        else
        {
            ValidateHttpsSecret(
                payout.BaseUrl,
                payout.ApiKey,
                "BankPayout",
                errors);
        }

        var connectionString = configuration
            .GetConnectionString("ToklongDatabase") ?? "";
        if (string.IsNullOrWhiteSpace(connectionString) ||
            connectionString.Contains(
                "toklong_dev",
                StringComparison.OrdinalIgnoreCase))
            errors.Add(
                "ToklongDatabase must use production credentials");
        if (configuration["AllowedHosts"] is "*" or null or "")
            errors.Add("AllowedHosts must be restricted");
        if (configuration.GetValue(
                "Database:ApplyMigrations",
                true))
            errors.Add(
                "Database:ApplyMigrations must be false; run the one-shot migration command before starting services");

        if (requirePersistentStorage)
        {
            try
            {
                DisputeEvidenceStoreOptions.ValidateConfiguration(
                    configuration,
                    environment);
            }
            catch (InvalidOperationException exception)
            {
                errors.Add(exception.Message);
            }
            ValidateAbsolutePath(
                configuration["DataProtection:KeysPath"],
                "DataProtection:KeysPath",
                errors);
            ValidateAbsolutePath(
                configuration["ProductImages:StoragePath"],
                "ProductImages:StoragePath",
                errors);
            ValidateAbsolutePath(
                configuration[
                    DisputeEvidenceStoreOptions.StoragePathKey],
                DisputeEvidenceStoreOptions.StoragePathKey,
                errors);
            ValidateAbsolutePath(
                configuration[
                    DataProtectionCertificateLoader
                        .CertificatePathConfigurationKey],
                DataProtectionCertificateLoader
                    .CertificatePathConfigurationKey,
                errors);
            ValidateAbsolutePath(
                configuration[
                    DataProtectionCertificateLoader
                        .CertificatePasswordFileConfigurationKey],
                DataProtectionCertificateLoader
                    .CertificatePasswordFileConfigurationKey,
                errors);
        }

        var stripe = StripePaymentOptions.From(configuration);
        if (stripe.Enabled)
        {
            var buyerProtection =
                BuyerProtectionFeeOptions.From(configuration);
            if (string.IsNullOrWhiteSpace(stripe.SecretKey) ||
                string.IsNullOrWhiteSpace(stripe.PublishableKey) ||
                string.IsNullOrWhiteSpace(stripe.WebhookSecret) ||
                !buyerProtection.Enabled)
                errors.Add(
                    "Stripe enabled configuration is incomplete");
            try
            {
                buyerProtection.Validate();
            }
            catch (InvalidOperationException exception)
            {
                errors.Add(exception.Message);
            }
            if (!stripe.ApiKeyModesMatch())
                errors.Add(
                    stripe.LiveMode
                        ? "Stripe live mode requires live keys"
                        : "Stripe test mode requires test keys");
        }

        if (requireMobileLinks)
        {
            if (string.IsNullOrWhiteSpace(
                    configuration["MobileLinks:AppleTeamId"]))
                errors.Add("MobileLinks:AppleTeamId is required");
            if (!configuration
                    .GetSection(
                        "MobileLinks:AndroidSha256Fingerprints")
                    .GetChildren()
                    .Any())
                errors.Add(
                    "MobileLinks Android signing fingerprint is required");
        }

        if (errors.Count > 0)
            throw new InvalidOperationException(
                "Production configuration is unsafe: " +
                string.Join("; ", errors));
    }

    private static void ValidateHttpsSecret(
        string baseUrl,
        string secret,
        string section,
        ICollection<string> errors)
    {
        if (!Uri.TryCreate(
                baseUrl,
                UriKind.Absolute,
                out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo))
            errors.Add($"{section}:BaseUrl must be HTTPS");
        if (secret.Length < 16)
            errors.Add(
                $"{section}:ApiKey must be supplied from secret storage");
    }

    private static void ValidateAbsolutePath(
        string? value,
        string configurationKey,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Path.IsPathFullyQualified(value))
            errors.Add(
                $"{configurationKey} must be an absolute persistent path");
    }
}
