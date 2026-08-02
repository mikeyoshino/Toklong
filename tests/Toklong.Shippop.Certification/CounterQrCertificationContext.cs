using System.Text.Json;
using Toklong.Application.Abstractions;
using Toklong.Infrastructure.Services;

namespace Toklong.Shippop.Certification;

internal sealed record CounterQrCertificationContext(
    string ServiceCode,
    ShippingQuoteRequest Shipment,
    string EvidenceDirectory,
    string BaseUrl,
    bool AllowInsecureHttp,
    string ApiKey,
    string AccountEmail)
{
    internal static async Task<CounterQrCertificationContext> LoadAsync()
    {
        var baseUrl = Required("SHIPPOP_BASE_URL");
        var allowInsecureHttp = Enabled(
            "SHIPPOP_ALLOW_INSECURE_HTTP");
        EnsureApprovedEndpoint(baseUrl, allowInsecureHttp);

        var repositoryRoot = Required("SHIPPOP_REPOSITORY_ROOT");
        var evidenceDirectory = Required(
            "SHIPPOP_EVIDENCE_DIRECTORY");
        CounterQrEvidenceReport.EnsureOutsideRepository(
            repositoryRoot,
            evidenceDirectory);

        var serviceCode = Required("SHIPPOP_SERVICE_CODE")
            .ToUpperInvariant();
        if (!ShippopShippingOptions.SupportedServiceCodes.Contains(
                serviceCode))
            throw new InvalidOperationException(
                "Counter QR observation service is not supported.");
        if (!Enabled("SHIPPOP_CERTIFY_MUTATIONS"))
            throw new InvalidOperationException(
                "counter-qr-mutation-observation-disabled");

        var fixturePath = Required(
            "SHIPPOP_SYNTHETIC_ADDRESS_JSON");
        if (!Path.IsPathFullyQualified(fixturePath) ||
            !File.Exists(fixturePath))
            throw new InvalidOperationException(
                "Counter QR synthetic fixture is unavailable.");

        using var document = JsonDocument.Parse(
            await File.ReadAllTextAsync(fixturePath));
        var root = document.RootElement;
        var shipment = new ShippingQuoteRequest(
            Text(root, "originPostalCode"),
            Text(root, "destinationPostalCode"),
            PositiveNumber(root, "weightGrams"),
            PositiveNumber(root, "widthCentimeters"),
            PositiveNumber(root, "lengthCentimeters"),
            PositiveNumber(root, "heightCentimeters"),
            Contact(root.GetProperty("origin")),
            Contact(root.GetProperty("destination")),
            Text(root, "parcelName"),
            NonNegativeSatang(root, "declaredValueSatang"));

        return new CounterQrCertificationContext(
            serviceCode,
            shipment,
            Path.GetFullPath(evidenceDirectory),
            baseUrl,
            allowInsecureHttp,
            Required("SHIPPOP_API_KEY"),
            Required("SHIPPOP_ACCOUNT_EMAIL"));
    }

    internal ShippopShippingProvider CreateProvider(
        CounterQrObservationHandler observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var http = new HttpClient(observer)
        {
            BaseAddress = new Uri($"{BaseUrl}/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        var profile = new ShippopServiceProfile(
            ServiceCode,
            QuoteEnabled: true,
            BookOutboundEnabled: true,
            ConfirmEnabled: true,
            ReturnEnabled: false,
            InsuranceEnabled: false,
            OperationLookupEnabled: true,
            HandoffMode: "DropOff",
            MaximumCoverageSatang: 0,
            CertificationReference: "COUNTER-QR-OBSERVATION-ONLY",
            IncludedCoverageSatang: 0,
            OptionalProtectionEnabled: false);
        return new ShippopShippingProvider(
            http,
            new ShippopShippingOptions
            {
                BaseUrl = BaseUrl,
                AllowInsecureHttp = AllowInsecureHttp,
                ApiKey = ApiKey,
                AccountEmail = AccountEmail,
                QuoteSigningSecret =
                    "counter-qr-observation-signing-key-32",
                ServiceCodes = [ServiceCode],
                ServiceProfiles =
                    new Dictionary<string, ShippopServiceProfile>(
                        StringComparer.Ordinal)
                    {
                        [ServiceCode] = profile
                    }
            },
            new SystemClock());
    }

    internal static void EnsureApprovedEndpoint(
        string baseUrl,
        bool allowInsecureHttp)
    {
        if (!allowInsecureHttp ||
            !string.Equals(
                baseUrl,
                "http://mkpservice.shippop.dev",
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Counter QR observation endpoint is not approved.");
    }

    private static ShippingContactAddress Contact(JsonElement value) =>
        new(
            Text(value, "name"),
            Text(value, "phoneNumber"),
            Text(value, "addressLine"),
            Text(value, "subdistrictName"),
            Text(value, "districtName"),
            Text(value, "provinceName"),
            Text(value, "postalCode"));

    private static string Text(JsonElement value, string property)
    {
        var text = value.GetProperty(property).GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException(
                "Counter QR synthetic fixture is incomplete.");
        return text;
    }

    private static int PositiveNumber(JsonElement value, string property)
    {
        if (!value.GetProperty(property).TryGetInt32(out var number) ||
            number <= 0)
            throw new InvalidOperationException(
                "Counter QR synthetic fixture has an invalid number.");
        return number;
    }

    private static long NonNegativeSatang(
        JsonElement value,
        string property)
    {
        if (!value.GetProperty(property).TryGetInt64(out var number) ||
            number < 0)
            throw new InvalidOperationException(
                "Counter QR synthetic fixture has invalid satang.");
        return number;
    }

    private static bool Enabled(string name) =>
        string.Equals(
            Environment.GetEnvironmentVariable(name),
            "1",
            StringComparison.Ordinal);

    private static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(name)?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Missing required environment variable: {name}");
        return value;
    }
}
