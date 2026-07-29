using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Features.Buyers;
using Toklong.Domain.Buyers;
using Toklong.Domain.Common;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Application.Tests.Buyers;

public sealed class BuyerEmailChangeConcurrencyTests
{
    static BuyerEmailChangeConcurrencyTests() =>
        SQLitePCL.raw.SetProvider(
            new SQLitePCL.SQLite3Provider_sqlite3());

    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Concurrent_exact_request_reloads_the_winner()
    {
        await using var database = await RelationalDatabase.CreateAsync();
        await using var losingContext = database.CreateContext();
        await using var winningContext = database.CreateContext();
        var blocker = new BlockingFirstSaveUnitOfWork(losingContext);
        var losingSender = new RecordingSender();
        var winningSender = new RecordingSender();
        var command = new RequestBuyerEmailChangeCommand(
            database.BuyerId,
            "new@example.com",
            NewKey());

        var losingTask = RequestHandler(
            losingContext,
            blocker,
            losingSender).Handle(command, default);
        await blocker.FirstSaveReached.WaitAsync(TimeSpan.FromSeconds(5));
        BuyerEmailChangeView winner;
        try
        {
            winner = await RequestHandler(
                winningContext,
                winningContext,
                winningSender).Handle(command, default);
        }
        finally
        {
            blocker.Release();
        }

        var replay = await losingTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(winner, replay);
        Assert.Empty(losingSender.Messages);
        Assert.Single(winningSender.Messages);
        await using var assertionContext = database.CreateContext();
        Assert.Single(
            await assertionContext.BuyerEmailChangeChallenges
                .ToListAsync());
    }

