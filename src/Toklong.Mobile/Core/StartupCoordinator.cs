namespace Toklong.Mobile.Core;

public interface IStartupMotionPreference
{
    bool IsReducedMotionEnabled { get; }
}

public sealed record StartupResult(
    string Route,
    Exception? SessionError);

public sealed class StartupCoordinator(
    IAuthenticationService authentication,
    IStartupMotionPreference motionPreference)
{
    private readonly object gate = new();
    private Task<StartupResult>? startupTask;

    public Task<StartupResult> StartAsync(
        Func<CancellationToken, Task> playAnimationAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playAnimationAsync);

        lock (gate)
        {
            return startupTask ??= RunAsync(
                playAnimationAsync,
                cancellationToken);
        }
    }

    private async Task<StartupResult> RunAsync(
        Func<CancellationToken, Task> playAnimationAsync,
        CancellationToken cancellationToken)
    {
        var sessionTask = ResolveSessionAsync();

        if (!motionPreference.IsReducedMotionEnabled)
        {
            await Task.WhenAll(
                sessionTask,
                playAnimationAsync(cancellationToken));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var session = await sessionTask;

        return new StartupResult(
            session.HasSession ? "//transactions" : "//welcome",
            session.Error);
    }

    private async Task<(bool HasSession, Exception? Error)>
        ResolveSessionAsync()
    {
        try
        {
            return (await authentication.HasSessionAsync(), null);
        }
        catch (Exception exception)
        {
            return (false, exception);
        }
    }
}
