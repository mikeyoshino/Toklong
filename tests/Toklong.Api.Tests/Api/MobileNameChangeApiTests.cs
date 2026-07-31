using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Toklong.Api.Security;
using Toklong.Application.Features.Authentication;
using Toklong.Domain.Accounts;
using Toklong.Domain.Buyers;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Api.Tests.Api;

public sealed class MobileNameChangeApiTests
    : IClassFixture<MobileApiFactory>
{
    private readonly MobileApiFactory factory;
    private static int accountSequence;

    public MobileNameChangeApiTests(MobileApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Authenticated_account_can_request_resume_and_verify_a_name_change()
    {
        using var buyer = await AuthenticatedBuyerAsync();

        using var eligibilityResponse = await buyer.Client.GetAsync(
            "/api/mobile/me/name-change/eligibility");
        eligibilityResponse.EnsureSuccessStatusCode();
        var eligibility = await eligibilityResponse.Content
            .ReadFromJsonAsync<EligibilityResponse>();
        Assert.NotNull(eligibility);
        Assert.True(eligibility.CanChange);
        Assert.Null(eligibility.NextAllowedAt);
        Assert.Equal("no-store", eligibilityResponse.Headers.CacheControl?.ToString());
        Assert.Contains(
            "nosniff",
            eligibilityResponse.Headers.GetValues("X-Content-Type-Options"));

        using var request = await buyer.Client.PostAsJsonAsync(
            "/api/mobile/me/name-change",
            new
            {
                FirstName = "สมชาย",
                LastName = "ใจดี",
                IdempotencyKey = NewKey()
            });
        request.EnsureSuccessStatusCode();
        var pending = await request.Content
            .ReadFromJsonAsync<PendingResponse>();
        Assert.NotNull(pending);
        Assert.Equal("สมชาย", pending.FirstName);
        Assert.Equal("ใจดี", pending.LastName);
        Assert.DoesNotContain("123456", await request.Content.ReadAsStringAsync());

        using var resumedClient = CreateClient(buyer.AccessToken);
        using var pendingResponse = await resumedClient.GetAsync(
            "/api/mobile/me/name-change");
        pendingResponse.EnsureSuccessStatusCode();
        var resumed = await pendingResponse.Content
            .ReadFromJsonAsync<PendingResponse>();
        Assert.NotNull(resumed);
        Assert.Equal(pending.ChallengeId, resumed.ChallengeId);

        using var verified = await buyer.Client.PostAsJsonAsync(
            $"/api/mobile/me/name-change/{pending.ChallengeId}/verify",
            new
            {
                Code = "123456",
                IdempotencyKey = NewKey()
            });
        verified.EnsureSuccessStatusCode();
        var completion = await verified.Content
            .ReadFromJsonAsync<VerifiedResponse>();
        Assert.NotNull(completion);
        Assert.Equal("สมชาย ใจดี", completion.DisplayName);

        using var profile = await buyer.Client.GetAsync("/api/mobile/me");
        profile.EnsureSuccessStatusCode();
        var current = await profile.Content.ReadFromJsonAsync<ProfileResponse>();
        Assert.NotNull(current);
        Assert.Equal("สมชาย", current.FirstName);
        Assert.Equal("ใจดี", current.LastName);
    }

    [Fact]
    public async Task Every_name_change_route_requires_authentication()
    {
        using var client = factory.CreateClient();
        var challengeId = Guid.NewGuid();
        var requests = new[]
        {
            new HttpRequestMessage(
                HttpMethod.Get,
                "/api/mobile/me/name-change/eligibility"),
            new HttpRequestMessage(
                HttpMethod.Get,
                "/api/mobile/me/name-change"),
            new HttpRequestMessage(
                HttpMethod.Post,
                "/api/mobile/me/name-change")
            {
                Content = JsonContent.Create(new
                {
                    FirstName = "สมชาย",
                    LastName = "ใจดี",
                    IdempotencyKey = Guid.NewGuid().ToString("N")
                })
            },
            new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/mobile/me/name-change/{challengeId}/resend")
            {
                Content = JsonContent.Create(new
                {
                    IdempotencyKey = Guid.NewGuid().ToString("N")
                })
            },
            new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/mobile/me/name-change/{challengeId}/verify")
            {
                Content = JsonContent.Create(new
                {
                    Code = "123456",
                    IdempotencyKey = Guid.NewGuid().ToString("N")
                })
            }
        };

        foreach (var request in requests)
        {
            using (request)
            {
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
        }
    }

    [Fact]
    public async Task Cooldown_is_returned_only_after_a_blocked_action_with_its_exact_timestamp()
    {
        using var buyer = await AuthenticatedBuyerAsync();
        var first = await RequestPendingAsync(
            buyer.Client,
            "สมชาย",
            "ใจดี");
        using (first)
        {
            first.EnsureSuccessStatusCode();
            var pending = await first.Content.ReadFromJsonAsync<PendingResponse>();
            Assert.NotNull(pending);
            using var verified = await VerifyAsync(
                buyer.Client,
                pending.ChallengeId,
                "123456");
            verified.EnsureSuccessStatusCode();
        }

        using var blocked = await RequestPendingAsync(
            buyer.Client,
            "สมหญิง",
            "ใจงาม");

        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var problem = await blocked.Content.ReadFromJsonAsync<NameChangeProblem>();
        Assert.NotNull(problem);
        Assert.Equal("name_change_cooldown", problem.Code);
        Assert.NotNull(problem.NextAllowedAt);
        Assert.Null(problem.RetryAfterSeconds);
    }

    [Fact]
    public async Task Another_account_cannot_enumerate_a_name_change_challenge()
    {
        using var owner = await AuthenticatedBuyerAsync();
        using var other = await AuthenticatedBuyerAsync();
        using var requested = await RequestPendingAsync(
            owner.Client,
            "สมชาย",
            "ใจดี");
        requested.EnsureSuccessStatusCode();
        var pending = await requested.Content.ReadFromJsonAsync<PendingResponse>();
        Assert.NotNull(pending);

        using var ownerPending = await owner.Client.GetAsync(
            "/api/mobile/me/name-change");
        ownerPending.EnsureSuccessStatusCode();
        using var otherPending = await other.Client.GetAsync(
            "/api/mobile/me/name-change");
        Assert.Equal(HttpStatusCode.NoContent, otherPending.StatusCode);

        using var foreignVerify = await VerifyAsync(
            other.Client,
            pending.ChallengeId,
            "123456");
        using var missingVerify = await VerifyAsync(
            other.Client,
            Guid.NewGuid(),
            "123456");
        var foreignVerifyProblem = await foreignVerify.Content
            .ReadFromJsonAsync<NameChangeProblem>();
        var missingVerifyProblem = await missingVerify.Content
            .ReadFromJsonAsync<NameChangeProblem>();
        Assert.Equal(missingVerify.StatusCode, foreignVerify.StatusCode);
        Assert.Equal(missingVerifyProblem, foreignVerifyProblem);
        Assert.NotNull(foreignVerifyProblem);
        Assert.Equal("name_change_challenge_unavailable", foreignVerifyProblem.Code);
        Assert.DoesNotContain(pending.ChallengeId.ToString(),
            await foreignVerify.Content.ReadAsStringAsync());

        using var foreignResend = await other.Client.PostAsJsonAsync(
            $"/api/mobile/me/name-change/{pending.ChallengeId}/resend",
            new { IdempotencyKey = NewKey() });
        using var missingResend = await other.Client.PostAsJsonAsync(
            $"/api/mobile/me/name-change/{Guid.NewGuid()}/resend",
            new { IdempotencyKey = NewKey() });
        var foreignResendProblem = await foreignResend.Content
            .ReadFromJsonAsync<NameChangeProblem>();
        var missingResendProblem = await missingResend.Content
            .ReadFromJsonAsync<NameChangeProblem>();
        Assert.Equal(missingResend.StatusCode, foreignResend.StatusCode);
        Assert.Equal(missingResendProblem, foreignResendProblem);
        Assert.NotNull(foreignResendProblem);
        Assert.Equal("name_change_challenge_unavailable", foreignResendProblem.Code);
    }

    [Fact]
    public async Task Authenticated_name_change_request_rate_limit_returns_a_consumer_safe_problem()
    {
        using var limitedFactory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("RateLimits:NameChangeRequestPermitLimit", "1"));
        using var buyer = await AuthenticatedBuyerAsync(limitedFactory);

        using var first = await RequestPendingAsync(
            buyer.Client,
            "สมชาย",
            "ใจดี");
        first.EnsureSuccessStatusCode();

        using var limited = await RequestPendingAsync(
            buyer.Client,
            "สมหญิง",
            "ใจงาม");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        var problem = await limited.Content
            .ReadFromJsonAsync<NameChangeProblem>();
        Assert.NotNull(problem);
        Assert.Equal("name_change_rate_limited", problem.Code);
        Assert.NotNull(problem.RetryAfterSeconds);
    }

    [Fact]
    public async Task Exact_request_replay_returns_the_same_challenge_without_sending_a_second_response_shape()
    {
        using var buyer = await AuthenticatedBuyerAsync();
        var key = NewKey();

        using var first = await RequestPendingAsync(
            buyer.Client,
            "สมชาย",
            "ใจดี",
            key);
        first.EnsureSuccessStatusCode();
        var firstPending = await first.Content.ReadFromJsonAsync<PendingResponse>();
        Assert.NotNull(firstPending);

        using var replay = await RequestPendingAsync(
            buyer.Client,
            "สมชาย",
            "ใจดี",
            key);
        replay.EnsureSuccessStatusCode();
        var replayPending = await replay.Content.ReadFromJsonAsync<PendingResponse>();
        Assert.NotNull(replayPending);
        Assert.Equal(firstPending.ChallengeId, replayPending.ChallengeId);
    }

    [Fact]
    public async Task Resend_cooldown_returns_retry_metadata_without_a_code_or_provider_detail()
    {
        using var buyer = await AuthenticatedBuyerAsync();
        var logCount = factory.LogMessages.Count;
        using var requested = await RequestPendingAsync(
            buyer.Client,
            "สมชาย",
            "ใจดี");
        requested.EnsureSuccessStatusCode();
        var pending = await requested.Content.ReadFromJsonAsync<PendingResponse>();
        Assert.NotNull(pending);

        using var resend = await buyer.Client.PostAsJsonAsync(
            $"/api/mobile/me/name-change/{pending.ChallengeId}/resend",
            new { IdempotencyKey = NewKey() });
        Assert.Equal(HttpStatusCode.TooManyRequests, resend.StatusCode);
        var body = await resend.Content.ReadAsStringAsync();
        var problem = await resend.Content.ReadFromJsonAsync<NameChangeProblem>();
        Assert.NotNull(problem);
        Assert.Equal("name_change_resend_cooldown", problem.Code);
        Assert.NotNull(problem.RetryAfterSeconds);
        Assert.DoesNotContain("123456", body);
        Assert.DoesNotContain("provider", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            factory.LogMessages.Skip(logCount),
            entry => entry.Contains("123456", StringComparison.Ordinal) ||
                     entry.Contains("provider", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Durable_five_per_day_send_limit_survives_a_new_api_scope()
    {
        using var buyer = await AuthenticatedBuyerAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider
                .GetRequiredService<ToklongDbContext>();
            var session = await database.MobileSessions.SingleAsync(item =>
                item.BuyerId == buyer.BuyerId);
            for (var index = 0; index < 5; index++)
            {
                var acceptedAt = DateTimeOffset.UtcNow.AddMinutes(
                    -10 - index);
                var challenge = AccountNameChangeChallenge.Create(
                    Guid.NewGuid(),
                    buyer.BuyerId,
                    null,
                    session.Id,
                    buyer.PhoneNumber,
                    "081-***-5678",
                    AccountName.Create("ชื่อเดิม", "ทดสอบ"),
                    NewKey(),
                    acceptedAt.AddSeconds(-1));
                challenge.MarkSendAccepted(
                    $"provider-{index}",
                    acceptedAt);
                database.AccountNameChangeChallenges.Add(challenge);
            }
            await database.SaveChangesAsync();
        }

        using var rejected = await RequestPendingAsync(
            buyer.Client,
            "สมชาย",
            "ใจดี");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        var problem = await rejected.Content
            .ReadFromJsonAsync<NameChangeProblem>();
        Assert.NotNull(problem);
        Assert.Equal("name_change_send_limit", problem.Code);
        Assert.NotNull(problem.RetryAfterSeconds);
    }

    [Fact]
    public async Task Exact_verification_replay_returns_the_original_completion()
    {
        using var buyer = await AuthenticatedBuyerAsync();
        using var requested = await RequestPendingAsync(
            buyer.Client,
            "สมชาย",
            "ใจดี");
        requested.EnsureSuccessStatusCode();
        var pending = await requested.Content.ReadFromJsonAsync<PendingResponse>();
        Assert.NotNull(pending);
        var key = NewKey();

        using var first = await VerifyAsync(
            buyer.Client,
            pending.ChallengeId,
            "123456",
            key);
        first.EnsureSuccessStatusCode();
        var completion = await first.Content.ReadFromJsonAsync<VerifiedResponse>();
        Assert.NotNull(completion);

        using var replay = await VerifyAsync(
            buyer.Client,
            pending.ChallengeId,
            "123456",
            key);
        replay.EnsureSuccessStatusCode();
        var replayed = await replay.Content.ReadFromJsonAsync<VerifiedResponse>();
        Assert.NotNull(replayed);
        Assert.Equal(completion.CompletedAt, replayed.CompletedAt);
        Assert.Equal(completion.DisplayName, replayed.DisplayName);
    }

    [Fact]
    public async Task Request_body_cannot_override_the_authenticated_account_subject()
    {
        using var owner = await AuthenticatedBuyerAsync();
        using var other = await AuthenticatedBuyerAsync();
        using var requested = await owner.Client.PostAsJsonAsync(
            "/api/mobile/me/name-change",
            new
            {
                FirstName = "สมชาย",
                LastName = "ใจดี",
                IdempotencyKey = NewKey(),
                BuyerId = other.BuyerId,
                SellerId = Guid.NewGuid(),
                SessionId = Guid.NewGuid(),
                PhoneNumber = other.PhoneNumber
            });
        requested.EnsureSuccessStatusCode();
        var pending = await requested.Content.ReadFromJsonAsync<PendingResponse>();
        Assert.NotNull(pending);

        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ToklongDbContext>();
        var challenge = await database.AccountNameChangeChallenges.SingleAsync(
            item => item.Id == pending.ChallengeId);
        Assert.Equal(owner.BuyerId, challenge.BuyerId);
        Assert.Equal(owner.PhoneNumber, challenge.PhoneNumber);
    }

    private async Task<BuyerSession> AuthenticatedBuyerAsync(
        WebApplicationFactory<Program>? host = null)
    {
        host ??= factory;
        var sequence = Interlocked.Increment(ref accountSequence);
        var phone = $"+669{sequence:D8}";
        var buyer = BuyerAccount.Create(
            phone,
            "ผู้ซื้อ เดิม",
            $"buyer-{sequence}@example.com",
            DateTimeOffset.UtcNow);
        await using var scope = host.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ToklongDbContext>();
        database.Buyers.Add(buyer);
        await database.SaveChangesAsync();
        var issued = await scope.ServiceProvider
            .GetRequiredService<MobileSessionTokenService>()
            .CreateAsync(
                new MobileSessionProfile(
                    buyer.Id,
                    null,
                    buyer.PhoneNumber,
                    buyer.FullName),
                CancellationToken.None);
        return new BuyerSession(
            buyer.Id,
            buyer.PhoneNumber,
            issued.AccessToken,
            CreateClient(issued.AccessToken, host));
    }

    private HttpClient CreateClient(
        string? accessToken = null,
        WebApplicationFactory<Program>? host = null)
    {
        var client = (host ?? factory).CreateClient();
        if (accessToken is not null)
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private static string NewKey() => Guid.NewGuid().ToString("N");

    private static Task<HttpResponseMessage> RequestPendingAsync(
        HttpClient client,
        string firstName,
        string lastName,
        string? idempotencyKey = null) =>
        client.PostAsJsonAsync(
            "/api/mobile/me/name-change",
            new
            {
                FirstName = firstName,
                LastName = lastName,
                IdempotencyKey = idempotencyKey ?? NewKey()
            });

    private static Task<HttpResponseMessage> VerifyAsync(
        HttpClient client,
        Guid challengeId,
        string code,
        string? idempotencyKey = null) =>
        client.PostAsJsonAsync(
            $"/api/mobile/me/name-change/{challengeId}/verify",
            new
            {
                Code = code,
                IdempotencyKey = idempotencyKey ?? NewKey()
            });

    private sealed record BuyerSession(
        Guid BuyerId,
        string PhoneNumber,
        string AccessToken,
        HttpClient Client) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }

    private sealed record EligibilityResponse(
        bool CanChange,
        DateTimeOffset? NextAllowedAt);

    private sealed record PendingResponse(
        Guid ChallengeId,
        string FirstName,
        string LastName);

    private sealed record VerifiedResponse(
        string FirstName,
        string LastName,
        string DisplayName,
        DateTimeOffset CompletedAt);

    private sealed record ProfileResponse(
        string? FirstName,
        string? LastName);

    private sealed record NameChangeProblem(
        string Code,
        DateTimeOffset? NextAllowedAt,
        int? RetryAfterSeconds);
}
