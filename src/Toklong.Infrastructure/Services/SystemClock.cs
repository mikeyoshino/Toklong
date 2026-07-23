using Toklong.Application.Abstractions;

namespace Toklong.Infrastructure.Services;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class ManualPayoutProvider : IManualPayoutProvider
{
    public string CreateInstructionReference(Guid transactionId) =>
        $"PAYOUT-{DateTime.UtcNow:yyMMdd}-{transactionId.ToString("N")[..8].ToUpperInvariant()}";
}
