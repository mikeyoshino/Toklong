using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Toklong.Mobile.Core;
using Toklong.Mobile.Services;

namespace Toklong.Mobile.Core.Tests;

public sealed class MobileAuthenticationServiceEmailChangeTests
{
    [Fact]
    public async Task GetPendingEmailChangeAsync_returns_null_for_no_content()
    {
        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.NoContent));
        var service = CreateService(handler);

        var pending = await service.GetPendingEmailChangeAsync();

        Assert.Null(pending);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/api/mobile/me/email-change", request.Path);
        Assert.Equal("Bearer", request.Authorization?.Scheme);
        Assert.Equal("access-token", request.Authorization?.Parameter);
    }

    [Fact]
    public async Task RequestEmailChangeAsync_posts_trimmed_email_and_idempotency_key()
    {
        var challengeId = Guid.Parse("10b0f6af-3ed4-4e07-b821-ccfe4491e5ce");
        var handler = new RecordingHandler(PendingResponse(challengeId));
        var service = CreateService(handler);

        var pending = await service.RequestEmailChangeAsync(
            "  buyer.next@example.test  ",
            "request-key");

        Assert.Equal(challengeId, pending.ChallengeId);
        Assert.Equal("b***@example.test", pending.MaskedEmail);
        Assert.Equal(
            DateTimeOffset.Parse("2026-07-29T13:00:00+07:00"),
            pending.ExpiresAt);
        Assert.Equal(
            DateTimeOffset.Parse("2026-07-29T12:01:00+07:00"),
            pending.ResendAvailableAt);
        Assert.Equal(5, pending.RemainingAttempts);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/mobile/me/email-change", request.Path);
        Assert.Equal("access-token", request.Authorization?.Parameter);
        AssertJson(
            "{\"email\":\"buyer.next@example.test\",\"idempotencyKey\":\"request-key\"}",
            request.Body);
    }

    [Fact]
    public async Task ResendEmailChangeAsync_posts_idempotency_key_to_challenge_route()
    {
        var challengeId = Guid.Parse("b70a0499-6900-4c25-a454-90dcba526f97");
        var handler = new RecordingHandler(PendingResponse(challengeId));
        var service = CreateService(handler);

        await service.ResendEmailChangeAsync(challengeId, "resend-key");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            $"/api/mobile/me/email-change/{challengeId}/resend",
            request.Path);
        Assert.Equal("access-token", request.Authorization?.Parameter);
        AssertJson("{\"idempotencyKey\":\"resend-key\"}", request.Body);
    }

    [Fact]
    public async Task Rate_limited_resend_preserves_status_and_retry_after_for_ui_copy()
    {
        var challengeId =
            Guid.Parse("b70a0499-6900-4c25-a454-90dcba526f97");
        var response =
            new HttpResponseMessage(
                HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent(
                    "{}",
                    Encoding.UTF8,
                    "application/json")
            };
        response.Headers.RetryAfter =
            new RetryConditionHeaderValue(
                TimeSpan.FromSeconds(17));
        var service = CreateService(
            new RecordingHandler(response));

        var exception =
            await Assert.ThrowsAsync<MobileApiRequestException>(
                () => service.ResendEmailChangeAsync(
                    challengeId,
                    "resend-key"));

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            exception.StatusCode);
        Assert.Equal(
            TimeSpan.FromSeconds(17),
            exception.RetryAfter);
    }

    [Fact]
    public async Task VerifyEmailChangeAsync_posts_trimmed_code_and_returns_confirmed_email()
    {
        var challengeId = Guid.Parse("44a35e35-4a68-4ed7-a23b-4a4016a5faf5");
        var handler = new RecordingHandler(JsonResponse(
            "{\"email\":\"buyer.next@example.test\",\"completedAt\":\"2026-07-29T12:00:00+07:00\"}"));
        var service = CreateService(handler);

        var email = await service.VerifyEmailChangeAsync(
            challengeId,
            " 123456 ",
            "verify-key");

        Assert.Equal("buyer.next@example.test", email);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            $"/api/mobile/me/email-change/{challengeId}/verify",
            request.Path);
        Assert.Equal("access-token", request.Authorization?.Parameter);
        AssertJson(
            "{\"code\":\"123456\",\"idempotencyKey\":\"verify-key\"}",
            request.Body);
    }

    [Fact]
    public async Task RequestEmailChangeAsync_refreshes_and_replays_unchanged_request_after_unauthorized()
    {
        var challengeId = Guid.Parse("738f3752-863a-430e-a461-fc608a1e7e90");
        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.Unauthorized),
            JsonResponse("{\"accessToken\":\"refreshed-token\",\"refreshToken\":\"refreshed-refresh\",\"accessTokenExpiresAt\":\"2030-01-01T00:00:00+00:00\",\"displayName\":\"Buyer\",\"phoneNumber\":\"0812345678\",\"canBuy\":true,\"canSell\":false}"),
            PendingResponse(challengeId));
        var service = CreateService(handler);

        await service.RequestEmailChangeAsync(
            " buyer.next@example.test ",
            "replay-key");

        Assert.Equal(3, handler.Requests.Count);
        var first = handler.Requests[0];
        var refresh = handler.Requests[1];
        var retried = handler.Requests[2];
        Assert.Equal("/api/mobile/me/email-change", first.Path);
        Assert.Equal("access-token", first.Authorization?.Parameter);
        Assert.Equal("/api/mobile/auth/refresh", refresh.Path);
        Assert.Equal("/api/mobile/me/email-change", retried.Path);
        Assert.Equal("refreshed-token", retried.Authorization?.Parameter);
        AssertJson(
            "{\"email\":\"buyer.next@example.test\",\"idempotencyKey\":\"replay-key\"}",
            first.Body);
        Assert.Equal(first.Body, retried.Body);
    }

    private static MobileAuthenticationService CreateService(
        RecordingHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://mobile-api.test/")
        };
        return new MobileAuthenticationService(
            new MobileApiClient(
                new SingleClientFactory(client),
                new SessionStore(new StoredMobileSession(
                    "access-token",
                    "refresh-token",
                    DateTimeOffset.UtcNow.AddHours(1)))),
            new InMemoryMobileSessionStore(),
            new PendingRegistrationStoreStub(),
            new InstallationIdStub(),
            new PushRegistrationStub());
    }

    private static HttpResponseMessage PendingResponse(Guid challengeId) =>
        JsonResponse($"{{\"challengeId\":\"{challengeId}\",\"maskedEmail\":\"b***@example.test\",\"expiresAt\":\"2026-07-29T13:00:00+07:00\",\"resendAvailableAt\":\"2026-07-29T12:01:00+07:00\",\"remainingAttempts\":5}}");

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static void AssertJson(string expected, string actual)
    {
        using var expectedDocument = JsonDocument.Parse(expected);
        using var actualDocument = JsonDocument.Parse(actual);
        Assert.True(
            JsonElement.DeepEquals(
                expectedDocument.RootElement,
                actualDocument.RootElement),
            $"Expected JSON {expected} but got {actual}.");
    }

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
                request.Headers.Authorization,
                request.Content is null
                    ? ""
                    : await request.Content.ReadAsStringAsync(cancellationToken)));
            return responses.Dequeue();
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        string Path,
        AuthenticationHeaderValue? Authorization,
        string Body);

    private sealed class SessionStore(StoredMobileSession session)
        : IMobileSessionStore
    {
        private StoredMobileSession? session = session;

        public Task<StoredMobileSession?> GetAsync() =>
            Task.FromResult(session);

        public Task SaveAsync(StoredMobileSession replacement)
        {
            session = replacement;
            return Task.CompletedTask;
        }

        public void Clear() => session = null;
    }

    private sealed class PendingRegistrationStoreStub : IPendingRegistrationStore
    {
        public Task<PendingMobileRegistration?> GetValidAsync(
            DateTimeOffset now) =>
            throw new NotSupportedException();

        public Task SaveAsync(PendingMobileRegistration pending) =>
            throw new NotSupportedException();

        public void Clear() =>
            throw new NotSupportedException();
    }

    private sealed class InstallationIdStub : IInstallationIdProvider
    {
        public string GetInstallationId() => "installation-id";
    }

    private sealed class PushRegistrationStub : IPushRegistrationService
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UploadTokenAsync(
            string pushToken,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UnregisterAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
