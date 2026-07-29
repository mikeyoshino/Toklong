using Toklong.Domain.Transactions;

namespace Toklong.Application.Abstractions;

public interface IShippingOperationRepository
{
    Task<ShippingOperation?> ClaimDueAsync(
        string workerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<ShippingOperation?> GetByIdAsync(
        Guid operationId,
        CancellationToken cancellationToken);
}
