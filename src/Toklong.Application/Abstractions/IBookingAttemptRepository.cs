using Toklong.Domain.Transactions;

namespace Toklong.Application.Abstractions;

public enum BookingAttemptAcquireState
{
    Acquired,
    InProgress,
    Succeeded,
    Failed,
    TimedOut,
    RetryLimitReached,
    FingerprintConflict
}

public sealed record AcquireBookingAttempt(
    Guid TransactionId,
    Guid ManagedShipmentId,
    Guid BuyerId,
    string IdempotencyKey,
    string RequestFingerprint,
    DateTimeOffset Now);

public sealed record AcquireBookingAttemptResult(
    BookingAttempt Attempt,
    BookingAttemptAcquireState State);

public interface IBookingAttemptRepository
{
    Task<AcquireBookingAttemptResult> AcquireAsync(
        AcquireBookingAttempt request,
        CancellationToken cancellationToken);

    Task<BookingAttempt?> GetAsync(
        Guid transactionId,
        string idempotencyKey,
        CancellationToken cancellationToken);
}
