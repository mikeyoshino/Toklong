using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Domain.Transactions;

namespace Toklong.Infrastructure.Persistence;

public sealed class TransactionRepository(ToklongDbContext dbContext) : ITransactionRepository
{
    public Task AddAsync(SaleTransaction transaction, CancellationToken cancellationToken) =>
        dbContext.Transactions.AddAsync(transaction, cancellationToken).AsTask();

    public Task AddRiskEventAsync(ActivationRiskEvent riskEvent, CancellationToken cancellationToken) =>
        dbContext.ActivationRiskEvents.AddAsync(riskEvent, cancellationToken).AsTask();

    public Task<SaleTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Query().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<SaleTransaction?> GetByPublicTokenAsync(string publicToken, CancellationToken cancellationToken) =>
        Query().SingleOrDefaultAsync(x => x.PublicToken == publicToken, cancellationToken);

    public Task<SaleTransaction?> GetBySellerTokenAsync(string sellerToken, CancellationToken cancellationToken) =>
        Query().SingleOrDefaultAsync(x => x.SellerAccessToken == sellerToken, cancellationToken);

    public Task<SaleTransaction?> GetByBuyerTokenAsync(string buyerToken, CancellationToken cancellationToken) =>
        Query().SingleOrDefaultAsync(x => x.BuyerAccessToken == buyerToken, cancellationToken);

    public async Task<IReadOnlyList<SaleTransaction>> GetDueForReleaseAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        await Query()
            .Where(x => x.State == TransactionState.DeliveredDisputeWindow && x.DisputeWindowEndsAt <= now)
            .ToListAsync(cancellationToken);

    private IQueryable<SaleTransaction> Query() =>
        dbContext.Transactions.Include(x => x.AuditEvents).Include(x => x.ExternalEvents);
}
