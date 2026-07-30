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
        IReadOnlySet<ShippingOperationType>
            allowedTypes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            allowedTypes);
        if (allowedTypes.Count == 0)
            throw new ArgumentException(
                "At least one shipping operation type is required.",
                nameof(allowedTypes));
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(
                cancellationToken);
        try
        {
            ShippingOperation? operation;
            if (dbContext.Database.IsNpgsql())
            {
                var allowedTypeNames =
                    allowedTypes
                        .Select(type =>
                            type.ToString())
                        .ToArray();
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
                        AND "OperationType" = ANY ({allowedTypeNames})
                        ORDER BY "NextAttemptAt", "CreatedAt"
                        FOR UPDATE SKIP LOCKED
                        LIMIT 1
                        """)
                    .FirstOrDefaultAsync(cancellationToken);
            }
            else
            {
                var due = await dbContext.ShippingOperations
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
                        """)
                    .ToListAsync(cancellationToken);
                operation = due.FirstOrDefault(
                    item => allowedTypes.Contains(
                        item.OperationType));
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
