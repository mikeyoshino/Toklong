using System.Text.Json;
using Toklong.Application.Abstractions;
using Toklong.Domain.Common;
using Toklong.Infrastructure.Services;

namespace Toklong.Shippop.Certification;

public sealed class ShippopServiceCertificationTests
{
    [CertificationFact]
    public async Task Protection_quote_and_booking_preserve_exact_values()
    {
        var context = await CertificationContext.LoadAsync();
        var evidence = new SanitizedEvidenceReport(context.ServiceCode);
        try
        {
            var provider = context.CreateProvider();
            var request = await context.CreateProtectionRequestAsync(
                provider);
            IParcelProtectionQuoteProvider protectionProvider = provider;
            var availability = await protectionProvider.GetAvailabilityAsync(
                request,
                CancellationToken.None);
            Assert.False(availability.ProviderCapabilityCertified);
            Assert.Null(availability.AddOn);

            RecordCurrentProviderBlockers(evidence);
            throw new InvalidOperationException(
                "Optional protection remains blocked until SHIPPOP documents the account-specific payload and mutation contract.");
        }
        catch
        {
            // Do not retain exception text: external responses can contain
            // provider identifiers or contact data. The failing assertion is
            // sufficient to keep the capability disabled.
            if (evidence.IsEmpty)
                RecordCurrentProviderBlockers(evidence);
            throw;
        }
        finally
        {
            evidence.Write();
        }
    }

    private static void RecordCurrentProviderBlockers(
        SanitizedEvidenceReport evidence)
    {
        evidence.Record("optional_protection_payload", "blocked");
        evidence.Record("included_coverage_satang", "blocked");
        evidence.Record("maximum_coverage_satang", "blocked");
        evidence.Record("provider_cost_satang_conversion", "blocked");
        evidence.Record("terms_and_insurance_code", "blocked");
        evidence.Record("buyer_elected_booking_result", "blocked");
        evidence.Record("safe_timeout_lookup_and_replay", "blocked");
        evidence.Record("cancellation_before_first_scan", "blocked");
        evidence.Record("provider_field_names_and_units", "blocked");
        evidence.Record("provider_weight_and_dimensions", "blocked");
    }

    [CertificationFact]
    public async Task Transport_requires_weight_and_each_dimension()
    {
        var context = await CertificationContext.LoadAsync();
        var provider = context.CreateProvider();
        var request = context.Shipment;

        await Assert.ThrowsAsync<DomainException>(() =>
            provider.GetQuotesAsync(
                request with { WeightGrams = 0 },
                CancellationToken.None));
        await Assert.ThrowsAsync<DomainException>(() =>
            provider.GetQuotesAsync(
                request with { WidthCentimeters = 0 },
                CancellationToken.None));
        await Assert.ThrowsAsync<DomainException>(() =>
            provider.GetQuotesAsync(
                request with { LengthCentimeters = 0 },
                CancellationToken.None));
        await Assert.ThrowsAsync<DomainException>(() =>
            provider.GetQuotesAsync(
                request with { HeightCentimeters = 0 },
                CancellationToken.None));
    }

