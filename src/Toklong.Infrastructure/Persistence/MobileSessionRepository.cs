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

        var candidates = await dbContext.MobileSessions
            .Where(session =>
                (buyerId.HasValue && session.BuyerId == buyerId ||
                 sellerId.HasValue && session.SellerId == sellerId))
            .ToListAsync(cancellationToken);
        return candidates
            .Where(session => session.IsActive(now))
            .ToList();
    }
}
