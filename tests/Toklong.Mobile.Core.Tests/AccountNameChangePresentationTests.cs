using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Toklong.Mobile.Core;
using Toklong.Mobile.Services;

namespace Toklong.Mobile.Core.Tests;

public sealed class AccountNameChangePresentationTests
{
    [Fact]
    public async Task Blocked_eligibility_preserves_the_exact_server_instant()
    {
        var nextAllowedAt = DateTimeOffset.Parse("2026-09-30T09:45:00+07:00");
        var service = CreateService(new RecordingHandler(JsonResponse(
            $$"""{"canChange":false,"nextAllowedAt":"{{nextAllowedAt:O}}"}""")));

        var eligibility = await service.GetAccountNameChangeEligibilityAsync();

        Assert.False(eligibility.CanChange);
        Assert.Equal(nextAllowedAt, eligibility.NextAllowedAt);
        Assert.Equal(
            nextAllowedAt,
            AccountNameChangeErrorPresentation
                .BlockedNotice(eligibility)!
                .NextAllowedAt);
    }

    [Fact]
    public async Task Request_posts_trimmed_fields_with_the_callers_stable_key()
    {
        var challengeId = Guid.Parse("f26b7734-0219-464e-8ec9-64265c6505af");
        var handler = new RecordingHandler(PendingResponse(challengeId));
        var service = CreateService(handler);

        await service.RequestAccountNameChangeAsync(
            "  ชื่อ  ",
            "  นามสกุล  ",
            "2e1fb0d1b9ce4edca850b17318191e16");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/mobile/me/name-change", request.Path);
        AssertJson(
            "{\"firstName\":\"ชื่อ\",\"lastName\":\"นามสกุล\",\"idempotencyKey\":\"2e1fb0d1b9ce4edca850b17318191e16\"}",
            request.Body);
    }

    [Fact]
    public async Task Pending_and_resend_use_the_authenticated_name_change_contract()
    {
        var challengeId = Guid.Parse("49c50160-6200-4ba9-bf07-723c1b5b3c51");
        var handler = new RecordingHandler(
            PendingResponse(challengeId),
            PendingResponse(challengeId));
        var service = CreateService(handler);

        var pending = await service.GetPendingAccountNameChangeAsync();
        await service.ResendAccountNameChangeAsync(
            challengeId,
            "a194b66d1fc14776b1b153c2749600b9");

        Assert.Equal(challengeId, pending!.ChallengeId);
        Assert.Equal("08x-xxx-1234", pending.MaskedPhoneNumber);
        Assert.Equal(5, pending.RemainingAttempts);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal("/api/mobile/me/name-change", handler.Requests[0].Path);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.Equal(
            $"/api/mobile/me/name-change/{challengeId}/resend",
            handler.Requests[1].Path);
        AssertJson(
            "{\"idempotencyKey\":\"a194b66d1fc14776b1b153c2749600b9\"}",
            handler.Requests[1].Body);
    }

    [Fact]
    public void Completion_state_can_be_consumed_only_once_by_the_current_session()
    {
        var session = new AuthenticatedSessionBoundary();
        var completion = new AccountNameChangeCompletionState(session);
        var generation = session.Capture();

        completion.RecordCompletion(generation);

        Assert.True(completion.TryConsume(generation));
        Assert.False(completion.TryConsume(generation));
        completion.RecordCompletion(generation);
        session.Reset();
        Assert.False(completion.TryConsume(generation));
    }

    [Fact]
    public async Task Verification_network_retry_reuses_the_callers_key_and_trims_the_code()
    {
        var challengeId = Guid.Parse("4fea5108-3673-43a4-824e-c6ce3a8f9eaa");
        var handler = new RecordingHandler(
            new HttpRequestException("network"),
            VerifiedResponse());
        var service = CreateService(handler);
        const string key = "dc0664be3ed943ccaf79953af01808bc";

        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.VerifyAccountNameChangeAsync(
                challengeId,
                " 123456 ",
                key));
        var verified = await service.VerifyAccountNameChangeAsync(
            challengeId,
            " 123456 ",
            key);

