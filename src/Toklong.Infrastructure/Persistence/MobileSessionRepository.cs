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
}
