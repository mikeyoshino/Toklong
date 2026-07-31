using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Accounts.NameChanges;
using Toklong.Domain.Accounts;
using Toklong.Domain.Authentication;
using Toklong.Domain.Buyers;
using Toklong.Domain.Sellers;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Application.Tests.Accounts;

public sealed class AccountNameChangeConcurrencyTests
{
    static AccountNameChangeConcurrencyTests() =>
        SQLitePCL.raw.SetProvider(
            new SQLitePCL.SQLite3Provider_sqlite3());

    private static readonly DateTimeOffset Now =
        new(2026, 7, 31, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Concurrent_exact_requests_return_one_authoritative_completion()
    {
        await using var database = await RelationalDatabase.CreateAsync();
        var challenge = await database.AddActiveChallengeAsync(
            AccountName.Create("สมศักดิ์", "ใจดี"));
        await using var losingContext = database.CreateContext();
        await using var winningContext = database.CreateContext();
        var blocker = new BlockingFirstSaveUnitOfWork(losingContext);
        var provider = new AcceptingProvider(database.PhoneNumber);
        var command = database.Command(challenge.Id);

        var losingTask = Handler(
            losingContext,
            blocker,
            provider).Handle(command, default);
        await WaitForBlockedSaveAsync(
            losingTask,
            blocker.FirstSaveReached);
        VerifiedAccountNameChange winner;
        try
        {
            winner = await Handler(
                winningContext,
                winningContext,
                provider).Handle(command, default);
        }
        finally
        {
            blocker.Release();
        }

        var replay = await losingTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(winner, replay);
        await using var assertion = database.CreateContext();
        Assert.Single(
            await assertion.AccountNameVerificationAttempts.ToListAsync());
        Assert.Single(
            await assertion.AccountNameChangeAuditEvents.ToListAsync());
        Assert.Equal(
            "สมศักดิ์ ใจดี",
            (await assertion.Buyers.SingleAsync()).FullName);
        Assert.Equal(
            "สมศักดิ์ ใจดี",
            (await assertion.Sellers.SingleAsync()).DisplayName);
        Assert.All(
            await assertion.MobileSessions.ToListAsync(),
            session => Assert.Equal(
                "สมศักดิ์ ใจดี",
                session.DisplayName));
    }

    [Fact]
    public async Task Concurrent_distinct_challenges_produce_one_winner_and_one_cooldown()
    {
        await using var database =
            await RelationalDatabase.CreateAsync(
                allowMultipleOpenChallenges: true);
        var losingChallenge = await database.AddActiveChallengeAsync(
            AccountName.Create("สมศักดิ์", "ใจดี"));
        var winningChallenge = await database.AddActiveChallengeAsync(
            AccountName.Create("สมปอง", "มั่นคง"));
        await using var losingContext = database.CreateContext();
        await using var winningContext = database.CreateContext();
        var blocker = new BlockingFirstSaveUnitOfWork(losingContext);
        var provider = new AcceptingProvider(database.PhoneNumber);

        var losingTask = Handler(
            losingContext,
            blocker,
            provider).Handle(
                database.Command(losingChallenge.Id),
                default);
        await WaitForBlockedSaveAsync(
            losingTask,
            blocker.FirstSaveReached);
        VerifiedAccountNameChange winner;
        try
        {
            winner = await Handler(
                winningContext,
                winningContext,
                provider).Handle(
                    database.Command(winningChallenge.Id),
                    default);
        }
        finally
        {
            blocker.Release();
        }

        var blocked =
            await Assert.ThrowsAsync<AccountNameChangeCooldownException>(
                () => losingTask);

        Assert.Equal("สมปอง มั่นคง", winner.DisplayName);
        Assert.Equal(
            AccountNameChangeCalendar.AddTwoBangkokCalendarMonths(Now),
            blocked.NextAllowedAt);
        await using var assertion = database.CreateContext();
        Assert.Single(
            await assertion.AccountNameVerificationAttempts.ToListAsync());
        Assert.Single(
            await assertion.AccountNameChangeAuditEvents.ToListAsync());
        Assert.Equal(
            "สมปอง มั่นคง",
            (await assertion.Buyers.SingleAsync()).FullName);
        Assert.Equal(
            AccountNameChangeStatus.Active,
            (await assertion.AccountNameChangeChallenges
                .SingleAsync(value => value.Id == losingChallenge.Id)).Status);
        Assert.Equal(
            AccountNameChangeStatus.Verified,
            (await assertion.AccountNameChangeChallenges
                .SingleAsync(value => value.Id == winningChallenge.Id)).Status);
    }

    private static VerifyAccountNameChangeHandler Handler(
        ToklongDbContext database,
        IUnitOfWork unitOfWork,
        IOtpVerificationProvider provider) =>
        new(
            new BuyerRepository(database),
            new SellerRepository(database),
            new MobileSessionRepository(database),
            new AccountNameChangeRepository(database),
            provider,
            new DeterministicSecurity(),
            unitOfWork,
            new FixedClock());

    private static async Task WaitForBlockedSaveAsync(
        Task<VerifiedAccountNameChange> operation,
        Task firstSaveReached)
    {
        var completed = await Task.WhenAny(
            operation,
            firstSaveReached,
            Task.Delay(TimeSpan.FromSeconds(5)));
        if (completed == operation)
            await operation;
        if (completed != firstSaveReached)
            throw new TimeoutException(
                "Verification did not reach its first save.");
    }

    private sealed class RelationalDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection anchor;
        private readonly DbContextOptions<ToklongDbContext> options;

        private RelationalDatabase(
            SqliteConnection anchor,
            DbContextOptions<ToklongDbContext> options,
            Guid buyerId,
            Guid sellerId,
            Guid sessionId,
            string phoneNumber)
        {
            this.anchor = anchor;
            this.options = options;
            BuyerId = buyerId;
            SellerId = sellerId;
            SessionId = sessionId;
            PhoneNumber = phoneNumber;
        }

        public Guid BuyerId { get; }
        public Guid SellerId { get; }
        public Guid SessionId { get; }
        public string PhoneNumber { get; }

        public static async Task<RelationalDatabase> CreateAsync(
            bool allowMultipleOpenChallenges = false)
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
            if (allowMultipleOpenChallenges)
            {
                await database.Database.ExecuteSqlRawAsync(
                    "DROP INDEX \"IX_account_name_change_challenges_PhoneNumber\"");
            }

            var phone = "+66921031202";
            var name = AccountName.Create("สมชาย", "ใจดี");
            var buyer = BuyerAccount.Create(
                phone,
                name,
                "buyer@example.com",
                Now.AddYears(-1));
            var seller = SellerAccount.Create(
                phone,
                Now.AddYears(-1),
                name);
            var session = MobileSession.Create(
                buyer.Id,
                seller.Id,
                name.DisplayName,
                phone,
                Hash("refresh-primary"),
                Now.AddDays(-1),
                Now.AddDays(30));
            var secondSession = MobileSession.Create(
                buyer.Id,
                null,
                name.DisplayName,
                phone,
                Hash("refresh-secondary"),
                Now.AddDays(-1),
                Now.AddDays(30));
            database.AddRange(
                buyer,
                seller,
                session,
                secondSession);
            await database.SaveChangesAsync();
            return new(
                anchor,
                options,
                buyer.Id,
                seller.Id,
                session.Id,
                phone);
        }

