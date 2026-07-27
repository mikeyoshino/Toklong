using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class StartupCoordinatorTests
{
    [Fact]
    public async Task StartAsync_WithSession_PlaysMotionAndRoutesToTransactions()
    {
        var authentication = new AuthenticationStub(() => Task.FromResult(true));
        var coordinator = new StartupCoordinator(
            authentication,
            new MotionPreferenceStub(false));
        var plays = 0;

        var result = await coordinator.StartAsync(_ =>
        {
            plays++;
            return Task.CompletedTask;
        });

        Assert.Equal("//transactions", result.Route);
        Assert.Null(result.SessionError);
        Assert.Equal(1, plays);
    }

    [Fact]
    public async Task StartAsync_WithReducedMotion_SkipsMotion()
    {
        var coordinator = new StartupCoordinator(
            new AuthenticationStub(() => Task.FromResult(false)),
            new MotionPreferenceStub(true));

        var result = await coordinator.StartAsync(
            _ => throw new InvalidOperationException("must not run"));

        Assert.Equal("//welcome", result.Route);
        Assert.Null(result.SessionError);
    }

    [Fact]
    public async Task StartAsync_WhenSessionLookupFails_FallsBackToWelcome()
    {
        var failure = new InvalidOperationException("secure store failed");
        var coordinator = new StartupCoordinator(
            new AuthenticationStub(() => Task.FromException<bool>(failure)),
            new MotionPreferenceStub(false));

        var result = await coordinator.StartAsync(_ => Task.CompletedTask);

        Assert.Equal("//welcome", result.Route);
        Assert.Same(failure, result.SessionError);
    }

    [Fact]
    public async Task StartAsync_ResolvesSessionWhileMotionIsStillPlaying()
    {
        var animationGate =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var authentication =
            new AuthenticationStub(() => Task.FromResult(true));
        var coordinator = new StartupCoordinator(
            authentication,
            new MotionPreferenceStub(false));

        var startup = coordinator.StartAsync(
            _ => animationGate.Task);

        Assert.Equal(1, authentication.SessionChecks);
        Assert.False(startup.IsCompleted);
        animationGate.SetResult();
        Assert.Equal(
            "//transactions",
            (await startup).Route);
    }

    [Fact]
    public async Task StartAsync_WhenCalledTwice_ReusesOneStartupTask()
    {
        var authentication = new AuthenticationStub(() => Task.FromResult(true));
        var coordinator = new StartupCoordinator(
            authentication,
            new MotionPreferenceStub(false));
        var plays = 0;

        var first = coordinator.StartAsync(_ =>
        {
            plays++;
            return Task.CompletedTask;
        });
        var second = coordinator.StartAsync(_ =>
        {
            plays++;
            return Task.CompletedTask;
        });

        Assert.Same(first, second);
        await Task.WhenAll(first, second);
        Assert.Equal(1, authentication.SessionChecks);
        Assert.Equal(1, plays);
    }

    private sealed class MotionPreferenceStub(bool reduced)
        : IStartupMotionPreference
    {
        public bool IsReducedMotionEnabled { get; } = reduced;
    }

    private sealed class AuthenticationStub(
        Func<Task<bool>> hasSession)
        : IAuthenticationService
    {
        public int SessionChecks { get; private set; }

        public Task<bool> HasSessionAsync()
        {
            SessionChecks++;
            return hasSession();
        }

        public Task<OtpChallengeResult> RequestCodeAsync(
            string phoneNumber,
            AuthenticationMode mode,
            string? fullName,
            string? email,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task VerifyCodeAsync(
            string challengeId,
            string code,
            AuthenticationMode mode,
            string? fullName,
            string? email,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MobileProfile> GetProfileAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string> UpdateEmailAsync(
            string email,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SignOutAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
