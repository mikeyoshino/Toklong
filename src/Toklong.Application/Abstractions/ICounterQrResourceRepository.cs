using Toklong.Domain.Transactions;

namespace Toklong.Application.Abstractions;

public interface ICounterQrResourceRepository
{
    Task<ShipmentCounterQrResource?> ClaimDueAsync(
        string workerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<ShipmentCounterQrResource?> GetByIdAsync(
        Guid resourceId,
        CancellationToken cancellationToken);

    Task<SaleTransaction?> GetTransactionAsync(
        Guid transactionId,
        CancellationToken cancellationToken);
}
