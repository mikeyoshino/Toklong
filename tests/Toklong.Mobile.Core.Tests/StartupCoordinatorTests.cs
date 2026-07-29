using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class StartupCoordinatorTests
{
    [Fact]
    public async Task StartAsync_WithSession_PlaysMotionAndRoutesToAuthenticatedHome()
    {
        var authentication = new AuthenticationStub(() => Task.FromResult(true));
        var coordinator = new StartupCoordinator(
            authentication,
            new PendingRegistrationStoreStub(false),
            new MotionPreferenceStub(false));
        var plays = 0;

        var result = await coordinator.StartAsync(_ =>
        {
            plays++;
            return Task.CompletedTask;
        });

        Assert.Equal(AuthenticatedHomeRoutes.Home, result.Route);
        Assert.Null(result.SessionError);
        Assert.Equal(1, plays);
    }

    [Fact]
    public async Task StartAsync_WithReducedMotion_SkipsMotion()
    {
        var coordinator = new StartupCoordinator(
            new AuthenticationStub(() => Task.FromResult(false)),
            new PendingRegistrationStoreStub(false),
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
            new PendingRegistrationStoreStub(false),
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
            new PendingRegistrationStoreStub(false),
            new MotionPreferenceStub(false));

        var startup = coordinator.StartAsync(
            _ => animationGate.Task);

        Assert.Equal(1, authentication.SessionChecks);
        Assert.False(startup.IsCompleted);
        animationGate.SetResult();
        Assert.Equal(
            AuthenticatedHomeRoutes.Home,
            (await startup).Route);
    }

    [Fact]
    public async Task StartAsync_WhenCalledTwice_ReusesOneStartupTask()
    {
        var authentication = new AuthenticationStub(() => Task.FromResult(true));
        var coordinator = new StartupCoordinator(
            authentication,
            new PendingRegistrationStoreStub(false),
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

    [Fact]
    public async Task StartAsync_without_session_with_pending_registration_routes_to_completion()
    {
        var coordinator = new StartupCoordinator(
            new AuthenticationStub(
                () => Task.FromResult(false)),
            new PendingRegistrationStoreStub(true),
            new MotionPreferenceStub(false));

        var result = await coordinator.StartAsync(
            _ => Task.CompletedTask);

        Assert.Equal(
            AuthenticationRoutes.CompleteRegistration,
            result.Route);
    }

    [Fact]
    public async Task StartAsync_prefers_authenticated_session_over_pending_registration()
    {
        var coordinator = new StartupCoordinator(
            new AuthenticationStub(
                () => Task.FromResult(true)),
            new PendingRegistrationStoreStub(true),
            new MotionPreferenceStub(false));

        var result = await coordinator.StartAsync(
            _ => Task.CompletedTask);

        Assert.Equal(AuthenticatedHomeRoutes.Home, result.Route);
    }

    private sealed class MotionPreferenceStub(bool reduced)
        : IStartupMotionPreference
    {
        public bool IsReducedMotionEnabled { get; } = reduced;
    }

    private sealed class PendingRegistrationStoreStub(bool valid)
        : IPendingRegistrationStore
    {
        public Task<PendingMobileRegistration?> GetValidAsync(
            DateTimeOffset now) =>
            Task.FromResult(
                valid
                    ? new PendingMobileRegistration(
                        "opaque-ticket",
                        now.AddMinutes(1),
                        "081-***-5678",
                        Guid.NewGuid().ToString("N"),
                        Guid.NewGuid().ToString("N"))
                    : null);

        public Task SaveAsync(
            PendingMobileRegistration pending) =>
            throw new NotSupportedException();

        public void Clear() =>
            throw new NotSupportedException();
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
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AuthenticationVerificationResult> VerifyCodeAsync(
            string challengeId,
            string code,
            AuthenticationMode mode,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CompleteRegistrationAsync(
            string fullName,
            string email,
            string termsVersion,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MobileProfile> GetProfileAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PendingEmailChange?> GetPendingEmailChangeAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PendingEmailChange> RequestEmailChangeAsync(
            string email,
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PendingEmailChange> ResendEmailChangeAsync(
            Guid challengeId,
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string> VerifyEmailChangeAsync(
            Guid challengeId,
            string code,
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SignOutAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
