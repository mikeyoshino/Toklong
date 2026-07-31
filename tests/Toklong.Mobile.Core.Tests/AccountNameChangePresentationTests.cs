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
            "  นามสกุล  ");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/mobile/me/name-change", request.Path);
        Assert.Equal(
            "ชื่อ",
            JsonDocument.Parse(request.Body).RootElement
                .GetProperty("firstName").GetString());
        Assert.Equal(
            "นามสกุล",
            JsonDocument.Parse(request.Body).RootElement
                .GetProperty("lastName").GetString());
        AssertValidIdempotencyKey(request.Body);
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
        await service.ResendAccountNameChangeAsync(challengeId);

        Assert.Equal(challengeId, pending!.ChallengeId);
        Assert.Equal("08x-xxx-1234", pending.MaskedPhoneNumber);
        Assert.Equal("ชื่อ", pending.FirstName);
        Assert.Equal("นามสกุล", pending.LastName);
        Assert.Equal(
            DateTimeOffset.Parse("2026-07-31T13:00:00+07:00"),
            pending.ExpiresAt);
        Assert.Equal(
            DateTimeOffset.Parse("2026-07-31T12:01:00+07:00"),
            pending.ResendAvailableAt);
        Assert.Equal(5, pending.RemainingAttempts);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal("/api/mobile/me/name-change", handler.Requests[0].Path);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.Equal(
            $"/api/mobile/me/name-change/{challengeId}/resend",
            handler.Requests[1].Path);
        AssertValidIdempotencyKey(handler.Requests[1].Body);
    }

    [Fact]
    public async Task Pending_returns_null_only_for_no_content_and_verified_parses_every_contract_field()
    {
        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.NoContent),
            VerifiedResponse());
        var service = CreateService(handler);
        var challengeId = Guid.Parse("d2c553c7-efee-4f79-9552-5ea8d2ea8dea");

        var pending = await service.GetPendingAccountNameChangeAsync();
        var verified = await service.VerifyAccountNameChangeAsync(
            challengeId,
            "123456");

        Assert.Null(pending);
        Assert.Equal("ชื่อ", verified.FirstName);
        Assert.Equal("นามสกุล", verified.LastName);
        Assert.Equal("ชื่อ นามสกุล", verified.DisplayName);
        Assert.Equal(
            DateTimeOffset.Parse("2026-07-31T12:00:00+07:00"),
            verified.CompletedAt);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.Equal(
            $"/api/mobile/me/name-change/{challengeId}/verify",
            handler.Requests[1].Path);
        AssertValidIdempotencyKey(handler.Requests[1].Body);
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
        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.VerifyAccountNameChangeAsync(
                challengeId,
                " 123456 "));
        var verified = await service.VerifyAccountNameChangeAsync(
            challengeId,
            " 123456 ");

        Assert.Equal("ชื่อ นามสกุล", verified.DisplayName);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal(
            "123456",
            JsonDocument.Parse(request.Body).RootElement
                .GetProperty("code").GetString()));
        Assert.Equal(
            JsonDocument.Parse(handler.Requests[0].Body).RootElement
                .GetProperty("idempotencyKey").GetString(),
            JsonDocument.Parse(handler.Requests[1].Body).RootElement
                .GetProperty("idempotencyKey").GetString());
        AssertValidIdempotencyKey(handler.Requests[0].Body);
    }

    [Fact]
    public async Task Service_owns_request_key_replay_and_rotation_after_server_outcomes()
    {
        var firstChallenge = Guid.Parse("90ca20ad-c6e8-4b15-89f2-d194346fcf65");
        var secondChallenge = Guid.Parse("20bbac4e-dc16-430c-b3f8-7bf659da6d3f");
        var handler = new RecordingHandler(
            ProblemResponse(HttpStatusCode.ServiceUnavailable, "name_change_provider_outcome_unknown"),
            PendingResponse(firstChallenge),
            ProblemResponse(HttpStatusCode.BadRequest, "name_change_invalid_request"),
            PendingResponse(secondChallenge));
        var service = CreateService(handler);

        await Assert.ThrowsAsync<MobileApiRequestException>(
            () => service.RequestAccountNameChangeAsync("ชื่อ", "นามสกุล"));
        await service.RequestAccountNameChangeAsync("ชื่อ", "นามสกุล");
        await Assert.ThrowsAsync<MobileApiRequestException>(
            () => service.RequestAccountNameChangeAsync("ชื่อ", "นามสกุล"));
        await service.RequestAccountNameChangeAsync("ชื่อ", "นามสกุล");

        Assert.Equal(4, handler.Requests.Count);
        var replayed = IdempotencyKey(handler.Requests[0].Body);
        Assert.Equal(replayed, IdempotencyKey(handler.Requests[1].Body));
        Assert.NotEqual(replayed, IdempotencyKey(handler.Requests[2].Body));
        Assert.NotEqual(
            IdempotencyKey(handler.Requests[2].Body),
            IdempotencyKey(handler.Requests[3].Body));
    }

    [Theory]
    [InlineData("name_change_first_name_invalid", "firstName", AccountNameChangeErrorTarget.FirstNameInput, "กรุณาตรวจสอบชื่อ")]
    [InlineData("name_change_last_name_invalid", "lastName", AccountNameChangeErrorTarget.LastNameInput, "กรุณาตรวจสอบนามสกุล")]
    [InlineData("name_change_code_invalid", "code", AccountNameChangeErrorTarget.CodeInput, "กรอกรหัสยืนยัน 6 หลัก")]
    [InlineData("name_change_unchanged", null, AccountNameChangeErrorTarget.RequestAction, "ชื่อนี้เป็นชื่อปัจจุบันของคุณแล้ว")]
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

    public static IEnumerable<object[]> StableProblemCases()
    {
        yield return ["request", "name_change_cooldown", AccountNameChangeErrorKind.Cooldown, AccountNameChangeErrorTarget.BlockedAction, "ยังเปลี่ยนชื่อไม่ได้ กรุณาลองใหม่เมื่อถึงเวลาที่แจ้ง", false];
        yield return ["request", "name_change_first_name_invalid", AccountNameChangeErrorKind.Invalid, AccountNameChangeErrorTarget.FirstNameInput, "กรุณาตรวจสอบชื่อ", false];
        yield return ["request", "name_change_last_name_invalid", AccountNameChangeErrorKind.Invalid, AccountNameChangeErrorTarget.LastNameInput, "กรุณาตรวจสอบนามสกุล", false];
        yield return ["request", "name_change_unchanged", AccountNameChangeErrorKind.Unchanged, AccountNameChangeErrorTarget.RequestAction, "ชื่อนี้เป็นชื่อปัจจุบันของคุณแล้ว", false];
        yield return ["request", "name_change_idempotency_invalid", AccountNameChangeErrorKind.Invalid, AccountNameChangeErrorTarget.RequestAction, "คำขอไม่ถูกต้อง กรุณาลองใหม่", false];
        yield return ["request", "name_change_idempotency_conflict", AccountNameChangeErrorKind.Invalid, AccountNameChangeErrorTarget.RequestAction, "คำขอนี้ไม่ตรงกับข้อมูลเดิม กรุณาลองใหม่", false];
        yield return ["request", "name_change_provider_unavailable", AccountNameChangeErrorKind.Unavailable, AccountNameChangeErrorTarget.RequestAction, "บริการยืนยันชื่อยังไม่พร้อมใช้งาน กรุณาลองใหม่ภายหลัง", false];
        yield return ["request", "name_change_provider_outcome_unknown", AccountNameChangeErrorKind.Unavailable, AccountNameChangeErrorTarget.RequestAction, "กำลังตรวจสอบผลการยืนยัน กรุณาลองอีกครั้งด้วยคำขอเดิม", true];
        yield return ["resend", "name_change_provider_throttled", AccountNameChangeErrorKind.Cooldown, AccountNameChangeErrorTarget.ResendAction, "กรุณารอก่อนขอรหัสยืนยันอีกครั้ง", false];
        yield return ["request", "name_change_send_limit", AccountNameChangeErrorKind.SendLimit, AccountNameChangeErrorTarget.RequestAction, "ขอรหัสยืนยันครบจำนวนแล้ว กรุณาลองใหม่ภายหลัง", false];
        yield return ["resend", "name_change_resend_cooldown", AccountNameChangeErrorKind.Cooldown, AccountNameChangeErrorTarget.ResendAction, "กรุณารอก่อนขอรหัสยืนยันอีกครั้ง", false];
        yield return ["resend", "name_change_rate_limited", AccountNameChangeErrorKind.RateLimited, AccountNameChangeErrorTarget.ResendAction, "มีการทำรายการบ่อยเกินไป กรุณารอสักครู่ก่อนลองอีกครั้ง", false];
        yield return ["request", "name_change_invalid_request", AccountNameChangeErrorKind.Invalid, AccountNameChangeErrorTarget.RequestAction, "ไม่สามารถทำรายการเปลี่ยนชื่อได้ กรุณาตรวจสอบข้อมูลแล้วลองใหม่", false];
        yield return ["verification", "name_change_code_incorrect", AccountNameChangeErrorKind.Invalid, AccountNameChangeErrorTarget.CodeInput, "รหัสยืนยันไม่ถูกต้อง ลองตรวจสอบแล้วกรอกอีกครั้ง", false];
        yield return ["verification", "name_change_code_invalid", AccountNameChangeErrorKind.Invalid, AccountNameChangeErrorTarget.CodeInput, "กรอกรหัสยืนยัน 6 หลัก", false];
        yield return ["verification", "name_change_locked", AccountNameChangeErrorKind.Locked, AccountNameChangeErrorTarget.NewRequestAction, "กรอกรหัสไม่ถูกต้องครบจำนวนแล้ว กรุณาขอรหัสใหม่", false];
        yield return ["verification", "name_change_expired", AccountNameChangeErrorKind.Expired, AccountNameChangeErrorTarget.NewRequestAction, "รหัสยืนยันหมดอายุแล้ว กรุณาขอรหัสใหม่", false];
        yield return ["resend", "name_change_challenge_unavailable", AccountNameChangeErrorKind.Missing, AccountNameChangeErrorTarget.AccountReturnAction, "คำขอเปลี่ยนชื่อนี้ใช้ไม่ได้แล้ว กรุณากลับไปหน้าบัญชี", false];
        yield return ["verification", "name_change_challenge_inactive", AccountNameChangeErrorKind.Missing, AccountNameChangeErrorTarget.AccountReturnAction, "คำขอเปลี่ยนชื่อนี้ใช้ไม่ได้แล้ว กรุณากลับไปหน้าบัญชี", false];
    }

    [Theory]
    [MemberData(nameof(StableProblemCases))]
    public void Every_stable_problem_code_has_consumer_owned_presentation(
        string source,
        string code,
        AccountNameChangeErrorKind expectedKind,
        AccountNameChangeErrorTarget expectedTarget,
        string expectedMessage,
        bool expectedSameKeyRetry)
    {
        var exception = new MobileApiRequestException(
            HttpStatusCode.TooManyRequests,
            "untrusted provider/database detail",
            TimeSpan.FromSeconds(9),
            code,
            remainingAttempts: 3,
            nextAllowedAt: DateTimeOffset.Parse("2026-09-30T09:45:00+07:00"));

        var notice = source switch
        {
            "request" => AccountNameChangeErrorPresentation.ForRequest(exception),
            "resend" => AccountNameChangeErrorPresentation.ForResend(exception),
            "verification" => AccountNameChangeErrorPresentation.ForVerification(exception),
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };

        Assert.Equal(expectedKind, notice.Kind);
        Assert.Equal(expectedTarget, notice.Target);
        Assert.Equal(expectedMessage, notice.Message);
        Assert.Equal(TimeSpan.FromSeconds(9), notice.RetryAfter);
        Assert.Equal(3, notice.RemainingAttempts);
        Assert.Equal(
            DateTimeOffset.Parse("2026-09-30T09:45:00+07:00"),
            notice.NextAllowedAt);
        Assert.Equal(expectedSameKeyRetry, notice.RetryWithSameIdempotencyKey);
        Assert.DoesNotContain("untrusted", notice.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unknown_problem_code_uses_a_safe_generic_presentation()
    {
        var notice = AccountNameChangeErrorPresentation.ForVerification(
            new MobileApiRequestException(
                HttpStatusCode.BadRequest,
                "provider secret response",
                null,
                "name_change_future_code"));

        Assert.Equal(AccountNameChangeErrorKind.Invalid, notice.Kind);
        Assert.Equal(AccountNameChangeErrorTarget.VerificationAction, notice.Target);
        Assert.Equal("เปลี่ยนชื่อไม่สำเร็จ กรุณาลองอีกครั้ง", notice.Message);
        Assert.False(notice.RetryWithSameIdempotencyKey);
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
            () => service.RequestAccountNameChangeAsync("ชื่อ", "นามสกุล"));

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
            new PushRegistrationStub(),
            new AccountNameChangeOperationState(
                new AuthenticatedSessionBoundary()));
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

    private static HttpResponseMessage ProblemResponse(
        HttpStatusCode status,
        string code) =>
        new(status)
        {
            Content = new StringContent(
                $$"""{"title":"ทำรายการไม่สำเร็จ","detail":"untrusted","code":"{{code}}"}""",
                Encoding.UTF8,
                "application/problem+json")
        };

    private static void AssertJson(string expected, string actual)
    {
        using var expectedDocument = JsonDocument.Parse(expected);
        using var actualDocument = JsonDocument.Parse(actual);
        Assert.True(JsonElement.DeepEquals(expectedDocument.RootElement, actualDocument.RootElement));
    }

    private static void AssertValidIdempotencyKey(string body)
    {
        var key = IdempotencyKey(body);
        Assert.NotNull(key);
        Assert.True(Guid.TryParseExact(key, "N", out _));
        Assert.Equal(32, key.Length);
    }

    private static string? IdempotencyKey(string body) =>
        JsonDocument.Parse(body).RootElement
            .GetProperty("idempotencyKey").GetString();

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
