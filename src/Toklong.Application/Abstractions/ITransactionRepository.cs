using Toklong.Domain.Transactions;

namespace Toklong.Application.Abstractions;

public interface ITransactionRepository
{
    Task AddAsync(SaleTransaction transaction, CancellationToken cancellationToken);
    Task AddRiskEventAsync(ActivationRiskEvent riskEvent, CancellationToken cancellationToken);
    Task<SaleTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<SaleTransaction?> GetByPublicTokenAsync(string publicToken, CancellationToken cancellationToken);
    Task<SaleTransaction?> GetBySellerTokenAsync(string sellerToken, CancellationToken cancellationToken);
    Task<SaleTransaction?> GetByBuyerTokenAsync(string buyerToken, CancellationToken cancellationToken);
    Task<IReadOnlyList<SaleTransaction>> GetDueForReleaseAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IManualPayoutProvider
{
    string CreateInstructionReference(Guid transactionId);
}

public interface IWebhookSignatureVerifier
{
    bool Verify(string payload, string signature);
}
