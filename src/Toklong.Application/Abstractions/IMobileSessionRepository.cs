using Toklong.Domain.Authentication;

namespace Toklong.Application.Abstractions;

public interface IMobileSessionRepository
{
    Task AddAsync(
        MobileSession session,
        CancellationToken cancellationToken);

    Task<MobileSession?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<MobileSession?> GetByRefreshTokenHashAsync(
        string refreshTokenHash,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MobileSession>> GetActiveByPartyAsync(
        Guid? buyerId,
        Guid? sellerId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
