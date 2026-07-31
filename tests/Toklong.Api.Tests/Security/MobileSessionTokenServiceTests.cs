using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Toklong.Api.Security;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Authentication;
using Toklong.Domain.Accounts;
using Toklong.Domain.Buyers;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Api.Tests.Security;

public sealed class MobileSessionTokenServiceTests
{
    static MobileSessionTokenServiceTests() =>
        SQLitePCL.raw.SetProvider(
            new SQLitePCL.SQLite3Provider_sqlite3());

    [Fact]
    public async Task Refresh_token_is_hashed_rotated_and_replay_is_rejected()
    {
        await using var db = CreateDatabase();
        var repository = new MobileSessionRepository(db);
        var buyer = BuyerAccount.Create(
            "+66812345678",
            AccountName.Create("ผู้ซื้อ", "ทดสอบ"),
            "buyer@example.com",
            DateTimeOffset.UtcNow.AddYears(-1));
        db.Buyers.Add(buyer);
        await db.SaveChangesAsync();
        var service = new MobileSessionTokenService(
            new EphemeralDataProtectionProvider(),
            TimeProvider.System,
            repository,
            new BuyerRepository(db),
            new SellerRepository(db),
            db,
            new ImmediatePhoneTransactions());

        var issued = await service.CreateAsync(
            new MobileSessionProfile(
                buyer.Id,
                null,
                "+66812345678",
                "ผู้ซื้อ ทดสอบ"),
            default);
        var stored = Assert.Single(db.MobileSessions);

        Assert.NotEqual(issued.RefreshToken, stored.RefreshTokenHash);
        Assert.Equal(64, stored.RefreshTokenHash.Length);
        Assert.NotNull(await service.ValidateAccessAsync(
            issued.AccessToken,
            default));

        var rotated = await service.RefreshAsync(
            issued.RefreshToken,
            default);

        Assert.NotNull(rotated);
        Assert.NotEqual(issued.RefreshToken, rotated.RefreshToken);
        Assert.Null(await service.RefreshAsync(
            issued.RefreshToken,
            default));
        Assert.NotNull(await service.ValidateAccessAsync(
            rotated.AccessToken,
            default));
    }

    [Fact]
    public async Task Logout_revokes_access_token_immediately()
    {
        await using var db = CreateDatabase();
        var repository = new MobileSessionRepository(db);
        var buyer = BuyerAccount.Create(
            "+66812345678",
            AccountName.Create("ผู้ซื้อ", "ทดสอบ"),
            "buyer@example.com",
            DateTimeOffset.UtcNow.AddYears(-1));
        db.Buyers.Add(buyer);
        await db.SaveChangesAsync();
        var service = new MobileSessionTokenService(
            new EphemeralDataProtectionProvider(),
            TimeProvider.System,
            repository,
            new BuyerRepository(db),
            new SellerRepository(db),
            db,
            new ImmediatePhoneTransactions());
        var issued = await service.CreateAsync(
            new MobileSessionProfile(
                buyer.Id,
                null,
                "+66812345678",
                "ผู้ซื้อ ทดสอบ"),
            default);
        var session = Assert.Single(db.MobileSessions);

        await service.RevokeAsync(session.Id, default);

        Assert.Null(await service.ValidateAccessAsync(
            issued.AccessToken,
            default));
        Assert.Null(await service.RefreshAsync(
            issued.RefreshToken,
            default));
    }

    [Fact]
    public async Task Session_creation_waits_and_uses_the_current_account_name()
    {
        var connectionString =
            $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseSqlite(connectionString)
            .Options;
        const string phone = "+66812345678";
        var now =
            new DateTimeOffset(2026, 7, 31, 5, 0, 0, TimeSpan.Zero);
        Guid buyerId;
        await using (var setup = new ToklongDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            var buyer = BuyerAccount.Create(
                phone,
                AccountName.Create("สมชาย", "ใจดี"),
                "buyer@example.com",
                now.AddYears(-1));
            buyerId = buyer.Id;
            setup.Buyers.Add(buyer);
            await setup.SaveChangesAsync();
        }

        var transactions = new BlockingPhoneTransactions();
        await using var completion =
            await transactions.BeginAsync(phone, default);
        await using var sessionDatabase = new ToklongDbContext(options);
        var service = new MobileSessionTokenService(
            new EphemeralDataProtectionProvider(),
            TimeProvider.System,
            new MobileSessionRepository(sessionDatabase),
            new BuyerRepository(sessionDatabase),
            new SellerRepository(sessionDatabase),
            sessionDatabase,
            transactions);
        var staleProfile = new MobileSessionProfile(
            buyerId,
            null,
            phone,
            "สมชาย ใจดี");

        var issuanceTask = service.CreateAsync(
            staleProfile,
            default);
        await transactions.WaiterReached.WaitAsync(
            TimeSpan.FromSeconds(5));
        Assert.False(issuanceTask.IsCompleted);

        await using (var completionDatabase =
                     new ToklongDbContext(options))
        {
            var buyer = await completionDatabase.Buyers.SingleAsync();
            buyer.ApplyAccountName(
                AccountName.Create("สมศักดิ์", "ใจดี"),
                now);
            await completionDatabase.SaveChangesAsync();
        }
        await completion.CommitAsync(default);
        await completion.DisposeAsync();

        var issued = await issuanceTask.WaitAsync(
            TimeSpan.FromSeconds(5));

        Assert.Equal("สมศักดิ์ ใจดี", issued.DisplayName);
        await using var assertion = new ToklongDbContext(options);
        Assert.Equal(
            "สมศักดิ์ ใจดี",
            (await assertion.MobileSessions.SingleAsync()).DisplayName);
    }

    private static ToklongDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ToklongDbContext(options);
    }

    private sealed class BlockingPhoneTransactions
        : IAccountPhoneTransactionManager
    {
        private readonly SemaphoreSlim gate = new(1, 1);
        private readonly TaskCompletionSource waiterReached =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaiterReached => waiterReached.Task;

        public async Task<IAccountPhoneTransaction> BeginAsync(
            string normalizedPhone,
            CancellationToken cancellationToken)
        {
            if (!await gate.WaitAsync(0, cancellationToken))
            {
                waiterReached.TrySetResult();
                await gate.WaitAsync(cancellationToken);
            }
            return new Handle(gate);
        }

        private sealed class Handle(SemaphoreSlim gate)
            : IAccountPhoneTransaction
        {
            private int disposed;

            public Task CommitAsync(
                CancellationToken cancellationToken) =>
                Task.CompletedTask;

            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref disposed, 1) == 0)
                    gate.Release();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class ImmediatePhoneTransactions
        : IAccountPhoneTransactionManager
    {
        public Task<IAccountPhoneTransaction> BeginAsync(
            string normalizedPhone,
            CancellationToken cancellationToken) =>
            Task.FromResult<IAccountPhoneTransaction>(new Handle());

        private sealed class Handle : IAccountPhoneTransaction
        {
            public Task CommitAsync(
                CancellationToken cancellationToken) =>
                Task.CompletedTask;

            public ValueTask DisposeAsync() =>
                ValueTask.CompletedTask;
        }
    }
}
