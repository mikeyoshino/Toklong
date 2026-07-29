using Microsoft.EntityFrameworkCore;
using Npgsql;
using Toklong.Application.Abstractions;
using Toklong.Domain.Buyers;

namespace Toklong.Infrastructure.Persistence;

public sealed class BuyerEmailChangeRepository(
    ToklongDbContext dbContext)
    : IBuyerEmailChangeRepository
{
    public Task<BuyerEmailChangeChallenge?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        dbContext.BuyerEmailChangeChallenges.SingleOrDefaultAsync(
            challenge => challenge.Id == id,
            cancellationToken);

    public Task<BuyerEmailChangeChallenge?> GetOpenByBuyerIdAsync(
        Guid buyerId,
        CancellationToken cancellationToken) =>
        dbContext.BuyerEmailChangeChallenges.SingleOrDefaultAsync(
            challenge =>
                challenge.BuyerId == buyerId &&
                (challenge.Status ==
                     BuyerEmailChangeStatus.PendingSend ||
                 challenge.Status ==
                     BuyerEmailChangeStatus.Active),
            cancellationToken);

    public Task<BuyerEmailChangeChallenge?> GetByRequestKeyAsync(
        Guid buyerId,
        string requestIdempotencyKey,
        CancellationToken cancellationToken) =>
        dbContext.BuyerEmailChangeChallenges.SingleOrDefaultAsync(
            challenge =>
                challenge.BuyerId == buyerId &&
                challenge.RequestIdempotencyKey ==
                    requestIdempotencyKey,
            cancellationToken);

    public Task<BuyerEmailVerificationAttempt?>
        GetVerificationAttemptAsync(
            Guid buyerId,
            Guid challengeId,
            string idempotencyKey,
            CancellationToken cancellationToken) =>
        dbContext.BuyerEmailVerificationAttempts.SingleOrDefaultAsync(
            attempt =>
                attempt.BuyerId == buyerId &&
                attempt.ChallengeId == challengeId &&
                attempt.IdempotencyKey == idempotencyKey,
            cancellationToken);

    public Task AddAsync(
        BuyerEmailChangeChallenge challenge,
        CancellationToken cancellationToken) =>
        dbContext.BuyerEmailChangeChallenges
            .AddAsync(challenge, cancellationToken)
            .AsTask();

    public Task AddVerificationAttemptAsync(
        BuyerEmailVerificationAttempt attempt,
        CancellationToken cancellationToken) =>
        dbContext.BuyerEmailVerificationAttempts
            .AddAsync(attempt, cancellationToken)
            .AsTask();

    public Task AddAuditAsync(
        BuyerEmailChangeAuditEvent auditEvent,
        CancellationToken cancellationToken) =>
        dbContext.BuyerEmailChangeAuditEvents
            .AddAsync(auditEvent, cancellationToken)
            .AsTask();

    public bool IsPersistenceConflict(Exception exception)
    {
        if (exception is DbUpdateConcurrencyException)
            return true;
        if (exception is not DbUpdateException updateException)
            return false;
        if (updateException.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            })
            return true;

        var providerException = updateException.InnerException;
        if (providerException?.GetType().FullName !=
            "Microsoft.Data.Sqlite.SqliteException")
            return false;
        var extendedErrorCode = providerException.GetType()
            .GetProperty("SqliteExtendedErrorCode")
            ?.GetValue(providerException);
        return extendedErrorCode is 1555 or 2067;
    }

    public void DiscardPendingChanges() =>
        dbContext.ChangeTracker.Clear();
}
