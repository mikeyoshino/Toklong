using Toklong.Domain.Accounts;

namespace Toklong.Application.Abstractions;

public interface IAccountNameChangeRepository
{
    Task<AccountNameChangeChallenge?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<AccountNameChangeChallenge?> GetOpenAsync(
        string phoneNumber,
        CancellationToken cancellationToken);

    Task<AccountNameChangeChallenge?> GetByRequestKeyAsync(
        string phoneNumber,
        string key,
        CancellationToken cancellationToken);

    Task<AccountNameVerificationAttempt?> GetAttemptAsync(
        Guid challengeId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<int> CountAcceptedSendsAsync(
        Guid? buyerId,
        Guid? sellerId,
        string phoneNumber,
        DateTimeOffset since,
        CancellationToken cancellationToken);

    Task AddAsync(
        AccountNameChangeChallenge value,
        CancellationToken cancellationToken);

    Task AddAttemptAsync(
        AccountNameVerificationAttempt value,
        CancellationToken cancellationToken);

    Task AddAuditAsync(
        AccountNameChangeAuditEvent value,
        CancellationToken cancellationToken);
}
