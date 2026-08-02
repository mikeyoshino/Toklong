using System.Text.Json;

namespace Toklong.Shippop.Certification;

internal sealed class CounterQrObservationHandler(
    HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    private readonly object sync = new();
    private readonly List<CounterQrResponseShape> observations = [];
    private readonly List<string> failureCodes = [];

    internal IReadOnlyList<CounterQrResponseShape> Observations
    {
        get
        {
            lock (sync)
                return observations.ToArray();
        }
    }

    internal IReadOnlyList<string> FailureCodes
    {
        get
        {
            lock (sync)
                return failureCodes.ToArray();
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        var endpoint = Endpoint(request.RequestUri);
        if (endpoint is null || response.Content is null)
            return response;

        var original = response.Content;
        var bytes = await original.ReadAsByteArrayAsync(cancellationToken);
        var replacement = new ByteArrayContent(bytes);
        foreach (var header in original.Headers)
            replacement.Headers.TryAddWithoutValidation(
                header.Key,
                header.Value);
        response.Content = replacement;
        original.Dispose();

        try
        {
            var shape = CounterQrResponseShapeParser.Parse(
                endpoint,
                bytes);
            lock (sync)
                observations.Add(shape);
        }
        catch (JsonException)
        {
            RecordUnsafeShape();
        }
        catch (InvalidOperationException)
        {
            RecordUnsafeShape();
        }

        return response;
    }

    private void RecordUnsafeShape()
    {
        lock (sync)
            failureCodes.Add("unsafe_response_shape");
    }

    private static string? Endpoint(Uri? uri) =>
        uri?.AbsolutePath.Trim('/').ToLowerInvariant() switch
        {
            "booking" => "booking/",
            "confirm" => "confirm/",
            _ => null
        };
}
