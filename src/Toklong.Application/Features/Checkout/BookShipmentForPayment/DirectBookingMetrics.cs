using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Toklong.Application.Features.Checkout.BookShipmentForPayment;

public sealed class DirectBookingMetrics
{
    private readonly Meter meter =
        new("Toklong.Shipping");
    private readonly Histogram<double> duration;
    private readonly Counter<long> results;
    private readonly Counter<long> bulkheadRejected;
    private readonly Counter<long> timeouts;

    public DirectBookingMetrics()
    {
        duration = meter.CreateHistogram<double>(
            "toklong.shipping.booking.duration",
            "ms");
        results = meter.CreateCounter<long>(
            "toklong.shipping.booking.result");
        bulkheadRejected =
            meter.CreateCounter<long>(
                "toklong.shipping.booking.bulkhead_rejected");
        timeouts = meter.CreateCounter<long>(
            "toklong.shipping.booking.timeout");
    }

    public void Record(
        string serviceCode,
        DirectBookingState result,
        TimeSpan elapsed)
    {
        var tags =
            new TagList
            {
                {
                    "service_code",
                    serviceCode
                },
                {
                    "result",
                    result.ToString()
                        .ToLowerInvariant()
                }
            };
        duration.Record(
            elapsed.TotalMilliseconds,
            tags);
        results.Add(1, tags);
        if (result ==
            DirectBookingState.TimedOut)
            timeouts.Add(1, tags);
    }

    public void RecordBulkheadRejection(
        string serviceCode) =>
        bulkheadRejected.Add(
            1,
            new KeyValuePair<string, object?>(
                "service_code",
                serviceCode));
}
