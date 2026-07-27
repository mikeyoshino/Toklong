using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Domain.Buyers;

namespace Toklong.Infrastructure.Persistence;

public sealed class BuyerRepository(ToklongDbContext dbContext) : IBuyerRepository
{
    public Task<BuyerAccount?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        dbContext.Buyers.SingleOrDefaultAsync(
            x => x.Id == id, cancellationToken);

    public Task<BuyerAccount?> GetByPhoneAsync(
        string phoneNumber,
        CancellationToken cancellationToken) =>
        dbContext.Buyers.SingleOrDefaultAsync(
            x => x.PhoneNumber == phoneNumber, cancellationToken);

    public Task AddAsync(
        BuyerAccount buyer,
        CancellationToken cancellationToken) =>
        dbContext.Buyers.AddAsync(buyer, cancellationToken).AsTask();
}
