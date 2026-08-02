using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Domain.Transactions;

namespace Toklong.Infrastructure.Persistence;

public sealed class CounterQrResourceRepository(
    ToklongDbContext dbContext) :
    ICounterQrResourceRepository
{
    public async Task<ShipmentCounterQrResource?> ClaimDueAsync(
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
            ShipmentCounterQrResource? resource;
            if (dbContext.Database.IsNpgsql())
            {
                resource = await dbContext.CounterQrResources
                    .FromSqlInterpolated($"""
                        SELECT *
                        FROM shipment_counter_qr_resources
                        WHERE (("Status" IN ('Pending', 'RetryableError')
                        AND "NextAttemptAt" <= {now})
                        OR ("Status" = 'Ready'
                        AND "ProviderExpiresAt" IS NOT NULL
                        AND "ProviderExpiresAt" <= {now}))
                        AND ("LeaseExpiresAt" IS NULL OR "LeaseExpiresAt" <= {now})
                        ORDER BY COALESCE("NextAttemptAt", "ProviderExpiresAt"), "CreatedAt"
                        FOR UPDATE SKIP LOCKED
                        LIMIT 1
                        """)
                    .FirstOrDefaultAsync(cancellationToken);
            }
            else
            {
                resource = await dbContext.CounterQrResources
                    .Where(item =>
                        (((item.Status == CounterQrResourceStatus.Pending ||
                           item.Status == CounterQrResourceStatus.RetryableError) &&
                          item.NextAttemptAt <= now) ||
                         (item.Status == CounterQrResourceStatus.Ready &&
                          item.ProviderExpiresAt.HasValue &&
                          item.ProviderExpiresAt <= now)) &&
                        (!item.LeaseExpiresAt.HasValue ||
                         item.LeaseExpiresAt <= now))
                    .OrderBy(item => item.NextAttemptAt ??
                        item.ProviderExpiresAt)
                    .ThenBy(item => item.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (resource is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            resource.Claim(workerId, now, leaseDuration);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return resource;
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

    public Task<ShipmentCounterQrResource?> GetByIdAsync(
        Guid resourceId,
        CancellationToken cancellationToken) =>
        dbContext.CounterQrResources.SingleOrDefaultAsync(
            item => item.Id == resourceId,
            cancellationToken);

    public Task<SaleTransaction?> GetTransactionAsync(
        Guid transactionId,
        CancellationToken cancellationToken) =>
        dbContext.Transactions
            .Include(item => item.AuditEvents)
            .Include(item => item.ManagedShipments)
                .ThenInclude(item => item.CounterQrResource)
            .SingleOrDefaultAsync(
                item => item.Id == transactionId,
                cancellationToken);
}