        Assert.Equal("ชื่อ นามสกุล", verified.DisplayName);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(
            handler.Requests,
            request => AssertJson(
                "{\"code\":\"123456\",\"idempotencyKey\":\"dc0664be3ed943ccaf79953af01808bc\"}",
                request.Body));
    }

    [Theory]
    [InlineData("name_change_first_name_invalid", "firstName", AccountNameChangeErrorTarget.FirstNameInput, "กรุณาตรวจสอบชื่อ")]
    [InlineData("name_change_last_name_invalid", "lastName", AccountNameChangeErrorTarget.LastNameInput, "กรุณาตรวจสอบนามสกุล")]
    [InlineData("name_change_code_invalid", "code", AccountNameChangeErrorTarget.CodeInput, "กรอกรหัสยืนยัน 6 หลัก")]
    [InlineData("name_change_unchanged", null, AccountNameChangeErrorTarget.VerificationAction, "ชื่อนี้เป็นชื่อปัจจุบันของคุณแล้ว")]
    public void Stable_problem_codes_map_to_owned_Thai_copy(
        string code,
        string? field,
        AccountNameChangeErrorTarget target,
        string copy)
    {
        var error = new MobileApiRequestException(
            HttpStatusCode.UnprocessableEntity,
            "provider or database detail must never be displayed",
            null,
            code,
            field);

        var notice = AccountNameChangeErrorPresentation.ForRequest(error);

        Assert.Equal(target, notice.Target);
        Assert.Equal(copy, notice.Message);
        Assert.DoesNotContain("provider", notice.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("database", notice.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verification_copy_preserves_bounded_attempt_and_retry_metadata()
    {
        var error = new MobileApiRequestException(
            HttpStatusCode.UnprocessableEntity,
            "untrusted detail",
            TimeSpan.FromSeconds(19),
            "name_change_code_incorrect",
            "code",
            remainingAttempts: 3);

        var notice = AccountNameChangeErrorPresentation.ForVerification(error);

        Assert.Equal(AccountNameChangeErrorTarget.CodeInput, notice.Target);
        Assert.Equal(3, notice.RemainingAttempts);
        Assert.Equal(TimeSpan.FromSeconds(19), notice.RetryAfter);
        Assert.Equal(
            "รหัสยืนยันไม่ถูกต้อง ลองตรวจสอบแล้วกรอกอีกครั้ง",
            notice.Message);
    }

    [Fact]
    public async Task Problem_parser_retains_only_stable_problem_metadata()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent(
                "{\"title\":\"ทำรายการไม่สำเร็จ\",\"detail\":\"untrusted\",\"code\":\"name_change_cooldown\",\"field\":null,\"remainingAttempts\":null,\"nextAllowedAt\":\"2026-09-30T09:45:00+07:00\",\"retryAfterSeconds\":13}",
                Encoding.UTF8,
                "application/problem+json")
        };
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(17));
        var service = CreateService(new RecordingHandler(response));

        var exception = await Assert.ThrowsAsync<MobileApiRequestException>(
            () => service.RequestAccountNameChangeAsync("ชื่อ", "นามสกุล", Guid.NewGuid().ToString("N")));

        Assert.Equal("name_change_cooldown", exception.Code);
        Assert.Equal(TimeSpan.FromSeconds(17), exception.RetryAfter);
        Assert.Equal(DateTimeOffset.Parse("2026-09-30T09:45:00+07:00"), exception.NextAllowedAt);
        var notice = AccountNameChangeErrorPresentation.ForRequest(exception);
        Assert.Equal(AccountNameChangeErrorTarget.BlockedAction, notice.Target);
        Assert.Equal(exception.NextAllowedAt, notice.NextAllowedAt);
    }

    private static MobileAuthenticationService CreateService(RecordingHandler handler)
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

    private static HttpResponseMessage PendingResponse(Guid challengeId) => JsonResponse(
        $$"""{"challengeId":"{{challengeId}}","maskedPhoneNumber":"08x-xxx-1234","firstName":"ชื่อ","lastName":"นามสกุล","expiresAt":"2026-07-31T13:00:00+07:00","resendAvailableAt":"2026-07-31T12:01:00+07:00","remainingAttempts":5}""");

    private static HttpResponseMessage VerifiedResponse() => JsonResponse(
        "{\"firstName\":\"ชื่อ\",\"lastName\":\"นามสกุล\",\"displayName\":\"ชื่อ นามสกุล\",\"completedAt\":\"2026-07-31T12:00:00+07:00\"}");

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static void AssertJson(string expected, string actual)
    {
        using var expectedDocument = JsonDocument.Parse(expected);
        using var actualDocument = JsonDocument.Parse(actual);
        Assert.True(JsonElement.DeepEquals(expectedDocument.RootElement, actualDocument.RootElement));
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(params object[] responses) : HttpMessageHandler
    {
        private readonly Queue<object> responses = new(responses);
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri?.AbsolutePath ?? "",
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            var next = responses.Dequeue();
            if (next is Exception exception)
                throw exception;
            return (HttpResponseMessage)next;
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, string Body);

    private sealed class SessionStore(StoredMobileSession session) : IMobileSessionStore
    {
        private StoredMobileSession? session = session;
        public Task<StoredMobileSession?> GetAsync() => Task.FromResult(session);
        public Task SaveAsync(StoredMobileSession replacement) { session = replacement; return Task.CompletedTask; }
        public void Clear() => session = null;
    }

    private sealed class PendingRegistrationStoreStub : IPendingRegistrationStore
    {
        public Task<PendingMobileRegistration?> GetValidAsync(DateTimeOffset now) => throw new NotSupportedException();
        public Task SaveAsync(PendingMobileRegistration pending) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
    }

    private sealed class InstallationIdStub : IInstallationIdProvider
    {
        public string GetInstallationId() => "installation-id";
    }

    private sealed class PushRegistrationStub : IPushRegistrationService
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UploadTokenAsync(string pushToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UnregisterAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
