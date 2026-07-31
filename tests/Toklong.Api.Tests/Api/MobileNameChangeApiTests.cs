using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Toklong.Api.Security;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Features.Accounts.NameChanges;
using Toklong.Application.Features.Authentication;
using Toklong.Domain.Accounts;
using Toklong.Domain.Buyers;
using Toklong.Domain.Sellers;
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
        AssertSecurityHeaders(request);

        using var resumedClient = CreateClient(buyer.AccessToken);
        using var pendingResponse = await resumedClient.GetAsync(
            "/api/mobile/me/name-change");
        pendingResponse.EnsureSuccessStatusCode();
        var resumed = await pendingResponse.Content
            .ReadFromJsonAsync<PendingResponse>();
        Assert.NotNull(resumed);
        Assert.Equal(pending.ChallengeId, resumed.ChallengeId);
        AssertSecurityHeaders(pendingResponse);

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
        AssertSecurityHeaders(verified);

        using var profile = await buyer.Client.GetAsync("/api/mobile/me");
        profile.EnsureSuccessStatusCode();
        var current = await profile.Content.ReadFromJsonAsync<ProfileResponse>();
        Assert.NotNull(current);
        Assert.Equal("สมชาย", current.FirstName);
        Assert.Equal("ใจดี", current.LastName);
        AssertSecurityHeaders(profile);
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
    public async Task Blocked_eligibility_exposes_exact_time_while_profile_and_pending_hide_cooldown_timing()
    {
        using var buyer = await AuthenticatedBuyerAsync();
        using var requested = await RequestPendingAsync(
            buyer.Client,
            "สมชาย",
            "ใจดี");
        requested.EnsureSuccessStatusCode();
        var pending = await requested.Content
            .ReadFromJsonAsync<PendingResponse>();
        Assert.NotNull(pending);
        var changedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var expected = AccountNameChangeCalendar
            .AddTwoBangkokCalendarMonths(changedAt);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider
                .GetRequiredService<ToklongDbContext>();
            var stored = await database.Buyers.SingleAsync(item =>
                item.Id == buyer.BuyerId);
            stored.ApplyAccountName(
                AccountName.Create("ชื่อ", "ปัจจุบัน"),
                changedAt);
            await database.SaveChangesAsync();
        }

        using var eligibilityResponse = await buyer.Client.GetAsync(
            "/api/mobile/me/name-change/eligibility");
        eligibilityResponse.EnsureSuccessStatusCode();
        var eligibility = await eligibilityResponse.Content
            .ReadFromJsonAsync<EligibilityResponse>();
        Assert.NotNull(eligibility);
        Assert.False(eligibility.CanChange);
        Assert.Equal(expected, eligibility.NextAllowedAt);

        using var profile = await buyer.Client.GetAsync("/api/mobile/me");
        profile.EnsureSuccessStatusCode();
        var profileBody = await profile.Content.ReadAsStringAsync();
        Assert.DoesNotContain(
            "nextAllowedAt",
            profileBody,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "cooldown",
            profileBody,
            StringComparison.OrdinalIgnoreCase);

        using var pendingResponse = await buyer.Client.GetAsync(
            "/api/mobile/me/name-change");
        pendingResponse.EnsureSuccessStatusCode();
        var pendingBody = await pendingResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(
            "nextAllowedAt",
            pendingBody,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "cooldown",
            pendingBody,
            StringComparison.OrdinalIgnoreCase);
        AssertSecurityHeaders(eligibilityResponse);
        AssertSecurityHeaders(profile);
        AssertSecurityHeaders(pendingResponse);
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
        AssertSecurityHeaders(limited);
    }

    [Fact]
    public async Task Rate_limit_retry_metadata_uses_the_rejected_lease_instead_of_a_hardcoded_window()
    {
        using var limitedFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimits:NameChangeRequestPermitLimit", "1");
            builder.UseSetting("RateLimits:NameChangeRequestWindowSeconds", "7");
        });
        using var buyer = await AuthenticatedBuyerAsync(limitedFactory);

        using var first = await RequestPendingAsync(
            buyer.Client,
            "สมชาย",
            "ใจดี");
        first.EnsureSuccessStatusCode();

        using var limited = await buyer.Client.PostAsJsonAsync(
            $"/api/mobile/me/name-change/{Guid.NewGuid()}/resend",
            new { IdempotencyKey = NewKey() });

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        var problem = await limited.Content
            .ReadFromJsonAsync<NameChangeProblem>();
        Assert.NotNull(problem);
        Assert.InRange(problem.RetryAfterSeconds!.Value, 1, 7);
        Assert.Equal(
            problem.RetryAfterSeconds.Value.ToString(),
            limited.Headers.GetValues("Retry-After").Single());
        AssertSecurityHeaders(limited);
    }

    [Fact]
    public async Task Request_and_resend_limit_is_shared_by_same_phone_roles_and_partitioned_by_network()
    {
        using var limitedFactory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("RateLimits:NameChangeRequestPermitLimit", "1"));
        var sessions = await AuthenticatedRoleSessionsAsync(limitedFactory);
        using var buyer = sessions.Buyer;
        using var seller = sessions.Seller;
        buyer.Client.DefaultRequestHeaders.Add(
            "X-Test-Remote-Address",
            "192.0.2.10");
        seller.Client.DefaultRequestHeaders.Add(
            "X-Test-Remote-Address",
            "192.0.2.10");

        using var first = await RequestPendingAsync(
            buyer.Client,
            "สมชาย",
            "ใจดี");
        first.EnsureSuccessStatusCode();

        using var sameAccountAndNetwork = await seller.Client.PostAsJsonAsync(
            $"/api/mobile/me/name-change/{Guid.NewGuid()}/resend",
            new { IdempotencyKey = NewKey() });
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            sameAccountAndNetwork.StatusCode);

        using var otherNetwork = CreateClient(
            seller.AccessToken,
            limitedFactory);
        otherNetwork.DefaultRequestHeaders.Add(
            "X-Test-Remote-Address",
            "198.51.100.20");
        using var independent = await otherNetwork.PostAsJsonAsync(
            $"/api/mobile/me/name-change/{Guid.NewGuid()}/resend",
            new { IdempotencyKey = NewKey() });
        Assert.Equal(HttpStatusCode.NotFound, independent.StatusCode);
    }

    [Fact]
    public async Task Verify_limit_is_shared_by_same_phone_roles_and_partitioned_by_network()
    {
        using var limitedFactory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("RateLimits:NameChangeVerifyPermitLimit", "1"));
        var sessions = await AuthenticatedRoleSessionsAsync(limitedFactory);
        using var buyer = sessions.Buyer;
        using var seller = sessions.Seller;
        buyer.Client.DefaultRequestHeaders.Add(
            "X-Test-Remote-Address",
            "192.0.2.30");
        seller.Client.DefaultRequestHeaders.Add(
            "X-Test-Remote-Address",
            "192.0.2.30");

        using var first = await VerifyAsync(
            buyer.Client,
            Guid.NewGuid(),
            "123456");
        Assert.Equal(HttpStatusCode.NotFound, first.StatusCode);

        using var limited = await VerifyAsync(
            seller.Client,
            Guid.NewGuid(),
            "123456");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        var problem = await limited.Content
            .ReadFromJsonAsync<NameChangeProblem>();
        Assert.NotNull(problem);
        Assert.Equal("name_change_rate_limited", problem.Code);
        Assert.InRange(problem.RetryAfterSeconds!.Value, 1, 600);
        AssertSecurityHeaders(limited);

        using var otherNetwork = CreateClient(
            seller.AccessToken,
            limitedFactory);
        otherNetwork.DefaultRequestHeaders.Add(
            "X-Test-Remote-Address",
            "198.51.100.30");
        using var independent = await VerifyAsync(
            otherNetwork,
            Guid.NewGuid(),
            "123456");
        Assert.Equal(HttpStatusCode.NotFound, independent.StatusCode);
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
    public async Task Durable_five_per_day_send_limit_crosses_buyer_and_seller_api_scopes_for_the_same_phone()
    {
        var sessions = await AuthenticatedRoleSessionsAsync(factory);
        using var buyer = sessions.Buyer;
        using var seller = sessions.Seller;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider
                .GetRequiredService<ToklongDbContext>();
            var session = await database.MobileSessions.SingleAsync(item =>
                item.BuyerId == buyer.BuyerId &&
                item.SellerId == null);
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
                challenge.Supersede(acceptedAt.AddSeconds(61));
                database.AccountNameChangeChallenges.Add(challenge);
            }
            await database.SaveChangesAsync();
        }

        using var rejected = await RequestPendingAsync(
            seller.Client,
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

    [Fact]
    public async Task Validation_and_code_errors_have_stable_field_contracts()
    {
        using var buyer = await AuthenticatedBuyerAsync();
        foreach (var item in new[]
        {
            new { FirstName = "123", LastName = "ใจดี", Field = "firstName", Code = "name_change_first_name_invalid" },
            new { FirstName = "สมชาย", LastName = "123", Field = "lastName", Code = "name_change_last_name_invalid" }
        })
        {
            using var response = await RequestPendingAsync(
                buyer.Client, item.FirstName, item.LastName);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<NameChangeProblem>();
            Assert.NotNull(problem);
            Assert.Equal(item.Code, problem.Code);
            Assert.Equal(item.Field, problem.Field);
        }

        using var badKey = await RequestPendingAsync(
            buyer.Client, "สมชาย", "ใจดี", "bad");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badKey.StatusCode);
        Assert.Equal("name_change_idempotency_invalid", (await badKey.Content.ReadFromJsonAsync<NameChangeProblem>())!.Code);

        using var requested = await RequestPendingAsync(buyer.Client, "สมชาย", "ใจดี");
        requested.EnsureSuccessStatusCode();
        var pending = (await requested.Content.ReadFromJsonAsync<PendingResponse>())!;
        using var malformed = await VerifyAsync(buyer.Client, pending.ChallengeId, "12");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, malformed.StatusCode);
        var malformedProblem = await malformed.Content.ReadFromJsonAsync<NameChangeProblem>();
        Assert.NotNull(malformedProblem);
        Assert.Equal("name_change_code_invalid", malformedProblem.Code);
        Assert.Equal("code", malformedProblem.Field);

        using var incorrect = await VerifyAsync(buyer.Client, pending.ChallengeId, "654321");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, incorrect.StatusCode);
        var incorrectProblem = await incorrect.Content.ReadFromJsonAsync<NameChangeProblem>();
        Assert.NotNull(incorrectProblem);
        Assert.Equal("name_change_code_incorrect", incorrectProblem.Code);
        Assert.Equal(4, incorrectProblem.RemainingAttempts);
    }

    [Fact]
    public async Task Malformed_request_resend_and_verify_keys_are_rejected_before_provider_or_attempt()
    {
        var provider = new ControlledOtpProvider();
        using var controlledFactory = CreateControlledFactory(provider);
        using var buyer = await AuthenticatedBuyerAsync(controlledFactory);

        using var malformedRequest = await RequestPendingAsync(
            buyer.Client,
            "สมชาย",
            "ใจดี",
            "bad");
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            malformedRequest.StatusCode);
        AssertProblemContract(
            await ReadProblemAsync(malformedRequest),
            422,
            "name_change_idempotency_invalid",
            "รหัสคำขอไม่ถูกต้อง",
            field: "idempotencyKey");
        Assert.Equal(0, provider.RequestCount);

        using var requested = await RequestPendingAsync(
            buyer.Client,
            "สมชาย",
            "ใจดี");
        requested.EnsureSuccessStatusCode();
        var pending = await requested.Content
            .ReadFromJsonAsync<PendingResponse>();
        Assert.NotNull(pending);

        using var malformedResend = await buyer.Client.PostAsJsonAsync(
            $"/api/mobile/me/name-change/{pending.ChallengeId}/resend",
            new { IdempotencyKey = "bad" });
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            malformedResend.StatusCode);
        AssertProblemContract(
            await ReadProblemAsync(malformedResend),
            422,
            "name_change_idempotency_invalid",
            "รหัสคำขอไม่ถูกต้อง",
            field: "idempotencyKey");

        using var malformedVerify = await VerifyAsync(
            buyer.Client,
            pending.ChallengeId,
            "123456",
            "bad");
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            malformedVerify.StatusCode);
        AssertProblemContract(
            await ReadProblemAsync(malformedVerify),
            422,
            "name_change_idempotency_invalid",
            "รหัสคำขอไม่ถูกต้อง",
            field: "idempotencyKey");
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(0, provider.VerificationCount);
        await using var scope = controlledFactory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        Assert.Empty(await database.AccountNameVerificationAttempts.ToListAsync());
        Assert.Empty(await database.AccountNameVerificationOperations.ToListAsync());
        AssertSecurityHeaders(malformedVerify);
    }

    [Fact]
    public async Task Valid_request_and_resend_keys_reused_with_different_content_return_conflict()
    {
        var provider = new ControlledOtpProvider();
        using var controlledFactory = CreateControlledFactory(provider);
        using var buyer = await AuthenticatedBuyerAsync(controlledFactory);
        var requestKey = NewKey();
        using var requested = await RequestPendingAsync(
            buyer.Client,
            "สมชาย",
            "ใจดี",
            requestKey);
        requested.EnsureSuccessStatusCode();
        var pending = await requested.Content
            .ReadFromJsonAsync<PendingResponse>();
        Assert.NotNull(pending);

        using var requestConflict = await RequestPendingAsync(
            buyer.Client,
            "สมหญิง",
            "ใจงาม",
            requestKey);
        Assert.Equal(HttpStatusCode.Conflict, requestConflict.StatusCode);
        AssertProblemContract(
            await ReadProblemAsync(requestConflict),
            409,
            "name_change_idempotency_conflict",
            "คำขอนี้ไม่ตรงกับข้อมูลเดิม กรุณาลองใหม่");

        await SetResendAvailableAsync(
            controlledFactory,
            pending.ChallengeId);
        var resendKey = NewKey();
        using var resent = await buyer.Client.PostAsJsonAsync(
            $"/api/mobile/me/name-change/{pending.ChallengeId}/resend",
            new { IdempotencyKey = resendKey });
        resent.EnsureSuccessStatusCode();
        var replacement = await resent.Content
            .ReadFromJsonAsync<PendingResponse>();
        Assert.NotNull(replacement);
        using var resendConflict = await buyer.Client.PostAsJsonAsync(
            $"/api/mobile/me/name-change/{replacement.ChallengeId}/resend",
            new { IdempotencyKey = resendKey });
        Assert.Equal(HttpStatusCode.Conflict, resendConflict.StatusCode);
        AssertProblemContract(
            await ReadProblemAsync(resendConflict),
            409,
            "name_change_idempotency_conflict",
            "คำขอนี้ไม่ตรงกับข้อมูลเดิม กรุณาลองใหม่");
        Assert.Equal(2, provider.RequestCount);
    }

    [Fact]
    public async Task Completed_verification_replay_with_a_different_code_digest_returns_conflict()
    {
        var provider = new ControlledOtpProvider();
        using var controlledFactory = CreateControlledFactory(provider);
        using var buyer = await AuthenticatedBuyerAsync(controlledFactory);
        using var requested = await RequestPendingAsync(
            buyer.Client,
            "สมชาย",
            "ใจดี");
        requested.EnsureSuccessStatusCode();
        var pending = await requested.Content
            .ReadFromJsonAsync<PendingResponse>();
        Assert.NotNull(pending);
        var key = NewKey();
        using var completed = await VerifyAsync(
            buyer.Client,
            pending.ChallengeId,
            "123456",
            key);
        completed.EnsureSuccessStatusCode();

        using var conflict = await VerifyAsync(
            buyer.Client,
            pending.ChallengeId,
            "654321",
            key);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        AssertProblemContract(
            await ReadProblemAsync(conflict),
            409,
            "name_change_idempotency_conflict",
            "คำขอนี้ไม่ตรงกับข้อมูลเดิม กรุณาลองใหม่");
        Assert.Equal(1, provider.VerificationCount);
    }

    [Fact]
    public async Task Unchanged_expired_and_locked_name_change_outcomes_have_stable_contracts()
    {
        var provider = new ControlledOtpProvider();
        using var controlledFactory = CreateControlledFactory(provider);
        using var buyer = await AuthenticatedBuyerAsync(controlledFactory);

        using var unchanged = await RequestPendingAsync(
            buyer.Client,
            "ผู้ซื้อ",
            "เดิม");
        Assert.Equal(HttpStatusCode.Conflict, unchanged.StatusCode);
        AssertProblemContract(
            await ReadProblemAsync(unchanged),
            409,
            "name_change_unchanged",
            "ชื่อนี้เป็นชื่อปัจจุบันของคุณแล้ว");

        using var requested = await RequestPendingAsync(
            buyer.Client,
            "สมชาย",
            "ใจดี");
        requested.EnsureSuccessStatusCode();
        var expiredPending = await requested.Content
            .ReadFromJsonAsync<PendingResponse>();
        Assert.NotNull(expiredPending);
        await SetChallengeTimingAsync(
            controlledFactory,
            expiredPending.ChallengeId,
            expiresAt: DateTimeOffset.UtcNow.AddSeconds(-1));
        using var expired = await VerifyAsync(
            buyer.Client,
            expiredPending.ChallengeId,
            "123456");
        Assert.Equal(HttpStatusCode.Conflict, expired.StatusCode);
        AssertProblemContract(
            await ReadProblemAsync(expired),
            409,
            "name_change_expired",
            "รหัสยืนยันหมดอายุแล้ว กรุณาขอรหัสใหม่");

        using var replacementRequest = await RequestPendingAsync(
            buyer.Client,
            "สมหญิง",
            "ใจงาม");
        replacementRequest.EnsureSuccessStatusCode();
        var lockedPending = await replacementRequest.Content
            .ReadFromJsonAsync<PendingResponse>();
        Assert.NotNull(lockedPending);
        for (var remaining = 4; remaining >= 1; remaining--)
        {
            using var incorrect = await VerifyAsync(
                buyer.Client,
                lockedPending.ChallengeId,
                "654321");
            Assert.Equal(
                HttpStatusCode.UnprocessableEntity,
                incorrect.StatusCode);
            AssertProblemContract(
                await ReadProblemAsync(incorrect),
                422,
                "name_change_code_incorrect",
                "รหัสยืนยันไม่ถูกต้อง ลองตรวจสอบแล้วกรอกอีกครั้ง",
                remainingAttempts: remaining);
        }
        using var locked = await VerifyAsync(
            buyer.Client,
            lockedPending.ChallengeId,
            "654321");
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
        AssertProblemContract(
            await ReadProblemAsync(locked),
            409,
            "name_change_locked",
            "กรอกรหัสไม่ถูกต้องครบจำนวนแล้ว กรุณาขอรหัสใหม่");
        AssertSecurityHeaders(locked);
    }

    [Fact]
    public async Task Provider_throttle_unavailable_and_invalid_response_have_bounded_HTTP_contracts()
    {
        var throttledProvider = new ControlledOtpProvider
        {
            RequestOutcome = ProviderRequestOutcome.Throttled
        };
        using (var throttledFactory =
               CreateControlledFactory(throttledProvider))
        using (var buyer = await AuthenticatedBuyerAsync(throttledFactory))
        using (var throttled = await RequestPendingAsync(
                   buyer.Client,
                   "สมชาย",
                   "ใจดี"))
        {
            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                throttled.StatusCode);
            AssertProblemContract(
                await ReadProblemAsync(throttled),
                429,
                "name_change_provider_throttled",
                "กรุณารอก่อนขอรหัสยืนยันอีกครั้ง",
                retryAfterSeconds: 17);
            Assert.Equal("17", throttled.Headers
                .GetValues("Retry-After").Single());
            AssertSecurityHeaders(throttled);
        }

        foreach (var unavailableProvider in new[]
        {
            new ControlledOtpProvider
            {
                Capabilities =
                    OtpProviderCapabilities.MobileAuthenticationOnly
            },
            new ControlledOtpProvider
            {
                RequestOutcome = ProviderRequestOutcome.InvalidResponse
            }
        })
        {
            using var unavailableFactory =
                CreateControlledFactory(unavailableProvider);
            using var buyer = await AuthenticatedBuyerAsync(
                unavailableFactory);
            using var unavailable = await RequestPendingAsync(
                buyer.Client,
                "สมชาย",
                "ใจดี");
            Assert.Equal(
                HttpStatusCode.ServiceUnavailable,
                unavailable.StatusCode);
            AssertProblemContract(
                await ReadProblemAsync(unavailable),
                503,
                "name_change_provider_unavailable",
                "บริการยืนยันชื่อยังไม่พร้อมใช้งาน กรุณาลองใหม่ภายหลัง");
            AssertSecurityHeaders(unavailable);
        }
    }

    [Fact]
    public async Task Provider_send_outcome_unknown_requires_same_request_replay_without_exposing_provider_text()
    {
        var provider = new ControlledOtpProvider
        {
            RequestOutcome = ProviderRequestOutcome.OutcomeUnknown
        };
        using var controlledFactory = CreateControlledFactory(provider);
        using var buyer = await AuthenticatedBuyerAsync(controlledFactory);
        var logCount = factory.LogMessages.Count;
        var key = NewKey();

        using var unknown = await RequestPendingAsync(
            buyer.Client,
            "สมชาย",
            "ใจดี",
            key);
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            unknown.StatusCode);
        AssertProblemContract(
            await ReadProblemAsync(unknown),
            503,
            "name_change_provider_outcome_unknown",
            "กำลังตรวจสอบผลการยืนยัน กรุณาลองอีกครั้งด้วยคำขอเดิม",
            retryAfterSeconds: 5);
        var body = await unknown.Content.ReadAsStringAsync();
        Assert.DoesNotContain(
            ControlledOtpProvider.SensitiveProviderText,
            body,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            factory.LogMessages.Skip(logCount),
            message => message.Contains(
                ControlledOtpProvider.SensitiveProviderText,
                StringComparison.Ordinal));

        using var replay = await RequestPendingAsync(
            buyer.Client,
            "สมชาย",
            "ใจดี",
            key);
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            replay.StatusCode);
        AssertProblemContract(
            await ReadProblemAsync(replay),
            503,
            "name_change_provider_outcome_unknown",
            "กำลังตรวจสอบผลการยืนยัน กรุณาลองอีกครั้งด้วยคำขอเดิม",
            retryAfterSeconds: 5);
        Assert.Equal(1, provider.RequestCount);
        AssertSecurityHeaders(replay);
    }

    [Fact]
    public async Task Outcome_unknown_verification_replay_with_a_different_digest_conflicts_without_leaking_secrets()
    {
        var provider = new ControlledOtpProvider();
        using var controlledFactory = CreateControlledFactory(provider);
        using var buyer = await AuthenticatedBuyerAsync(controlledFactory);
        using var requested = await RequestPendingAsync(
            buyer.Client,
            "สมชาย",
            "ใจดี");
        requested.EnsureSuccessStatusCode();
        var pending = await requested.Content
            .ReadFromJsonAsync<PendingResponse>();
        Assert.NotNull(pending);
        provider.ThrowDuringVerification = true;
        var logCount = factory.LogMessages.Count;
        var key = NewKey();

        using var unknown = await VerifyAsync(
            buyer.Client,
            pending.ChallengeId,
            "123456",
            key);
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            unknown.StatusCode);
        AssertProblemContract(
            await ReadProblemAsync(unknown),
            503,
            "name_change_provider_outcome_unknown",
            "กำลังตรวจสอบผลการยืนยัน กรุณาลองอีกครั้งด้วยคำขอเดิม",
            retryAfterSeconds: 5);
        Assert.Equal("5", unknown.Headers
            .GetValues("Retry-After").Single());
        AssertSecurityHeaders(unknown);
        string digest;
        await using (var scope =
                     controlledFactory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider
                .GetRequiredService<ToklongDbContext>();
            digest = (await database.AccountNameVerificationOperations
                .SingleAsync()).SubmittedDigest;
        }
        var body = await unknown.Content.ReadAsStringAsync();
        Assert.DoesNotContain("123456", body, StringComparison.Ordinal);
        Assert.DoesNotContain(digest, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            ControlledOtpProvider.SensitiveProviderText,
            body,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            factory.LogMessages.Skip(logCount),
            message =>
                message.Contains("123456", StringComparison.Ordinal) ||
                message.Contains(digest, StringComparison.OrdinalIgnoreCase) ||
                message.Contains(
                    ControlledOtpProvider.SensitiveProviderText,
                    StringComparison.Ordinal));

        using var conflict = await VerifyAsync(
            buyer.Client,
            pending.ChallengeId,
            "654321",
            key);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        AssertProblemContract(
            await ReadProblemAsync(conflict),
            409,
            "name_change_idempotency_conflict",
            "คำขอนี้ไม่ตรงกับข้อมูลเดิม กรุณาลองใหม่");
        Assert.Equal(1, provider.VerificationCount);
    }

    [Fact]
    public async Task Resend_succeeds_and_exact_replay_returns_the_same_replacement()
    {
        using var buyer = await AuthenticatedBuyerAsync();
        using var requested = await RequestPendingAsync(buyer.Client, "สมชาย", "ใจดี");
        requested.EnsureSuccessStatusCode();
        var pending = (await requested.Content.ReadFromJsonAsync<PendingResponse>())!;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ToklongDbContext>();
            var challenge = await database.AccountNameChangeChallenges.SingleAsync(x => x.Id == pending.ChallengeId);
            database.Entry(challenge).Property("ResendAvailableAt").CurrentValue = DateTimeOffset.UtcNow.AddSeconds(-1);
            await database.SaveChangesAsync();
        }
        var key = NewKey();
        using var resend = await buyer.Client.PostAsJsonAsync($"/api/mobile/me/name-change/{pending.ChallengeId}/resend", new { IdempotencyKey = key });
        resend.EnsureSuccessStatusCode();
        var replacement = (await resend.Content.ReadFromJsonAsync<PendingResponse>())!;
        Assert.NotEqual(pending.ChallengeId, replacement.ChallengeId);
        using var replay = await buyer.Client.PostAsJsonAsync($"/api/mobile/me/name-change/{pending.ChallengeId}/resend", new { IdempotencyKey = key });
        replay.EnsureSuccessStatusCode();
        Assert.Equal(replacement.ChallengeId, (await replay.Content.ReadFromJsonAsync<PendingResponse>())!.ChallengeId);
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

    private async Task<(BuyerSession Buyer, SellerSession Seller)>
        AuthenticatedRoleSessionsAsync(
            WebApplicationFactory<Program> host)
    {
        var sequence = Interlocked.Increment(ref accountSequence);
        var phone = $"+668{sequence:D8}";
        var name = AccountName.Create("ผู้ใช้", "ร่วมบัญชี");
        var buyer = BuyerAccount.Create(
            phone,
            name,
            $"shared-{sequence}@example.com",
            DateTimeOffset.UtcNow);
        var seller = SellerAccount.Create(
            phone,
            DateTimeOffset.UtcNow,
            name);
        await using var scope = host.Services.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        database.AddRange(buyer, seller);
        await database.SaveChangesAsync();
        var tokens = scope.ServiceProvider
            .GetRequiredService<MobileSessionTokenService>();
        var buyerSession = await tokens.CreateAsync(
            new MobileSessionProfile(
                buyer.Id,
                null,
                phone,
                buyer.FullName),
            CancellationToken.None);
        var sellerSession = await tokens.CreateAsync(
            new MobileSessionProfile(
                null,
                seller.Id,
                phone,
                seller.DisplayName),
            CancellationToken.None);
        return (
            new BuyerSession(
                buyer.Id,
                phone,
                buyerSession.AccessToken,
                CreateClient(buyerSession.AccessToken, host)),
            new SellerSession(
                seller.Id,
                phone,
                sellerSession.AccessToken,
                CreateClient(sellerSession.AccessToken, host)));
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

    private WebApplicationFactory<Program> CreateControlledFactory(
        ControlledOtpProvider provider) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "RateLimits:NameChangeRequestPermitLimit",
                "100");
            builder.UseSetting(
                "RateLimits:NameChangeVerifyPermitLimit",
                "100");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IOtpVerificationProvider>();
                services.AddSingleton<IOtpVerificationProvider>(provider);
            });
        });

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

    private static Task SetResendAvailableAsync(
        WebApplicationFactory<Program> host,
        Guid challengeId) =>
        SetChallengeTimingAsync(
            host,
            challengeId,
            resendAvailableAt: DateTimeOffset.UtcNow.AddSeconds(-1));

    private static async Task SetChallengeTimingAsync(
        WebApplicationFactory<Program> host,
        Guid challengeId,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? resendAvailableAt = null)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        var challenge = await database.AccountNameChangeChallenges
            .SingleAsync(item => item.Id == challengeId);
        if (expiresAt.HasValue)
            database.Entry(challenge)
                .Property(nameof(AccountNameChangeChallenge.ExpiresAt))
                .CurrentValue = expiresAt.Value;
        if (resendAvailableAt.HasValue)
            database.Entry(challenge)
                .Property(nameof(AccountNameChangeChallenge.ResendAvailableAt))
                .CurrentValue = resendAvailableAt.Value;
        await database.SaveChangesAsync();
    }

    private static async Task<NameChangeProblem> ReadProblemAsync(
        HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<NameChangeProblem>() ??
        throw new Xunit.Sdk.XunitException(
            "Expected an account-name problem response.");

    private static void AssertProblemContract(
        NameChangeProblem problem,
        int status,
        string code,
        string detail,
        string? field = null,
        int? retryAfterSeconds = null,
        int? remainingAttempts = null,
        DateTimeOffset? nextAllowedAt = null)
    {
        Assert.Equal("ทำรายการไม่สำเร็จ", problem.Title);
        Assert.Equal(status, problem.Status);
        Assert.Equal(detail, problem.Detail);
        Assert.Equal(code, problem.Code);
        Assert.Equal(field, problem.Field);
        Assert.Equal(retryAfterSeconds, problem.RetryAfterSeconds);
        Assert.Equal(remainingAttempts, problem.RemainingAttempts);
        Assert.Equal(nextAllowedAt, problem.NextAllowedAt);
    }

    private static void AssertSecurityHeaders(
        HttpResponseMessage response)
    {
        Assert.Equal(
            "no-store",
            response.Headers.CacheControl?.ToString());
        Assert.Contains(
            "nosniff",
            response.Headers.GetValues("X-Content-Type-Options"));
    }

    private sealed record BuyerSession(
        Guid BuyerId,
        string PhoneNumber,
        string AccessToken,
        HttpClient Client) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }

    private sealed record SellerSession(
        Guid SellerId,
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
        string Title,
        int Status,
        string Detail,
        string Code,
        DateTimeOffset? NextAllowedAt,
        int? RetryAfterSeconds,
        int? RemainingAttempts = null,
        string? Field = null);

    private enum ProviderRequestOutcome
    {
        Accepted,
        Throttled,
        InvalidResponse,
        OutcomeUnknown
    }

    private sealed class ControlledOtpProvider
        : IOtpVerificationProvider
    {
        private readonly Dictionary<string, string> phones = [];

        public const string SensitiveProviderText =
            "provider-secret-detail-must-not-leak";
        public OtpProviderCapabilities Capabilities { get; set; } =
            new(
                SupportsAccountNameChange: true,
                AccountNameChangeCodeLifetime: TimeSpan.FromMinutes(10),
                SupportsRequestLookup: true)
            {
                SupportsVerificationLookup = true
            };
        public ProviderRequestOutcome RequestOutcome { get; set; }
        public bool ThrowDuringVerification { get; set; }
        public int RequestCount { get; private set; }
        public int VerificationCount { get; private set; }
        public int VerificationLookupCount { get; private set; }

        public Task<OtpChallenge> RequestAsync(
            string phoneNumber,
            OtpPurpose purpose,
            string providerRequestKey,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (RequestOutcome == ProviderRequestOutcome.Throttled)
                throw new RequestCooldownException(
                    SensitiveProviderText,
                    TimeSpan.FromSeconds(17),
                    "provider-sensitive-throttle-code");
            if (RequestOutcome == ProviderRequestOutcome.OutcomeUnknown)
                throw new HttpRequestException(SensitiveProviderText);
            if (RequestOutcome == ProviderRequestOutcome.InvalidResponse)
                return Task.FromResult(
                    new OtpChallenge("", "0••-•••-0000", null));

            var challengeId = $"controlled-{providerRequestKey}";
            phones[challengeId] = phoneNumber;
            return Task.FromResult(
                new OtpChallenge(
                    challengeId,
                    "0••-•••-0000",
                    null));
        }

        public Task<string?> VerifyAsync(
            string challengeId,
            string code,
            OtpPurpose purpose,
            CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task<OtpProviderVerificationEvidence>
            VerifyIdempotentlyAsync(
                string challengeId,
                string code,
                OtpPurpose purpose,
                string verificationRequestKey,
                CancellationToken cancellationToken)
        {
            VerificationCount++;
            if (ThrowDuringVerification)
                throw new HttpRequestException(SensitiveProviderText);
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(
                new OtpProviderVerificationEvidence(
                    verificationRequestKey,
                    challengeId,
                    purpose,
                    phones.TryGetValue(challengeId, out var phone)
                        ? phone
                        : "+66800000000",
                    code == "123456"
                        ? OtpProviderVerificationOutcome.Verified
                        : OtpProviderVerificationOutcome.Rejected,
                    now,
                    now));
        }

        public Task<OtpProviderVerificationEvidence?>
            LookupVerificationAsync(
                string verificationRequestKey,
                string challengeId,
                string phoneNumber,
                OtpPurpose purpose,
                CancellationToken cancellationToken)
        {
            VerificationLookupCount++;
            return Task.FromResult<OtpProviderVerificationEvidence?>(null);
        }
    }
}
