using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Features.Buyers;
using Toklong.Domain.Buyers;
using Toklong.Domain.Common;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Application.Tests.Buyers;

public sealed class BuyerEmailChangeHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Request_keeps_confirmed_email_and_activates_sent_challenge()
    {
        await using var scenario = await Scenario.CreateAsync();
        var command = scenario.RequestCommand();

        var result = await scenario.RequestHandler().Handle(command, default);

        Assert.Equal("old@example.com", scenario.Buyer.Email);
        Assert.Equal("ne••@example.com", result.MaskedEmail);
        var challenge = Assert.Single(scenario.Database.BuyerEmailChangeChallenges);
        Assert.Equal(BuyerEmailChangeStatus.Active, challenge.Status);
        Assert.DoesNotContain(
            "123456",
            JsonSerializer.Serialize(challenge));
        var message = Assert.Single(scenario.Sender.Messages);
        Assert.Equal("new@example.com", message.Recipient);
        Assert.Equal(challenge.Id.ToString("N"), message.IdempotencyKey);
        Assert.Equal(challenge.Id.ToString("N"), message.CorrelationId);
        Assert.Equal(2, scenario.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Request_rejects_the_confirmed_email_case_insensitively()
    {
        await using var scenario = await Scenario.CreateAsync();

        await Assert.ThrowsAsync<DomainException>(() =>
            scenario.RequestHandler().Handle(
                scenario.RequestCommand("  OLD@example.com  "),
                default));

        Assert.Empty(scenario.Database.BuyerEmailChangeChallenges);
        Assert.Empty(scenario.Sender.Messages);
        Assert.Equal("old@example.com", scenario.Buyer.Email);
    }

    [Fact]
    public async Task Exact_request_replay_returns_the_same_view_without_another_send()
    {
        await using var scenario = await Scenario.CreateAsync();
        var command = scenario.RequestCommand();
        var first = await scenario.RequestHandler().Handle(command, default);

        var replay = await scenario.RequestHandler().Handle(command, default);

        Assert.Equal(first, replay);
        Assert.Single(scenario.Sender.Messages);
        Assert.Single(
            scenario.Database.BuyerEmailChangeAuditEvents,
            audit => audit.Name == "account.email_change_requested");
        Assert.Equal(2, scenario.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Reusing_a_request_key_for_another_destination_is_rejected()
    {
        await using var scenario = await Scenario.CreateAsync();
        var command = scenario.RequestCommand();
        await scenario.RequestHandler().Handle(command, default);

        await Assert.ThrowsAsync<DomainException>(() =>
            scenario.RequestHandler().Handle(
                command with { Email = "other@example.com" },
                default));

        Assert.Single(scenario.Sender.Messages);
        Assert.Single(scenario.Database.BuyerEmailChangeChallenges);
    }

    [Fact]
    public async Task Resend_key_is_not_an_exact_initial_request_replay()
    {
        await using var scenario = await Scenario.CreateAsync();
        var first = await scenario.RequestHandler().Handle(
            scenario.RequestCommand(),
            default);
        scenario.Clock.Advance(TimeSpan.FromSeconds(60));
        var resendKey = Scenario.NewKey();
        var replacement = await scenario.ResendHandler().Handle(
            new ResendBuyerEmailChangeCommand(
                scenario.Buyer.Id,
                first.ChallengeId,
                resendKey),
            default);
        var sendCount = scenario.Sender.Messages.Count;

        await Assert.ThrowsAsync<DomainException>(() =>
            scenario.RequestHandler().Handle(
                scenario.RequestCommand(
                    "new@example.com",
                    resendKey),
                default));

        Assert.Equal(sendCount, scenario.Sender.Messages.Count);
        Assert.Equal(
            BuyerEmailChangeStatus.Active,
            scenario.Database.BuyerEmailChangeChallenges
                .Single(challenge =>
                    challenge.Id == replacement.ChallengeId)
                .Status);
    }

    [Fact]
    public async Task Sender_failure_never_exposes_an_active_challenge()
    {
        await using var scenario = await Scenario.CreateAsync();
        scenario.Sender.Failure = new TransactionalEmailSendException(
            "provider detail that must not escape",
            TransactionalEmailFailureKind.Transient);

        var exception = await Assert.ThrowsAsync<DomainException>(() =>
            scenario.RequestHandler().Handle(
                scenario.RequestCommand(),
                default));

        Assert.Equal(
            "ยังส่งอีเมลไม่ได้ กรุณาลองอีกครั้ง",
            exception.Message);
        Assert.Equal("old@example.com", scenario.Buyer.Email);
        Assert.Equal(
            BuyerEmailChangeStatus.SendFailed,
            Assert.Single(
                scenario.Database.BuyerEmailChangeChallenges).Status);
        Assert.Null(await scenario.PendingHandler().Handle(
            new GetPendingBuyerEmailChangeQuery(scenario.Buyer.Id),
            default));
        var audit = Assert.Single(
            scenario.Database.BuyerEmailChangeAuditEvents);
        Assert.Equal("account.email_change_send_failed", audit.Name);
        Assert.Equal("transient", audit.Result);
        Assert.DoesNotContain("provider detail", JsonSerializer.Serialize(audit));
        Assert.Equal(2, scenario.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Pending_query_returns_only_an_active_unexpired_masked_view()
    {
        await using var scenario = await Scenario.CreateAsync();
        var requested = await scenario.RequestHandler().Handle(
            scenario.RequestCommand(),
            default);

        var pending = await scenario.PendingHandler().Handle(
            new GetPendingBuyerEmailChangeQuery(scenario.Buyer.Id),
            default);

        Assert.Equal(requested, pending);
        Assert.DoesNotContain("new@example.com", JsonSerializer.Serialize(pending));

        scenario.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Null(await scenario.PendingHandler().Handle(
            new GetPendingBuyerEmailChangeQuery(scenario.Buyer.Id),
            default));
    }

    [Fact]
    public async Task Pending_query_hides_pending_send()
    {
        await using var scenario = await Scenario.CreateAsync();
        await scenario.AddChallengeAsync(active: false);

        var pending = await scenario.PendingHandler().Handle(
            new GetPendingBuyerEmailChangeQuery(scenario.Buyer.Id),
            default);

        Assert.Null(pending);
    }

    [Fact]
    public async Task Resend_enforces_timing_then_supersedes_and_returns_a_new_identifier()
    {
        await using var scenario = await Scenario.CreateAsync();
        var originalView = await scenario.RequestHandler().Handle(
            scenario.RequestCommand(),
            default);
        var original = Assert.Single(
            scenario.Database.BuyerEmailChangeChallenges);
        scenario.Clock.Advance(TimeSpan.FromSeconds(59));

        await Assert.ThrowsAsync<DomainException>(() =>
            scenario.ResendHandler().Handle(
                new ResendBuyerEmailChangeCommand(
                    scenario.Buyer.Id,
                    original.Id,
                    Scenario.NewKey()),
                default));

        scenario.Clock.Advance(TimeSpan.FromSeconds(1));
        var replacement = await scenario.ResendHandler().Handle(
            new ResendBuyerEmailChangeCommand(
                scenario.Buyer.Id,
                original.Id,
                Scenario.NewKey()),
            default);

        Assert.Equal(BuyerEmailChangeStatus.Superseded, original.Status);
        Assert.NotEqual(originalView.ChallengeId, replacement.ChallengeId);
        Assert.Equal(
            BuyerEmailChangeStatus.Active,
            scenario.Database.BuyerEmailChangeChallenges
                .Single(challenge => challenge.Id == replacement.ChallengeId)
                .Status);
        Assert.Equal(2, scenario.Sender.Messages.Count);
        Assert.Contains(
            scenario.Database.BuyerEmailChangeAuditEvents,
            audit => audit.Name == "account.email_change_code_resent");
        Assert.Equal("old@example.com", scenario.Buyer.Email);
    }

    [Fact]
    public async Task Exact_resend_replay_does_not_send_another_code()
    {
        await using var scenario = await Scenario.CreateAsync();
        var original = await scenario.RequestHandler().Handle(
            scenario.RequestCommand(),
            default);
        scenario.Clock.Advance(TimeSpan.FromSeconds(60));
        var command = new ResendBuyerEmailChangeCommand(
            scenario.Buyer.Id,
            original.ChallengeId,
            Scenario.NewKey());
        var first = await scenario.ResendHandler().Handle(command, default);

        var replay = await scenario.ResendHandler().Handle(command, default);

        Assert.Equal(first, replay);
        Assert.Equal(2, scenario.Sender.Messages.Count);
        Assert.Single(
            scenario.Database.BuyerEmailChangeAuditEvents,
            audit => audit.Name == "account.email_change_code_resent");
    }

    [Fact]
    public async Task Resend_key_from_another_source_challenge_is_not_an_exact_replay()
    {
        await using var scenario = await Scenario.CreateAsync();
        var first = await scenario.RequestHandler().Handle(
            scenario.RequestCommand(),
            default);
        scenario.Clock.Advance(TimeSpan.FromSeconds(60));
        var resendKey = Scenario.NewKey();
        var replacement = await scenario.ResendHandler().Handle(
            new ResendBuyerEmailChangeCommand(
                scenario.Buyer.Id,
                first.ChallengeId,
                resendKey),
            default);
        var later = await scenario.RequestHandler().Handle(
            scenario.RequestCommand(
                "new@example.com",
                Scenario.NewKey()),
            default);
        var sendCount = scenario.Sender.Messages.Count;

        await Assert.ThrowsAsync<DomainException>(() =>
            scenario.ResendHandler().Handle(
                new ResendBuyerEmailChangeCommand(
                    scenario.Buyer.Id,
                    later.ChallengeId,
                    resendKey),
                default));

        Assert.Equal(sendCount, scenario.Sender.Messages.Count);
        Assert.Equal(
            BuyerEmailChangeStatus.Superseded,
            scenario.Database.BuyerEmailChangeChallenges
                .Single(challenge =>
                    challenge.Id == replacement.ChallengeId)
                .Status);
        Assert.Equal(
            BuyerEmailChangeStatus.Active,
            scenario.Database.BuyerEmailChangeChallenges
                .Single(challenge => challenge.Id == later.ChallengeId)
                .Status);
        Assert.Equal(3, scenario.Database.BuyerEmailChangeChallenges.Count());
    }

    [Fact]
    public async Task Request_supersedes_an_existing_active_challenge()
    {
        await using var scenario = await Scenario.CreateAsync();
        var first = await scenario.RequestHandler().Handle(
            scenario.RequestCommand(),
            default);

        var second = await scenario.RequestHandler().Handle(
            scenario.RequestCommand(
                "replacement@example.com",
                Scenario.NewKey()),
            default);

        Assert.Equal(
            BuyerEmailChangeStatus.Superseded,
            scenario.Database.BuyerEmailChangeChallenges
                .Single(challenge => challenge.Id == first.ChallengeId)
                .Status);
        Assert.NotEqual(first.ChallengeId, second.ChallengeId);
        Assert.Equal("old@example.com", scenario.Buyer.Email);
    }

    [Fact]
    public async Task Wrong_attempt_is_persisted_before_the_error_is_returned()
    {
        await using var scenario = await Scenario.CreateAsync();
        var requested = await scenario.RequestHandler().Handle(
            scenario.RequestCommand(),
            default);
        scenario.UnitOfWork.Reset();

        await Assert.ThrowsAsync<DomainException>(() =>
            scenario.VerifyHandler().Handle(
                new VerifyBuyerEmailChangeCommand(
                    scenario.Buyer.Id,
                    requested.ChallengeId,
                    "000000",
                    Scenario.NewKey()),
                default));

        Assert.Equal(1, scenario.UnitOfWork.SaveCount);
        scenario.Database.ChangeTracker.Clear();
        var stored = await scenario.Database.BuyerEmailChangeChallenges
            .SingleAsync(challenge => challenge.Id == requested.ChallengeId);
        Assert.Equal(1, stored.IncorrectAttempts);
        Assert.Equal(4, stored.RemainingAttempts);
        Assert.Equal(BuyerEmailChangeStatus.Active, stored.Status);
        Assert.Equal("old@example.com", scenario.Buyer.Email);
    }

    [Fact]
    public async Task Exact_wrong_attempt_replay_returns_the_same_error_without_consuming_an_attempt()
    {
        await using var scenario = await Scenario.CreateAsync();
        var requested = await scenario.RequestHandler().Handle(
            scenario.RequestCommand(),
            default);
        var command = new VerifyBuyerEmailChangeCommand(
            scenario.Buyer.Id,
            requested.ChallengeId,
            "000000",
            Scenario.NewKey());
        scenario.UnitOfWork.Reset();

        var first = await Assert.ThrowsAsync<DomainException>(
            () => scenario.VerifyHandler().Handle(command, default));
        var replay = await Assert.ThrowsAsync<DomainException>(
            () => scenario.VerifyHandler().Handle(command, default));

        Assert.Equal(first.Message, replay.Message);
        Assert.Equal(1, scenario.UnitOfWork.SaveCount);
        var stored = Assert.Single(
            scenario.Database.BuyerEmailChangeChallenges,
            challenge => challenge.Id == requested.ChallengeId);
        Assert.Equal(1, stored.IncorrectAttempts);
        var attempt = Assert.Single(
            scenario.Database.BuyerEmailVerificationAttempts);
        Assert.Equal(command.IdempotencyKey, attempt.IdempotencyKey);
        Assert.Equal(
            BuyerEmailVerificationAttemptOutcome.Incorrect,
            attempt.Outcome);
        Assert.Equal(4, attempt.RemainingAttempts);
        var serialized = JsonSerializer.Serialize(attempt);
        Assert.DoesNotContain("000000", serialized);
        Assert.DoesNotContain("new@example.com", serialized);
    }

    [Fact]
    public async Task Wrong_attempt_key_reused_with_a_different_code_is_rejected()
    {
        await using var scenario = await Scenario.CreateAsync();
        var requested = await scenario.RequestHandler().Handle(
            scenario.RequestCommand(),
            default);
        var key = Scenario.NewKey();
        await Assert.ThrowsAsync<DomainException>(() =>
            scenario.VerifyHandler().Handle(
                new VerifyBuyerEmailChangeCommand(
                    scenario.Buyer.Id,
                    requested.ChallengeId,
                    "000000",
                    key),
                default));
        scenario.UnitOfWork.Reset();

        var mismatch = await Assert.ThrowsAsync<DomainException>(() =>
            scenario.VerifyHandler().Handle(
                new VerifyBuyerEmailChangeCommand(
                    scenario.Buyer.Id,
                    requested.ChallengeId,
                    "123456",
                    key),
                default));

        Assert.Equal(
            "คำขอนี้ไม่ตรงกับข้อมูลเดิม กรุณาลองใหม่",
            mismatch.Message);
        Assert.Equal(0, scenario.UnitOfWork.SaveCount);
        Assert.Equal("old@example.com", scenario.Buyer.Email);
        Assert.Single(scenario.Database.BuyerEmailVerificationAttempts);
        Assert.Equal(
            1,
            scenario.Database.BuyerEmailChangeChallenges
                .Single(challenge =>
                    challenge.Id == requested.ChallengeId)
                .IncorrectAttempts);
    }

    [Fact]
    public async Task Fifth_wrong_attempt_persists_lock_and_audit()
    {
        await using var scenario = await Scenario.CreateAsync();
        var requested = await scenario.RequestHandler().Handle(
            scenario.RequestCommand(),
            default);
        scenario.UnitOfWork.Reset();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Assert.ThrowsAsync<DomainException>(() =>
                scenario.VerifyHandler().Handle(
                    new VerifyBuyerEmailChangeCommand(
                        scenario.Buyer.Id,
                        requested.ChallengeId,
                        "000000",
                        Scenario.NewKey()),
                    default));
        }

        var challenge = scenario.Database.BuyerEmailChangeChallenges
            .Single(item => item.Id == requested.ChallengeId);
        Assert.Equal(BuyerEmailChangeStatus.Locked, challenge.Status);
        Assert.Equal(0, challenge.RemainingAttempts);
        var audit = Assert.Single(
            scenario.Database.BuyerEmailChangeAuditEvents,
            item => item.Name == "account.email_change_locked");
        Assert.Equal("locked", audit.Result);
        Assert.Equal(5, scenario.UnitOfWork.SaveCount);
        Assert.Equal("old@example.com", scenario.Buyer.Email);
    }

    [Fact]
    public async Task Verification_expiry_is_persisted_and_keeps_confirmed_email()
    {
        await using var scenario = await Scenario.CreateAsync();
        var requested = await scenario.RequestHandler().Handle(
            scenario.RequestCommand(),
            default);
        scenario.UnitOfWork.Reset();
        scenario.Clock.Advance(TimeSpan.FromMinutes(10));

        await Assert.ThrowsAsync<DomainException>(() =>
            scenario.VerifyHandler().Handle(
                new VerifyBuyerEmailChangeCommand(
                    scenario.Buyer.Id,
                    requested.ChallengeId,
                    "123456",
                    Scenario.NewKey()),
                default));

        Assert.Equal(
            BuyerEmailChangeStatus.Expired,
            scenario.Database.BuyerEmailChangeChallenges
                .Single(item => item.Id == requested.ChallengeId)
                .Status);
        Assert.Equal("old@example.com", scenario.Buyer.Email);
        Assert.Equal(1, scenario.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Another_buyer_cannot_read_resend_or_verify_a_challenge()
    {
        await using var scenario = await Scenario.CreateAsync();
        var requested = await scenario.RequestHandler().Handle(
            scenario.RequestCommand(),
            default);
        var otherBuyer = BuyerAccount.Create(
            "+66822222222",
            "Other Buyer",
            "other@example.com",
            Now);
        scenario.Database.Buyers.Add(otherBuyer);
        await scenario.Database.SaveChangesAsync();
        scenario.UnitOfWork.Reset();

        Assert.Null(await scenario.PendingHandler().Handle(
            new GetPendingBuyerEmailChangeQuery(otherBuyer.Id),
            default));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            scenario.ResendHandler().Handle(
                new ResendBuyerEmailChangeCommand(
                    otherBuyer.Id,
                    requested.ChallengeId,
                    Scenario.NewKey()),
                default));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            scenario.VerifyHandler().Handle(
                new VerifyBuyerEmailChangeCommand(
                    otherBuyer.Id,
                    requested.ChallengeId,
                    "123456",
                    Scenario.NewKey()),
                default));

        Assert.Equal(0, scenario.UnitOfWork.SaveCount);
        Assert.Equal(
            BuyerEmailChangeStatus.Active,
            scenario.Database.BuyerEmailChangeChallenges
                .Single(item => item.Id == requested.ChallengeId)
                .Status);
    }

    [Fact]
    public async Task Verify_activates_buyer_challenge_and_redacted_audit_in_one_save()
    {
        await using var scenario = await Scenario.CreateAsync();
        var requested = await scenario.RequestHandler().Handle(
            scenario.RequestCommand(),
            default);
        scenario.Clock.Advance(TimeSpan.FromSeconds(5));
        scenario.UnitOfWork.Reset();

        var result = await scenario.VerifyHandler().Handle(
            new VerifyBuyerEmailChangeCommand(
                scenario.Buyer.Id,
                requested.ChallengeId,
                "123456",
                Scenario.NewKey()),
            default);

        Assert.Equal("new@example.com", result.Email);
        Assert.Equal(scenario.Clock.UtcNow, result.CompletedAt);
        Assert.Equal("new@example.com", scenario.Buyer.Email);
        Assert.Equal(
            BuyerEmailChangeStatus.Verified,
            scenario.Database.BuyerEmailChangeChallenges
                .Single(item => item.Id == requested.ChallengeId)
                .Status);
        Assert.Equal(1, scenario.UnitOfWork.SaveCount);

        var audits = scenario.Database.BuyerEmailChangeAuditEvents.ToArray();
        Assert.Equal(2, audits.Length);
        var verified = Assert.Single(
            audits,
            audit => audit.Name == "account.email_change_verified");
        Assert.Equal("ne••@example.com", verified.MaskedDestination);
        Assert.Equal(
            scenario.CodeService.HashDestination("new@example.com"),
            verified.DestinationHash);
        var serializedAudits = JsonSerializer.Serialize(audits);
        Assert.DoesNotContain("new@example.com", serializedAudits);
        Assert.DoesNotContain("123456", serializedAudits);
    }

    [Fact]
    public async Task Exact_verification_replay_returns_the_original_completion()
    {
        await using var scenario = await Scenario.CreateAsync();
        var requested = await scenario.RequestHandler().Handle(
            scenario.RequestCommand(),
            default);
        var command = new VerifyBuyerEmailChangeCommand(
            scenario.Buyer.Id,
            requested.ChallengeId,
            "123456",
            Scenario.NewKey());
        scenario.UnitOfWork.Reset();
        var first = await scenario.VerifyHandler().Handle(command, default);
        scenario.Clock.Advance(TimeSpan.FromSeconds(10));

        var replay = await scenario.VerifyHandler().Handle(command, default);

        Assert.Equal(first, replay);
        Assert.Equal(1, scenario.UnitOfWork.SaveCount);
        Assert.Single(
            scenario.Database.BuyerEmailChangeAuditEvents,
            audit => audit.Name == "account.email_change_verified");
    }

    [Fact]
    public async Task Older_verification_replay_keeps_its_original_email_result()
    {
        await using var scenario = await Scenario.CreateAsync();
        var firstRequest = await scenario.RequestHandler().Handle(
            scenario.RequestCommand(),
            default);
        var firstCommand = new VerifyBuyerEmailChangeCommand(
            scenario.Buyer.Id,
            firstRequest.ChallengeId,
            "123456",
            Scenario.NewKey());
        var firstResult = await scenario.VerifyHandler().Handle(
            firstCommand,
            default);
        var secondRequest = await scenario.RequestHandler().Handle(
            scenario.RequestCommand(
                "later@example.com",
                Scenario.NewKey()),
            default);
        await scenario.VerifyHandler().Handle(
            new VerifyBuyerEmailChangeCommand(
                scenario.Buyer.Id,
                secondRequest.ChallengeId,
                "123456",
                Scenario.NewKey()),
            default);

        var replay = await scenario.VerifyHandler().Handle(
            firstCommand,
            default);

        Assert.Equal(firstResult, replay);
        Assert.Equal("later@example.com", scenario.Buyer.Email);
    }

    [Fact]
    public async Task Completed_verification_rejects_a_different_key()
    {
        await using var scenario = await Scenario.CreateAsync();
        var requested = await scenario.RequestHandler().Handle(
            scenario.RequestCommand(),
            default);
        await scenario.VerifyHandler().Handle(
            new VerifyBuyerEmailChangeCommand(
                scenario.Buyer.Id,
                requested.ChallengeId,
                "123456",
                Scenario.NewKey()),
            default);
        scenario.UnitOfWork.Reset();

        await Assert.ThrowsAsync<DomainException>(() =>
            scenario.VerifyHandler().Handle(
                new VerifyBuyerEmailChangeCommand(
                    scenario.Buyer.Id,
                    requested.ChallengeId,
                    "123456",
                    Scenario.NewKey()),
                default));

        Assert.Equal(0, scenario.UnitOfWork.SaveCount);
        Assert.Single(
            scenario.Database.BuyerEmailChangeAuditEvents,
            audit => audit.Name == "account.email_change_verified");
    }

    [Fact]
    public async Task Every_audit_uses_only_masked_and_keyed_destination_evidence()
    {
        await using var scenario = await Scenario.CreateAsync();
        var first = await scenario.RequestHandler().Handle(
            scenario.RequestCommand(),
            default);
        scenario.Clock.Advance(TimeSpan.FromSeconds(60));
        var replacement = await scenario.ResendHandler().Handle(
            new ResendBuyerEmailChangeCommand(
                scenario.Buyer.Id,
                first.ChallengeId,
                Scenario.NewKey()),
            default);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Assert.ThrowsAsync<DomainException>(() =>
                scenario.VerifyHandler().Handle(
                    new VerifyBuyerEmailChangeCommand(
                        scenario.Buyer.Id,
                        replacement.ChallengeId,
                        "000000",
                        Scenario.NewKey()),
                    default));
        }

        var audits = scenario.Database.BuyerEmailChangeAuditEvents.ToArray();
        Assert.Equal(3, audits.Length);
        Assert.All(audits, audit =>
        {
            Assert.Equal("ne••@example.com", audit.MaskedDestination);
            Assert.Equal(
                scenario.CodeService.HashDestination("new@example.com"),
                audit.DestinationHash);
        });
        var serialized = JsonSerializer.Serialize(audits);
        Assert.DoesNotContain("new@example.com", serialized);
        Assert.DoesNotContain("123456", serialized);
        Assert.DoesNotContain("000000", serialized);
    }

    private sealed class Scenario : IAsyncDisposable
    {
        private Scenario(
            ToklongDbContext database,
            BuyerAccount buyer)
        {
            Database = database;
            Buyer = buyer;
            BuyerRepository = new BuyerRepository(database);
            EmailChangeRepository =
                new BuyerEmailChangeRepository(database);
            UnitOfWork = new CountingUnitOfWork(database);
        }

        public ToklongDbContext Database { get; }
        public BuyerAccount Buyer { get; }
        public BuyerRepository BuyerRepository { get; }
        public BuyerEmailChangeRepository EmailChangeRepository { get; }
        public DeterministicCodeService CodeService { get; } = new();
        public RecordingTemplate Template { get; } = new();
        public RecordingSender Sender { get; } = new();
        public MutableClock Clock { get; } = new(Now);
        public CountingUnitOfWork UnitOfWork { get; }

        public static async Task<Scenario> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<ToklongDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var database = new ToklongDbContext(options);
            var buyer = BuyerAccount.Create(
                "+66811111111",
                "Buyer Example",
                "old@example.com",
                Now);
            database.Buyers.Add(buyer);
            await database.SaveChangesAsync();
            return new Scenario(database, buyer);
        }

        public RequestBuyerEmailChangeCommand RequestCommand(
            string email = "new@example.com",
            string? key = null) =>
            new(Buyer.Id, email, key ?? NewKey());

        public RequestBuyerEmailChangeHandler RequestHandler() =>
            new(
                BuyerRepository,
                EmailChangeRepository,
                CodeService,
                Template,
                Sender,
                UnitOfWork,
                Clock);

        public GetPendingBuyerEmailChangeHandler PendingHandler() =>
            new(EmailChangeRepository, Clock);

        public ResendBuyerEmailChangeHandler ResendHandler() =>
            new(
                EmailChangeRepository,
                CodeService,
                Template,
                Sender,
                UnitOfWork,
                Clock);

        public VerifyBuyerEmailChangeHandler VerifyHandler() =>
            new(
                BuyerRepository,
                EmailChangeRepository,
                CodeService,
                UnitOfWork,
                Clock);

        public async Task<BuyerEmailChangeChallenge> AddChallengeAsync(
            bool active)
        {
            var id = Guid.NewGuid();
            var challenge = BuyerEmailChangeChallenge.Create(
                id,
                Buyer.Id,
                "new@example.com",
                "ne••@example.com",
                CodeService.Issue(id).Digest,
                NewKey(),
                Clock.UtcNow);
            if (active)
                challenge.MarkSendAccepted(Clock.UtcNow);
            await EmailChangeRepository.AddAsync(challenge, default);
            await Database.SaveChangesAsync();
            return challenge;
        }

        public static string NewKey() => Guid.NewGuid().ToString("N");

        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }

    private sealed class DeterministicCodeService
        : IEmailVerificationCodeService
    {
        public EmailVerificationCodePair Issue(Guid challengeId) =>
            new("123456", Digest(challengeId, "123456"));

        public string Digest(Guid challengeId, string code) =>
            Hash($"{challengeId:N}:{code}");

        public string HashDestination(string normalizedEmail) =>
            Hash($"destination:{normalizedEmail}");

        private static string Hash(string value) =>
            Convert.ToHexString(
                    SHA256.HashData(
                        Encoding.UTF8.GetBytes(value)))
                .ToLowerInvariant();
    }

    private sealed class RecordingTemplate : IEmailVerificationTemplate
    {
        public RenderedEmail Render(string code) =>
            new(
                "Verify your TOKLONG email",
                $"Code: {code}",
                $"<strong>{code}</strong>");
    }

    private sealed class RecordingSender : ITransactionalEmailSender
    {
        public List<TransactionalEmailMessage> Messages { get; } = [];
        public TransactionalEmailSendException? Failure { get; set; }

        public Task<EmailSendAcceptance> SendAsync(
            TransactionalEmailMessage message,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Messages.Add(message);
            return Failure is not null
                ? Task.FromException<EmailSendAcceptance>(Failure)
                : Task.FromResult(
                    new EmailSendAcceptance(
                        $"accepted-{message.CorrelationId}"));
        }
    }

    private sealed class MutableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = now;

        public void Advance(TimeSpan duration) => UtcNow += duration;
    }

    private sealed class CountingUnitOfWork(
        IUnitOfWork inner) : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public async Task<int> SaveChangesAsync(
            CancellationToken cancellationToken)
        {
            SaveCount++;
            return await inner.SaveChangesAsync(cancellationToken);
        }

        public void Reset() => SaveCount = 0;
    }
}
