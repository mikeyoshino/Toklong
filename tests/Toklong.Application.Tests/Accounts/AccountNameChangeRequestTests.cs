using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Features.Accounts.NameChanges;
using Toklong.Domain.Accounts;
using Toklong.Domain.Buyers;
using Toklong.Domain.Common;
using Toklong.Domain.Sellers;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Application.Tests.Accounts;

public sealed class AccountNameChangeRequestTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 31, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task First_change_is_available_immediately_for_one_or_two_roles()
    {
        await using var buyerOnly = await Scenario.CreateAsync();
        await using var bothRoles = await Scenario.CreateAsync(withSeller: true);

        var buyerResult = await buyerOnly.EligibilityHandler().Handle(
            new(buyerOnly.Subject),
            default);
        var bothResult = await bothRoles.EligibilityHandler().Handle(
            new(bothRoles.Subject),
            default);

        Assert.True(buyerResult.CanChange);
        Assert.Null(buyerResult.NextAllowedAt);
        Assert.True(bothResult.CanChange);
        Assert.Null(bothResult.NextAllowedAt);
    }

    [Fact]
    public async Task Eligibility_uses_later_role_timestamp_and_allows_exact_boundary()
    {
        await using var scenario = await Scenario.CreateAsync(withSeller: true);
        var buyerChangedAt = Now.AddMonths(-3);
        var sellerChangedAt = Now.AddMonths(-1);
        scenario.Buyer!.ApplyAccountName(
            AccountName.Create("Buyer", "Changed"),
            buyerChangedAt);
        scenario.Seller!.ApplyAccountName(
            AccountName.Create("Seller", "Changed"),
            sellerChangedAt);
        await scenario.Database.SaveChangesAsync();
        var nextAllowedAt =
            AccountNameChangeCalendar.AddTwoBangkokCalendarMonths(
                sellerChangedAt);
        scenario.Clock.UtcNow = nextAllowedAt.AddTicks(-1);

        var blocked = await scenario.EligibilityHandler().Handle(
            new(scenario.Subject),
            default);
        scenario.Clock.UtcNow = nextAllowedAt;
        var allowed = await scenario.EligibilityHandler().Handle(
            new(scenario.Subject),
            default);

        Assert.False(blocked.CanChange);
        Assert.Equal(nextAllowedAt, blocked.NextAllowedAt);
        Assert.True(allowed.CanChange);
        Assert.Null(allowed.NextAllowedAt);
    }

    [Theory]
    [InlineData(
        "2026-01-31T03:15:00+00:00",
        "2026-03-31T03:15:00+00:00")]
    [InlineData(
        "2024-02-29T01:45:00+00:00",
        "2024-04-29T01:45:00+00:00")]
    public void Calendar_months_preserve_Bangkok_wall_clock(
        string changedAtText,
        string expectedText)
    {
        var changedAt = DateTimeOffset.Parse(changedAtText);
        var expected = DateTimeOffset.Parse(expectedText);

        var actual =
            AccountNameChangeCalendar.AddTwoBangkokCalendarMonths(changedAt);

        Assert.Equal(expected, actual);
        Assert.Equal(
            TimeZoneInfo.ConvertTime(changedAt, Bangkok()).TimeOfDay,
            TimeZoneInfo.ConvertTime(actual, Bangkok()).TimeOfDay);
    }

    [Fact]
    public async Task Eligibility_rejects_subject_without_a_role()
    {
        await using var scenario = await Scenario.CreateAsync();
        var invalid = scenario.Subject with
        {
            BuyerId = null,
            SellerId = null
        };

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            scenario.EligibilityHandler().Handle(new(invalid), default));
    }

    [Fact]
    public async Task Eligibility_rejects_phone_that_does_not_match_every_role()
    {
        await using var scenario = await Scenario.CreateAsync(withSeller: true);
        var invalid = scenario.Subject with
        {
            PhoneNumber = "+66999999999"
        };

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            scenario.EligibilityHandler().Handle(new(invalid), default));
    }

    [Fact]
    public async Task Unchanged_name_does_not_create_a_challenge_or_call_provider()
    {
        await using var scenario = await Scenario.CreateAsync();

        await Assert.ThrowsAsync<AccountNameChangeUnchangedNameException>(() =>
            scenario.RequestHandler().Handle(
                scenario.Request(
                    scenario.Buyer!.FirstName,
                    scenario.Buyer.LastName),
                default));

        Assert.Equal(0, scenario.Provider.RequestCount);
        Assert.Empty(scenario.Database.AccountNameChangeChallenges);
    }

    [Fact]
    public async Task Exact_request_replay_sends_once_and_mismatched_reuse_fails()
    {
        await using var scenario = await Scenario.CreateAsync();
        var key = Guid.NewGuid().ToString("N");
        var command = scenario.Request("สมศักดิ์", "ใจดี", key);

        var first = await scenario.RequestHandler().Handle(command, default);
        var replay = await scenario.RequestHandler().Handle(command, default);
        var mismatch = command with { FirstName = "สมปอง" };

        Assert.Equal(first, replay);
        Assert.Equal(1, scenario.Provider.RequestCount);
        await Assert.ThrowsAsync<AccountNameChangeIdempotencyException>(() =>
            scenario.RequestHandler().Handle(mismatch, default));
        Assert.Equal(1, scenario.Provider.RequestCount);
    }

    [Fact]
    public async Task Exact_request_replay_returns_its_original_result_after_supersede()
    {
        await using var scenario = await Scenario.CreateAsync();
        var command = scenario.Request("สมศักดิ์", "ใจดี");
        var first = await scenario.RequestHandler().Handle(command, default);
        scenario.Clock.UtcNow = first.ResendAvailableAt;
        await scenario.ResendHandler().Handle(
            scenario.Resend(first.ChallengeId),
            default);

        var replay = await scenario.RequestHandler().Handle(command, default);

        Assert.Equal(first, replay);
        Assert.Equal(2, scenario.Provider.RequestCount);
    }

    [Fact]
    public async Task Exact_request_replay_bypasses_a_later_name_cooldown()
    {
        await using var scenario = await Scenario.CreateAsync();
        var command = scenario.Request("สมศักดิ์", "ใจดี");
        var first = await scenario.RequestHandler().Handle(command, default);
        scenario.Buyer!.ApplyAccountName(
            AccountName.Create("สมศักดิ์", "ใจดี"),
            scenario.Clock.UtcNow);
        await scenario.Database.SaveChangesAsync();

        var replay = await scenario.RequestHandler().Handle(command, default);

        Assert.Equal(first, replay);
        Assert.Equal(1, scenario.Provider.RequestCount);
    }

    [Fact]
    public async Task Provider_rejection_marks_send_failed()
    {
        await using var scenario = await Scenario.CreateAsync();
        scenario.Provider.Behavior = SendBehavior.Rejected;

        await Assert.ThrowsAsync<AccountNameChangeProviderThrottleException>(() =>
            scenario.RequestHandler().Handle(
                scenario.Request("สมศักดิ์", "ใจดี"),
                default));

        var stored = Assert.Single(
            scenario.Database.AccountNameChangeChallenges);
        Assert.Equal(AccountNameChangeStatus.SendFailed, stored.Status);
        Assert.NotNull(stored.SendFailedAt);
    }

    [Fact]
    public async Task Exact_rejected_request_replays_original_cooldown_evidence()
    {
        await using var scenario = await Scenario.CreateAsync();
        scenario.Provider.Behavior = SendBehavior.Rejected;
        scenario.Provider.RejectionMessage = "กรุณารออีก 37 วินาที";
        scenario.Provider.RejectionCode = "otp_provider_cooldown";
        scenario.Provider.RejectionRetryAfter =
            TimeSpan.FromSeconds(37.25);
        var command = scenario.Request("สมศักดิ์", "ใจดี");

        var first = await Assert.ThrowsAsync<AccountNameChangeProviderThrottleException>(() =>
            scenario.RequestHandler().Handle(command, default));
        var replay = await Assert.ThrowsAsync<AccountNameChangeProviderThrottleException>(() =>
            scenario.RequestHandler().Handle(command, default));

        Assert.Equal(first.Message, replay.Message);
        Assert.Equal(first.RetryAfter, replay.RetryAfter);
        Assert.Equal(1, scenario.Provider.RequestCount);
        Assert.Equal(0, scenario.Provider.LookupCount);
    }

    [Fact]
    public async Task Exact_rejected_resend_replays_without_provider_call()
    {
        await using var scenario = await Scenario.CreateAsync();
        var original = await scenario.RequestHandler().Handle(
            scenario.Request("สมศักดิ์", "ใจดี"),
            default);
        scenario.Clock.UtcNow = original.ResendAvailableAt;
        scenario.Provider.Behavior = SendBehavior.Rejected;
        scenario.Provider.RejectionMessage = "ขอรหัสใหม่เร็วเกินไป";
        scenario.Provider.RejectionCode = "otp_resend_cooldown";
        scenario.Provider.RejectionRetryAfter =
            TimeSpan.FromSeconds(42);
        var command = scenario.Resend(
            original.ChallengeId,
            Guid.NewGuid().ToString("N"));

        var first = await Assert.ThrowsAsync<AccountNameChangeProviderThrottleException>(() =>
            scenario.ResendHandler().Handle(command, default));
        var replay = await Assert.ThrowsAsync<AccountNameChangeProviderThrottleException>(() =>
            scenario.ResendHandler().Handle(command, default));

        Assert.Equal(first.Message, replay.Message);
        Assert.Equal(first.RetryAfter, replay.RetryAfter);
        Assert.Equal(2, scenario.Provider.RequestCount);

        var mismatchedInitial = scenario.Request(
            "สมปอง",
            "ใจดี",
            command.IdempotencyKey);
        await Assert.ThrowsAsync<AccountNameChangeIdempotencyException>(() =>
            scenario.RequestHandler().Handle(
                mismatchedInitial,
                default));
        Assert.Equal(2, scenario.Provider.RequestCount);
    }

    [Fact]
    public async Task Lost_provider_response_remains_pending_until_lookup_recovers_it()
    {
        await using var scenario = await Scenario.CreateAsync();
        scenario.Provider.Behavior = SendBehavior.AcceptedThenResponseLost;
        var command = scenario.Request("สมศักดิ์", "ใจดี");

        await Assert.ThrowsAsync<AccountNameChangeProviderOutcomeUnknownException>(() =>
            scenario.RequestHandler().Handle(command, default));
        var pending = Assert.Single(
            scenario.Database.AccountNameChangeChallenges);
        Assert.Equal(AccountNameChangeStatus.PendingSend, pending.Status);
        Assert.Null(await scenario.PendingHandler().Handle(
            new(scenario.Subject),
            default));

        scenario.Provider.Behavior = SendBehavior.Accepted;
        var recovered = await scenario.RequestHandler().Handle(
            command,
            default);

        Assert.Equal(pending.Id, recovered.ChallengeId);
        Assert.Equal(1, scenario.Provider.RequestCount);
        Assert.Equal(1, scenario.Provider.LookupCount);
        Assert.Equal(
            AccountNameChangeStatus.Active,
            scenario.Database.AccountNameChangeChallenges.Single().Status);
    }

    [Fact]
    public async Task Pending_query_returns_only_an_active_owned_unexpired_challenge()
    {
        await using var scenario = await Scenario.CreateAsync();
        var requested = await scenario.RequestHandler().Handle(
            scenario.Request("สมศักดิ์", "ใจดี"),
            default);

        var pending = await scenario.PendingHandler().Handle(
            new(scenario.Subject),
            default);
        var otherSession = scenario.Subject with
        {
            SessionId = Guid.NewGuid()
        };
        var notOwned = await scenario.PendingHandler().Handle(
            new(otherSession),
            default);
        scenario.Clock.UtcNow = requested.ExpiresAt;
        var expired = await scenario.PendingHandler().Handle(
            new(scenario.Subject),
            default);

        Assert.Equal(requested, pending);
        Assert.Null(notOwned);
        Assert.Null(expired);
    }

    [Fact]
    public async Task Pending_query_rechecks_the_current_verified_role_phone()
    {
        await using var scenario = await Scenario.CreateAsync();
        await scenario.RequestHandler().Handle(
            scenario.Request("สมศักดิ์", "ใจดี"),
            default);
        scenario.Buyer!.UpdatePhoneVerification(
            "+66999999999",
            scenario.Clock.UtcNow);
        await scenario.Database.SaveChangesAsync();

        var pending = await scenario.PendingHandler().Handle(
            new(scenario.Subject),
            default);

        Assert.Null(pending);
    }

    [Fact]
    public async Task Fresh_request_at_exact_expiry_replaces_the_active_challenge()
    {
        await using var scenario = await Scenario.CreateAsync();
        var original = await scenario.RequestHandler().Handle(
            scenario.Request("สมศักดิ์", "ใจดี"),
            default);
        scenario.Clock.UtcNow = original.ExpiresAt;

        Assert.Null(await scenario.PendingHandler().Handle(
            new(scenario.Subject),
            default));
        var replacement = await scenario.RequestHandler().Handle(
            scenario.Request("สมศักดิ์", "ใจดี"),
            default);

        Assert.NotEqual(original.ChallengeId, replacement.ChallengeId);
        Assert.Equal(2, scenario.Provider.RequestCount);
        Assert.Equal(
            AccountNameChangeStatus.Expired,
            scenario.Database.AccountNameChangeChallenges
                .Single(value => value.Id == original.ChallengeId).Status);
        Assert.Single(
            scenario.Database.AccountNameChangeChallenges,
            value => value.Status is
                AccountNameChangeStatus.PendingSend or
                AccountNameChangeStatus.Active);
    }

    [Fact]
    public async Task New_session_can_replace_an_expired_challenge()
    {
        await using var scenario = await Scenario.CreateAsync();
        var original = await scenario.RequestHandler().Handle(
            scenario.Request("สมศักดิ์", "ใจดี"),
            default);
        scenario.Clock.UtcNow = original.ExpiresAt;
        var newSubject = scenario.Subject with
        {
            SessionId = Guid.NewGuid()
        };
        var command = scenario.Request(
            "สมศักดิ์",
            "ใจดี") with
        {
            Subject = newSubject
        };

        var replacement = await scenario.RequestHandler().Handle(
            command,
            default);

        Assert.Equal(newSubject.SessionId, scenario.Database
            .AccountNameChangeChallenges
            .Single(value => value.Id == replacement.ChallengeId)
            .SessionId);
        Assert.Equal(2, scenario.Provider.RequestCount);
    }

    [Fact]
    public async Task Expired_challenge_does_not_strand_a_new_session_with_an_attached_role()
    {
        await using var scenario = await Scenario.CreateAsync();
        var original = await scenario.RequestHandler().Handle(
            scenario.Request("สมศักดิ์", "ใจดี"),
            default);
        scenario.Clock.UtcNow = original.ExpiresAt;
        var seller = SellerAccount.Create(
            scenario.Subject.PhoneNumber,
            scenario.Clock.UtcNow,
            AccountName.Create("สมชาย", "ใจดี"));
        scenario.Database.Sellers.Add(seller);
        await scenario.Database.SaveChangesAsync();
        var newSubject = scenario.Subject with
        {
            SellerId = seller.Id,
            SessionId = Guid.NewGuid()
        };

        var replacement = await scenario.RequestHandler().Handle(
            scenario.Request("สมศักดิ์", "ใจดี") with
            {
                Subject = newSubject
            },
            default);

        Assert.Equal(seller.Id, scenario.Database
            .AccountNameChangeChallenges
            .Single(value => value.Id == replacement.ChallengeId)
            .SellerId);
        Assert.Equal(2, scenario.Provider.RequestCount);
    }

    [Fact]
    public async Task Resend_at_exact_expiry_creates_one_replacement()
    {
        await using var scenario = await Scenario.CreateAsync();
        var original = await scenario.RequestHandler().Handle(
            scenario.Request("สมศักดิ์", "ใจดี"),
            default);
        scenario.Clock.UtcNow = original.ExpiresAt;

        var replacement = await scenario.ResendHandler().Handle(
            scenario.Resend(original.ChallengeId),
            default);

        Assert.NotEqual(original.ChallengeId, replacement.ChallengeId);
        Assert.Equal(2, scenario.Provider.RequestCount);
        Assert.Equal(
            AccountNameChangeStatus.Expired,
            scenario.Database.AccountNameChangeChallenges
                .Single(value => value.Id == original.ChallengeId).Status);
    }

    [Fact]
    public async Task Unresolved_pending_send_remains_blocked_after_ten_minutes()
    {
        await using var scenario = await Scenario.CreateAsync();
        scenario.Provider.Behavior =
            SendBehavior.AcceptedThenResponseLost;

        await Assert.ThrowsAsync<AccountNameChangeProviderOutcomeUnknownException>(() =>
            scenario.RequestHandler().Handle(
                scenario.Request("สมศักดิ์", "ใจดี"),
                default));
        scenario.Clock.UtcNow = scenario.Clock.UtcNow.AddHours(1);
        scenario.Provider.Behavior = SendBehavior.Accepted;

        await Assert.ThrowsAsync<AccountNameChangeProviderOutcomeUnknownException>(() =>
            scenario.RequestHandler().Handle(
                scenario.Request("สมศักดิ์", "ใจดี"),
                default));

        Assert.Equal(1, scenario.Provider.RequestCount);
        Assert.Equal(
            AccountNameChangeStatus.PendingSend,
            scenario.Database.AccountNameChangeChallenges.Single().Status);
    }

    [Fact]
    public async Task Resend_is_blocked_for_sixty_seconds_then_supersedes_the_code()
    {
        await using var scenario = await Scenario.CreateAsync();
        var original = await scenario.RequestHandler().Handle(
            scenario.Request("สมศักดิ์", "ใจดี"),
            default);

        var cooldown = await Assert.ThrowsAsync<RequestCooldownException>(() =>
            scenario.ResendHandler().Handle(
                scenario.Resend(original.ChallengeId),
                default));
        Assert.Equal(TimeSpan.FromSeconds(60), cooldown.RetryAfter);

        scenario.Clock.UtcNow = original.ResendAvailableAt;
        var resent = await scenario.ResendHandler().Handle(
            scenario.Resend(original.ChallengeId),
            default);

        Assert.NotEqual(original.ChallengeId, resent.ChallengeId);
        Assert.Equal(2, scenario.Provider.RequestCount);
        Assert.Equal(
            AccountNameChangeStatus.Superseded,
            scenario.Database.AccountNameChangeChallenges
                .Single(value => value.Id == original.ChallengeId).Status);
        Assert.Equal(
            AccountNameChangeStatus.Active,
            scenario.Database.AccountNameChangeChallenges
                .Single(value => value.Id == resent.ChallengeId).Status);
    }

    private static TimeZoneInfo Bangkok() =>
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok");

    private enum SendBehavior
    {
        Accepted,
        Rejected,
        AcceptedThenResponseLost
    }

    private sealed class FakeOtpProvider(MutableClock clock)
        : IOtpVerificationProvider
    {
        private readonly Dictionary<string, OtpChallengeRecovery> _recoveries =
            new(StringComparer.Ordinal);

        public OtpProviderCapabilities Capabilities { get; } =
            new(true, TimeSpan.FromMinutes(10), true);
        public SendBehavior Behavior { get; set; } = SendBehavior.Accepted;
        public string RejectionMessage { get; set; } =
            "ผู้ให้บริการปฏิเสธคำขอ";
        public string RejectionCode { get; set; } =
            "otp_provider_cooldown";
        public TimeSpan RejectionRetryAfter { get; set; } =
            TimeSpan.FromSeconds(30);
        public int RequestCount { get; private set; }
        public int LookupCount { get; private set; }

        public Task<OtpChallenge> RequestAsync(
            string phoneNumber,
            OtpPurpose purpose,
            string providerRequestKey,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (Behavior == SendBehavior.Rejected)
                throw new RequestCooldownException(
                    RejectionMessage,
                    RejectionRetryAfter,
                    RejectionCode);

            var challenge = new OtpChallenge(
                $"provider-{providerRequestKey}",
                "0••-•••-1202",
                null);
            _recoveries[providerRequestKey] = new(
                challenge,
                providerRequestKey,
                purpose,
                phoneNumber,
                clock.UtcNow,
                clock.UtcNow.AddMinutes(10));
            if (Behavior == SendBehavior.AcceptedThenResponseLost)
                throw new HttpRequestException("response lost");
            return Task.FromResult(challenge);
        }

        public Task<OtpChallengeRecovery?> LookupAsync(
            string providerRequestKey,
            string phoneNumber,
            OtpPurpose purpose,
            CancellationToken cancellationToken)
        {
            LookupCount++;
            return Task.FromResult(
                _recoveries.GetValueOrDefault(providerRequestKey));
        }

        public Task<string?> VerifyAsync(
            string challengeId,
            string code,
            OtpPurpose purpose,
            CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
    }

    private sealed class Scenario : IAsyncDisposable
    {
        private Scenario(
            ToklongDbContext database,
            BuyerAccount? buyer,
            SellerAccount? seller,
            AccountNameChangeSubject subject,
            MutableClock clock,
            FakeOtpProvider provider)
        {
            Database = database;
            Buyer = buyer;
            Seller = seller;
            Subject = subject;
            Clock = clock;
            Provider = provider;
        }

        public ToklongDbContext Database { get; }
        public BuyerAccount? Buyer { get; }
        public SellerAccount? Seller { get; }
        public AccountNameChangeSubject Subject { get; }
        public MutableClock Clock { get; }
        public FakeOtpProvider Provider { get; }

        public static async Task<Scenario> CreateAsync(
            bool withSeller = false)
        {
            var options = new DbContextOptionsBuilder<ToklongDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
            var database = new ToklongDbContext(options);
            var clock = new MutableClock();
            var phone = "+66921031202";
            var name = AccountName.Create("สมชาย", "ใจดี");
            var buyer = BuyerAccount.Create(
                phone,
                name,
                "buyer@example.com",
                Now.AddYears(-1));
            SellerAccount? seller = withSeller
                ? SellerAccount.Create(phone, Now.AddYears(-1), name)
                : null;
            database.Buyers.Add(buyer);
            if (seller is not null)
                database.Sellers.Add(seller);
            await database.SaveChangesAsync();
            var subject = new AccountNameChangeSubject(
                buyer.Id,
                seller?.Id,
                Guid.NewGuid(),
                phone);
            return new Scenario(
                database,
                buyer,
                seller,
                subject,
                clock,
                new FakeOtpProvider(clock));
        }

        public RequestAccountNameChangeCommand Request(
            string firstName,
            string lastName,
            string? key = null) =>
            new(
                Subject,
                firstName,
                lastName,
                key ?? Guid.NewGuid().ToString("N"));

        public ResendAccountNameChangeCodeCommand Resend(
            Guid challengeId,
            string? key = null) =>
            new(
                Subject,
                challengeId,
                key ?? Guid.NewGuid().ToString("N"));

        public GetAccountNameChangeEligibilityHandler EligibilityHandler() =>
            new(
                new BuyerRepository(Database),
                new SellerRepository(Database),
                Clock);

        public RequestAccountNameChangeHandler RequestHandler() =>
            new(
                new BuyerRepository(Database),
                new SellerRepository(Database),
                new AccountNameChangeRepository(Database),
                Provider,
                Database,
                Clock);

        public GetPendingAccountNameChangeHandler PendingHandler() =>
            new(
                new BuyerRepository(Database),
                new SellerRepository(Database),
                new AccountNameChangeRepository(Database),
                Clock);

        public ResendAccountNameChangeCodeHandler ResendHandler() =>
            new(
                new BuyerRepository(Database),
                new SellerRepository(Database),
                new AccountNameChangeRepository(Database),
                Provider,
                Database,
                Clock);

        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }
}
