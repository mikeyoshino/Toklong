using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Sellers;
using Toklong.Domain.Accounts;
using Toklong.Domain.Buyers;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Application.Tests.Accounts;

public sealed class AccountPhoneSerializationTests
{
    static AccountPhoneSerializationTests() =>
        SQLitePCL.raw.SetProvider(
            new SQLitePCL.SQLite3Provider_sqlite3());

    [Fact]
    public async Task Seller_creation_waits_and_inherits_the_current_buyer_name()
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
        await using (var setup = new ToklongDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Buyers.Add(BuyerAccount.Create(
                phone,
                AccountName.Create("สมชาย", "ใจดี"),
                "buyer@example.com",
                now.AddYears(-1)));
            await setup.SaveChangesAsync();
        }

        var transactions = new BlockingPhoneTransactions();
        await using var completion =
            await transactions.BeginAsync(phone, default);
        await using var sellerDatabase = new ToklongDbContext(options);
        var handler = new EnsureSellerProfileHandler(
            new SellerRepository(sellerDatabase),
            new BuyerRepository(sellerDatabase),
            sellerDatabase,
            new FixedClock(now),
            transactions);

        var sellerTask = handler.Handle(
            new EnsureSellerProfileCommand(phone),
            default);
        await transactions.WaiterReached.WaitAsync(
            TimeSpan.FromSeconds(5));
        Assert.False(sellerTask.IsCompleted);

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

        var seller = await sellerTask.WaitAsync(
            TimeSpan.FromSeconds(5));

        Assert.Equal("สมศักดิ์ ใจดี", seller.DisplayName);
        await using var assertion = new ToklongDbContext(options);
        Assert.Equal(
            (await assertion.Buyers.SingleAsync()).FullName,
            (await assertion.Sellers.SingleAsync()).DisplayName);
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

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
