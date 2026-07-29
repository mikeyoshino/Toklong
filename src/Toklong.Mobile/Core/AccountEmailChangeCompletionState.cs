namespace Toklong.Mobile.Core;

public sealed class AccountEmailChangeCompletionState
{
    private readonly AuthenticatedSessionBoundary session;
    private readonly object sync = new();
    private long? completedSessionGeneration;

    public AccountEmailChangeCompletionState(
        AuthenticatedSessionBoundary session)
    {
        this.session = session;
        session.ResetRequested +=
            OnSessionResetRequested;
    }

    public void RecordCompletion(
        long sessionGeneration)
    {
        lock (sync)
        {
            completedSessionGeneration =
                session.IsCurrent(sessionGeneration)
                    ? sessionGeneration
                    : null;
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

    private void OnSessionResetRequested(
        object? sender,
        EventArgs eventArgs)
    {
        lock (sync)
        {
            completedSessionGeneration = null;
        }
    }
}
