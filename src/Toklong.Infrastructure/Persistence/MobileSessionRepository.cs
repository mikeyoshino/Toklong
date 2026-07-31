using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Domain.Authentication;

namespace Toklong.Infrastructure.Persistence;

public sealed class MobileSessionRepository(ToklongDbContext dbContext)
    : IMobileSessionRepository
{
    public Task AddAsync(
        MobileSession session,
        CancellationToken cancellationToken) =>
        dbContext.MobileSessions.AddAsync(session, cancellationToken).AsTask();

    public Task<MobileSession?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        dbContext.MobileSessions.SingleOrDefaultAsync(
            session => session.Id == id,
            cancellationToken);

    public Task<MobileSession?> GetByRefreshTokenHashAsync(
        string refreshTokenHash,
        CancellationToken cancellationToken) =>
        dbContext.MobileSessions.SingleOrDefaultAsync(
            session => session.RefreshTokenHash == refreshTokenHash,
            cancellationToken);

    public async Task<IReadOnlyList<MobileSession>> GetActiveByPartyAsync(
        Guid? buyerId,
        Guid? sellerId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!buyerId.HasValue && !sellerId.HasValue)
            return [];

        if (!dbContext.Database.IsRelational())
            return await dbContext.MobileSessions
                .Where(
                    session =>
                        session.RevokedAt == null &&
                        session.ExpiresAt > now &&
                        ((buyerId.HasValue &&
                          session.BuyerId == buyerId.Value) ||
                         (sellerId.HasValue &&
                          session.SellerId == sellerId.Value)))
                .ToListAsync(cancellationToken);

        IQueryable<MobileSession> query;
        if (buyerId.HasValue && sellerId.HasValue)
            query = dbContext.MobileSessions.FromSqlInterpolated(
                $"""
                 SELECT *
                 FROM "mobile_sessions"
                 WHERE "RevokedAt" IS NULL
                   AND "ExpiresAt" > {now}
                   AND (
                     "BuyerId" = {buyerId.Value}
                     OR "SellerId" = {sellerId.Value})
                 """);
        else if (buyerId.HasValue)
            query = dbContext.MobileSessions.FromSqlInterpolated(
                $"""
                 SELECT *
                 FROM "mobile_sessions"
                 WHERE "RevokedAt" IS NULL
                   AND "ExpiresAt" > {now}
                   AND "BuyerId" = {buyerId.Value}
                 """);
        else
            query = dbContext.MobileSessions.FromSqlInterpolated(
                $"""
                 SELECT *
                 FROM "mobile_sessions"
                 WHERE "RevokedAt" IS NULL
                   AND "ExpiresAt" > {now}
                   AND "SellerId" = {sellerId!.Value}
                 """);

        return await query
            .ToListAsync(cancellationToken);
    }
}
