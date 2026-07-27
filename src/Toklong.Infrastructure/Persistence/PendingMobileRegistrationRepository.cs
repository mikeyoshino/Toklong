using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Domain.Authentication;

namespace Toklong.Infrastructure.Persistence;

public sealed class PendingMobileRegistrationRepository(
    ToklongDbContext dbContext)
    : IPendingMobileRegistrationRepository
{
    public Task<PendingMobileRegistration?> GetByTicketHashAsync(
        string ticketHash,
        CancellationToken cancellationToken) =>
        dbContext.PendingMobileRegistrations.SingleOrDefaultAsync(
            item => item.TicketHash == ticketHash,
            cancellationToken);

    public Task AddAsync(
        PendingMobileRegistration pending,
        CancellationToken cancellationToken) =>
        dbContext.PendingMobileRegistrations
            .AddAsync(pending, cancellationToken)
            .AsTask();

    public Task AddAcceptanceAsync(
        MobileAccountTermsAcceptance acceptance,
        CancellationToken cancellationToken) =>
        dbContext.MobileAccountTermsAcceptances
            .AddAsync(acceptance, cancellationToken)
            .AsTask();

    public Task<int> DeleteExpiredBeforeAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        var query = dbContext.PendingMobileRegistrations
            .Where(item =>
                item.ExpiresAt <= cutoff ||
                item.ConsumedAt <= cutoff);
        return dbContext.Database.IsRelational()
            ? query.ExecuteDeleteAsync(cancellationToken)
            : DeleteTrackedAsync(query, cancellationToken);
    }

    private async Task<int> DeleteTrackedAsync(
        IQueryable<PendingMobileRegistration> query,
        CancellationToken cancellationToken)
    {
        var rows = await query.ToListAsync(cancellationToken);
        dbContext.PendingMobileRegistrations.RemoveRange(rows);
        await dbContext.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }
}