        public ToklongDbContext CreateContext() => new(options);

        public async Task<AccountNameChangeChallenge>
            AddActiveChallengeAsync(AccountName pendingName)
        {
            await using var database = CreateContext();
            var challenge = AccountNameChangeChallenge.Create(
                Guid.NewGuid(),
                BuyerId,
                SellerId,
                SessionId,
                PhoneNumber,
                "0••-•••-1202",
                pendingName,
                Guid.NewGuid().ToString("N"),
                Now.AddMinutes(-1));
            challenge.MarkSendAccepted(
                $"provider-{challenge.Id:N}",
                Now.AddMinutes(-1));
            database.AccountNameChangeChallenges.Add(challenge);
            await database.SaveChangesAsync();
            return challenge;
        }

        public VerifyAccountNameChangeCommand Command(
            Guid challengeId) =>
            new(
                new AccountNameChangeSubject(
                    BuyerId,
                    SellerId,
                    SessionId,
                    PhoneNumber),
                challengeId,
                "123456",
                Guid.NewGuid().ToString("N"));

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

    private sealed class AcceptingProvider(string phoneNumber)
        : IOtpVerificationProvider
    {
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
            CancellationToken cancellationToken) =>
            Task.FromResult<string?>(phoneNumber);
    }

    private sealed class DeterministicSecurity
        : IAccountNameVerificationSecurity
    {
        public string Digest(Guid challengeId, string code) =>
            Hash($"account-name:{challengeId:N}:{code}");

        public string DigestAuditValue(Guid challengeId, string value) =>
            Hash($"account-name-audit:{challengeId:N}:{value}");
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
