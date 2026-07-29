namespace Toklong.Mobile.Core;

internal sealed class EmailChangePageLifetime(
    AuthenticatedSessionBoundary session)
{
    private readonly object sync = new();
    private CancellationTokenSource? activation;
    private long activationEpoch;

    public void Activate()
    {
        CancellationTokenSource? previous;
        lock (sync)
        {
            previous = activation;
            activation = new CancellationTokenSource();
            activationEpoch++;
        }

        Cancel(previous);
    }

    public void Deactivate()
    {
        CancellationTokenSource? previous;
        lock (sync)
        {
            previous = activation;
            activation = null;
            activationEpoch++;
        }

        Cancel(previous);
    }

    public EmailChangeOperation? Capture()
    {
        lock (sync)
        {
            return activation is null
                ? null
                : new EmailChangeOperation(
                    activation,
                    activation.Token,
                    activationEpoch,
                    session.Capture());
        }
    }

    public bool IsCurrent(EmailChangeOperation operation)
    {
        lock (sync)
        {
            return ReferenceEquals(
                       activation,
                       operation.Activation) &&
                   activationEpoch ==
                   operation.ActivationEpoch &&
                   !operation.Token.IsCancellationRequested &&
                   session.IsCurrent(
                       operation.SessionGeneration);
        }
    }

    private static void Cancel(
        CancellationTokenSource? source)
    {
        if (source is null)
            return;

        source.Cancel();
        source.Dispose();
    }
}

internal readonly record struct EmailChangeOperation(
    CancellationTokenSource Activation,
    CancellationToken Token,
    long ActivationEpoch,
    long SessionGeneration);
