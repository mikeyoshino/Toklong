using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Domain.Sellers;

namespace Toklong.Infrastructure.Persistence;

public sealed class SellerRepository(ToklongDbContext dbContext) : ISellerRepository
{
    public Task<SellerAccount?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        dbContext.Sellers
            .Include(x => x.PayoutAccounts)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<SellerAccount?> GetByPhoneAsync(
        string phoneNumber,
        CancellationToken cancellationToken) =>
        dbContext.Sellers
            .Include(x => x.PayoutAccounts)
            .SingleOrDefaultAsync(x => x.PhoneNumber == phoneNumber, cancellationToken);

    public Task AddAsync(
        SellerAccount seller,
        CancellationToken cancellationToken) =>
        dbContext.Sellers.AddAsync(seller, cancellationToken).AsTask();

    public Task AddPayoutAccountAsync(
        SellerPayoutAccount account,
        CancellationToken cancellationToken)
    {
        dbContext.Entry(account).State = EntityState.Added;
        return Task.CompletedTask;
    }
}
