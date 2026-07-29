using System.Text.Json;
using Toklong.Application.Abstractions;
using Toklong.Infrastructure.Services;

namespace Toklong.Shippop.Certification;

public sealed class ShippopServiceCertificationTests
{
    [CertificationFact]
    public async Task Certified_service_returns_full_value_insured_quote()
    {
        var baseUrl = Required("SHIPPOP_BASE_URL");
        var apiKey = Required("SHIPPOP_API_KEY");
        var accountEmail = Required("SHIPPOP_ACCOUNT_EMAIL");
        var serviceCode = Required("SHIPPOP_SERVICE_CODE")
            .ToUpperInvariant();
        var addressPath = Required(
            "SHIPPOP_SYNTHETIC_ADDRESS_JSON");
        using var document = JsonDocument.Parse(
            await File.ReadAllTextAsync(addressPath));
        var root = document.RootElement;
        var request = new ShippingQuoteRequest(
            Text(root, "originPostalCode"),
            Text(root, "destinationPostalCode"),
            Number(root, "weightGrams"),
            Number(root, "widthCentimeters"),
            Number(root, "lengthCentimeters"),
            Number(root, "heightCentimeters"),
            Contact(root.GetProperty("origin")),
            Contact(root.GetProperty("destination")),
            Text(root, "parcelName"),
            root.GetProperty("declaredValueSatang").GetInt64());
        using var http = new HttpClient
        {
            BaseAddress = new Uri(
                baseUrl.EndsWith('/')
                    ? baseUrl
                    : $"{baseUrl}/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        var provider = new ShippopShippingProvider(
            http,
            new ShippopShippingOptions
            {
                BaseUrl = baseUrl,
                AllowInsecureHttp =
                    Enabled("SHIPPOP_ALLOW_INSECURE_HTTP"),
                ApiKey = apiKey,
                AccountEmail = accountEmail,
                QuoteSigningSecret =
                    "certification-only-signing-secret-32-characters",
                ServiceCodes = [serviceCode]
            },
            new SystemClock());

        var quote = Assert.Single(
            await provider.GetQuotesAsync(request, default));

        Assert.Equal(serviceCode, quote.ServiceCode);
        Assert.True(quote.FeeSatang > 0);
        Assert.True(quote.InsuranceFeeSatang > 0);
        Assert.True(
            quote.DeclaredValueSatang >=
            request.DeclaredValueSatang);
        Assert.False(
            string.IsNullOrWhiteSpace(quote.InsuranceCode));
    }

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
