namespace Toklong.Mobile.Core;

public sealed class InMemoryPendingRegistrationStore
    : IPendingRegistrationStore
{
    private PendingMobileRegistration? pending;

    public Task<PendingMobileRegistration?> GetValidAsync(
        DateTimeOffset now)
    {
        if (pending is not null &&
            pending.ExpiresAt <= now)
            pending = null;
        return Task.FromResult(pending);
    }

    public Task SaveAsync(PendingMobileRegistration value)
    {
        ArgumentNullException.ThrowIfNull(value);
        pending = value;
        return Task.CompletedTask;
    }

    public void Clear() => pending = null;
}
