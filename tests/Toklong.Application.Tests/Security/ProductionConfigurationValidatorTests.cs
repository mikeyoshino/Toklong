using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Toklong.Infrastructure;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Tests.Security;

public sealed class ProductionConfigurationValidatorTests
{
    [Fact]
    public void Production_worker_shares_protected_counter_qr_key_ring_with_api()
    {
        var composePath = FindRepositoryFile("compose.linux.yml");
        var compose = File.ReadAllText(composePath);
        var workerStart = compose.IndexOf(
            "  worker:",
            StringComparison.Ordinal);
        var workerEnd = compose.IndexOf(
            "\n  crm-migrate:",
            workerStart,
            StringComparison.Ordinal);
        var worker = compose[workerStart..workerEnd];

        Assert.Contains(
            "DataProtection__KeysPath: /var/lib/toklong/data-protection/api",
            worker);
        Assert.Contains(
            "DataProtection__CertificatePath: /run/secrets/data-protection-certificate",
            worker);
        Assert.Contains(
            "ProductImages__StoragePath: /var/lib/toklong/product-images",
            worker);
        Assert.Contains("- data-protection-certificate", worker);
        var program = File.ReadAllText(
            FindRepositoryFile("src/Toklong.Worker/Program.cs"));
        Assert.Contains("requirePersistentStorage: true", program);
    }

