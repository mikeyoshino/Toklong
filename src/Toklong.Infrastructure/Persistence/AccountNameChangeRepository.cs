using Microsoft.EntityFrameworkCore;
using Npgsql;
using Toklong.Application.Abstractions;
using Toklong.Domain.Accounts;

namespace Toklong.Infrastructure.Persistence;

public sealed class AccountNameChangeRepository(
    ToklongDbContext dbContext)
    : IAccountNameChangeRepository
{
    public Task<AccountNameChangeChallenge?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        dbContext.AccountNameChangeChallenges.SingleOrDefaultAsync(
            challenge => challenge.Id == id,
            cancellationToken);

    public Task<AccountNameChangeChallenge?> GetOpenAsync(
        string phoneNumber,
        CancellationToken cancellationToken) =>
        dbContext.AccountNameChangeChallenges.SingleOrDefaultAsync(
            challenge =>
                challenge.PhoneNumber == phoneNumber &&
                (challenge.Status == AccountNameChangeStatus.PendingSend ||
                 challenge.Status == AccountNameChangeStatus.Active),
            cancellationToken);

    public Task<AccountNameChangeChallenge?> GetByRequestKeyAsync(
        string phoneNumber,
        string key,
        CancellationToken cancellationToken) =>
        dbContext.AccountNameChangeChallenges.SingleOrDefaultAsync(
            challenge =>
                challenge.PhoneNumber == phoneNumber &&
                challenge.RequestIdempotencyKey == key,
            cancellationToken);

    public Task<AccountNameChangeChallenge?>
        GetBySourceChallengeIdAsync(
            Guid sourceChallengeId,
            CancellationToken cancellationToken) =>
        dbContext.AccountNameChangeChallenges.SingleOrDefaultAsync(
            challenge =>
                challenge.SourceChallengeId ==
                sourceChallengeId,
            cancellationToken);

    public Task<AccountNameVerificationAttempt?> GetAttemptAsync(
        Guid challengeId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        dbContext.AccountNameVerificationAttempts.SingleOrDefaultAsync(
            attempt =>
                attempt.ChallengeId == challengeId &&
                attempt.IdempotencyKey == idempotencyKey,
            cancellationToken);

    public Task<int> CountAcceptedSendsAsync(
        Guid? buyerId,
        Guid? sellerId,
        string phoneNumber,
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        if (!buyerId.HasValue && !sellerId.HasValue)
            return Task.FromResult(0);

        return dbContext.AccountNameChangeChallenges.CountAsync(
            challenge =>
                challenge.PhoneNumber == phoneNumber &&
                challenge.SendAcceptedAt > since &&
                (buyerId.HasValue && challenge.BuyerId == buyerId ||
                 sellerId.HasValue && challenge.SellerId == sellerId),
            cancellationToken);
    }

    public Task<DateTimeOffset?> GetOldestAcceptedSendAtAsync(
        Guid? buyerId,
        Guid? sellerId,
        string phoneNumber,
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        if (!buyerId.HasValue && !sellerId.HasValue)
            return Task.FromResult<DateTimeOffset?>(null);

        return dbContext.AccountNameChangeChallenges
            .Where(challenge =>
                challenge.PhoneNumber == phoneNumber &&
                challenge.SendAcceptedAt > since &&
                (buyerId.HasValue && challenge.BuyerId == buyerId ||
                 sellerId.HasValue && challenge.SellerId == sellerId))
            .MinAsync(
                challenge => challenge.SendAcceptedAt,
                cancellationToken);
    }

    public Task AddAsync(
        AccountNameChangeChallenge value,
        CancellationToken cancellationToken) =>
        dbContext.AccountNameChangeChallenges
            .AddAsync(value, cancellationToken)
            .AsTask();

    public Task AddAttemptAsync(
        AccountNameVerificationAttempt value,
        CancellationToken cancellationToken) =>
        dbContext.AccountNameVerificationAttempts
            .AddAsync(value, cancellationToken)
            .AsTask();

    public Task AddAuditAsync(
        AccountNameChangeAuditEvent value,
        CancellationToken cancellationToken) =>
        dbContext.AccountNameChangeAuditEvents
            .AddAsync(value, cancellationToken)
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