    [Fact]
    public async Task Concurrent_request_with_mismatched_destination_returns_a_domain_error()
    {
        await using var database = await RelationalDatabase.CreateAsync();
        await using var losingContext = database.CreateContext();
        await using var winningContext = database.CreateContext();
        var blocker = new BlockingFirstSaveUnitOfWork(losingContext);
        var losingSender = new RecordingSender();
        var key = NewKey();
        var losingTask = RequestHandler(
            losingContext,
            blocker,
            losingSender).Handle(
                new RequestBuyerEmailChangeCommand(
                    database.BuyerId,
                    "loser@example.com",
                    key),
                default);
        await blocker.FirstSaveReached.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await RequestHandler(
                winningContext,
                winningContext,
                new RecordingSender()).Handle(
                    new RequestBuyerEmailChangeCommand(
                        database.BuyerId,
                        "winner@example.com",
                        key),
                    default);
        }
        finally
        {
            blocker.Release();
        }

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => losingTask);

        Assert.Equal(
            "คำขอนี้ไม่ตรงกับข้อมูลเดิม กรุณาลองใหม่",
            exception.Message);
        Assert.Empty(losingSender.Messages);
    }

    [Fact]
    public async Task Concurrent_exact_resend_reloads_the_winner()
    {
        await using var database = await RelationalDatabase.CreateAsync();
        var original = await database.AddActiveChallengeAsync();
        await using var losingContext = database.CreateContext();
        await using var winningContext = database.CreateContext();
        var blocker = new BlockingFirstSaveUnitOfWork(losingContext);
        var losingSender = new RecordingSender();
        var winningSender = new RecordingSender();
        var command = new ResendBuyerEmailChangeCommand(
            database.BuyerId,
            original.Id,
            NewKey());

        var losingTask = ResendHandler(
            losingContext,
            blocker,
            losingSender).Handle(command, default);
        await blocker.FirstSaveReached.WaitAsync(TimeSpan.FromSeconds(5));
        BuyerEmailChangeView winner;
        try
        {
            winner = await ResendHandler(
                winningContext,
                winningContext,
                winningSender).Handle(command, default);
        }
        finally
        {
            blocker.Release();
        }

        var replay = await losingTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(winner, replay);
        Assert.Empty(losingSender.Messages);
        Assert.Single(winningSender.Messages);
        await using var assertionContext = database.CreateContext();
        Assert.Equal(
            2,
            await assertionContext.BuyerEmailChangeChallenges.CountAsync());
    }

    [Fact]
    public async Task Concurrent_exact_verification_reloads_the_winner()
    {
        await using var database = await RelationalDatabase.CreateAsync();
        var challenge = await database.AddActiveChallengeAsync();
        await using var losingContext = database.CreateContext();
        await using var winningContext = database.CreateContext();
        var blocker = new BlockingFirstSaveUnitOfWork(losingContext);
        var command = new VerifyBuyerEmailChangeCommand(
            database.BuyerId,
            challenge.Id,
            "123456",
            NewKey());

        var losingTask = VerifyHandler(
            losingContext,
            blocker).Handle(command, default);
        await blocker.FirstSaveReached.WaitAsync(TimeSpan.FromSeconds(5));
        VerifiedBuyerEmailChangeView winner;
        try
        {
            winner = await VerifyHandler(
                winningContext,
                winningContext).Handle(command, default);
        }
        finally
        {
            blocker.Release();
        }

        var replay = await losingTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(winner, replay);
        await using var assertionContext = database.CreateContext();
        Assert.Single(
            await assertionContext.BuyerEmailVerificationAttempts
                .ToListAsync());
        Assert.Single(
            await assertionContext.BuyerEmailChangeAuditEvents
                .Where(audit =>
                    audit.Name == "account.email_change_verified")
                .ToListAsync());
        Assert.Equal(
            "new@example.com",
            (await assertionContext.Buyers.SingleAsync()).Email);
    }

    [Fact]
    public async Task Concurrent_verification_with_mismatched_code_returns_a_domain_error()
    {
        await using var database = await RelationalDatabase.CreateAsync();
        var challenge = await database.AddActiveChallengeAsync();
        await using var losingContext = database.CreateContext();
        await using var winningContext = database.CreateContext();
        var blocker = new BlockingFirstSaveUnitOfWork(losingContext);
        var key = NewKey();
        var losingTask = VerifyHandler(
            losingContext,
            blocker).Handle(
                new VerifyBuyerEmailChangeCommand(
                    database.BuyerId,
                    challenge.Id,
                    "123456",
                    key),
                default);
        await blocker.FirstSaveReached.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await Assert.ThrowsAsync<DomainException>(() =>
                VerifyHandler(
                    winningContext,
                    winningContext).Handle(
                        new VerifyBuyerEmailChangeCommand(
                            database.BuyerId,
                            challenge.Id,
                            "000000",
                            key),
                        default));
        }
        finally
        {
            blocker.Release();
        }

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => losingTask);

        Assert.Equal(
            "คำขอนี้ไม่ตรงกับข้อมูลเดิม กรุณาลองใหม่",
            exception.Message);
        await using var assertionContext = database.CreateContext();
        Assert.Equal(
            1,
            (await assertionContext.BuyerEmailChangeChallenges
                .SingleAsync()).IncorrectAttempts);
        Assert.Equal(
            "old@example.com",
            (await assertionContext.Buyers.SingleAsync()).Email);
        Assert.Single(
            await assertionContext.BuyerEmailVerificationAttempts
                .ToListAsync());
    }

    [Fact]
    public async Task Concurrent_distinct_wrong_verifications_are_both_counted()
    {
        await using var database = await RelationalDatabase.CreateAsync();
        var challenge = await database.AddActiveChallengeAsync();
        await using var losingContext = database.CreateContext();
        await using var winningContext = database.CreateContext();
        var blocker = new BlockingFirstSaveUnitOfWork(losingContext);
        var losingTask = CaptureDomainExceptionAsync(() =>
            VerifyHandler(
                losingContext,
                blocker).Handle(
                    new VerifyBuyerEmailChangeCommand(
                        database.BuyerId,
                        challenge.Id,
                        "000000",
                        NewKey()),
                    default));
        await blocker.FirstSaveReached.WaitAsync(
            TimeSpan.FromSeconds(5));
        DomainException winningError;
        try
        {
            winningError = await CaptureDomainExceptionAsync(() =>
                VerifyHandler(
                    winningContext,
                    winningContext).Handle(
                        new VerifyBuyerEmailChangeCommand(
                            database.BuyerId,
                            challenge.Id,
                            "111111",
                            NewKey()),
                        default));
        }
        finally
        {
            blocker.Release();
        }

        var losingError = await losingTask.WaitAsync(
            TimeSpan.FromSeconds(5));

        Assert.Equal(
            "รหัสไม่ถูกต้อง ลองตรวจสอบแล้วกรอกอีกครั้ง",
            winningError.Message);
        Assert.Equal(winningError.Message, losingError.Message);
        await using var assertionContext = database.CreateContext();
        var stored = await assertionContext
            .BuyerEmailChangeChallenges
            .SingleAsync();
        Assert.Equal(2, stored.IncorrectAttempts);
        Assert.Equal(3, stored.RemainingAttempts);
        Assert.Equal(
            2,
            await assertionContext.BuyerEmailVerificationAttempts
                .CountAsync());
    }

    [Fact]
    public async Task Five_concurrent_distinct_wrong_verifications_lock_the_challenge()
    {
        await using var database = await RelationalDatabase.CreateAsync();
        var challenge = await database.AddActiveChallengeAsync();
        var contexts = Enumerable.Range(0, 5)
            .Select(_ => database.CreateContext())
            .ToArray();
        var blockers = contexts
            .Select(context =>
                new BlockingFirstSaveUnitOfWork(context))
            .ToArray();
        var wrongCodes = new[]
        {
            "000000",
            "111111",
            "222222",
            "333333",
            "444444"
        };

        try
        {
            var submissions = contexts
                .Select((context, index) =>
                    CaptureDomainExceptionAsync(() =>
                        VerifyHandler(
                            context,
                            blockers[index]).Handle(
                                new VerifyBuyerEmailChangeCommand(
                                    database.BuyerId,
                                    challenge.Id,
                                    wrongCodes[index],
                                    NewKey()),
                                default)))
                .ToArray();
            await Task.WhenAll(
                    blockers.Select(blocker =>
                        blocker.FirstSaveReached))
                .WaitAsync(TimeSpan.FromSeconds(5));

            var errors = new List<DomainException>();
            for (var index = 0; index < blockers.Length; index++)
            {
                blockers[index].Release();
                errors.Add(await submissions[index].WaitAsync(
                    TimeSpan.FromSeconds(5)));
            }

            Assert.All(
                errors.Take(4),
                error => Assert.Equal(
                    "รหัสไม่ถูกต้อง ลองตรวจสอบแล้วกรอกอีกครั้ง",
                    error.Message));
            Assert.Equal(
                "กรอกรหัสไม่ถูกต้องครบจำนวนแล้ว กรุณาขอรหัสใหม่",
                errors[4].Message);
        }
        finally
        {
            foreach (var blocker in blockers)
                blocker.Release();
            foreach (var context in contexts)
                await context.DisposeAsync();
        }

        await using var assertionContext = database.CreateContext();
        var stored = await assertionContext
            .BuyerEmailChangeChallenges
            .SingleAsync();
        Assert.Equal(5, stored.IncorrectAttempts);
        Assert.Equal(0, stored.RemainingAttempts);
        Assert.Equal(BuyerEmailChangeStatus.Locked, stored.Status);
        Assert.Equal(
            5,
            await assertionContext.BuyerEmailVerificationAttempts
                .CountAsync());
        Assert.Single(
            await assertionContext.BuyerEmailChangeAuditEvents
                .Where(audit =>
                    audit.Name == "account.email_change_locked")
                .ToListAsync());
    }

    private static RequestBuyerEmailChangeHandler RequestHandler(
        ToklongDbContext database,
        IUnitOfWork unitOfWork,
        RecordingSender sender) =>
        new(
            new BuyerRepository(database),
            new BuyerEmailChangeRepository(database),
            new DeterministicCodeService(),
            new Template(),
            sender,
            unitOfWork,
            new Clock(Now.AddSeconds(61)));

    private static ResendBuyerEmailChangeHandler ResendHandler(
        ToklongDbContext database,
        IUnitOfWork unitOfWork,
        RecordingSender sender) =>
        new(
            new BuyerEmailChangeRepository(database),
            new DeterministicCodeService(),
            new Template(),
            sender,
            unitOfWork,
            new Clock(Now.AddSeconds(61)));

    private static VerifyBuyerEmailChangeHandler VerifyHandler(
        ToklongDbContext database,
        IUnitOfWork unitOfWork) =>
        new(
            new BuyerRepository(database),
            new BuyerEmailChangeRepository(database),
            new DeterministicCodeService(),
            unitOfWork,
            new Clock(Now.AddSeconds(61)));

    private static string NewKey() => Guid.NewGuid().ToString("N");

    private static async Task<DomainException>
        CaptureDomainExceptionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (DomainException exception)
        {
            return exception;
        }

        throw new InvalidOperationException(
            "Expected a domain rejection.");
    }

    private sealed class RelationalDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection anchor;
        private readonly DbContextOptions<ToklongDbContext> options;

        private RelationalDatabase(
            SqliteConnection anchor,
            DbContextOptions<ToklongDbContext> options,
            Guid buyerId)
        {
            this.anchor = anchor;
            this.options = options;
            BuyerId = buyerId;
        }

        public Guid BuyerId { get; }

        public static async Task<RelationalDatabase> CreateAsync()
        {
            var connectionString =
                $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var anchor = new SqliteConnection(connectionString);
            await anchor.OpenAsync();
            var options =
                new DbContextOptionsBuilder<ToklongDbContext>()
                    .UseSqlite(connectionString)
                    .Options;
            await using var database = new ToklongDbContext(options);
            await database.Database.EnsureCreatedAsync();
            var buyer = BuyerAccount.Create(
                "+66811111111",
                "Buyer Example",
                "old@example.com",
                Now);
            database.Buyers.Add(buyer);
            await database.SaveChangesAsync();
            return new RelationalDatabase(anchor, options, buyer.Id);
        }

        public ToklongDbContext CreateContext() => new(options);

        public async Task<BuyerEmailChangeChallenge>
            AddActiveChallengeAsync()
        {
            await using var database = CreateContext();
            var id = Guid.NewGuid();
            var codes = new DeterministicCodeService();
            var challenge = BuyerEmailChangeChallenge.Create(
                id,
                BuyerId,
                "new@example.com",
                "ne••@example.com",
                codes.Issue(id).Digest,
                NewKey(),
                Now);
            challenge.MarkSendAccepted(Now.AddSeconds(1));
            database.BuyerEmailChangeChallenges.Add(challenge);
            await database.SaveChangesAsync();
            return challenge;
        }

        public async ValueTask DisposeAsync() =>
            await anchor.DisposeAsync();
    }

    private sealed class BlockingFirstSaveUnitOfWork(
        IUnitOfWork inner) : IUnitOfWork
    {
        private readonly TaskCompletionSource firstSaveReached =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int saveCount;

        public Task FirstSaveReached => firstSaveReached.Task;

        public async Task<int> SaveChangesAsync(
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref saveCount) == 1)
            {
                firstSaveReached.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }

            return await inner.SaveChangesAsync(cancellationToken);
        }

        public void Release() => release.TrySetResult();
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

    private sealed class Template : IEmailVerificationTemplate
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

        public Task<EmailSendAcceptance> SendAsync(
            TransactionalEmailMessage message,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Messages.Add(message);
            return Task.FromResult(
                new EmailSendAcceptance(
                    $"accepted-{message.CorrelationId}"));
        }
    }

    private sealed class Clock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
