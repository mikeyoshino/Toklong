namespace Toklong.Shippop.Certification;

[CollectionDefinition(
    CertificationEnvironmentCollection.Name,
    DisableParallelization = true)]
public sealed class CertificationEnvironmentCollection
{
    public const string Name = "SHIPPOP certification environment";
}

[Collection(CertificationEnvironmentCollection.Name)]
public sealed class FullLifecycleCertificationContextTests
{
    [Fact]
    public async Task Context_requires_mutation_gate_before_credentials()
    {
        using var environment = CertificationEnvironment.Valid();
        environment.Set("SHIPPOP_CERTIFY_MUTATIONS", "0");
        environment.Set(
            "SHIPPOP_API_KEY",
            "forbidden-marker-api-key");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            FullLifecycleCertificationContext.LoadAsync);

        Assert.Equal(
            "SHIPPOP mutation certification is not enabled.",
            error.Message);
        Assert.DoesNotContain(
            "forbidden-marker",
            error.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Context_rejects_http_before_reading_fixture_or_credentials()
    {
        using var environment = CertificationEnvironment.Valid();
        environment.Set(
            "SHIPPOP_BASE_URL",
            "http://mkpservice.shippop.dev");
        environment.Set(
            "SHIPPOP_SYNTHETIC_ADDRESS_JSON",
            "/path/that/must/not/be/read.json");
        environment.Set(
            "SHIPPOP_API_KEY",
            "forbidden-marker-api-key");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            FullLifecycleCertificationContext.LoadAsync);

        Assert.Equal(
            "SHIPPOP certification endpoint is not approved.",
            error.Message);
        Assert.DoesNotContain(
            "forbidden-marker",
            error.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Context_rejects_unmarked_fixture_before_credentials()
    {
        using var environment = CertificationEnvironment.Valid(
            certificationFixture: false);
        environment.Set(
            "SHIPPOP_API_KEY",
            "forbidden-marker-api-key");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            FullLifecycleCertificationContext.LoadAsync);

        Assert.Equal(
            "Synthetic certification fixture is not approved.",
            error.Message);
        Assert.DoesNotContain(
            "forbidden-marker",
            error.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Context_loads_only_valid_test_marked_fixture()
    {
        using var environment = CertificationEnvironment.Valid();

        var context =
            await FullLifecycleCertificationContext.LoadAsync();

        Assert.Equal("EMST", context.ServiceCode);
        Assert.Equal(
            "TOKLONG TEST PARCEL",
            context.Shipment.ParcelName);
        Assert.Equal(
            "TOKLONG TEST ORIGIN",
            context.Shipment.Origin!.Name);
        Assert.Equal(
            "0000000000",
            context.Shipment.Destination!.PhoneNumber);
        Assert.Equal("shippop", context.CreateProvider().ProviderName);
    }

    private sealed class CertificationEnvironment : IDisposable
    {
        private static readonly string[] Names =
        [
            "SHIPPOP_CERTIFY",
            "SHIPPOP_CERTIFY_MUTATIONS",
            "SHIPPOP_BASE_URL",
            "SHIPPOP_SERVICE_CODE",
            "SHIPPOP_SYNTHETIC_ADDRESS_JSON",
            "SHIPPOP_API_KEY",
            "SHIPPOP_ACCOUNT_EMAIL"
        ];

        private readonly Dictionary<string, string?> original =
            Names.ToDictionary(
                name => name,
                Environment.GetEnvironmentVariable,
                StringComparer.Ordinal);
        private readonly string fixturePath;

        private CertificationEnvironment(
            bool certificationFixture)
        {
            fixturePath = Path.Combine(
                Path.GetTempPath(),
                $"toklong-shippop-certification-{Guid.NewGuid():N}.json");
            File.WriteAllText(
                fixturePath,
                Fixture(certificationFixture));
            Set("SHIPPOP_CERTIFY", "1");
            Set("SHIPPOP_CERTIFY_MUTATIONS", "1");
            Set(
                "SHIPPOP_BASE_URL",
                "https://mkpservice.shippop.dev");
            Set("SHIPPOP_SERVICE_CODE", "EMST");
            Set(
                "SHIPPOP_SYNTHETIC_ADDRESS_JSON",
                fixturePath);
            Set("SHIPPOP_API_KEY", "synthetic-api-key");
            Set(
                "SHIPPOP_ACCOUNT_EMAIL",
                "synthetic@example.invalid");
        }

        public static CertificationEnvironment Valid(
            bool certificationFixture = true) =>
            new(certificationFixture);

        public void Set(string name, string? value) =>
            Environment.SetEnvironmentVariable(name, value);

        public void Dispose()
        {
            foreach (var pair in original)
                Environment.SetEnvironmentVariable(
                    pair.Key,
                    pair.Value);
            File.Delete(fixturePath);
        }

        private static string Fixture(
            bool certificationFixture) =>
            $$"""
            {
              "certificationFixture": {{certificationFixture.ToString().ToLowerInvariant()}},
              "originPostalCode": "10100",
              "destinationPostalCode": "10240",
              "weightGrams": 1000,
              "widthCentimeters": 20,
              "lengthCentimeters": 30,
              "heightCentimeters": 15,
              "parcelName": "TOKLONG TEST PARCEL",
              "declaredValueSatang": 100000,
              "origin": {
                "name": "TOKLONG TEST ORIGIN",
                "phoneNumber": "0000000000",
                "addressLine": "1 TEST ROAD",
                "subdistrictName": "TEST SUBDISTRICT",
                "districtName": "TEST DISTRICT",
                "provinceName": "TEST PROVINCE",
                "postalCode": "10100"
              },
              "destination": {
                "name": "TOKLONG TEST DESTINATION",
                "phoneNumber": "0000000000",
                "addressLine": "2 TEST ROAD",
                "subdistrictName": "TEST SUBDISTRICT",
                "districtName": "TEST DISTRICT",
                "provinceName": "TEST PROVINCE",
                "postalCode": "10240"
              }
            }
            """;
    }
}