    [Fact]
    public void Unsafe_production_defaults_fail_closed()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["AllowedHosts"] = "*",
                    ["ConnectionStrings:ToklongDatabase"] =
                        "Host=localhost;Password=toklong_dev"
                })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(
                configuration,
                new TestEnvironment("Production"),
                requireMobileLinks: true,
                requirePersistentStorage: true));

        Assert.Contains("Production configuration is unsafe", exception.Message);
        Assert.Contains("Otp:Provider", exception.Message);
        Assert.Contains("Notifications", exception.Message);
        Assert.Contains("AllowedHosts", exception.Message);
        Assert.Contains("Database:ApplyMigrations", exception.Message);
        Assert.Contains("DataProtection:KeysPath", exception.Message);
    }

    [Fact]
    public void Development_configuration_is_not_forced_to_use_live_providers()
    {
        ProductionConfigurationValidator.Validate(
            new ConfigurationBuilder().Build(),
            new TestEnvironment("Development"),
            requireMobileLinks: true,
            requirePersistentStorage: true);
    }

    [Fact]
    public void Production_test_mode_rejects_live_Stripe_keys()
    {
        var values = SafeProductionValues();
        values["Stripe:Enabled"] = "true";
        values["Stripe:LiveMode"] = "false";
        values["Stripe:SecretKey"] = "sk_live_not_real";
        values["Stripe:PublishableKey"] = "pk_live_not_real";
        values["Stripe:WebhookSecret"] = "whsec_not_real";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(
                configuration,
                new TestEnvironment("Production"),
                requireMobileLinks: false,
                requirePersistentStorage: true));

        Assert.Contains(
            "Stripe test mode requires test keys",
            exception.Message);
    }

    [Fact]
    public void Production_rejects_development_shipping_quotes()
    {
        var values = SafeProductionValues();
        values["ShippingQuotes:Provider"] = "Development";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(
                configuration,
                new TestEnvironment("Production"),
                requireMobileLinks: false,
                requirePersistentStorage: true));

        Assert.Contains(
            "ShippingQuotes:Provider must be Shippop",
            exception.Message);
    }

    [Fact]
    public void Production_rejects_insecure_shippop_http_opt_in()
    {
        var values = SafeProductionValues();
        values["Shippop:BaseUrl"] =
            "http://mkpservice.shippop.dev/";
        values["Shippop:AllowInsecureHttp"] = "true";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(
                configuration,
                new TestEnvironment("Production"),
                requireMobileLinks: false,
                requirePersistentStorage: true));

        Assert.Contains(
            "Shippop:BaseUrl must be HTTPS",
            exception.Message);
        Assert.Contains(
            "Shippop:AllowInsecureHttp must be false",
            exception.Message);
    }

    [Fact]
    public void Production_rejects_development_email_delivery()
    {
        var values = SafeProductionValues();
        values["EmailVerification:Provider"] = "Development";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(
                configuration,
                new TestEnvironment("Production"),
                requireMobileLinks: false,
                requirePersistentStorage: true));

        Assert.Contains(
            "EmailVerification:Provider must not be Development",
            exception.Message);
    }

    [Fact]
    public void Production_rejects_account_name_change_with_ThaiBulkSms()
    {
        var values = SafeProductionValues();
        values["Otp:Provider"] = "ThaiBulkSms";
        values["Otp:ApiSecret"] =
            "thai-bulk-sms-secret-at-least-16";
        values["Otp:AccountNameChangeEnabled"] = "true";

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(values)
                    .Build(),
                new TestEnvironment("Production"),
                requireMobileLinks: false,
                requirePersistentStorage: true));

        Assert.Contains(
            "ThaiBulkSms cannot enable account-name change",
            exception.Message);
    }

    [Fact]
    public void Production_requires_certified_ten_minute_Http_name_codes()
    {
        var values = SafeProductionValues();
        values["Otp:AccountNameChangeEnabled"] = "true";
        values["Otp:AccountNameChangeCodeLifetimeSeconds"] =
            "300";
        values["Otp:AccountNameChangeCertificationReference"] =
            "";

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(values)
                    .Build(),
                new TestEnvironment("Production"),
                requireMobileLinks: false,
                requirePersistentStorage: true));

        Assert.Contains(
            "AccountNameChangeCodeLifetimeSeconds must be 600",
            exception.Message);
        Assert.Contains(
            "AccountNameChangeCertificationReference",
            exception.Message);
    }

    [Fact]
    public void Production_requires_authoritative_name_verification_lookup()
    {
        var values = SafeProductionValues();
        values["Otp:AccountNameChangeEnabled"] = "true";
        values["Otp:AccountNameChangeCodeLifetimeSeconds"] = "600";
        values["Otp:AccountNameChangeCertificationReference"] =
            "cert-account-name-001";
        values["Otp:ApiKey"] =
            "otp-key-long-enough-for-name-change";
        values[
            "Otp:AccountNameChangeVerificationLookupEnabled"] =
            "false";

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(values)
                    .Build(),
                new TestEnvironment("Production"),
                requireMobileLinks: false,
                requirePersistentStorage: true));

        Assert.Contains(
            "AccountNameChangeVerificationLookupEnabled",
            exception.Message);
    }

    [Fact]
    public void Production_requires_email_digest_key_from_secret_storage()
    {
        var values = SafeProductionValues();
        values.Remove("EmailVerification:DigestKey");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(
                configuration,
                new TestEnvironment("Production"),
                requireMobileLinks: false,
                requirePersistentStorage: true));

        Assert.Contains(
            "EmailVerification:DigestKey must be at least 32 characters",
            exception.Message);
    }

    [Fact]
    public void Production_requires_32_email_digest_key_characters()
    {
        var values = SafeProductionValues();
        values["EmailVerification:DigestKey"] =
            "กขคงจฉชซฌญฎฏฐฑฒณ";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(
                configuration,
                new TestEnvironment("Production"),
                requireMobileLinks: false,
                requirePersistentStorage: true));

        Assert.Contains(
            "EmailVerification:DigestKey must be at least 32 characters",
            exception.Message);
    }

    [Fact]
    public void Production_rejects_unknown_shippop_service()
    {
        var values = SafeProductionValues();
        values["Shippop:ServiceCodes:0"] = "UNKNOWN";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(
                configuration,
                new TestEnvironment("Production"),
                requireMobileLinks: false,
                requirePersistentStorage: true));

        Assert.Contains(
            "Shippop:ServiceCodes contains an unsupported service",
            exception.Message);
    }

    [Fact]
    public void Production_rejects_booking_without_safe_lookup()
    {
        var values = SafeProductionValues();
        values["Shippop:Services:EMST:BookOutboundEnabled"] =
            "true";
        values["Shippop:Services:EMST:CertificationReference"] =
            "CERT-2026-001";
        values["Shippop:Services:EMST:MaximumCoverageSatang"] =
            SaleTransaction.MaximumProtectedItemPriceSatang
                .ToString();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(
                configuration,
                new TestEnvironment("Production"),
                requireMobileLinks: false,
                requirePersistentStorage: true));

        Assert.Contains(
            "booking requires operation lookup",
            exception.Message);
    }

    [Fact]
    public void Production_rejects_optional_protection_until_buyer_terms_route_is_certified()
    {
        var values = SafeProductionValues();
        values["Shippop:Services:EMST:OptionalProtectionEnabled"] =
            "true";
        values["Shippop:Services:EMST:InsuranceEnabled"] = "true";
        values["Shippop:Services:EMST:CertificationReference"] =
            "CERT-2026-001";
        values["Shippop:Services:EMST:IncludedCoverageSatang"] = "0";
        values["Shippop:Services:EMST:MaximumCoverageSatang"] = "100000";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(
                configuration,
                new TestEnvironment("Production"),
                requireMobileLinks: false,
                requirePersistentStorage: true));

        Assert.Contains(
            "requires a certified buyer terms and exclusions document",
            exception.Message);
    }

    [Fact]
    public void Production_rejects_uncertified_optional_protection_with_zero_included_coverage()
    {
        var values = SafeProductionValues();
        values["Shippop:Services:EMST:OptionalProtectionEnabled"] =
            "true";
        values["Shippop:Services:EMST:InsuranceEnabled"] = "true";
        values["Shippop:Services:EMST:IncludedCoverageSatang"] = "0";
        values["Shippop:Services:EMST:MaximumCoverageSatang"] = "100000";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(
                configuration,
                new TestEnvironment("Production"),
                requireMobileLinks: false,
                requirePersistentStorage: true));

        Assert.Contains(
            "requires a certification reference",
            exception.Message);
    }

    [Fact]
    public void Safe_single_host_production_shape_is_accepted()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(SafeProductionValues())
            .Build();

        ProductionConfigurationValidator.Validate(
            configuration,
            new TestEnvironment("Production"),
            requireMobileLinks: false,
            requirePersistentStorage: true);
    }

    [Fact]
    public void Worker_does_not_require_web_file_storage()
    {
        var values = SafeProductionValues();
        values.Remove("DataProtection:KeysPath");
        values.Remove("ProductImages:StoragePath");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        ProductionConfigurationValidator.Validate(
            configuration,
            new TestEnvironment("Production"),
            requireMobileLinks: false,
            requirePersistentStorage: false);
    }

    [Fact]
    public void Production_rejects_direct_booking_without_certification()
    {
        var values =
            ValidDirectBookingValues();
        values[
            "Shippop:DirectBookingCertificationReference"] =
            "";

        var exception =
            Assert.Throws<
                InvalidOperationException>(() =>
                ProductionConfigurationValidator
                    .Validate(
                        new ConfigurationBuilder()
                            .AddInMemoryCollection(
                                values)
                            .Build(),
                        new TestEnvironment(
                            "Production"),
                        requireMobileLinks: false,
                        requirePersistentStorage:
                            true));

        Assert.Contains(
            "DirectBookingCertificationReference",
            exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2201)]
    public void Production_rejects_invalid_direct_booking_timeout(
        int milliseconds)
    {
        var values =
            ValidDirectBookingValues();
        values[
            "Shippop:DirectBookingTimeoutMilliseconds"] =
            milliseconds.ToString();

        var exception =
            Assert.Throws<
                InvalidOperationException>(() =>
                ProductionConfigurationValidator
                    .Validate(
                        new ConfigurationBuilder()
                            .AddInMemoryCollection(
                                values)
                            .Build(),
                        new TestEnvironment(
                            "Production"),
                        requireMobileLinks: false,
                        requirePersistentStorage:
                            true));

        Assert.Contains(
            "DirectBookingTimeoutMilliseconds",
            exception.Message);
    }

    private static Dictionary<string, string?>
        ValidDirectBookingValues()
    {
        var values =
            SafeProductionValues();
        values[
            "Shippop:DirectBookingEnabled"] =
            "true";
        values[
            "Shippop:DirectBookingTimeoutMilliseconds"] =
            "2200";
        values[
            "Shippop:DirectBookingMaximumConcurrency"] =
            "32";
        values[
            "Shippop:DirectBookingCertificationReference"] =
            "cert-direct-001";
        values[
            "Shippop:Services:EMST:BookOutboundEnabled"] =
            "true";
        values[
            "Shippop:Services:EMST:ConfirmEnabled"] =
            "true";
        values[
            "Shippop:Services:EMST:OperationLookupEnabled"] =
            "true";
        values[
            "Shippop:Services:EMST:CertificationReference"] =
            "cert-service-001";
        return values;
    }

    private static Dictionary<string, string?> SafeProductionValues() =>
        new()
        {
            ["AllowedHosts"] = "api.toklong.co.th",
            ["ConnectionStrings:ToklongDatabase"] =
                "Host=db;Database=toklong;Username=app;Password=not-dev",
            ["Database:ApplyMigrations"] = "false",
            ["DataProtection:KeysPath"] =
                "/var/lib/toklong/data-protection",
            ["ProductImages:StoragePath"] =
                "/var/lib/toklong/product-images",
            ["DisputeEvidence:StoragePath"] =
                "/var/lib/toklong/dispute-evidence",
            ["DisputeEvidence:EncryptionKeyBase64"] =
                Convert.ToBase64String(new byte[32]),
            ["DataProtection:CertificatePath"] =
                "/run/secrets/data-protection-certificate",
            ["DataProtection:CertificatePasswordFile"] =
                "/run/secrets/data-protection-certificate-password",
            ["Otp:Provider"] = "Http",
            ["Otp:BaseUrl"] = "https://otp.example.com",
            ["Otp:ApiKey"] = "otp-key-long-enough",
            ["EmailVerification:Provider"] = "Unavailable",
            ["EmailVerification:DigestKey"] =
                "email-digest-key-at-least-32-characters",
            ["Notifications:Enabled"] = "true",
            ["Notifications:BaseUrl"] =
                "https://notifications.example.com",
            ["Notifications:ApiKey"] =
                "notification-key-long-enough",
            ["BankPayout:Provider"] = "Manual",
            ["BankPayout:AllowManualInProduction"] = "true",
            ["Reconciliation:SigningSecret"] =
                "reconciliation-secret-at-least-32-characters",
            ["ShippingQuotes:Provider"] = "Shippop",
            ["Shippop:BaseUrl"] =
                "https://mkpservice.shippop.com/",
            ["Shippop:ApiKey"] =
                "shippop-api-key-long-enough",
            ["Shippop:AccountEmail"] =
                "shipping@toklong.co.th",
            ["Shippop:QuoteSigningSecret"] =
                "shippop-quote-signing-secret-at-least-32-characters",
            ["Shippop:ServiceCodes:0"] = "EMST",
            ["Shippop:Services:EMST:QuoteEnabled"] = "false",
            ["Shippop:Services:EMST:BookOutboundEnabled"] =
                "false",
            ["Shippop:Services:EMST:ConfirmEnabled"] =
                "false",
            ["Shippop:Services:EMST:ReturnEnabled"] =
                "false",
            ["Shippop:Services:EMST:InsuranceEnabled"] =
                "false",
            ["Shippop:Services:EMST:OperationLookupEnabled"] =
                "false",
            ["Shippop:Services:EMST:HandoffMode"] = "DropOff",
            ["Shippop:Services:EMST:MaximumCoverageSatang"] =
                "0",
            ["Shippop:Services:EMST:CertificationReference"] =
                ""
        };

    private sealed class TestEnvironment(string name)
        : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } =
            Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }

    private static string FindRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(
            $"Could not locate repository file {relativePath}.");
    }
}
