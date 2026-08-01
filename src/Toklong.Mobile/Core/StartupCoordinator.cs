namespace Toklong.Mobile.Core;

public interface IStartupMotionPreference
{
    bool IsReducedMotionEnabled { get; }
}

public sealed record StartupResult(
    string Route,
    Exception? SessionError,
    Exception? PendingRegistrationError);

public sealed class StartupCoordinator(
    IAuthenticationService authentication,
    IPendingRegistrationStore pendingRegistrations,
    IStartupMotionPreference motionPreference,
    IWorkspaceRolePreference workspaceRoles)
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
        var pendingTask = ResolvePendingRegistrationAsync();
        var motionTask = motionPreference.IsReducedMotionEnabled
            ? Task.CompletedTask
            : playAnimationAsync(cancellationToken);
        await Task.WhenAll(
            sessionTask,
            pendingTask,
            motionTask);

        cancellationToken.ThrowIfCancellationRequested();
        var session = await sessionTask;
        var pending = await pendingTask;

        return new StartupResult(
            session.HasSession
                ? AuthenticatedHomeRoutes.Root(
                    workspaceRoles.GetPreferredRole())
                : pending.HasPending
                    ? AuthenticationRoutes
                        .CompleteRegistration
                    : "//welcome",
            session.Error,
            pending.Error);
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

    private async Task<(bool HasPending, Exception? Error)>
        ResolvePendingRegistrationAsync()
    {
        try
        {
            return (
                await pendingRegistrations.GetValidAsync(
                    DateTimeOffset.UtcNow) is not null,
                null);
        }
        catch (Exception exception)
        {
            return (false, exception);
        }
    }
}
