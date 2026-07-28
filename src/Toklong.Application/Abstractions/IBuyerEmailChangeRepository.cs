using Toklong.Domain.Buyers;

namespace Toklong.Application.Abstractions;

public interface IBuyerEmailChangeRepository
{
    Task<BuyerEmailChangeChallenge?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<BuyerEmailChangeChallenge?> GetOpenByBuyerIdAsync(
        Guid buyerId,
        CancellationToken cancellationToken);

    Task<BuyerEmailChangeChallenge?> GetByRequestKeyAsync(
        Guid buyerId,
        string requestIdempotencyKey,
        CancellationToken cancellationToken);

    Task AddAsync(
        BuyerEmailChangeChallenge challenge,
        CancellationToken cancellationToken);

    Task AddAuditAsync(
        BuyerEmailChangeAuditEvent auditEvent,
        CancellationToken cancellationToken);
}
