using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;

namespace Toklong.Infrastructure.Persistence;

public sealed class MobileSessionAccountNameReader(
    ToklongDbContext database) : IMobileSessionAccountNameReader
{
    public Task<MobileSessionAccountName?> GetBuyerAsync(
        Guid buyerId,
        CancellationToken cancellationToken) =>
        database.Buyers
            .AsNoTracking()
            .Where(buyer => buyer.Id == buyerId)
            .Select(buyer => new MobileSessionAccountName(
                buyer.Id,
                buyer.PhoneNumber,
                buyer.FirstName,
                buyer.LastName,
                buyer.FullName))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<MobileSessionAccountName?> GetSellerAsync(
        Guid sellerId,
        CancellationToken cancellationToken) =>
        database.Sellers
            .AsNoTracking()
            .Where(seller => seller.Id == sellerId)
            .Select(seller => new MobileSessionAccountName(
                seller.Id,
                seller.PhoneNumber,
                seller.FirstName,
                seller.LastName,
                seller.DisplayName))
            .SingleOrDefaultAsync(cancellationToken);
}
