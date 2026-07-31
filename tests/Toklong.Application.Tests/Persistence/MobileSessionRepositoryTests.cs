using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Toklong.Domain.Accounts;
using Toklong.Domain.Authentication;
using Toklong.Domain.Buyers;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Application.Tests.Persistence;

public sealed class MobileSessionRepositoryTests
{
    static MobileSessionRepositoryTests() =>
        SQLitePCL.raw.SetProvider(
            new SQLitePCL.SQLite3Provider_sqlite3());

    [Fact]
    public async Task Active_lookup_materializes_only_unrevoked_unexpired_sessions()
    {
        await using var connection = new SqliteConnection(
            "Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseSqlite(connection)
            .Options;
        var now =
            new DateTimeOffset(2026, 7, 31, 5, 0, 0, TimeSpan.Zero);
        Guid activeId;
        Guid expiredId;
        Guid revokedId;
        await using (var setup = new ToklongDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            var buyer = BuyerAccount.Create(
                "+66812345678",
                AccountName.Create("สมชาย", "ใจดี"),
                "buyer@example.com",
                now.AddYears(-1));
            var active = Session(
                buyer.Id,
                "active",
                now.AddDays(1));
            var expired = Session(
                buyer.Id,
                "expired",
                now);
            var revoked = Session(
                buyer.Id,
                "revoked",
                now.AddDays(1));
            revoked.Revoke(now.AddMinutes(-1));
            activeId = active.Id;
            expiredId = expired.Id;
            revokedId = revoked.Id;
            setup.AddRange(buyer, active, expired, revoked);
            await setup.SaveChangesAsync();
        }

        await using var database = new ToklongDbContext(options);
        var repository = new MobileSessionRepository(database);
        var activeSessions = await repository.GetActiveByPartyAsync(
            database.Buyers.Select(value => value.Id).Single(),
            null,
            now,
            default);

        Assert.Equal(activeId, Assert.Single(activeSessions).Id);
        Assert.Equal(
            [activeId],
            database.ChangeTracker.Entries<MobileSession>()
                .Select(entry => entry.Entity.Id)
                .ToArray());
        activeSessions[0].UpdateDisplayName("สมศักดิ์ ใจดี");
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        Assert.Equal(
            "สมชาย ใจดี",
            (await database.MobileSessions.SingleAsync(
                value => value.Id == expiredId)).DisplayName);
        Assert.Equal(
            "สมชาย ใจดี",
            (await database.MobileSessions.SingleAsync(
                value => value.Id == revokedId)).DisplayName);
    }

    private static MobileSession Session(
        Guid buyerId,
        string token,
        DateTimeOffset expiresAt) =>
        MobileSession.Create(
            buyerId,
            null,
            "สมชาย ใจดี",
            "+66812345678",
            Hash(token),
            new DateTimeOffset(
                2026,
                7,
                30,
                5,
                0,
                0,
                TimeSpan.Zero),
            expiresAt);

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
