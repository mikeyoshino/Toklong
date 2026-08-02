using Toklong.Application.Abstractions;

namespace Toklong.Shippop.Certification;

[Collection(CertificationEnvironmentCollection.Name)]
public sealed class FullLifecycleCertificationTests
{
    [Fact]
    public async Task Full_lifecycle_calls_each_endpoint_once_and_cancels_last()
    {
        var provider = new RecordingShipmentProvider();

        var result = await new FullLifecycleCertificationHarness(
                provider,
                provider)
            .RunAsync(
                SyntheticShipment(),
                "EMST",
                mutationsEnabled: true,
                CancellationToken.None);

        Assert.True(result.Passed);
        Assert.Equal(
            [
                "pricelist",
                "booking",
                "confirm",
                "label",
                "tracking",
                "cancel"
            ],
            provider.Calls);
        Assert.All(
            result.Checks,
            check => Assert.Equal(
                FullLifecycleOutcome.Pass,
                check.Outcome));
        Assert.Equal(
            "certification-",
            provider.ReservationRequest!.OperationReference[..14]);
        Assert.Equal(
            "purchase-test",
            provider.ConfirmationPurchaseReference);
        Assert.Equal(
            "courier-track-test",
            provider.LabelRequest!.TrackingNumber);
        Assert.Equal(
            "provider-track-test",
            provider.TrackingProviderCode);
        Assert.Equal(
            "courier-track-test",
            provider.CancelCourierCode);
    }

    [Fact]
    public async Task Mutation_gate_blocks_before_booking()
    {
        var provider = new RecordingShipmentProvider();

        var result = await Harness(provider).RunAsync(
            SyntheticShipment(),
            "EMST",
            mutationsEnabled: false,
            CancellationToken.None);

        Assert.Equal(["pricelist"], provider.Calls);
        Assert.Equal(
            FullLifecycleOutcome.Blocked,
            Row(result, "booking").Outcome);
        Assert.False(result.Passed);
    }

    [Fact]
    public async Task Unknown_booking_outcome_is_not_retried_or_cancelled()
    {
        var provider = new RecordingShipmentProvider("booking");

        var result = await Harness(provider).RunAsync(
            SyntheticShipment(),
            "EMST",
            mutationsEnabled: true,
            CancellationToken.None);

        Assert.Equal(
            1,
            provider.Calls.Count(call => call == "booking"));
        Assert.DoesNotContain("confirm", provider.Calls);
        Assert.DoesNotContain("cancel", provider.Calls);
        Assert.Equal(
            FullLifecycleOutcome.Fail,
            Row(result, "booking").Outcome);
        Assert.False(result.Passed);
    }

    [Theory]
    [InlineData("label")]
    [InlineData("tracking")]
    public async Task Read_failure_still_cancels_once(
        string failedCapability)
    {
        var provider = new RecordingShipmentProvider(
            failedCapability);

        var result = await Harness(provider).RunAsync(
            SyntheticShipment(),
            "EMST",
            mutationsEnabled: true,
            CancellationToken.None);

        Assert.Equal(
            1,
            provider.Calls.Count(call => call == "cancel"));
        Assert.Equal(
            FullLifecycleOutcome.Fail,
            Row(result, failedCapability).Outcome);
        Assert.Equal(
            FullLifecycleOutcome.Pass,
            Row(result, "cancel").Outcome);
        Assert.False(result.Passed);
    }

    [Fact]
    public async Task Unknown_cancel_outcome_is_called_once_and_requires_cleanup()
    {
        var provider = new RecordingShipmentProvider("cancel");

        var result = await Harness(provider).RunAsync(
            SyntheticShipment(),
            "EMST",
            mutationsEnabled: true,
            CancellationToken.None);

        Assert.Equal(
            1,
            provider.Calls.Count(call => call == "cancel"));
        Assert.Equal(
            FullLifecycleOutcome.CleanupRequired,
            Row(result, "cancel").Outcome);
        Assert.False(result.Passed);
    }