    private sealed class CertificationContext(
        string baseUrl,
        string apiKey,
        string accountEmail,
        string serviceCode,
        ShippingQuoteRequest shipment)
    {
        public string ServiceCode { get; } = serviceCode;
        public ShippingQuoteRequest Shipment { get; } = shipment;

        public static async Task<CertificationContext> LoadAsync()
        {
            var addressPath = Required("SHIPPOP_SYNTHETIC_ADDRESS_JSON");
            using var document = JsonDocument.Parse(
                await File.ReadAllTextAsync(addressPath));
            var root = document.RootElement;
            var serviceCode = Required("SHIPPOP_SERVICE_CODE")
                .ToUpperInvariant();
            if (!ShippopShippingOptions.SupportedServiceCodes.Contains(
                    serviceCode))
                throw new InvalidOperationException(
                    "SHIPPOP_SERVICE_CODE must be a supported TOKLONG service.");
            return new CertificationContext(
                Required("SHIPPOP_BASE_URL"),
                Required("SHIPPOP_API_KEY"),
                Required("SHIPPOP_ACCOUNT_EMAIL"),
                serviceCode,
                new ShippingQuoteRequest(
                    Text(root, "originPostalCode"),
                    Text(root, "destinationPostalCode"),
                    Number(root, "weightGrams"),
                    Number(root, "widthCentimeters"),
                    Number(root, "lengthCentimeters"),
                    Number(root, "heightCentimeters"),
                    Contact(root.GetProperty("origin")),
                    Contact(root.GetProperty("destination")),
                    Text(root, "parcelName"),
                    PositiveSatang(root, "declaredValueSatang")));
        }

        public ShippopShippingProvider CreateProvider()
        {
            var http = new HttpClient
            {
                BaseAddress = new Uri(
                    baseUrl.EndsWith('/')
                        ? baseUrl
                        : $"{baseUrl}/"),
                Timeout = TimeSpan.FromSeconds(30)
            };
            return new ShippopShippingProvider(
                http,
                new ShippopShippingOptions
                {
                    BaseUrl = baseUrl,
                    AllowInsecureHttp = Enabled(
                        "SHIPPOP_ALLOW_INSECURE_HTTP"),
                    ApiKey = apiKey,
                    AccountEmail = accountEmail,
                    QuoteSigningSecret =
                        "certification-only-signing-secret-32-characters",
                    ServiceCodes = [ServiceCode]
                },
                new SystemClock());
        }

        public async Task<ParcelProtectionQuoteRequest>
            CreateProtectionRequestAsync(
                IShippingQuoteProvider provider)
        {
            var quote = Assert.Single(
                await provider.GetQuotesAsync(
                    Shipment,
                    CancellationToken.None),
                candidate =>
                    string.Equals(
                        candidate.ServiceCode,
                        ServiceCode,
                        StringComparison.Ordinal));
            return new ParcelProtectionQuoteRequest(
                Shipment,
                quote.CarrierCode,
                quote.ServiceCode,
                quote.QuoteReference,
                Shipment.DeclaredValueSatang);
        }
    }

    private sealed class SanitizedEvidenceReport(string serviceCode)
    {
        private readonly List<SanitizedEvidenceCheck> checks = [];

        public bool IsEmpty => checks.Count == 0;

        public void Record(string capability, string outcome) =>
            checks.Add(new SanitizedEvidenceCheck(capability, outcome));

        public void Write()
        {
            var directory = Path.Combine(
                Directory.GetCurrentDirectory(),
                "TestResults",
                "shippop-certification");
            Directory.CreateDirectory(directory);
            var report = new SanitizedEvidenceDocument(
                serviceCode,
                DateTimeOffset.UtcNow,
                "satang",
                ["weight:grams", "width:centimeters", "length:centimeters", "height:centimeters"],
                checks);
            var path = Path.Combine(
                directory,
                $"{serviceCode.ToLowerInvariant()}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json");
            File.WriteAllText(
                path,
                JsonSerializer.Serialize(
                    report,
                    new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private sealed record SanitizedEvidenceDocument(
        string ServiceCode,
        DateTimeOffset RecordedAtUtc,
        string MoneyUnit,
        IReadOnlyList<string> SubmittedParcelFields,
        IReadOnlyList<SanitizedEvidenceCheck> Checks);

    private sealed record SanitizedEvidenceCheck(
        string Capability,
        string Outcome);

    private static ShippingContactAddress Contact(
        JsonElement value) =>
        new(
            Text(value, "name"),
            Text(value, "phoneNumber"),
            Text(value, "addressLine"),
            Text(value, "subdistrictName"),
            Text(value, "districtName"),
            Text(value, "provinceName"),
            Text(value, "postalCode"));

    private static string Text(
        JsonElement value,
        string property) =>
        value.GetProperty(property).GetString()
        ?? throw new InvalidOperationException(
            $"Synthetic field {property} is required.");

    private static int Number(
        JsonElement value,
        string property) =>
        value.GetProperty(property).GetInt32();

    private static long PositiveSatang(
        JsonElement value,
        string property)
    {
        var satang = value.GetProperty(property).GetInt64();
        return satang > 0
            ? satang
            : throw new InvalidOperationException(
                $"Synthetic field {property} must be positive.");
    }

    private static string Required(string variable) =>
        Environment.GetEnvironmentVariable(variable)?.Trim()
        is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"{variable} is required.");

    private static bool Enabled(string variable) =>
        string.Equals(
            Environment.GetEnvironmentVariable(variable)?.Trim(),
            "1",
            StringComparison.Ordinal);

    private sealed class SystemClock : IClock
    {
        public DateTimeOffset UtcNow =>
            DateTimeOffset.UtcNow;
    }
}

public sealed class CertificationFactAttribute : FactAttribute
{
    public CertificationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(
                "SHIPPOP_CERTIFY") != "1")
            Skip =
                "Set SHIPPOP_CERTIFY=1 and provide rotated credentials plus synthetic addresses.";
    }
}
