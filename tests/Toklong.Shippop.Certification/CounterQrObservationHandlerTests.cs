using System.Net;
using System.Text;

namespace Toklong.Shippop.Certification;

public sealed class CounterQrObservationHandlerTests
{
    [Fact]
    public async Task Handler_observes_confirm_and_preserves_content()
    {
        const string body =
            "{\"status\":true,\"counter_qr\":\"SECRET\"}";
        var observer = new CounterQrObservationHandler(
            new StubHandler(body));
        using var client = new HttpClient(observer)
        {
            BaseAddress = new Uri("http://mkpservice.shippop.dev/")
        };

        using var response = await client.PostAsync(
            "confirm/",
            new StringContent("request"));

        Assert.Equal(body, await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "confirm/",
            Assert.Single(observer.Observations).Endpoint);
        Assert.Equal(
            "application/json",
            response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Handler_ignores_pricelist_and_tracking()
    {
        var observer = new CounterQrObservationHandler(
            new StubHandler("{\"counter_qr\":\"SECRET\"}"));
        using var client = new HttpClient(observer)
        {
            BaseAddress = new Uri("http://mkpservice.shippop.dev/")
        };

        using var quote = await client.PostAsync(
            "pricelist/",
            new StringContent("request"));
        using var tracking = await client.PostAsync(
            "tracking/",
            new StringContent("request"));

        Assert.Empty(observer.Observations);
        Assert.Empty(observer.FailureCodes);
    }

    [Fact]
    public async Task Handler_preserves_malformed_content_and_records_only_a_safe_failure()
    {
        const string body = "not-json SECRET";
        var observer = new CounterQrObservationHandler(
            new StubHandler(body));
        using var client = new HttpClient(observer)
        {
            BaseAddress = new Uri("http://mkpservice.shippop.dev/")
        };

        using var response = await client.PostAsync(
            "confirm/",
            new StringContent("request"));

        Assert.Equal(body, await response.Content.ReadAsStringAsync());
        Assert.Equal(["unsafe_response_shape"], observer.FailureCodes);
        Assert.Empty(observer.Observations);
    }

    [Fact]
    public async Task Handler_observation_never_contains_provider_values()
    {
        const string body =
            "{\"purchase_id\":\"PRIVATE\",\"counter_qr\":\"SECRET\"}";
        var observer = new CounterQrObservationHandler(
            new StubHandler(body));
        using var client = new HttpClient(observer)
        {
            BaseAddress = new Uri("http://mkpservice.shippop.dev/")
        };

        using var response = await client.PostAsync(
            "booking/",
            new StringContent("request"));

        var serialized = System.Text.Json.JsonSerializer.Serialize(
            Assert.Single(observer.Observations));
        Assert.DoesNotContain("PRIVATE", serialized);
        Assert.DoesNotContain("SECRET", serialized);
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    body,
                    Encoding.UTF8,
                    "application/json")
            });
    }
}
