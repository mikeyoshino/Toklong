using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Toklong.Crm.Persistence;

namespace Toklong.Crm.Authentication;

public sealed class CrmTicketStore(
    IDbContextFactory<CrmDbContext> databaseFactory,
    IDataProtectionProvider dataProtection,
    TimeProvider timeProvider) : ITicketStore
{
    private static readonly TimeSpan DefaultLifetime =
        TimeSpan.FromHours(8);
    private static readonly TimeSpan ValidationWriteInterval =
        TimeSpan.FromMinutes(5);
    private readonly IDataProtector protector = dataProtection
        .CreateProtector("Toklong.Crm.ServerTicket.v1");

    public async Task<string> StoreAsync(
        AuthenticationTicket ticket)
    {
        var userId = RequiredUserId(ticket.Principal);
        var key = WebEncoders.Base64UrlEncode(
            RandomNumberGenerator.GetBytes(32));
        var now = timeProvider.GetUtcNow();
        var expiresAt =
            ticket.Properties.ExpiresUtc ??
            now.Add(DefaultLifetime);
        var session = CrmSession.Create(
            userId,
            Hash(key),
            Protect(ticket),
            now,
            expiresAt);
        await using var database =
            await databaseFactory.CreateDbContextAsync();
        database.Sessions.Add(session);
        await database.SaveChangesAsync();
        return key;
    }

    public async Task RenewAsync(
        string key,
        AuthenticationTicket ticket)
    {
        var now = timeProvider.GetUtcNow();
        await using var database =
            await databaseFactory.CreateDbContextAsync();
        var session = await database.Sessions
            .SingleOrDefaultAsync(
                item => item.TicketHash == Hash(key));
        if (session is null || !session.IsActive(now))
            return;
        session.Renew(
            Protect(ticket),
            ticket.Properties.ExpiresUtc ??
            now.Add(DefaultLifetime),
            now);
        await SaveConcurrentSessionUpdateAsync(database);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(
        string key)
    {
        var now = timeProvider.GetUtcNow();
        await using var database =
            await databaseFactory.CreateDbContextAsync();
        var session = await database.Sessions
            .SingleOrDefaultAsync(
                item => item.TicketHash == Hash(key));
        if (session is null || !session.IsActive(now))
            return null;

        var userIsActive = await database.Users
            .Where(item =>
                item.Id == session.UserId &&
                item.Status == CrmUserStatus.Active)
            .AnyAsync();
        if (!userIsActive)
        {
            session.Revoke(now);
            await SaveConcurrentSessionUpdateAsync(database);
            return null;
        }

        AuthenticationTicket? ticket;
        try
        {
            ticket = TicketSerializer.Default.Deserialize(
                protector.Unprotect(
                    session.ProtectedTicket));
        }
        catch (CryptographicException)
        {
            session.Revoke(now);
            await SaveConcurrentSessionUpdateAsync(database);
            return null;
        }

        if (ticket is null ||
            ticket.Properties.ExpiresUtc is { } expiresAt &&
            expiresAt <= now)
        {
            session.Revoke(now);
            await SaveConcurrentSessionUpdateAsync(database);
            return null;
        }

        if (session.MarkValidated(
                now,
                ValidationWriteInterval))
            await SaveConcurrentSessionUpdateAsync(database);
        return ticket;
    }

    public async Task RemoveAsync(string key)
    {
        var now = timeProvider.GetUtcNow();
        await using var database =
            await databaseFactory.CreateDbContextAsync();
        var session = await database.Sessions
            .SingleOrDefaultAsync(
                item => item.TicketHash == Hash(key));
        if (session is null)
            return;
        session.Revoke(now);
        await SaveConcurrentSessionUpdateAsync(database);
    }

    private byte[] Protect(AuthenticationTicket ticket) =>
        protector.Protect(
            TicketSerializer.Default.Serialize(ticket));

    private static Guid RequiredUserId(
        ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(
            CrmAuthenticationDefaults.UserIdClaim);
        return Guid.TryParse(value, out var userId)
            ? userId
            : throw new InvalidOperationException(
                "CRM ticket does not contain a valid local user ID.");
    }

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static async Task SaveConcurrentSessionUpdateAsync(
        CrmDbContext database)
    {
        try
        {
            await database.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Concurrent requests for one cookie may validate or revoke the
            // same session together. The competing committed update is the
            // authoritative result; the next request re-reads it.
        }
    }
}
