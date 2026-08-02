using System.Text.Json;
using Toklong.Application.Abstractions;
using Toklong.Domain.Common;
using Toklong.Infrastructure.Services;

namespace Toklong.Shippop.Certification;

internal sealed class FullLifecycleCertificationContext(
    string baseUrl,
    string apiKey,
    string accountEmail,
    string serviceCode,
    ShippingQuoteRequest shipment)
{
    public string ServiceCode { get; } = serviceCode;
    public ShippingQuoteRequest Shipment { get; } = shipment;

    public static async Task<FullLifecycleCertificationContext>
        LoadAsync()
    {
        if (!Enabled("SHIPPOP_CERTIFY"))
            throw new InvalidOperationException(
                "SHIPPOP certification is not enabled.");
        if (!Enabled("SHIPPOP_CERTIFY_MUTATIONS"))
            throw new InvalidOperationException(
                "SHIPPOP mutation certification is not enabled.");

        var baseUrl = Required("SHIPPOP_BASE_URL");
        CertificationEndpointGuard.EnsureApproved(baseUrl);

        var serviceCode = Required("SHIPPOP_SERVICE_CODE")
            .ToUpperInvariant();
        if (!ShippopShippingOptions.SupportedServiceCodes.Contains(
                serviceCode))
            throw new InvalidOperationException(
                "SHIPPOP certification service is not approved.");

        var fixturePath = Required(
            "SHIPPOP_SYNTHETIC_ADDRESS_JSON");
        if (!Path.IsPathFullyQualified(fixturePath) ||
            !File.Exists(fixturePath))
            throw new InvalidOperationException(
                "Synthetic certification fixture is not approved.");

        ShippingQuoteRequest shipment;
        try
        {
            await using var stream = File.OpenRead(fixturePath);
            using var document = await JsonDocument.ParseAsync(stream);
            shipment = ParseShipment(document.RootElement);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            throw new InvalidOperationException(
                "Synthetic certification fixture is not approved.");
        }

        return new FullLifecycleCertificationContext(
            baseUrl,
            Required("SHIPPOP_API_KEY"),
            Required("SHIPPOP_ACCOUNT_EMAIL"),
            serviceCode,
            shipment);
    }

    public ShippopShippingProvider CreateProvider()
    {
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
            CertificationReference:
                "sandbox-full-lifecycle-audit");
        var http = new HttpClient
        {
            BaseAddress = new Uri(
                baseUrl.EndsWith(
                    "/",
                    StringComparison.Ordinal)
                    ? baseUrl
                    : $"{baseUrl}/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        return new ShippopShippingProvider(
            http,
            new ShippopShippingOptions
            {
                BaseUrl = baseUrl,
                AllowInsecureHttp = false,
                ApiKey = apiKey,
                AccountEmail = accountEmail,
                QuoteSigningSecret =
                    "certification-only-signing-secret-32-characters",
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

    private static ShippingQuoteRequest ParseShipment(
        JsonElement root)
    {
        if (!root.TryGetProperty(
                "certificationFixture",
                out var marker) ||
            marker.ValueKind != JsonValueKind.True)
            throw FixtureFailure();

        var origin = Contact(root, "origin");
        var destination = Contact(root, "destination");
        var originPostalCode = Text(
            root,
            "originPostalCode");
        var destinationPostalCode = Text(
            root,
            "destinationPostalCode");
        var parcelName = Text(root, "parcelName");
        if (!origin.Name.Contains(
                "TOKLONG TEST",
                StringComparison.Ordinal) ||
            !destination.Name.Contains(
                "TOKLONG TEST",
                StringComparison.Ordinal) ||
            origin.PhoneNumber != "0000000000" ||
            destination.PhoneNumber != "0000000000" ||
            parcelName != "TOKLONG TEST PARCEL" ||
            origin.PostalCode != originPostalCode ||
            destination.PostalCode != destinationPostalCode)
            throw FixtureFailure();

        return new ShippingQuoteRequest(
            originPostalCode,
            destinationPostalCode,
            PositiveInt(root, "weightGrams"),
            PositiveInt(root, "widthCentimeters"),
            PositiveInt(root, "lengthCentimeters"),
            PositiveInt(root, "heightCentimeters"),
            origin,
            destination,
            parcelName,
            PositiveLong(root, "declaredValueSatang"));
    }

    private static ShippingContactAddress Contact(
        JsonElement root,
        string property)
    {
        var value = root.GetProperty(property);
        return new ShippingContactAddress(
            Text(value, "name"),
            Text(value, "phoneNumber"),
            Text(value, "addressLine"),
            Text(value, "subdistrictName"),
            Text(value, "districtName"),
            Text(value, "provinceName"),
            Text(value, "postalCode"));
    }

    private static string Text(
        JsonElement value,
        string property) =>
        value.GetProperty(property).GetString()?.Trim()
        is { Length: > 0 } text
            ? text
            : throw FixtureFailure();

    private static int PositiveInt(
        JsonElement value,
        string property)
    {
        var number = value.GetProperty(property).GetInt32();
        return number > 0
            ? number
            : throw FixtureFailure();
    }

    private static long PositiveLong(
        JsonElement value,
        string property)
    {
        var number = value.GetProperty(property).GetInt64();
        return number > 0
            ? number
            : throw FixtureFailure();
    }

    private static InvalidOperationException FixtureFailure() =>
        new("Synthetic certification fixture is not approved.");

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name)?.Trim()
        is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"{name} is required.");

    private static bool Enabled(string name) =>
        string.Equals(
            Environment.GetEnvironmentVariable(name)?.Trim(),
            "1",
            StringComparison.Ordinal);

    private sealed class SystemClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}

public sealed class FullLifecycleCertificationFactAttribute :
    FactAttribute
{
    public FullLifecycleCertificationFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(
                    "SHIPPOP_CERTIFY"),
                "1",
                StringComparison.Ordinal) ||
            !string.Equals(
                Environment.GetEnvironmentVariable(
                    "SHIPPOP_CERTIFY_MUTATIONS"),
                "1",
                StringComparison.Ordinal))
            Skip =
                "Set both SHIPPOP certification gates for one synthetic lifecycle.";
    }
}