    [Fact]
    public async Task Unknown_confirmation_without_cleanup_identifier_requires_review()
    {
        var provider = new RecordingShipmentProvider("confirm");

        var result = await Harness(provider).RunAsync(
            SyntheticShipment(),
            "EMST",
            mutationsEnabled: true,
            CancellationToken.None);

        Assert.Equal(
            1,
            provider.Calls.Count(call => call == "confirm"));
        Assert.DoesNotContain("label", provider.Calls);
        Assert.DoesNotContain("tracking", provider.Calls);
        Assert.DoesNotContain("cancel", provider.Calls);
        Assert.Equal(
            FullLifecycleOutcome.CleanupRequired,
            Row(result, "cancel").Outcome);
        Assert.False(result.Passed);
    }

    [Fact]
    public void Report_contains_only_sanitized_lifecycle_fields()
    {
        var report = FullLifecycleCertificationReport.Serialize(
            ResultWithAllPasses(),
            DateTimeOffset.UnixEpoch);

        Assert.Contains(
            "\"environment\": \"shippop-sandbox\"",
            report,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"capability\": \"pricelist\"",
            report,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "purchase-test",
            report,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "courier-track-test",
            report,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "<html",
            report,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Report_rejects_a_non_allow_listed_reason()
    {
        var result = new FullLifecycleCertificationResult(
            [
                new FullLifecycleCheck(
                    "pricelist",
                    FullLifecycleOutcome.Fail,
                    "raw provider body")
            ]);

        Assert.Throws<InvalidOperationException>(() =>
            FullLifecycleCertificationReport.Serialize(
                result,
                DateTimeOffset.UnixEpoch));
    }

    [FullLifecycleCertificationFact]
    [Trait("Category", "ShippopFullLifecycle")]
    public async Task Full_lifecycle_calls_every_current_endpoint_and_cleans_up()
    {
        var context =
            await FullLifecycleCertificationContext.LoadAsync();
        var provider = context.CreateProvider();
        var result = await new FullLifecycleCertificationHarness(
                provider,
                provider)
            .RunAsync(
                context.Shipment,
                context.ServiceCode,
                mutationsEnabled: true,
                CancellationToken.None);

        FullLifecycleCertificationReport.Write(
            result,
            DateTimeOffset.UtcNow,
            Console.Out);

        Assert.True(
            result.Passed,
            "SHIPPOP Sandbox lifecycle did not pass; inspect sanitized rows only.");
    }

    private static FullLifecycleCertificationResult
        ResultWithAllPasses() =>
        new(
            [
                new("pricelist", FullLifecycleOutcome.Pass, "quote_valid"),
                new("booking", FullLifecycleOutcome.Pass, "booking_valid"),
                new("confirm", FullLifecycleOutcome.Pass, "confirm_valid"),
                new("label", FullLifecycleOutcome.Pass, "label_valid"),
                new("tracking", FullLifecycleOutcome.Pass, "tracking_valid"),
                new("cancel", FullLifecycleOutcome.Pass, "cancel_confirmed")
            ]);

    private static FullLifecycleCertificationHarness Harness(
        RecordingShipmentProvider provider) =>
        new(provider, provider);

    private static FullLifecycleCheck Row(
        FullLifecycleCertificationResult result,
        string capability) =>
        Assert.Single(
            result.Checks,
            check => check.Capability == capability);

    private static ShippingQuoteRequest SyntheticShipment() =>
        new(
            "10100",
            "10240",
            1_000,
            20,
            30,
            15,
            SyntheticContact("10100"),
            SyntheticContact("10240"),
            "TOKLONG TEST PARCEL",
            100_000);

    private static ShippingContactAddress SyntheticContact(
        string postalCode) =>
        new(
            "TOKLONG TEST",
            "0000000000",
            "1 TEST ROAD",
            "TEST SUBDISTRICT",
            "TEST DISTRICT",
            "TEST PROVINCE",
            postalCode);

    private sealed class RecordingShipmentProvider(
        string? failAt = null) :
        IShippingQuoteProvider,
        IShipmentProvider
    {
        private static readonly DateTimeOffset Now =
            new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

        public List<string> Calls { get; } = [];
        public ShipmentReservationRequest? ReservationRequest
        { get; private set; }
        public string? ConfirmationPurchaseReference
        { get; private set; }
        public ShipmentLabelRequest? LabelRequest
        { get; private set; }
        public string? TrackingProviderCode
        { get; private set; }
        public string? CancelCourierCode
        { get; private set; }

        public string ProviderName => "shippop";

        public Task<IReadOnlyList<ShippingQuoteOption>> GetQuotesAsync(
            ShippingQuoteRequest request,
            CancellationToken cancellationToken)
        {
            Calls.Add("pricelist");
            return Task.FromResult<IReadOnlyList<ShippingQuoteOption>>(
                [
                    new ShippingQuoteOption(
                        "shippop",
                        "signed-quote",
                        "THAIPOST",
                        "EMST",
                        "EMS Thailand Post",
                        5_200,
                        0,
                        0,
                        null,
                        Now.AddHours(1))
                ]);
        }

        public Task<ShippingQuoteOption> ValidateQuoteAsync(
            ShippingQuoteRequest request,
            string quoteReference,
            long disclosedFeeSatang,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ShipmentReservation> ReserveAsync(
            ShipmentReservationRequest request,
            CancellationToken cancellationToken)
        {
            Calls.Add("booking");
            ThrowIfConfigured("booking");
            ReservationRequest = request;
            return Task.FromResult(
                new ShipmentReservation(
                    "shippop",
                    "purchase-test",
                    "provider-track-test",
                    null,
                    "THAIPOST",
                    "EMST",
                    5_200,
                    0,
                    100_000,
                    null,
                    Now));
        }

        public Task<ShipmentConfirmation> ConfirmAsync(
            string purchaseReference,
            string providerTrackingCode,
            string carrierCode,
            CancellationToken cancellationToken) =>
            ConfirmServiceAsync(
                purchaseReference,
                providerTrackingCode,
                carrierCode,
                "EMST",
                cancellationToken);

        public Task<ShipmentConfirmation> ConfirmServiceAsync(
            string purchaseReference,
            string providerTrackingCode,
            string carrierCode,
            string serviceCode,
            CancellationToken cancellationToken)
        {
            Calls.Add("confirm");
            ThrowIfConfigured("confirm");
            ConfirmationPurchaseReference = purchaseReference;
            return Task.FromResult(
                new ShipmentConfirmation(
                    "provider-track-test",
                    "courier-track-test",
                    "THAIPOST",
                    "booking",
                    Now));
        }

        public Task<string> GetLabelHtmlAsync(
            ShipmentLabelRequest request,
            CancellationToken cancellationToken)
        {
            Calls.Add("label");
            ThrowIfConfigured("label");
            LabelRequest = request;
            return Task.FromResult(
                "<html><body>synthetic label</body></html>");
        }

        public Task<ShipmentTrackingUpdate> GetTrackingAsync(
            string providerTrackingCode,
            string carrierCode,
            CancellationToken cancellationToken)
        {
            Calls.Add("tracking");
            ThrowIfConfigured("tracking");
            TrackingProviderCode = providerTrackingCode;
            return Task.FromResult(
                new ShipmentTrackingUpdate(
                    "provider-track-test",
                    "courier-track-test",
                    "THAIPOST",
                    "booking",
                    null,
                    "synthetic-event",
                    null));
        }

        public Task CancelAsync(
            string courierTrackingCode,
            CancellationToken cancellationToken) =>
            CancelServiceAsync(
                courierTrackingCode,
                "EMST",
                false,
                cancellationToken);

        public Task CancelServiceAsync(
            string courierTrackingCode,
            string serviceCode,
            bool isReturn,
            CancellationToken cancellationToken)
        {
            Calls.Add("cancel");
            ThrowIfConfigured("cancel");
            CancelCourierCode = courierTrackingCode;
            return Task.CompletedTask;
        }

        private void ThrowIfConfigured(string capability)
        {
            if (!string.Equals(
                    failAt,
                    capability,
                    StringComparison.Ordinal))
                return;
            if (capability is "booking" or "confirm" or "cancel")
                throw new ShipmentMutationException(
                    ShipmentMutationOutcome.OutcomeUnknown,
                    $"synthetic-{capability}-unknown");
            throw new InvalidOperationException(
                $"Synthetic {capability} failure.");
        }
    }
}
