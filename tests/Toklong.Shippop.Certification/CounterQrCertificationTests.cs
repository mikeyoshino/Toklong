using Toklong.Application.Abstractions;

namespace Toklong.Shippop.Certification;

public sealed class CounterQrCertificationTests
{
    [CertificationFact]
    public async Task Observe_booking_and_confirm_for_counter_qr_candidate()
    {
        var context = await CounterQrCertificationContext.LoadAsync();
        var observer = new CounterQrObservationHandler(
            new HttpClientHandler());
        var provider = context.CreateProvider(observer);
        var result = "execution_blocked";
        var cleanup = "cleanup_unavailable";
        string? trackingForCleanup = null;

        try
        {
            var quote = Assert.Single(
                await provider.GetQuotesAsync(
                    context.Shipment,
                    CancellationToken.None),
                candidate =>
                    candidate.ServiceCode == context.ServiceCode);
            var shipmentId = Guid.NewGuid();
            var reservation = await provider.ReserveAsync(
                new ShipmentReservationRequest(
                    Guid.NewGuid(),
                    context.Shipment,
                    quote,
                    shipmentId,
                    IsReturn: false,
                    OperationReference:
                        $"cert-qr-{shipmentId:N}"),
                CancellationToken.None);
            trackingForCleanup = reservation.CourierTrackingCode;
            var confirmation = await provider.ConfirmServiceAsync(
                reservation.PurchaseReference,
                reservation.ProviderTrackingCode,
                reservation.CarrierCode,
                reservation.ServiceCode,
                CancellationToken.None);
            trackingForCleanup = confirmation.CourierTrackingCode;

            var candidates = observer.Observations
                .SelectMany(response => response.CandidatePaths)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            result = observer.FailureCodes.Count > 0
                ? "execution_blocked"
                : candidates.Length > 0
                    ? "candidate_observed"
                    : "not_observed";
        }
        catch
        {
            result = "execution_blocked";
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(trackingForCleanup))
            {
                try
                {
                    await provider.CancelServiceAsync(
                        trackingForCleanup,
                        context.ServiceCode,
                        isReturn: false,
                        CancellationToken.None);
                    cleanup = "cancelled";
                }
                catch
                {
                    cleanup = "cleanup_failed";
                }
            }

            if (cleanup != "cancelled")
                result = "cleanup_failed";

            CounterQrEvidenceReport.Write(
                context.EvidenceDirectory,
                new CounterQrEvidenceDocument(
                    context.ServiceCode,
                    DateTimeOffset.UtcNow,
                    result,
                    cleanup,
                    observer.FailureCodes,
                    observer.Observations));
        }

        Assert.Equal("cancelled", cleanup);
        Assert.Equal("candidate_observed", result);
    }

    [Theory]
    [InlineData("https://mkpservice.shippop.com", true)]
    [InlineData("http://mkpservice.shippop.dev/", true)]
    [InlineData("http://mkpservice.shippop.dev", false)]
    public void Context_rejects_unapproved_origin_or_missing_opt_in(
        string baseUrl,
        bool allowInsecureHttp)
    {
        Assert.Throws<InvalidOperationException>(() =>
            CounterQrCertificationContext.EnsureApprovedEndpoint(
                baseUrl,
                allowInsecureHttp));
    }

    [Fact]
    public void Evidence_writer_rejects_a_repository_descendant()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CounterQrEvidenceReport.EnsureOutsideRepository(
                "/work/Toklong",
                "/work/Toklong/TestResults/qr"));
    }

    [Fact]
    public void Evidence_writer_allows_a_sibling_directory()
    {
        CounterQrEvidenceReport.EnsureOutsideRepository(
            "/work/Toklong",
            "/work/shippop-counter-qr-evidence");
    }

    [Fact]
    public void Evidence_document_cannot_hold_artifact_references()
    {
        var names = typeof(CounterQrEvidenceDocument)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(names, name =>
            name.Contains("Value", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Artifact", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Tracking", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Purchase", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Address", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evidence_writer_rejects_unknown_result_before_writing()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"counter-qr-evidence-{Guid.NewGuid():N}");
        try
        {
            var document = new CounterQrEvidenceDocument(
                "EMST",
                DateTimeOffset.UtcNow,
                "passed",
                "cancelled",
                [],
                []);

            Assert.Throws<InvalidOperationException>(() =>
                CounterQrEvidenceReport.Write(directory, document));
            Assert.False(Directory.Exists(directory));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
