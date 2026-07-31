using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Toklong.Domain.Accounts;
using Toklong.Domain.Buyers;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Application.Tests.Persistence;

public sealed class AccountPhoneTransactionManagerTests
{
    static AccountPhoneTransactionManagerTests() =>
        SQLitePCL.raw.SetProvider(
            new SQLitePCL.SQLite3Provider_sqlite3());

    [Fact]
    public async Task Same_phone_nested_lease_keeps_one_transaction_until_outer_commit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var manager = new PostgresAccountPhoneTransactionManager(
            fixture.Database);
        await using var outer = await manager.BeginAsync(
            "0812345678",
            default);
        var transaction = fixture.Database.Database.CurrentTransaction;
        Assert.NotNull(transaction);

        await using (var nested = await manager.BeginAsync(
                         "+66812345678",
                         default))
        {
            await nested.CommitAsync(default);
        }

        Assert.Same(
            transaction,
            fixture.Database.Database.CurrentTransaction);
        await outer.CommitAsync(default);
        await outer.DisposeAsync();
        Assert.Null(fixture.Database.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Uncommitted_nested_lease_poisons_outer_commit_and_rolls_back()
    {
        await using var fixture = await Fixture.CreateAsync();
        var manager = new PostgresAccountPhoneTransactionManager(
            fixture.Database);
        await using var outer = await manager.BeginAsync(
            "+66812345678",
            default);
        fixture.Database.Buyers.Add(BuyerAccount.Create(
            "+66812345678",
            AccountName.Create("สมชาย", "ใจดี"),
            "buyer@example.com",
            DateTimeOffset.UtcNow));
        await fixture.Database.SaveChangesAsync();

        await using (var nested = await manager.BeginAsync(
                         "+66812345678",
                         default))
        {
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => outer.CommitAsync(default));
        await outer.DisposeAsync();
        fixture.Database.ChangeTracker.Clear();

        Assert.Empty(await fixture.Database.Buyers.AsNoTracking().ToListAsync());
        Assert.Null(fixture.Database.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Different_phone_nesting_is_rejected_without_poisoning_outer()
    {
        await using var fixture = await Fixture.CreateAsync();
        var manager = new PostgresAccountPhoneTransactionManager(
            fixture.Database);
        await using var outer = await manager.BeginAsync(
            "+66812345678",
            default);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.BeginAsync(
                "+66822222222",
                default));

        await outer.CommitAsync(default);
    }

    [Fact]
    public async Task Out_of_order_and_double_commit_are_rejected_but_valid_LIFO_can_finish()
    {
        await using var fixture = await Fixture.CreateAsync();
        var manager = new PostgresAccountPhoneTransactionManager(
            fixture.Database);
        await using var outer = await manager.BeginAsync(
            "+66812345678",
            default);
        await using var nested = await manager.BeginAsync(
            "+66812345678",
            default);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => outer.CommitAsync(default));
        await nested.CommitAsync(default);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => nested.CommitAsync(default));
        await nested.DisposeAsync();
        await outer.CommitAsync(default);
    }

    [Fact]
    public async Task Canceled_nested_begin_leaves_outer_lease_usable()
    {
        await using var fixture = await Fixture.CreateAsync();
        var manager = new PostgresAccountPhoneTransactionManager(
            fixture.Database);
        await using var outer = await manager.BeginAsync(
            "+66812345678",
            default);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            manager.BeginAsync(
                "+66812345678",
                canceled.Token));

        await outer.CommitAsync(default);
    }

    [Fact]
    public async Task Existing_raw_transaction_is_rejected_without_adoption()
    {
        await using var fixture = await Fixture.CreateAsync();
        var manager = new PostgresAccountPhoneTransactionManager(
            fixture.Database);
        await using (var raw =
                     await fixture.Database.Database.BeginTransactionAsync())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                manager.BeginAsync(
                    "+66812345678",
                    default));
            Assert.Same(
                raw,
                fixture.Database.Database.CurrentTransaction);
        }

        await using var owned = await manager.BeginAsync(
            "+66812345678",
            default);
        await owned.CommitAsync(default);
    }

    private sealed class Fixture(
        SqliteConnection connection,
        ToklongDbContext database) : IAsyncDisposable
    {
        public ToklongDbContext Database { get; } = database;

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var database = new ToklongDbContext(
                new DbContextOptionsBuilder<ToklongDbContext>()
                    .UseSqlite(connection)
                    .Options);
            await database.Database.EnsureCreatedAsync();
            return new Fixture(connection, database);
        }

        public async ValueTask DisposeAsync()
        {
            await Database.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
