using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Toklong.Crm.Authentication;
using Toklong.Crm.Persistence;

namespace Toklong.Crm.Tests.Authentication;

public sealed class CrmTicketStoreTests
{
    [Fact]
    public async Task Ticket_key_is_hashed_and_disabled_user_is_revoked()
    {
        var options =
            new DbContextOptionsBuilder<CrmDbContext>()
                .UseInMemoryDatabase(
                    Guid.NewGuid().ToString("N"))
                .Options;
        var factory = new TestDatabaseFactory(options);
        var user = CrmUser.Create(
            "b301f1d0-f83d-4279-9c0a-b4715b44622b",
            "a90d9406-6376-4a69-af07-d1eca12c20f8",
            "admin@example.test",
            "Admin Test",
            null,
            DateTimeOffset.UtcNow);
        await using (var database =
                     await factory.CreateDbContextAsync())
        {
            database.Users.Add(user);
            await database.SaveChangesAsync();
        }

        var store = new CrmTicketStore(
            factory,
            new EphemeralDataProtectionProvider(),
            TimeProvider.System);
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
            [
                new Claim(
                    CrmAuthenticationDefaults.UserIdClaim,
                    user.Id.ToString("N")),
                new Claim(
                    CrmAuthenticationDefaults.RoleClaim,
                    CrmRoles.Admin)
            ], CrmAuthenticationDefaults.CookieScheme));
        var ticket = new AuthenticationTicket(
            principal,
            new AuthenticationProperties
            {
                ExpiresUtc =
                    DateTimeOffset.UtcNow.AddHours(1)
            },
            CrmAuthenticationDefaults.CookieScheme);

        var key = await store.StoreAsync(ticket);

        await using (var database =
                     await factory.CreateDbContextAsync())
        {
            var session = await database.Sessions.SingleAsync();
            Assert.NotEqual(key, session.TicketHash);
            Assert.Equal(64, session.TicketHash.Length);
            Assert.True(session.ProtectedTicket.Length > 0);
        }
        Assert.NotNull(await store.RetrieveAsync(key));

        await using (var database =
                     await factory.CreateDbContextAsync())
        {
            var storedUser = await database.Users
                .SingleAsync();
            storedUser.Disable(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow);
            await database.SaveChangesAsync();
        }

        Assert.Null(await store.RetrieveAsync(key));
        await using (var database =
                     await factory.CreateDbContextAsync())
        {
            Assert.NotNull(
                (await database.Sessions.SingleAsync())
                .RevokedAt);
        }
    }

    [Fact]
    public async Task Concurrent_ticket_reads_remain_valid()
    {
        var options =
            new DbContextOptionsBuilder<CrmDbContext>()
                .UseInMemoryDatabase(
                    Guid.NewGuid().ToString("N"))
                .Options;
        var factory = new TestDatabaseFactory(options);
        var user = CrmUser.Create(
            "d401f1d0-f83d-4279-9c0a-b4715b44622b",
            "b90d9406-6376-4a69-af07-d1eca12c20f8",
            "parallel@example.test",
            "Parallel Admin",
            null,
            DateTimeOffset.UtcNow);
        await using (var database =
                     await factory.CreateDbContextAsync())
        {
            database.Users.Add(user);
            await database.SaveChangesAsync();
        }

        var time = new TestTimeProvider(
            DateTimeOffset.Parse(
                "2026-07-26T08:00:00+00:00"));
        var store = new CrmTicketStore(
            factory,
            new EphemeralDataProtectionProvider(),
            time);
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
            [
                new Claim(
                    CrmAuthenticationDefaults.UserIdClaim,
                    user.Id.ToString("N")),
                new Claim(
                    CrmAuthenticationDefaults.RoleClaim,
                    CrmRoles.Admin)
            ], CrmAuthenticationDefaults.CookieScheme));
        var ticket = new AuthenticationTicket(
            principal,
            new AuthenticationProperties
            {
                ExpiresUtc = time.GetUtcNow().AddHours(1)
            },
            CrmAuthenticationDefaults.CookieScheme);
        var key = await store.StoreAsync(ticket);
        time.Advance(TimeSpan.FromMinutes(6));

        var reads = await Task.WhenAll(
            Enumerable.Range(0, 20)
                .Select(_ => store.RetrieveAsync(key)));

        Assert.All(reads, Assert.NotNull);
    }

    private sealed class TestDatabaseFactory(
        DbContextOptions<CrmDbContext> options)
        : IDbContextFactory<CrmDbContext>
    {
        public CrmDbContext CreateDbContext() =>
            new(options);

        public Task<CrmDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class TestTimeProvider(
        DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() =>
            _utcNow;

        public void Advance(TimeSpan duration) =>
            _utcNow = _utcNow.Add(duration);
    }
}
