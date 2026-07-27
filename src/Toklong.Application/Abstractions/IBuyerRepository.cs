using Toklong.Domain.Buyers;

namespace Toklong.Application.Abstractions;

public interface IBuyerRepository
{
    Task<BuyerAccount?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<BuyerAccount?> GetByPhoneAsync(
        string phoneNumber,
        CancellationToken cancellationToken);

    Task AddAsync(
        BuyerAccount buyer,
        CancellationToken cancellationToken);
}
