using Toklong.Application.Abstractions;

namespace Toklong.Infrastructure.Services;

public sealed class DirectBookingAdmission
    : IDirectBookingAdmission,
      IDisposable
{
    private const int FailureThreshold = 5;
    private static readonly TimeSpan CircuitDuration =
        TimeSpan.FromSeconds(10);
    private readonly SemaphoreSlim permits;
    private readonly bool enabled;
    private readonly object circuitLock = new();
    private int consecutiveFailures;
    private DateTimeOffset circuitOpenUntil;
    public TimeSpan ProviderTimeout
    { get; }

    public DirectBookingAdmission(
        ShippopShippingOptions options)
    {
        enabled =
            options.DirectBookingEnabled;
        ProviderTimeout =
            TimeSpan.FromMilliseconds(
                Math.Clamp(
                    options
                        .DirectBookingTimeoutMilliseconds,
                    500,
                    2_200));
        permits = new SemaphoreSlim(
            Math.Clamp(
                options
                    .DirectBookingMaximumConcurrency,
                1,
                256));
    }

    public bool TryEnter(
        out IDisposable lease)
    {
        if (!enabled)
        {
            lease = NullLease.Instance;
            return false;
        }
        lock (circuitLock)
        {
            if (circuitOpenUntil >
                DateTimeOffset.UtcNow)
            {
                lease = NullLease.Instance;
                return false;
            }
        }
        if (!permits.Wait(0))
        {
            lease = NullLease.Instance;
            return false;
        }
        lease = new PermitLease(permits);
        return true;
    }

    public void RecordProviderSuccess()
    {
        lock (circuitLock)
        {
            consecutiveFailures = 0;
            circuitOpenUntil = default;
        }
    }

    public void RecordProviderFailure(
        DateTimeOffset now)
    {
        lock (circuitLock)
        {
            consecutiveFailures++;
            if (consecutiveFailures <
                FailureThreshold)
                return;
            circuitOpenUntil =
                now.Add(CircuitDuration);
            consecutiveFailures = 0;
        }
    }

    public void Dispose() =>
        permits.Dispose();

    private sealed class PermitLease(
        SemaphoreSlim permits)
        : IDisposable
    {
        private SemaphoreSlim? current =
            permits;

        public void Dispose() =>
            Interlocked.Exchange(
                    ref current,
                    null)
                ?.Release();
    }

    private sealed class NullLease
        : IDisposable
    {
        public static NullLease Instance
        { get; } = new();

        public void Dispose()
        {
        }
    }
}
