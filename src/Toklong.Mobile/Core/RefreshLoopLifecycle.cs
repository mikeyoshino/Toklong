namespace Toklong.Mobile.Core;

public sealed class RefreshLoopLifecycle : IDisposable
{
    private CancellationTokenSource? current;

    public CancellationToken Begin()
    {
        End();
        var source = new CancellationTokenSource();
        current = source;
        return source.Token;
    }

    public bool IsCurrent(CancellationToken token) =>
        current is { } source &&
        source.Token == token &&
        !token.IsCancellationRequested;

    public void End()
    {
        var source = current;
        current = null;
        if (source is null)
            return;

        source.Cancel();
        source.Dispose();
    }

    public void Dispose() => End();
}
