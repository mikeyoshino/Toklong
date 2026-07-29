using Toklong.Mobile.Core;
using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Core.Tests;

public abstract class EmailChangeViewModelTestBase
{
    protected static readonly DateTimeOffset Now =
        new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);

    protected static AccountViewModel Account(
        RecordingAuthentication authentication)
    {
        var session =
            new AuthenticatedSessionBoundary();
        return new(
            authentication,
            session,
            new AccountEmailChangeCompletionState(
                session));
    }

    protected static ChangeEmailViewModel Change(
        RecordingAuthentication authentication,
        RecordingAnalytics analytics)
    {
        var viewModel = new ChangeEmailViewModel(
            authentication,
            analytics,
            new AuthenticatedSessionBoundary());
        viewModel.Activate();
        return viewModel;
    }

    protected static VerifyEmailChangeViewModel Verify(
        RecordingAuthentication authentication,
        RecordingAnalytics? analytics = null,
        TimeProvider? time = null)
    {
        var session =
            new AuthenticatedSessionBoundary();
        var viewModel = new VerifyEmailChangeViewModel(
            authentication,
            analytics ?? new RecordingAnalytics(),
            time ?? new ManualTimeProvider(Now),
            session,
            new AccountEmailChangeCompletionState(
                session));
        viewModel.Activate();
        return viewModel;
    }

    protected static MobileProfile Profile(
        string? email,
        bool canBuy = true,
        bool canSell = false) =>
        new(
            "Buyer Example",
            "0812345678",
            email,
            null,
            null,
            null,
            canBuy,
            canSell);

    protected static PendingEmailChange Pending(
        Guid? challengeId = null,
        string maskedEmail = "n••@example.com",
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? resendAvailableAt = null) =>
        new(
            challengeId ??
            Guid.Parse(
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            maskedEmail,
            expiresAt ?? Now.AddMinutes(10),
            resendAvailableAt ?? Now.AddSeconds(60),
            5);

    protected static void AssertFailedReason(
        RecordingAnalytics analytics,
        string reason)
    {
        var failed = analytics.Events.Last(
            value =>
                value.Name ==
                "account_email_change_failed");
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["reason"] = reason
            },
            failed.Properties);
    }

    protected sealed class ManualTimeProvider(
        DateTimeOffset now) : TimeProvider
    {
        private readonly object sync = new();
        private readonly List<ManualTimer> timers = [];

        public int CreatedTimerCount { get; private set; }

        public int ActiveTimerCount
        {
            get
            {
                lock (sync)
                    return timers.Count;
            }
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (sync)
                return now;
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            lock (sync)
            {
                CreatedTimerCount++;
                var timer = new ManualTimer(
                    this,
                    callback,
                    state,
                    DueAt(dueTime),
                    period);
                timers.Add(timer);
                return timer;
            }
        }

        public async Task AdvanceAsync(
            TimeSpan value)
        {
            if (value < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(value));

            DateTimeOffset target;
            lock (sync)
                target = now + value;

            while (true)
            {
                ManualTimer? next;
                lock (sync)
                {
                    next = timers
                        .Where(timer =>
                            timer.DueAt is not null &&
                            timer.DueAt <= target)
                        .OrderBy(timer => timer.DueAt)
                        .FirstOrDefault();
                    if (next is null)
                    {
                        now = target;
                        break;
                    }

                    now = next.DueAt!.Value;
                }

                next.Fire();
                await DrainContinuationsAsync();
            }

            await DrainContinuationsAsync();
        }

        private DateTimeOffset? DueAt(
            TimeSpan dueTime) =>
            dueTime == Timeout.InfiniteTimeSpan
                ? null
                : now + dueTime;

        private void Remove(ManualTimer timer)
        {
            lock (sync)
                timers.Remove(timer);
        }

        private static async Task DrainContinuationsAsync()
        {
            for (var index = 0; index < 4; index++)
                await Task.Yield();
        }

        private sealed class ManualTimer(
            ManualTimeProvider owner,
            TimerCallback callback,
            object? state,
            DateTimeOffset? dueAt,
            TimeSpan period) : ITimer
        {
            private bool disposed;

            public DateTimeOffset? DueAt { get; private set; } =
                dueAt;

            public bool Change(
                TimeSpan dueTime,
                TimeSpan replacementPeriod)
            {
                if (disposed)
                    return false;

                DueAt = owner.DueAt(dueTime);
                period = replacementPeriod;
                return true;
            }

            public void Fire()
            {
                if (disposed || DueAt is null)
                    return;

                DueAt = period == Timeout.InfiniteTimeSpan
                    ? null
                    : owner.GetUtcNow() + period;
                callback(state);
            }

            public void Dispose()
            {
                if (disposed)
                    return;

                disposed = true;
                owner.Remove(this);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    protected sealed class RecordingAnalytics : IMobileAnalytics
    {
        public List<MobileAnalyticsEvent> Events { get; } = [];

        public void Track(MobileAnalyticsEvent value) =>
            Events.Add(value);
    }

    protected sealed class RecordingAuthentication :
        IAuthenticationService
    {
        public Func<Task<MobileProfile>> GetProfile { get; set; } =
            () => Task.FromResult(Profile("old@example.com"));
        public Func<Task<PendingEmailChange?>> GetPending { get; set; } =
            () => Task.FromResult<PendingEmailChange?>(null);
        public Func<
            string,
            string,
            Task<PendingEmailChange>> RequestEmail { get; set; } =
            (_, _) => Task.FromResult(Pending());
        public Func<
            Guid,
            string,
            Task<PendingEmailChange>> ResendEmail { get; set; } =
            (_, _) => Task.FromResult(Pending());
        public Func<
            Guid,
            string,
            string,
            Task<string>> VerifyEmail { get; set; } =
            (_, _, _) => Task.FromResult("new@example.com");

        public int ProfileCalls { get; private set; }
        public int PendingCalls { get; private set; }
        public bool SignedOut { get; private set; }
        public List<(string Email, string Key)> RequestCalls { get; } = [];
        public List<(Guid ChallengeId, string Key)> ResendCalls { get; } = [];
        public List<CancellationToken> RequestTokens { get; } = [];
        public List<CancellationToken> ResendTokens { get; } = [];
        public List<CancellationToken> VerifyTokens { get; } = [];
        public List<(Guid ChallengeId, string Code, string Key)> VerifyCalls
        {
            get;
        } = [];

        public Task<MobileProfile> GetProfileAsync(
            CancellationToken cancellationToken = default)
        {
            ProfileCalls++;
            return GetProfile();
        }

        public Task<PendingEmailChange?> GetPendingEmailChangeAsync(
            CancellationToken cancellationToken = default)
        {
            PendingCalls++;
            return GetPending();
        }

        public Task<PendingEmailChange> RequestEmailChangeAsync(
            string email,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            RequestCalls.Add((email, idempotencyKey));
            RequestTokens.Add(cancellationToken);
            return RequestEmail(email, idempotencyKey);
        }

        public Task<PendingEmailChange> ResendEmailChangeAsync(
            Guid challengeId,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            ResendCalls.Add((challengeId, idempotencyKey));
            ResendTokens.Add(cancellationToken);
            return ResendEmail(challengeId, idempotencyKey);
        }

        public Task<string> VerifyEmailChangeAsync(
            Guid challengeId,
            string code,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            VerifyCalls.Add((
                challengeId,
                code,
                idempotencyKey));
            VerifyTokens.Add(cancellationToken);
            return VerifyEmail(
                challengeId,
                code,
                idempotencyKey);
        }

        public Task SignOutAsync(
            CancellationToken cancellationToken = default)
        {
            SignedOut = true;
            return Task.CompletedTask;
        }

        public Task<bool> HasSessionAsync() =>
            throw new NotSupportedException();

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
    }
}
