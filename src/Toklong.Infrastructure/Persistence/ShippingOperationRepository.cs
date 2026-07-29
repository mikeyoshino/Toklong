using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Domain.Transactions;

namespace Toklong.Infrastructure.Persistence;

public sealed class ShippingOperationRepository(
    ToklongDbContext dbContext)
    : IShippingOperationRepository
{
    public async Task<ShippingOperation?> ClaimDueAsync(
        string workerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(
                cancellationToken);
        try
        {
            ShippingOperation? operation;
            if (dbContext.Database.IsNpgsql())
            {
                operation = await dbContext.ShippingOperations
                    .FromSqlInterpolated($"""
                        SELECT *
                        FROM shipping_operations
                        WHERE (
                            "Status" IN ('Pending', 'RetryScheduled')
                            OR (
                                "Status" = 'Processing'
                                AND "LeaseExpiresAt" <= {now}
                            )
                        )
                        AND "NextAttemptAt" <= {now}
                        ORDER BY "NextAttemptAt", "CreatedAt"
                        FOR UPDATE SKIP LOCKED
                        LIMIT 1
                        """)
                    .SingleOrDefaultAsync(cancellationToken);
            }
            else
            {
                operation = await dbContext.ShippingOperations
                    .FromSqlInterpolated($"""
                        SELECT *
                        FROM shipping_operations
                        WHERE (
                            "Status" IN ('Pending', 'RetryScheduled')
                            OR (
                                "Status" = 'Processing'
                                AND "LeaseExpiresAt" <= {now}
                            )
                        )
                        AND "NextAttemptAt" <= {now}
                        ORDER BY "NextAttemptAt", "CreatedAt"
                        LIMIT 1
                        """)
                    .SingleOrDefaultAsync(cancellationToken);
            }

            if (operation is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            operation.Claim(workerId, now, leaseDuration);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return operation;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return null;
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return null;
        }
    }

    public Task<ShippingOperation?> GetByIdAsync(
        Guid operationId,
        CancellationToken cancellationToken) =>
        dbContext.ShippingOperations.SingleOrDefaultAsync(
            operation => operation.Id == operationId,
            cancellationToken);
}
