using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Features.Accounts.NameChanges;
using Toklong.Domain.Accounts;
using Toklong.Domain.Authentication;
using Toklong.Domain.Buyers;
using Toklong.Domain.Common;
using Toklong.Domain.Sellers;
using Toklong.Infrastructure.Persistence;
using Toklong.Application.Tests.TestSupport;

namespace Toklong.Application.Tests.Accounts;

public sealed class AccountNameChangeVerificationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 31, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Successful_verification_updates_both_roles_and_all_active_sessions_atomically()
    {
        await using var scenario = await Scenario.CreateAsync();
        var unitOfWork = new CountingUnitOfWork(scenario.Database);

        var result = await scenario.Handler(unitOfWork).Handle(
            scenario.Command(),
            default);

        Assert.Equal("สมศักดิ์", result.FirstName);
        Assert.Equal("ใจดี", result.LastName);
        Assert.Equal("สมศักดิ์ ใจดี", result.DisplayName);
        Assert.Equal(Now, result.CompletedAt);
        Assert.Equal(result.DisplayName, scenario.Buyer.FullName);
        Assert.Equal(result.DisplayName, scenario.Seller.DisplayName);
        Assert.Equal(Now, scenario.Buyer.NameChangedAt);
        Assert.Equal(Now, scenario.Seller.NameChangedAt);
        Assert.All(
            scenario.ActiveSessions,
            session => Assert.Equal(result.DisplayName, session.DisplayName));
        Assert.All(
            scenario.InactiveSessions,
            session => Assert.Equal(
                "สมชาย ใจดี",
                session.DisplayName));
        Assert.Equal(
            AccountNameChangeStatus.Verified,
            scenario.Challenge.Status);
        Assert.Equal(Now, scenario.Challenge.VerifiedAt);
        var attempt = Assert.Single(
            scenario.Database.AccountNameVerificationAttempts);
        Assert.Equal(
            AccountNameVerificationAttemptOutcome.Verified,
            attempt.Outcome);
        Assert.Equal(5, attempt.RemainingAttempts);
        var audit = Assert.Single(
            scenario.Database.AccountNameChangeAuditEvents);
        Assert.Equal("account.name_change_verified", audit.Name);
        Assert.Equal("test:v1", audit.ProtectionVersion);
        Assert.True(
            audit.ProtectedNameEvidence!.AsSpan().IndexOf(
                Encoding.UTF8.GetBytes("สมชาย")) < 0);
        Assert.True(
            audit.ProtectedNameEvidence.AsSpan().IndexOf(
                Encoding.UTF8.GetBytes("สมศักดิ์")) < 0);
        Assert.DoesNotContain("123456", attempt.SubmittedDigest);
        Assert.Equal(2, unitOfWork.SaveCount);
        Assert.Equal(OtpPurpose.AccountNameChange, scenario.Provider.Purpose);
    }

    [Fact]
    public async Task Verification_claim_is_committed_before_provider_mutation()
    {
        await using var scenario = await Scenario.CreateAsync();
        scenario.Provider.BeforeIdempotentVerify = async () =>
        {
            scenario.Database.ChangeTracker.Clear();
            Assert.Single(
                await scenario.Database.AccountNameVerificationOperations
                    .AsNoTracking()
                    .ToListAsync());
        };

        await scenario.Handler().Handle(scenario.Command(), default);

        Assert.Equal(1, scenario.Provider.IdempotentVerifyCount);
        Assert.Single(
            scenario.Database.AccountNameVerificationOperations);
    }

    [Fact]
    public async Task Accepted_response_loss_is_recovered_without_consuming_code_again()
    {
        await using var scenario = await Scenario.CreateAsync();
        var command = scenario.Command();
        scenario.Provider.ThrowAfterAcceptanceOnce = true;
        scenario.Provider.LookupAvailable = false;

        await Assert.ThrowsAsync<DomainException>(
            () => scenario.Handler().Handle(command, default));
        scenario.Database.ChangeTracker.Clear();
        Assert.Single(
            await scenario.Database.AccountNameVerificationOperations
                .AsNoTracking()
                .ToListAsync());
        Assert.Empty(
            await scenario.Database.AccountNameVerificationAttempts
                .AsNoTracking()
                .ToListAsync());

        scenario.Provider.LookupAvailable = true;
        scenario.Clock.UtcNow =
            scenario.Challenge.ExpiresAt!.Value.AddMinutes(1);
        var recovered = await scenario.Handler().Handle(
            command,
            default);

        Assert.Equal("สมศักดิ์ ใจดี", recovered.DisplayName);
        Assert.Equal(1, scenario.Provider.IdempotentVerifyCount);
        Assert.Single(
            await scenario.Database.AccountNameVerificationOperations
                .AsNoTracking()
                .ToListAsync());
        Assert.Single(
            await scenario.Database.AccountNameVerificationAttempts
                .AsNoTracking()
                .ToListAsync());
    }

    [Fact]
    public async Task Mismatched_provider_evidence_never_changes_the_account()
    {
        await using var scenario = await Scenario.CreateAsync();
        scenario.Provider.MutateEvidence = evidence =>
            evidence with { ChallengeId = "another-challenge" };

        await Assert.ThrowsAsync<DomainException>(
            () => scenario.Handler().Handle(
                scenario.Command(),
                default));

        Assert.Equal("สมชาย ใจดี", scenario.Buyer.FullName);
        Assert.Empty(
            scenario.Database.AccountNameVerificationAttempts);
        Assert.Empty(
            scenario.Database.AccountNameChangeAuditEvents);
    }

    [Fact]
    public async Task Incorrect_codes_are_recorded_and_the_fifth_locks_the_challenge()
    {
        await using var scenario = await Scenario.CreateAsync();

        for (var index = 0; index < 4; index++)
        {
            var error = await Assert.ThrowsAsync<DomainException>(() =>
                scenario.Handler().Handle(
                    scenario.Command(
                        $"{index}{index}{index}{index}{index}{index}"),
                    default));
            Assert.Contains("รหัสไม่ถูกต้อง", error.Message);
        }

        var locked = await Assert.ThrowsAsync<DomainException>(() =>
            scenario.Handler().Handle(
                scenario.Command("999999"),
                default));
        var rejectedAfterLock =
            await Assert.ThrowsAsync<DomainException>(() =>
                scenario.Handler().Handle(
                    scenario.Command("123456"),
                    default));

        Assert.Contains("ครบจำนวน", locked.Message);
        Assert.Equal(locked.Message, rejectedAfterLock.Message);
        Assert.Equal(AccountNameChangeStatus.Locked, scenario.Challenge.Status);
        Assert.Equal(5, scenario.Challenge.IncorrectAttempts);
        Assert.Equal(
            5,
            scenario.Database.AccountNameVerificationAttempts.Count());
        Assert.Equal(5, scenario.Provider.VerifyCount);
        Assert.Equal("สมชาย ใจดี", scenario.Buyer.FullName);
        Assert.Empty(scenario.Database.AccountNameChangeAuditEvents);
    }

    [Fact]
    public async Task Revoked_authentication_session_cannot_verify_the_change()
    {
        await using var scenario = await Scenario.CreateAsync();
        scenario.ActiveSessions[0].Revoke(Now.AddSeconds(-1));
        await scenario.Database.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            scenario.Handler().Handle(scenario.Command(), default));

        Assert.Equal(0, scenario.Provider.VerifyCount);
        Assert.Equal("สมชาย ใจดี", scenario.Buyer.FullName);
        Assert.Empty(scenario.Database.AccountNameVerificationAttempts);
        Assert.Empty(scenario.Database.AccountNameChangeAuditEvents);
    }

    [Fact]
    public async Task Another_active_session_cannot_take_over_the_challenge()
    {
        await using var scenario = await Scenario.CreateAsync();
        var otherSession = MobileSession.Create(
            scenario.Buyer.Id,
            scenario.Seller.Id,
            "สมชาย ใจดี",
            scenario.Subject.PhoneNumber,
            Hash("refresh-takeover"),
            Now.AddMinutes(-1),
            Now.AddDays(30));
        scenario.Database.MobileSessions.Add(otherSession);
        await scenario.Database.SaveChangesAsync();
        var otherSubject = scenario.Subject with
        {
            SessionId = otherSession.Id
        };

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            scenario.Handler().Handle(
                scenario.Command() with { Subject = otherSubject },
                default));

        Assert.Equal(0, scenario.Provider.VerifyCount);
        Assert.Equal(AccountNameChangeStatus.Active, scenario.Challenge.Status);
        Assert.Empty(scenario.Database.AccountNameVerificationAttempts);
    }

    [Fact]
    public async Task A_changed_current_account_phone_invalidates_the_challenge()
    {
        await using var scenario = await Scenario.CreateAsync();
        scenario.Buyer.UpdatePhoneVerification(
            "+66999999999",
            Now);
        await scenario.Database.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            scenario.Handler().Handle(scenario.Command(), default));

        Assert.Equal(0, scenario.Provider.VerifyCount);
        Assert.Equal(AccountNameChangeStatus.Active, scenario.Challenge.Status);
        Assert.Empty(scenario.Database.AccountNameVerificationAttempts);
    }

    [Fact]
    public async Task Expired_code_is_recorded_without_calling_the_provider()
    {
        await using var scenario = await Scenario.CreateAsync();
        scenario.Clock.UtcNow = scenario.Challenge.ExpiresAt!.Value;

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            scenario.Handler().Handle(scenario.Command(), default));

        Assert.Contains("หมดอายุ", error.Message);
        Assert.Equal(AccountNameChangeStatus.Expired, scenario.Challenge.Status);
        Assert.Equal(0, scenario.Provider.VerifyCount);
        Assert.Equal(
            AccountNameVerificationAttemptOutcome.Expired,
            Assert.Single(
                scenario.Database.AccountNameVerificationAttempts).Outcome);
        Assert.Equal("สมชาย ใจดี", scenario.Buyer.FullName);
    }

    [Fact]
    public async Task Provider_phone_mismatch_never_changes_the_account()
    {
        await using var scenario = await Scenario.CreateAsync();
        scenario.Provider.VerifiedPhone = "+66999999999";

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            scenario.Handler().Handle(scenario.Command(), default));

        Assert.Contains("ตรวจสอบผลยืนยัน", error.Message);
        Assert.Equal(AccountNameChangeStatus.Active, scenario.Challenge.Status);
        Assert.Equal(0, scenario.Challenge.IncorrectAttempts);
        Assert.Equal("สมชาย ใจดี", scenario.Buyer.FullName);
        Assert.Equal("สมชาย ใจดี", scenario.Seller.DisplayName);
        Assert.Empty(scenario.Database.AccountNameChangeAuditEvents);
    }

    [Fact]
    public async Task Exact_replay_returns_the_completed_result_without_verifying_again()
    {
        await using var scenario = await Scenario.CreateAsync();
        var command = scenario.Command();

        var first = await scenario.Handler().Handle(command, default);
        var replay = await scenario.Handler().Handle(command, default);

        Assert.Equal(first, replay);
        Assert.Equal(1, scenario.Provider.VerifyCount);
        Assert.Single(scenario.Database.AccountNameVerificationAttempts);
        Assert.Single(scenario.Database.AccountNameChangeAuditEvents);
    }

    [Fact]
    public async Task Reusing_a_verification_key_with_a_different_code_digest_is_rejected()
    {
        await using var scenario = await Scenario.CreateAsync();
        var key = Guid.NewGuid().ToString("N");
        await scenario.Handler().Handle(
            scenario.Command("123456", key),
            default);

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            scenario.Handler().Handle(
                scenario.Command("654321", key),
                default));

        Assert.Contains("ไม่ตรงกับข้อมูลเดิม", error.Message);
        Assert.Equal(1, scenario.Provider.VerifyCount);
        Assert.Single(scenario.Database.AccountNameVerificationAttempts);
        Assert.Single(scenario.Database.AccountNameChangeAuditEvents);
    }

    [Fact]
    public async Task A_second_challenge_cannot_overwrite_the_name_during_the_new_cooldown()
    {
        await using var scenario = await Scenario.CreateAsync();
        await scenario.Handler().Handle(scenario.Command(), default);
        var second = scenario.AddActiveChallenge(
            AccountName.Create("สมปอง", "ใจดี"));
        await scenario.Database.SaveChangesAsync();
        var secondCommand = scenario.Command() with
        {
            ChallengeId = second.Id,
            IdempotencyKey = Guid.NewGuid().ToString("N")
        };

        var blocked =
            await Assert.ThrowsAsync<AccountNameChangeCooldownException>(() =>
                scenario.Handler().Handle(secondCommand, default));

        Assert.Equal(
            AccountNameChangeCalendar.AddTwoBangkokCalendarMonths(Now),
            blocked.NextAllowedAt);
        Assert.Equal(1, scenario.Provider.VerifyCount);
        Assert.Equal("สมศักดิ์ ใจดี", scenario.Buyer.FullName);
        Assert.Equal(AccountNameChangeStatus.Active, second.Status);
        Assert.Single(scenario.Database.AccountNameChangeAuditEvents);
    }

    [Fact]
    public async Task Failure_before_commit_persists_no_partial_name_or_success_evidence()
    {
        await using var scenario = await Scenario.CreateAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scenario.Handler(new ThrowingUnitOfWork()).Handle(
                scenario.Command(),
                default));
        scenario.Database.ChangeTracker.Clear();

        var buyer = await scenario.Database.Buyers.SingleAsync();
        var seller = await scenario.Database.Sellers.SingleAsync();
        var challenge =
            await scenario.Database.AccountNameChangeChallenges.SingleAsync();
        var sessions =
            await scenario.Database.MobileSessions.ToListAsync();
        Assert.Equal("สมชาย ใจดี", buyer.FullName);
        Assert.Equal("สมชาย ใจดี", seller.DisplayName);
        Assert.All(
            sessions,
            session => Assert.Equal(
                "สมชาย ใจดี",
                session.DisplayName));
        Assert.Equal(AccountNameChangeStatus.Active, challenge.Status);
        Assert.Empty(scenario.Database.AccountNameVerificationAttempts);
        Assert.Empty(scenario.Database.AccountNameChangeAuditEvents);
    }

    private sealed class Scenario : IAsyncDisposable
    {
        private Scenario(
            ToklongDbContext database,
            BuyerAccount buyer,
            SellerAccount seller,
            MobileSession[] activeSessions,
            MobileSession[] inactiveSessions,
            AccountNameChangeChallenge challenge,
            AccountNameChangeSubject subject,
            RecordingOtpProvider provider,
            MutableClock clock)
        {
            Database = database;
            Buyer = buyer;
            Seller = seller;
            ActiveSessions = activeSessions;
            InactiveSessions = inactiveSessions;
            Challenge = challenge;
            Subject = subject;
            Provider = provider;
            Clock = clock;
        }

        public ToklongDbContext Database { get; }
        public BuyerAccount Buyer { get; }
        public SellerAccount Seller { get; }
        public MobileSession[] ActiveSessions { get; }
        public MobileSession[] InactiveSessions { get; }
        public AccountNameChangeChallenge Challenge { get; }
        public AccountNameChangeSubject Subject { get; }
        public RecordingOtpProvider Provider { get; }
        public MutableClock Clock { get; }

        public static async Task<Scenario> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<ToklongDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
            var database = new ToklongDbContext(options);
            var phone = "+66921031202";
            var oldName = AccountName.Create("สมชาย", "ใจดี");
            var buyer = BuyerAccount.Create(
                phone,
                oldName,
                "buyer@example.com",
                Now.AddYears(-1));
            var seller = SellerAccount.Create(
                phone,
                Now.AddYears(-1),
                oldName);
            var firstSession = MobileSession.Create(
                buyer.Id,
                seller.Id,
                oldName.DisplayName,
                phone,
                Hash("refresh-one"),
                Now.AddDays(-1),
                Now.AddDays(30));
            var secondSession = MobileSession.Create(
                buyer.Id,
                null,
                oldName.DisplayName,
                phone,
                Hash("refresh-two"),
                Now.AddDays(-1),
                Now.AddDays(30));
            var expiredSession = MobileSession.Create(
                buyer.Id,
                seller.Id,
                oldName.DisplayName,
                phone,
                Hash("refresh-expired"),
                Now.AddDays(-2),
                Now.AddDays(-1));
            var revokedSession = MobileSession.Create(
                buyer.Id,
                seller.Id,
                oldName.DisplayName,
                phone,
                Hash("refresh-revoked"),
                Now.AddDays(-1),
                Now.AddDays(30));
            revokedSession.Revoke(Now.AddMinutes(-1));
            var challenge = AccountNameChangeChallenge.Create(
                Guid.NewGuid(),
                buyer.Id,
                seller.Id,
                firstSession.Id,
                phone,
                "0••-•••-1202",
                AccountName.Create("สมศักดิ์", "ใจดี"),
                Guid.NewGuid().ToString("N"),
                Now.AddMinutes(-1));
            challenge.MarkSendAccepted(
                "provider-name-change",
                Now.AddMinutes(-1));
            database.AddRange(
                buyer,
                seller,
                firstSession,
                secondSession,
                expiredSession,
                revokedSession,
                challenge);
            await database.SaveChangesAsync();
            var clock = new MutableClock();
            return new(
                database,
                buyer,
                seller,
                [firstSession, secondSession],
                [expiredSession, revokedSession],
                challenge,
                new AccountNameChangeSubject(
                    buyer.Id,
                    seller.Id,
                    firstSession.Id,
                    phone),
                new RecordingOtpProvider(phone),
                clock);
        }

        public AccountNameChangeChallenge AddActiveChallenge(
            AccountName pendingName)
        {
            var challenge = AccountNameChangeChallenge.Create(
                Guid.NewGuid(),
                Buyer.Id,
                Seller.Id,
                Subject.SessionId,
                Subject.PhoneNumber,
                "0••-•••-1202",
                pendingName,
                Guid.NewGuid().ToString("N"),
                Clock.UtcNow);
            challenge.MarkSendAccepted(
                $"provider-{challenge.Id:N}",
                Clock.UtcNow);
            Database.AccountNameChangeChallenges.Add(challenge);
            return challenge;
        }

        public VerifyAccountNameChangeCommand Command(
            string code = "123456",
            string? key = null) =>
            new(
                Subject,
                Challenge.Id,
                code,
                key ?? Guid.NewGuid().ToString("N"));

        public VerifyAccountNameChangeHandler Handler(
            IUnitOfWork? unitOfWork = null) =>
            new(
                new BuyerRepository(Database),
                new SellerRepository(Database),
                new MobileSessionRepository(Database),
                new AccountNameChangeRepository(Database),
                Provider,
                new DeterministicSecurity(),
                new DeterministicAccountNameAuditEvidenceWriter(),
                unitOfWork ?? Database,
                Clock,
                new ImmediateAccountPhoneTransactionManager());

        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }

    private sealed class RecordingOtpProvider
        : IOtpVerificationProvider
    {
        private readonly string phone;
        private readonly Dictionary<
            string,
            OtpProviderVerificationEvidence> evidenceByKey = [];

        public RecordingOtpProvider(string phone)
        {
            this.phone = phone;
            VerifiedPhone = phone;
        }

        public OtpProviderCapabilities Capabilities { get; } =
            new(true, TimeSpan.FromMinutes(10), true)
            {
                SupportsVerificationLookup = true
            };
        public OtpPurpose? Purpose { get; private set; }
        public int VerifyCount { get; private set; }
        public int IdempotentVerifyCount { get; private set; }
        public string? VerifiedPhone { get; set; }
        public Func<Task>? BeforeIdempotentVerify { get; set; }
        public bool ThrowAfterAcceptanceOnce { get; set; }
        public bool LookupAvailable { get; set; } = true;
        public Func<
            OtpProviderVerificationEvidence,
            OtpProviderVerificationEvidence>? MutateEvidence { get; set; }

        public Task<OtpChallenge> RequestAsync(
            string phoneNumber,
            OtpPurpose purpose,
            string providerRequestKey,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> VerifyAsync(
            string challengeId,
            string code,
            OtpPurpose purpose,
            CancellationToken cancellationToken)
        {
            Purpose = purpose;
            VerifyCount++;
            return Task.FromResult<string?>(
                code == "123456" ? VerifiedPhone : null);
        }

        public async Task<OtpProviderVerificationEvidence>
            VerifyIdempotentlyAsync(
                string challengeId,
                string code,
                OtpPurpose purpose,
                string verificationRequestKey,
                CancellationToken cancellationToken)
        {
            Purpose = purpose;
            VerifyCount++;
            IdempotentVerifyCount++;
            if (BeforeIdempotentVerify is not null)
                await BeforeIdempotentVerify();
            if (!evidenceByKey.TryGetValue(
                    verificationRequestKey,
                    out var evidence))
            {
                evidence = new(
                    verificationRequestKey,
                    challengeId,
                    purpose,
                    code == "123456"
                        ? VerifiedPhone ?? phone
                        : phone,
                    code == "123456" && VerifiedPhone is not null
                        ? OtpProviderVerificationOutcome.Verified
                        : OtpProviderVerificationOutcome.Rejected,
                    Now.AddSeconds(-1),
                    Now);
                evidenceByKey[verificationRequestKey] = evidence;
            }
            var result = MutateEvidence?.Invoke(evidence) ?? evidence;
            if (ThrowAfterAcceptanceOnce)
            {
                ThrowAfterAcceptanceOnce = false;
                throw new HttpRequestException(
                    "accepted response lost");
            }
            return result;
        }

        public Task<OtpProviderVerificationEvidence?>
            LookupVerificationAsync(
                string verificationRequestKey,
                string challengeId,
                string phoneNumber,
                OtpPurpose purpose,
                CancellationToken cancellationToken)
        {
            if (!LookupAvailable ||
                !evidenceByKey.TryGetValue(
                    verificationRequestKey,
                    out var evidence))
                return Task.FromResult<
                    OtpProviderVerificationEvidence?>(null);
            var result = MutateEvidence?.Invoke(evidence) ?? evidence;
            return Task.FromResult<
                OtpProviderVerificationEvidence?>(result);
        }
    }

    private sealed class DeterministicSecurity
        : IAccountNameVerificationSecurity
    {
        public string Digest(Guid challengeId, string code) =>
            Hash($"account-name:{challengeId:N}:{code}");
    }

    private sealed class CountingUnitOfWork(IUnitOfWork inner)
        : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(
            CancellationToken cancellationToken)
        {
            SaveCount++;
            return inner.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
    }

    private sealed class ThrowingUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("save failed");
    }

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
