using System.Text.Json;
using Toklong.Application.Abstractions;
using Toklong.Application.Pricing;
using Toklong.Domain.Common;
using Toklong.Infrastructure.Services;

namespace Toklong.Shippop.Certification;

public sealed class ShippopServiceCertificationTests
{
    [Fact]
    public async Task Capability_harness_passes_when_option_booking_replay_and_cancel_match()
    {
        var provider = new DeterministicCertifiedProvider();
        var harness = new ParcelProtectionCertificationHarness(
            provider,
            provider,
            new ParcelProtectionPricingPolicy());

        var result = await harness.RunAsync(
            CertificationRequest(),
            CertificationEvidence(),
            mutationsEnabled: true,
            CancellationToken.None);

        Assert.True(result.Passed);
        Assert.Empty(result.Blockers);
        Assert.Equal(
            ["weight:grams", "width:centimeters", "length:centimeters",
             "height:centimeters"],
            result.ParcelFieldEvidence);
        Assert.Equal(2, provider.BookCalls);
        Assert.Equal(2, provider.LookupCalls);
        Assert.Equal(1, provider.CancelCalls);
    }

    [Fact]
    public async Task Capability_harness_fails_when_replay_changes_the_booking_result()
    {
        var provider = new DeterministicCertifiedProvider(
            replayHasDifferentProviderCost: true);
        var harness = new ParcelProtectionCertificationHarness(
            provider,
            provider,
            new ParcelProtectionPricingPolicy());

        var result = await harness.RunAsync(
            CertificationRequest(),
            CertificationEvidence(),
            mutationsEnabled: true,
            CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Contains("booking_replay_mismatch", result.Failures);
        Assert.Equal(0, provider.CancelCalls);
    }

    [Fact]
    public async Task Capability_harness_fails_when_a_required_dimension_has_no_unit()
    {
        var provider = new DeterministicCertifiedProvider(
            heightUnit: "");
        var harness = new ParcelProtectionCertificationHarness(
            provider,
            provider,
            new ParcelProtectionPricingPolicy());

        var result = await harness.RunAsync(
            CertificationRequest(),
            CertificationEvidence(),
            mutationsEnabled: true,
            CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Contains("parcel_requirements_mismatch", result.Failures);
        Assert.Equal(0, provider.BookCalls);
    }

    [Fact]
    public async Task Capability_harness_fails_when_a_provider_field_name_is_not_allow_listed()
    {
        var provider = new DeterministicCertifiedProvider(
            weightFieldName: "weight_kg");
        var harness = new ParcelProtectionCertificationHarness(
            provider,
            provider,
            new ParcelProtectionPricingPolicy());

        var result = await harness.RunAsync(
            CertificationRequest(),
            CertificationEvidence(),
            mutationsEnabled: true,
            CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Contains("parcel_requirements_mismatch", result.Failures);
    }

    [Fact]
    public async Task Capability_harness_fails_when_a_provider_field_unit_is_not_allow_listed()
    {
        var provider = new DeterministicCertifiedProvider(
            weightUnit: "kilograms");
        var harness = new ParcelProtectionCertificationHarness(
            provider,
            provider,
            new ParcelProtectionPricingPolicy());

        var result = await harness.RunAsync(
            CertificationRequest(),
            CertificationEvidence(),
            mutationsEnabled: true,
            CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Contains("parcel_requirements_mismatch", result.Failures);
    }

    [Fact]
    public async Task Capability_harness_fails_when_lookup_returns_another_operations_booking()
    {
        var provider = new DeterministicCertifiedProvider(
            returnsBookingForDifferentLookup: true);
        var harness = new ParcelProtectionCertificationHarness(
            provider,
            provider,
            new ParcelProtectionPricingPolicy());

        var result = await harness.RunAsync(
            CertificationRequest(),
            CertificationEvidence(),
            mutationsEnabled: true,
            CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Contains("booking_lookup_reference_mismatch", result.Failures);
        Assert.Equal(0, provider.CancelCalls);
    }

    [Fact]
    public async Task Capability_harness_passes_when_provider_has_no_included_coverage()
    {
        var provider = new DeterministicCertifiedProvider(
            includedCoverageLimitSatang: 0);
        var harness = new ParcelProtectionCertificationHarness(
            provider,
            provider,
            new ParcelProtectionPricingPolicy());

        var result = await harness.RunAsync(
            CertificationRequest(),
            CertificationEvidence(includedCoverageLimitSatang: 0),
            mutationsEnabled: true,
            CancellationToken.None);

        Assert.True(result.Passed);
    }

    [Fact]
    public async Task Capability_harness_rejects_evidence_maximum_that_differs_from_selected_maximum()
    {
        var provider = new DeterministicCertifiedProvider(
            selectedCoverageLimitSatang: 100_000);
        var harness = new ParcelProtectionCertificationHarness(
            provider,
            provider,
            new ParcelProtectionPricingPolicy());

        var result = await harness.RunAsync(
            CertificationRequest(),
            CertificationEvidence(),
            mutationsEnabled: true,
            CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Contains("coverage_limit_mismatch", result.Failures);
    }

    [Theory]
    [InlineData(99_999)]
    [InlineData(100_000)]
    public async Task Capability_harness_rejects_maximum_not_above_positive_included_coverage(
        long maximumCoverageLimitSatang)
    {
        var provider = new DeterministicCertifiedProvider(
            includedCoverageLimitSatang: 100_000,
            selectedCoverageLimitSatang: maximumCoverageLimitSatang);
        var harness = new ParcelProtectionCertificationHarness(
            provider,
            provider,
            new ParcelProtectionPricingPolicy());

        var result = await harness.RunAsync(
            CertificationRequest(),
            CertificationEvidence(
                maximumCoverageLimitSatang: maximumCoverageLimitSatang),
            mutationsEnabled: true,
            CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Contains("coverage_limit_mismatch", result.Failures);
    }

    [Fact]
    public async Task Capability_harness_rejects_selected_coverage_below_included_coverage()
    {
        var provider = new DeterministicCertifiedProvider(
            includedCoverageLimitSatang: 100_000,
            selectedCoverageLimitSatang: 50_000);
        var harness = new ParcelProtectionCertificationHarness(
            provider,
            provider,
            new ParcelProtectionPricingPolicy());

        var result = await harness.RunAsync(
            CertificationRequest(),
            CertificationEvidence(),
            mutationsEnabled: true,
            CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Contains("coverage_limit_mismatch", result.Failures);
    }

    [Fact]
    public async Task Capability_harness_rejects_a_nonpositive_certified_maximum()
    {
        var provider = new DeterministicCertifiedProvider();
        var harness = new ParcelProtectionCertificationHarness(
            provider,
            provider,
            new ParcelProtectionPricingPolicy());

        var result = await harness.RunAsync(
            CertificationRequest(),
            CertificationEvidence(maximumCoverageLimitSatang: 0),
            mutationsEnabled: true,
            CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Contains("coverage_limit_mismatch", result.Failures);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(450_001)]
    public async Task Capability_harness_rejects_selected_coverage_outside_the_certified_maximum(
        long selectedCoverageLimitSatang)
    {
        var provider = new DeterministicCertifiedProvider(
            selectedCoverageLimitSatang: selectedCoverageLimitSatang);
        var harness = new ParcelProtectionCertificationHarness(
            provider,
            provider,
            new ParcelProtectionPricingPolicy());

        var result = await harness.RunAsync(
            CertificationRequest(),
            CertificationEvidence(),
            mutationsEnabled: true,
            CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Contains("coverage_limit_mismatch", result.Failures);
    }

    [Theory]
    [InlineData(" ", "PROTECT", "option-v1")]
    [InlineData("เงื่อนไข-v1", "PROTECT", "option-v1")]
    [InlineData("terms-v1", "invalid code!", "option-v1")]
    [InlineData("terms-v1", "PROTECT", " ")]
    public async Task Capability_harness_rejects_non_substantive_option_identifiers(
        string termsVersion,
        string insuranceCode,
        string optionReference)
    {
        var provider = new DeterministicCertifiedProvider(
            termsVersion: termsVersion,
            insuranceCode: insuranceCode,
            optionReference: optionReference);
        var harness = new ParcelProtectionCertificationHarness(
            provider,
            provider,
            new ParcelProtectionPricingPolicy());

        var result = await harness.RunAsync(
            CertificationRequest(),
            CertificationEvidence(
                termsVersion: termsVersion,
                insuranceCode: insuranceCode,
                optionReference: optionReference),
            mutationsEnabled: true,
            CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Contains("option_identifier_invalid", result.Failures);
    }

    [Theory]
    [InlineData("terms-v2", "PROTECT", "option-v1")]
    [InlineData("terms-v1", "PROTECT-V2", "option-v1")]
    [InlineData("terms-v1", "PROTECT", "option-v2")]
    public async Task Capability_harness_rejects_option_identifiers_that_do_not_match_evidence(
        string termsVersion,
        string insuranceCode,
        string optionReference)
    {
        var provider = new DeterministicCertifiedProvider();
        var harness = new ParcelProtectionCertificationHarness(
            provider,
            provider,
            new ParcelProtectionPricingPolicy());

        var result = await harness.RunAsync(
            CertificationRequest(),
            CertificationEvidence(
                termsVersion: termsVersion,
                insuranceCode: insuranceCode,
                optionReference: optionReference),
            mutationsEnabled: true,
            CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Contains("option_validation_mismatch", result.Failures);
    }

    [Fact]
    public async Task Capability_harness_rejects_blank_provider_booking_reference()
    {
        var provider = new DeterministicCertifiedProvider(
            providerBookingReference: " ");
        var harness = new ParcelProtectionCertificationHarness(
            provider,
            provider,
            new ParcelProtectionPricingPolicy());

        var result = await harness.RunAsync(
            CertificationRequest(),
            CertificationEvidence(),
            mutationsEnabled: true,
            CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Contains("booking_identifier_invalid", result.Failures);
    }

    [Fact]
    public async Task Capability_harness_rejects_an_overlong_provider_booking_reference()
    {
        var provider = new DeterministicCertifiedProvider(
            providerBookingReference: new string('a', 81));
        var harness = new ParcelProtectionCertificationHarness(
            provider,
            provider,
            new ParcelProtectionPricingPolicy());

        var result = await harness.RunAsync(
            CertificationRequest(),
            CertificationEvidence(),
            mutationsEnabled: true,
            CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Contains("booking_identifier_invalid", result.Failures);
    }

    [Theory]
    [InlineData(" ", "lookup_reference_invalid")]
    [InlineData("different-operation", "booking_lookup_mismatch")]
    public async Task Capability_harness_rejects_invalid_or_mismatched_lookup_reference(
        string lookupOperationReference,
        string expectedFailure)
    {
        var provider = new DeterministicCertifiedProvider(
            lookupOperationReferenceOverride: lookupOperationReference);
        var harness = new ParcelProtectionCertificationHarness(
            provider,
            provider,
            new ParcelProtectionPricingPolicy());

        var result = await harness.RunAsync(
            CertificationRequest(),
            CertificationEvidence(),
            mutationsEnabled: true,
            CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Contains(expectedFailure, result.Failures);
    }

    [Fact]
    public async Task Capability_harness_rejects_non_ascii_lookup_reference()
    {
        var provider = new DeterministicCertifiedProvider(
            lookupOperationReferenceOverride: "รายการ-123");
        var harness = new ParcelProtectionCertificationHarness(
            provider,
            provider,
            new ParcelProtectionPricingPolicy());

        var result = await harness.RunAsync(
            CertificationRequest(),
            CertificationEvidence(),
            mutationsEnabled: true,
            CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Contains("lookup_reference_invalid", result.Failures);
    }

    [Theory]
    [InlineData("https://mkpservice.shippop.dev")]
    [InlineData("https://mkpservice.shippop.dev/")]
    public void Certification_endpoint_allows_only_the_approved_https_dev_origin(
        string baseUrl)
    {
        CertificationEndpointGuard.EnsureApproved(baseUrl);
    }

    [Theory]
    [InlineData("http://mkpservice.shippop.dev")]
    [InlineData("https://mkpservice.shippop.com")]
    [InlineData("https://mkpservice.shippop.dev:443")]
    [InlineData("https://user@mkpservice.shippop.dev")]
    [InlineData("https://mkpservice.shippop.dev/booking")]
    [InlineData("https://mkpservice.shippop.dev?trace=1")]
    [InlineData("https://mkpservice.shippop.dev.evil.test")]
    public void Certification_endpoint_rejects_every_unapproved_origin_before_credentials(
        string baseUrl)
    {
        Assert.Throws<InvalidOperationException>(() =>
            CertificationEndpointGuard.EnsureApproved(baseUrl));
    }

    [CertificationFact]
    public async Task Protection_quote_and_booking_preserve_exact_values()
    {
        var context = await CertificationContext.LoadAsync();
        var evidence = new SanitizedEvidenceReport(context.ServiceCode);
        try
        {
            var provider = context.CreateProvider();
            var result = await new ParcelProtectionCertificationHarness(
                provider,
                (object)provider as IParcelProtectionCertificationOperations,
                new ParcelProtectionPricingPolicy()).RunAsync(
                await context.CreateProtectionRequestAsync(provider),
                context.Evidence,
                context.MutationsEnabled,
                CancellationToken.None);

            foreach (var blocker in result.Blockers)
                evidence.Record(blocker, "blocked");
            foreach (var failure in result.Failures)
                evidence.Record(failure, "failed");
            if (result.Passed)
            {
                evidence.RecordParcelFields(result.ParcelFieldEvidence);
                evidence.Record("capability_certification", "passed");
            }

            Assert.True(
                result.Passed,
                "Optional protection certification did not pass.");
        }
        catch
        {
            // Do not retain exception text: external responses can contain
            // provider identifiers or contact data. The failing assertion is
            // sufficient to keep the capability disabled.
            if (evidence.IsEmpty)
                evidence.Record("certification_execution", "blocked");
            throw;
        }
        finally
        {
            evidence.Write();
        }
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
        bool allowInsecureHttp,
        string apiKey,
        string accountEmail,
        string serviceCode,
        ShippingQuoteRequest shipment,
        ParcelProtectionCertificationEvidence? evidence,
        bool mutationsEnabled)
    {
        public string ServiceCode { get; } = serviceCode;
        public ShippingQuoteRequest Shipment { get; } = shipment;
        public ParcelProtectionCertificationEvidence? Evidence { get; } =
            evidence;
        public bool MutationsEnabled { get; } = mutationsEnabled;

        public static async Task<CertificationContext> LoadAsync()
        {
            var baseUrl = Required("SHIPPOP_BASE_URL");
            CertificationEndpointGuard.EnsureApproved(baseUrl);

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
                baseUrl,
                allowInsecureHttp: false,
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
                    PositiveSatang(root, "declaredValueSatang")),
                TryEvidence(root),
                Enabled("SHIPPOP_CERTIFY_MUTATIONS"));
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
                    AllowInsecureHttp = allowInsecureHttp,
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
                Shipment.DeclaredValueSatang,
                quote.ExpiresAt,
                DateTimeOffset.UtcNow.AddHours(1));
        }

        private static ParcelProtectionCertificationEvidence?
            TryEvidence(JsonElement root)
        {
            if (!root.TryGetProperty(
                    "certificationEvidence",
                    out var value) ||
                value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return null;
            if (value.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException(
                    "Certification evidence must be an object.");
            return new ParcelProtectionCertificationEvidence(
                NonNegativeSatang(value, "includedCoverageLimitSatang"),
                PositiveSatang(value, "maximumCoverageLimitSatang"),
                PositiveSatang(value, "providerCostSatang"),
                PositiveSatang(value, "customerPriceSatang"),
                Text(value, "termsVersion"),
                Text(value, "insuranceCode"),
                Text(value, "optionReference"));
        }
    }

    private sealed record ParcelProtectionCertificationEvidence(
        long IncludedCoverageLimitSatang,
        long MaximumCoverageLimitSatang,
        long ProviderCostSatang,
        long CustomerPriceSatang,
        string TermsVersion,
        string InsuranceCode,
        string OptionReference);

    private sealed record ParcelProtectionCertificationResult(
        bool Passed,
        IReadOnlyList<string> Blockers,
        IReadOnlyList<string> Failures,
        IReadOnlyList<string> ParcelFieldEvidence)
    {
        public static ParcelProtectionCertificationResult Blocked(
            string blocker) =>
            new(false, [blocker], [], []);

        public static ParcelProtectionCertificationResult Failed(
            string failure) =>
            new(false, [], [failure], []);

        public static ParcelProtectionCertificationResult PassedResult(
            IReadOnlyList<string> parcelFieldEvidence) =>
            new(true, [], [], parcelFieldEvidence);
    }

    private sealed class ParcelProtectionCertificationHarness(
        IParcelProtectionQuoteProvider protectionProvider,
        IParcelProtectionCertificationOperations? operations,
        IParcelProtectionPricingPolicy pricingPolicy)
    {
        public async Task<ParcelProtectionCertificationResult> RunAsync(
            ParcelProtectionQuoteRequest request,
            ParcelProtectionCertificationEvidence? evidence,
            bool mutationsEnabled,
            CancellationToken cancellationToken)
        {
            var availability = await protectionProvider.GetAvailabilityAsync(
                request,
                cancellationToken);
            if (!availability.ProviderCapabilityCertified)
                return ParcelProtectionCertificationResult.Blocked(
                    "provider_capability_uncertified");
            if (availability.AddOn is not { } option)
                return ParcelProtectionCertificationResult.Blocked(
                    "optional_protection_unavailable");
            if (evidence is null)
                return ParcelProtectionCertificationResult.Blocked(
                    "certification_evidence_missing");

            if (!HasValidOptionIdentifiers(option) ||
                !HasValidEvidenceIdentifiers(evidence))
                return ParcelProtectionCertificationResult.Failed(
                    "option_identifier_invalid");
            var validated = await protectionProvider.ValidateOptionAsync(
                request,
                option.OptionReference,
                cancellationToken);
            if (!OptionMatches(option, validated))
                return ParcelProtectionCertificationResult.Failed(
                    "option_validation_mismatch");
            if (!CoverageMatchesEvidence(
                    availability.IncludedCoverageLimitSatang,
                    option,
                    validated,
                    evidence))
                return ParcelProtectionCertificationResult.Failed(
                    "coverage_limit_mismatch");
            if (!OptionMetadataMatchesEvidence(option, evidence))
                return ParcelProtectionCertificationResult.Failed(
                    "option_validation_mismatch");
            if (pricingPolicy.Price(option.ProviderCostSatang)
                .CustomerPriceSatang != evidence.CustomerPriceSatang)
                return ParcelProtectionCertificationResult.Failed(
                    "provider_cost_customer_price_mismatch");
            if (operations is null)
                return ParcelProtectionCertificationResult.Blocked(
                    "booking_operations_unavailable");
            if (!mutationsEnabled)
                return ParcelProtectionCertificationResult.Blocked(
                    "mutation_certification_not_enabled");

            var requirements = await operations.GetParcelRequirementsAsync(
                request,
                cancellationToken);
            if (!TryGetParcelFieldEvidence(
                    requirements,
                    out var parcelFieldEvidence))
                return ParcelProtectionCertificationResult.Failed(
                    "parcel_requirements_mismatch");

            var operationReference = $"certification-{Guid.NewGuid():N}";
            var bookingRequest = new ParcelProtectionCertificationBookingRequest(
                request,
                option,
                operationReference);
            var booking = await operations.BookAsync(
                bookingRequest,
                cancellationToken);
            if (!IsSafeReference(booking.ProviderBookingReference) ||
                !IsSafeReference(booking.OperationReference))
                return ParcelProtectionCertificationResult.Failed(
                    "booking_identifier_invalid");
            if (!BookingMatches(booking, bookingRequest))
                return ParcelProtectionCertificationResult.Failed(
                    "booking_result_mismatch");

            var replay = await operations.BookAsync(
                bookingRequest,
                cancellationToken);
            if (!BookingMatches(replay, bookingRequest) ||
                !BookingMatches(replay, booking))
                return ParcelProtectionCertificationResult.Failed(
                    "booking_replay_mismatch");

            var lookup = await operations.LookupAsync(
                operationReference,
                cancellationToken);
            if (lookup is null)
                return ParcelProtectionCertificationResult.Blocked(
                    "booking_lookup_not_found");
            if (!IsSafeReference(lookup.OperationReference))
                return ParcelProtectionCertificationResult.Failed(
                    "lookup_reference_invalid");
            if (!BookingMatches(lookup, booking))
                return ParcelProtectionCertificationResult.Failed(
                    "booking_lookup_mismatch");

            var unrelatedLookup = await operations.LookupAsync(
                $"certification-{Guid.NewGuid():N}",
                cancellationToken);
            if (unrelatedLookup is not null)
                return ParcelProtectionCertificationResult.Failed(
                    "booking_lookup_reference_mismatch");

            var cancellation = await operations.CancelBeforeFirstScanAsync(
                booking,
                cancellationToken);
            return cancellation is
                { Cancelled: true, FirstCarrierScanDetected: false }
                ? ParcelProtectionCertificationResult.PassedResult(
                    parcelFieldEvidence)
                : ParcelProtectionCertificationResult.Failed(
                    "pre_scan_cancel_failed");
        }

        private static bool OptionMatches(
            ProviderParcelProtectionOption left,
            ProviderParcelProtectionOption right) =>
            left.OptionReference == right.OptionReference &&
            left.IncludedCoverageLimitSatang ==
            right.IncludedCoverageLimitSatang &&
            left.SelectedCoverageLimitSatang ==
            right.SelectedCoverageLimitSatang &&
            left.ProviderCostSatang == right.ProviderCostSatang &&
            left.TermsVersion == right.TermsVersion &&
            left.InsuranceCode == right.InsuranceCode;

        private static bool CoverageMatchesEvidence(
            long availabilityIncludedCoverageLimitSatang,
            ProviderParcelProtectionOption option,
            ProviderParcelProtectionOption validated,
            ParcelProtectionCertificationEvidence evidence) =>
            evidence.IncludedCoverageLimitSatang >= 0 &&
            evidence.MaximumCoverageLimitSatang > 0 &&
            availabilityIncludedCoverageLimitSatang ==
            evidence.IncludedCoverageLimitSatang &&
            option.IncludedCoverageLimitSatang ==
            evidence.IncludedCoverageLimitSatang &&
            validated.IncludedCoverageLimitSatang ==
            evidence.IncludedCoverageLimitSatang &&
            option.SelectedCoverageLimitSatang > 0 &&
            option.SelectedCoverageLimitSatang >=
            evidence.IncludedCoverageLimitSatang &&
            option.SelectedCoverageLimitSatang <=
            evidence.MaximumCoverageLimitSatang &&
            option.SelectedCoverageLimitSatang ==
            evidence.MaximumCoverageLimitSatang &&
            validated.SelectedCoverageLimitSatang ==
            evidence.MaximumCoverageLimitSatang &&
            (evidence.IncludedCoverageLimitSatang == 0 ||
             evidence.MaximumCoverageLimitSatang >
             evidence.IncludedCoverageLimitSatang);

        private static bool OptionMetadataMatchesEvidence(
            ProviderParcelProtectionOption option,
            ParcelProtectionCertificationEvidence evidence) =>
            option.ProviderCostSatang == evidence.ProviderCostSatang &&
            option.TermsVersion == evidence.TermsVersion &&
            option.InsuranceCode == evidence.InsuranceCode &&
            option.OptionReference == evidence.OptionReference;

        private static bool HasValidOptionIdentifiers(
            ProviderParcelProtectionOption option) =>
            IsSafeReference(option.TermsVersion) &&
            IsSafeReference(option.InsuranceCode) &&
            IsSafeReference(option.OptionReference);

        private static bool HasValidEvidenceIdentifiers(
            ParcelProtectionCertificationEvidence evidence) =>
            IsSafeReference(evidence.TermsVersion) &&
            IsSafeReference(evidence.InsuranceCode) &&
            IsSafeReference(evidence.OptionReference);

        private static bool TryGetParcelFieldEvidence(
            ParcelProtectionCertificationParcelRequirements requirements,
            out IReadOnlyList<string> evidence)
        {
            if (MatchesParcelField(requirements.Weight, "weight", "grams") &&
                MatchesParcelField(
                    requirements.Width,
                    "width",
                    "centimeters") &&
                MatchesParcelField(
                    requirements.Length,
                    "length",
                    "centimeters") &&
                MatchesParcelField(
                    requirements.Height,
                    "height",
                    "centimeters"))
            {
                evidence =
                [
                    $"{requirements.Weight.FieldName}:{requirements.Weight.Unit}",
                    $"{requirements.Width.FieldName}:{requirements.Width.Unit}",
                    $"{requirements.Length.FieldName}:{requirements.Length.Unit}",
                    $"{requirements.Height.FieldName}:{requirements.Height.Unit}"
                ];
                return true;
            }

            evidence = [];
            return false;
        }

        private static bool MatchesParcelField(
            ParcelProtectionCertificationParcelField field,
            string expectedFieldName,
            string expectedUnit) =>
            string.Equals(
                field.FieldName,
                expectedFieldName,
                StringComparison.Ordinal) &&
            string.Equals(
                field.Unit,
                expectedUnit,
                StringComparison.Ordinal);

        private static bool IsSafeReference(string? value) =>
            value is { Length: > 0 and <= 80 } &&
            value.All(character =>
                char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_' or '.');

        private static bool BookingMatches(
            ParcelProtectionCertificationBooking booking,
            ParcelProtectionCertificationBookingRequest request) =>
            booking.OperationReference == request.OperationReference &&
            booking.OptionReference == request.Option.OptionReference &&
            booking.IncludedCoverageLimitSatang ==
            request.Option.IncludedCoverageLimitSatang &&
            booking.SelectedCoverageLimitSatang ==
            request.Option.SelectedCoverageLimitSatang &&
            booking.ProviderCostSatang == request.Option.ProviderCostSatang &&
            booking.TermsVersion == request.Option.TermsVersion &&
            booking.InsuranceCode == request.Option.InsuranceCode;

        private static bool BookingMatches(
            ParcelProtectionCertificationBooking left,
            ParcelProtectionCertificationBooking right) =>
            left.OperationReference == right.OperationReference &&
            left.ProviderBookingReference == right.ProviderBookingReference &&
            left.OptionReference == right.OptionReference &&
            left.IncludedCoverageLimitSatang ==
            right.IncludedCoverageLimitSatang &&
            left.SelectedCoverageLimitSatang ==
            right.SelectedCoverageLimitSatang &&
            left.ProviderCostSatang == right.ProviderCostSatang &&
            left.TermsVersion == right.TermsVersion &&
            left.InsuranceCode == right.InsuranceCode;
    }

    private static ParcelProtectionQuoteRequest CertificationRequest()
    {
        var shipment = new ShippingQuoteRequest(
            "10100",
            "10240",
            1_000,
            20,
            30,
            15,
            SyntheticContact("10100"),
            SyntheticContact("10240"),
            "Synthetic parcel",
            450_000);
        return new ParcelProtectionQuoteRequest(
            shipment,
            "THAIPOST",
            "EMST",
            "synthetic-delivery-quote",
            450_000,
            DateTimeOffset.UtcNow.AddHours(2),
            DateTimeOffset.UtcNow.AddHours(1));
    }

    private static ParcelProtectionCertificationEvidence
        CertificationEvidence(
            long includedCoverageLimitSatang = 100_000,
            long maximumCoverageLimitSatang = 450_000,
            string termsVersion = "terms-v1",
            string insuranceCode = "PROTECT",
            string optionReference = "option-v1") =>
        new(
            includedCoverageLimitSatang,
            maximumCoverageLimitSatang,
            4_500,
            6_000,
            termsVersion,
            insuranceCode,
            optionReference);

    private static ShippingContactAddress SyntheticContact(
        string postalCode) =>
        new(
            "Synthetic",
            "0000000000",
            "1 Test Road",
            "Test subdistrict",
            "Test district",
            "Test province",
            postalCode);

    private sealed class DeterministicCertifiedProvider(
        bool replayHasDifferentProviderCost = false,
        bool returnsBookingForDifferentLookup = false,
        long includedCoverageLimitSatang = 100_000,
        long selectedCoverageLimitSatang = 450_000,
        string termsVersion = "terms-v1",
        string insuranceCode = "PROTECT",
        string optionReference = "option-v1",
        string providerBookingReference = "provider-booking-v1",
        string weightFieldName = "weight",
        string weightUnit = "grams",
        string heightUnit = "centimeters",
        string? lookupOperationReferenceOverride = null)
        : IParcelProtectionQuoteProvider,
            IParcelProtectionCertificationOperations
    {
        private readonly ProviderParcelProtectionOption option = new(
            "shippop",
            optionReference,
            includedCoverageLimitSatang,
            selectedCoverageLimitSatang,
            4_500,
            termsVersion,
            insuranceCode,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddHours(1));
        private readonly Dictionary<string, ParcelProtectionCertificationBooking>
            bookings = new(StringComparer.Ordinal);
        private ParcelProtectionCertificationBooking? firstBooking;

        public int BookCalls { get; private set; }
        public int LookupCalls { get; private set; }
        public int CancelCalls { get; private set; }

        public Task<ParcelProtectionAvailability> GetAvailabilityAsync(
            ParcelProtectionQuoteRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ParcelProtectionAvailability(
                option.IncludedCoverageLimitSatang,
                option,
                ProviderCapabilityCertified: true));

        public Task<ProviderParcelProtectionOption> ValidateOptionAsync(
            ParcelProtectionQuoteRequest request,
            string optionReference,
            CancellationToken cancellationToken) =>
            Task.FromResult(option);

        public Task<ParcelProtectionCertificationParcelRequirements>
            GetParcelRequirementsAsync(
                ParcelProtectionQuoteRequest request,
                CancellationToken cancellationToken) =>
            Task.FromResult(
                new ParcelProtectionCertificationParcelRequirements(
                    new ParcelProtectionCertificationParcelField(
                        weightFieldName,
                        weightUnit),
                    new ParcelProtectionCertificationParcelField(
                        "width",
                        "centimeters"),
                    new ParcelProtectionCertificationParcelField(
                        "length",
                        "centimeters"),
                    new ParcelProtectionCertificationParcelField(
                        "height",
                        heightUnit)));

        public Task<ParcelProtectionCertificationBooking> BookAsync(
            ParcelProtectionCertificationBookingRequest request,
            CancellationToken cancellationToken)
        {
            BookCalls++;
            var providerCostSatang = replayHasDifferentProviderCost &&
                BookCalls == 2
                ? option.ProviderCostSatang + 1
                : option.ProviderCostSatang;
            var booking = new ParcelProtectionCertificationBooking(
                request.OperationReference,
                providerBookingReference,
                option.OptionReference,
                option.IncludedCoverageLimitSatang,
                option.SelectedCoverageLimitSatang,
                providerCostSatang,
                option.TermsVersion,
                option.InsuranceCode);
            bookings.TryAdd(request.OperationReference, booking);
            firstBooking ??= booking;
            return Task.FromResult(booking);
        }

        public Task<ParcelProtectionCertificationBooking?> LookupAsync(
            string operationReference,
            CancellationToken cancellationToken)
        {
            LookupCalls++;
            if (bookings.TryGetValue(operationReference, out var booking))
                return Task.FromResult<ParcelProtectionCertificationBooking?>(
                    lookupOperationReferenceOverride is null
                        ? booking
                        : booking with
                        {
                            OperationReference =
                                lookupOperationReferenceOverride
                        });
            return Task.FromResult(
                returnsBookingForDifferentLookup
                    ? firstBooking
                    : null);
        }

        public Task<ParcelProtectionCertificationCancellation>
            CancelBeforeFirstScanAsync(
                ParcelProtectionCertificationBooking booking,
                CancellationToken cancellationToken)
        {
            CancelCalls++;
            return Task.FromResult(
                new ParcelProtectionCertificationCancellation(
                    Cancelled: true,
                    FirstCarrierScanDetected: false));
        }
    }

    private sealed class SanitizedEvidenceReport(string serviceCode)
    {
        private static readonly IReadOnlySet<string> AllowedCapabilities =
            new HashSet<string>(
                [
                    "capability_certification",
                    "certification_execution",
                    "provider_capability_uncertified",
                    "optional_protection_unavailable",
                    "certification_evidence_missing",
                    "option_identifier_invalid",
                    "coverage_limit_mismatch",
                    "option_validation_mismatch",
                    "provider_cost_customer_price_mismatch",
                    "booking_operations_unavailable",
                    "mutation_certification_not_enabled",
                    "parcel_requirements_mismatch",
                    "booking_result_mismatch",
                    "booking_identifier_invalid",
                    "booking_replay_mismatch",
                    "booking_lookup_not_found",
                    "lookup_reference_invalid",
                    "booking_lookup_mismatch",
                    "booking_lookup_reference_mismatch",
                    "pre_scan_cancel_failed"
                ],
                StringComparer.Ordinal);
        private static readonly IReadOnlyList<string> AllowedParcelFields =
        [
            "weight:grams",
            "width:centimeters",
            "length:centimeters",
            "height:centimeters"
        ];
        private readonly List<SanitizedEvidenceCheck> checks = [];
        private IReadOnlyList<string> parcelFields = [];

        public bool IsEmpty => checks.Count == 0;

        public void Record(string capability, string outcome)
        {
            if (!AllowedCapabilities.Contains(capability) ||
                outcome is not ("passed" or "blocked" or "failed"))
                throw new InvalidOperationException(
                    "Certification report entry is not allow-listed.");
            checks.Add(new SanitizedEvidenceCheck(capability, outcome));
        }

        public void RecordParcelFields(
            IReadOnlyList<string> returnedParcelFields)
        {
            if (!returnedParcelFields.SequenceEqual(
                    AllowedParcelFields,
                    StringComparer.Ordinal))
                throw new InvalidOperationException(
                    "Certification parcel fields are not allow-listed.");
            parcelFields = returnedParcelFields;
        }

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
                parcelFields,
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

    private static long NonNegativeSatang(
        JsonElement value,
        string property)
    {
        var satang = value.GetProperty(property).GetInt64();
        return satang >= 0
            ? satang
            : throw new InvalidOperationException(
                $"Synthetic field {property} must be non-negative.");
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
