using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Toklong.Mobile.Core;
using Toklong.Mobile.Services;

namespace Toklong.Mobile.Core.Tests;

public sealed class ApiTransactionServiceParcelProtectionTests
{
    [Fact]
    public async Task Election_posts_the_disclosed_choice_with_an_idempotency_key()
    {
        var transactionId = Guid.NewGuid();
        var handler = new RecordingHandler(JsonResponse(
            "{\"bookingStatus\":\"preparing\"}"));
        var service = CreateService(handler);

        var status = await service.ChooseParcelProtectionAsync(
            transactionId, true, "option-1", 600, "choice-key");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            $"/api/mobile/transactions/{transactionId}/parcel-protection-election",
            request.Path);
        Assert.Equal("choice-key", request.IdempotencyKey);
        using var body = JsonDocument.Parse(request.Body);
        Assert.True(body.RootElement.GetProperty("addProtection").GetBoolean());
        Assert.Equal("option-1", body.RootElement.GetProperty("optionReference").GetString());
        Assert.Equal(600, body.RootElement.GetProperty(
            "disclosedCustomerPriceSatang").GetInt64());
        Assert.Equal("preparing", status);
    }

    [Fact]
    public async Task Prepare_maps_provider_ready_state_and_sends_idempotency_key()
    {
        var transactionId = Guid.NewGuid();
        var handler = new RecordingHandler(JsonResponse(
            "{\"requiresChoice\":false,\"addOnAvailable\":true,\"includedCoverageLimitSatang\":5000,\"maximumCoverageLimitSatang\":10000,\"customerPriceSatang\":600,\"optionReference\":\"option-1\",\"termsVersion\":\"terms-v1\",\"election\":\"Accepted\",\"bookingReady\":true,\"reconfirmationRequired\":false}"));
        var service = CreateService(handler);

        var result = await service.PrepareParcelProtectionAsync(
            transactionId, "prepare-key");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            $"/api/mobile/transactions/{transactionId}/parcel-protection/prepare",
            request.Path);
        Assert.Equal("prepare-key", request.IdempotencyKey);
        Assert.True(result.BookingReady);
        Assert.Equal("Accepted", result.Election);
        Assert.Equal(10_000, result.MaximumCoverageLimitSatang);
    }

    [Fact]
    public async Task Election_conflict_requires_a_fresh_reconfirmation()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(
            HttpStatusCode.Conflict));
        var service = CreateService(handler);

        var result = await service.ChooseParcelProtectionAsync(
            Guid.NewGuid(), false, null, null, "decline-key");

        Assert.Equal("reconfirmation_required", result);
    }

    private static ApiTransactionService CreateService(RecordingHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://mobile.test/")
        };
        return new ApiTransactionService(new MobileApiClient(
            new SingleClientFactory(client),
            new SessionStore(new StoredMobileSession(
                "access-token", "refresh-token",
                DateTimeOffset.UtcNow.AddHours(1)))));
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(
        params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new(responses);

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri?.AbsolutePath ?? "",
                request.Headers.GetValues("Idempotency-Key").SingleOrDefault(),
                request.Content is null
                    ? ""
                    : await request.Content.ReadAsStringAsync(cancellationToken)));
            return responses.Dequeue();
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        string Path,
        string? IdempotencyKey,
        string Body);

    private sealed class SessionStore(StoredMobileSession initial)
        : IMobileSessionStore
    {
        private StoredMobileSession? session = initial;

        public Task<StoredMobileSession?> GetAsync() => Task.FromResult(session);
        public Task SaveAsync(StoredMobileSession replacement)
        {
            session = replacement;
            return Task.CompletedTask;
        }
        public void Clear() => session = null;
    }
}
