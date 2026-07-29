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

    Task<BuyerEmailVerificationAttempt?> GetVerificationAttemptAsync(
        Guid buyerId,
        Guid challengeId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task AddAsync(
        BuyerEmailChangeChallenge challenge,
        CancellationToken cancellationToken);

    Task AddVerificationAttemptAsync(
        BuyerEmailVerificationAttempt attempt,
        CancellationToken cancellationToken);

    Task AddAuditAsync(
        BuyerEmailChangeAuditEvent auditEvent,
        CancellationToken cancellationToken);

    bool IsPersistenceConflict(Exception exception);

    void DiscardPendingChanges();
}
