using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class RefreshLoopLifecycleTests
{
    [Fact]
    public void EndDuringInitialLoad_InvalidatesCapturedTokenWithoutThrowing()
    {
        using var lifecycle = new RefreshLoopLifecycle();
        var token = lifecycle.Begin();

        lifecycle.End();

        Assert.True(token.IsCancellationRequested);
        Assert.False(lifecycle.IsCurrent(token));
    }

    [Fact]
    public void NewAppearance_InvalidatesPreviousRefreshLoop()
    {
        using var lifecycle = new RefreshLoopLifecycle();
        var previous = lifecycle.Begin();

        var current = lifecycle.Begin();

        Assert.True(previous.IsCancellationRequested);
        Assert.False(lifecycle.IsCurrent(previous));
        Assert.True(lifecycle.IsCurrent(current));
    }
}
