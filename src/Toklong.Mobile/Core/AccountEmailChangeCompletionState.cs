namespace Toklong.Mobile.Core;

public sealed class AccountEmailChangeCompletionState(
    AuthenticatedSessionBoundary session)
{
    private readonly object sync = new();
    private long? completedSessionGeneration;

    public void RecordCompletion()
    {
        lock (sync)
        {
            completedSessionGeneration =
                session.Capture();
        }
    }

    public bool TryConsume()
    {
        lock (sync)
        {
            var generation =
                completedSessionGeneration;
            completedSessionGeneration = null;
            return generation is { } value &&
                   session.IsCurrent(value);
        }
    }
}
