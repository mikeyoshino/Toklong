namespace Toklong.Mobile.Core;

public sealed class AuthenticatedSessionBoundary
{
    private long generation;

    public event EventHandler? ResetRequested;

    public long Capture() =>
        Interlocked.Read(ref generation);

    public bool IsCurrent(long value) =>
        Interlocked.Read(ref generation) == value;

    public void Reset()
    {
        Interlocked.Increment(ref generation);
        ResetRequested?.Invoke(this, EventArgs.Empty);
    }
}
