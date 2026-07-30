using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Domain.Transactions;

namespace Toklong.Infrastructure.Persistence;

public sealed class BookingAttemptRepository(
    ToklongDbContext dbContext)
    : IBookingAttemptRepository
{
    private static readonly TimeSpan ActiveCallBudget =
        TimeSpan.FromSeconds(3);

    public async Task<AcquireBookingAttemptResult>
        AcquireAsync(
            AcquireBookingAttempt request,
            CancellationToken cancellationToken)
    {
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(
                cancellationToken);
        try
        {
            var existing = await dbContext.BookingAttempts
                .SingleOrDefaultAsync(
                    attempt =>
                        attempt.TransactionId ==
                            request.TransactionId &&
                        attempt.IdempotencyKey ==
                            request.IdempotencyKey,
                    cancellationToken);
            if (existing is not null)
            {
                var result = Existing(
                    existing,
                    request);
                await dbContext.SaveChangesAsync(
                    cancellationToken);
                await transaction.CommitAsync(
                    cancellationToken);
                return result;
            }

            var active = await dbContext.BookingAttempts
                .Where(
                    attempt =>
                        attempt.TransactionId ==
                            request.TransactionId &&
                        (attempt.Status ==
                            BookingAttemptStatus.Created ||
                         attempt.Status ==
                            BookingAttemptStatus.CallingProvider))
                .OrderByDescending(
                    attempt => attempt.AttemptNumber)
                .FirstOrDefaultAsync(
                    cancellationToken);
            if (active is not null)
            {
                var result = Existing(
                    active,
                    request,
                    enforceKeyFingerprint: false);
                await dbContext.SaveChangesAsync(
                    cancellationToken);
                await transaction.CommitAsync(
                    cancellationToken);
                return result;
            }

            var recent = await dbContext.BookingAttempts
                .Where(
                    attempt =>
                        attempt.TransactionId ==
                            request.TransactionId)
                .OrderByDescending(
                    attempt => attempt.AttemptNumber)
                .ToListAsync(
                    cancellationToken);
            if (recent.Count >= 3)
            {
                await transaction.CommitAsync(
                    cancellationToken);
                return new(
                    recent[0],
                    BookingAttemptAcquireState
                        .RetryLimitReached);
            }

            var attempt = BookingAttempt.Create(
                request.TransactionId,
                request.ManagedShipmentId,
                request.BuyerId,
                request.IdempotencyKey,
                request.RequestFingerprint,
                recent.Count + 1,
                request.Now);
            attempt.Claim(request.Now);
            dbContext.BookingAttempts.Add(attempt);
            await dbContext.SaveChangesAsync(
                cancellationToken);
            await transaction.CommitAsync(
                cancellationToken);
            return new(
                attempt,
                BookingAttemptAcquireState.Acquired);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(
                cancellationToken);
            dbContext.ChangeTracker.Clear();
            var winner = await dbContext.BookingAttempts
                .Where(
                    attempt =>
                        attempt.TransactionId ==
                            request.TransactionId &&
                        (attempt.IdempotencyKey ==
                            request.IdempotencyKey ||
                         attempt.Status ==
                            BookingAttemptStatus.Created ||
                         attempt.Status ==
                            BookingAttemptStatus.CallingProvider))
                .OrderByDescending(
                    attempt => attempt.AttemptNumber)
                .FirstOrDefaultAsync(
                    cancellationToken);
            if (winner is null)
                throw;
            return Existing(
                winner,
                request,
                enforceKeyFingerprint:
                    winner.IdempotencyKey ==
                    request.IdempotencyKey);
        }
    }

    public Task<BookingAttempt?> GetAsync(
        Guid transactionId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        dbContext.BookingAttempts
            .SingleOrDefaultAsync(
                attempt =>
                    attempt.TransactionId ==
                        transactionId &&
                    attempt.IdempotencyKey ==
                        idempotencyKey,
                cancellationToken);

    private static AcquireBookingAttemptResult Existing(
        BookingAttempt attempt,
        AcquireBookingAttempt request,
        bool enforceKeyFingerprint = true)
    {
        if (enforceKeyFingerprint &&
            !string.Equals(
                attempt.RequestFingerprint,
                request.RequestFingerprint,
                StringComparison.Ordinal))
            return new(
                attempt,
                BookingAttemptAcquireState
                    .FingerprintConflict);

        if (attempt.Status ==
                BookingAttemptStatus.CallingProvider &&
            attempt.StartedAt.HasValue &&
            attempt.StartedAt.Value
                .Add(ActiveCallBudget) <= request.Now)
        {
            attempt.TimeOut(
                "checkout-process-interrupted",
                request.Now);
            return new(
                attempt,
                BookingAttemptAcquireState.TimedOut);
        }

        return new(
            attempt,
            attempt.Status switch
            {
                BookingAttemptStatus.Created or
                    BookingAttemptStatus.CallingProvider =>
                    BookingAttemptAcquireState.InProgress,
                BookingAttemptStatus.Succeeded =>
                    BookingAttemptAcquireState.Succeeded,
                BookingAttemptStatus.Failed =>
                    BookingAttemptAcquireState.Failed,
                BookingAttemptStatus.TimedOut =>
                    BookingAttemptAcquireState.TimedOut,
                _ => throw new InvalidOperationException(
                    "booking-attempt-status-unsupported")
            });
    }
}
