using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Toklong.Api.Security;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Authentication;
using Toklong.Domain.Accounts;
using Toklong.Domain.Authentication;
using Toklong.Domain.Sellers;
using Toklong.Infrastructure.Persistence;
using Toklong.Infrastructure.Security;

namespace Toklong.Api.Tests.Security;

public sealed class MobileRegistrationTransactionCompositionTests
{
    private const string Phone = "+66812345678";
    private static readonly DateTimeOffset Now =
        new(2026, 7, 31, 5, 0, 0, TimeSpan.Zero);

    static MobileRegistrationTransactionCompositionTests() =>
        SQLitePCL.raw.SetProvider(
            new SQLitePCL.SQLite3Provider_sqlite3());

    [Fact]
    public async Task Outer_commit_persists_registration_and_session_with_the_seller_name()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var database = CreateDatabase(connection);
        await database.Database.EnsureCreatedAsync();
        var rawTicket = await SeedPendingRegistrationAsync(database);
        database.Sellers.Add(SellerAccount.Create(
            Phone,
            Now.AddYears(-1),
            AccountName.Create("ชื่อผู้ขาย", "เดิม")));
        await database.SaveChangesAsync();
        var transactions =
            new PostgresAccountPhoneTransactionManager(database);
        var handler = CreateHandler(database, transactions);
        var tokens = CreateTokenService(database, transactions);

        IssuedMobileSession issued;
        await using (var outer =
                     await transactions.BeginAsync(Phone, default))
        {
            var profile = await handler.Handle(
                ValidCompletion(rawTicket),
                default);
            issued = await tokens.CreateAsync(profile, default);

            Assert.NotNull(database.Database.CurrentTransaction);
            await outer.CommitAsync(default);
        }

        database.ChangeTracker.Clear();
        var buyer = await database.Buyers.SingleAsync();
        var session = await database.MobileSessions.SingleAsync();
        Assert.Equal("ชื่อผู้ขาย", buyer.FirstName);
        Assert.Equal("เดิม", buyer.LastName);
        Assert.Equal("ชื่อผู้ขาย เดิม", issued.DisplayName);
        Assert.Equal("ชื่อผู้ขาย เดิม", session.DisplayName);
        Assert.Single(await database.MobileAccountTermsAcceptances.ToListAsync());
        Assert.NotNull(
            (await database.PendingMobileRegistrations.SingleAsync()).ConsumedAt);
    }

    [Fact]
    public async Task Omitting_outer_commit_rolls_back_registration_and_session_together()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var database = CreateDatabase(connection);
        await database.Database.EnsureCreatedAsync();
        var rawTicket = await SeedPendingRegistrationAsync(database);
        var transactions =
            new PostgresAccountPhoneTransactionManager(database);
        var handler = CreateHandler(database, transactions);
        var tokens = CreateTokenService(database, transactions);

        await using (await transactions.BeginAsync(Phone, default))
        {
            var profile = await handler.Handle(
                ValidCompletion(rawTicket),
                default);
            _ = await tokens.CreateAsync(profile, default);
        }

        database.ChangeTracker.Clear();
        Assert.Empty(await database.Buyers.ToListAsync());
        Assert.Empty(await database.MobileAccountTermsAcceptances.ToListAsync());
        Assert.Empty(await database.MobileSessions.ToListAsync());
        Assert.Null(
            (await database.PendingMobileRegistrations.SingleAsync()).ConsumedAt);
    }

    private static CompleteMobileRegistrationHandler CreateHandler(
        ToklongDbContext database,
        IAccountPhoneTransactionManager transactions) =>
        new(
            new RegistrationTicketService(),
            new PendingMobileRegistrationRepository(database),
            new BuyerRepository(database),
            new SellerRepository(database),
            database,
            new FixedClock(Now),
            transactions);

    private static MobileSessionTokenService CreateTokenService(
        ToklongDbContext database,
        IAccountPhoneTransactionManager transactions) =>
        new(
            new EphemeralDataProtectionProvider(),
            TimeProvider.System,
            new MobileSessionRepository(database),
            new MobileSessionAccountNameReader(database),
            database,
            transactions);

    private static async Task<string> SeedPendingRegistrationAsync(
        ToklongDbContext database)
    {
        var ticket = new RegistrationTicketService().Issue();
        database.PendingMobileRegistrations.Add(
            PendingMobileRegistration.Create(
                ticket.TicketHash,
                Phone,
                InstallationId,
                Now,
                Now.AddMinutes(15)));
        await database.SaveChangesAsync();
        return ticket.RawTicket;
    }

    private static CompleteMobileRegistrationCommand ValidCompletion(
        string rawTicket) =>
        new(
            rawTicket,
            "ชื่อที่ส่งมา",
            "ต้องไม่ถูกใช้",
            "buyer@example.com",
            CompleteMobileRegistrationHandler.CurrentTermsVersion,
            InstallationId,
            IdempotencyKey);

    private static ToklongDbContext CreateDatabase(
        SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ToklongDbContext>()
            .UseSqlite(connection)
            .Options);

    private static string InstallationId { get; } =
        Guid.NewGuid().ToString("N");

    private static string IdempotencyKey { get; } =
        Guid.NewGuid().ToString("N");

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
