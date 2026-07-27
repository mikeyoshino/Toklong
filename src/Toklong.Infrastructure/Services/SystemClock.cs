using Toklong.Application.Abstractions;

namespace Toklong.Infrastructure.Services;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class ManualPayoutProvider : IPayoutProvider
{
    public Task<PayoutInstructionPreparation> CreateInstructionAsync(
        Guid transactionId,
        long amountSatang,
        string currency,
        string bankCode,
        string accountName,
        string accountNumber,
        CancellationToken cancellationToken) =>
        Task.FromResult(new PayoutInstructionPreparation(
            "manual-bank",
            $"PAYOUT-{DateTime.UtcNow:yyMMdd}-" +
            transactionId.ToString("N")[..8].ToUpperInvariant(),
            "accepted"));
}
