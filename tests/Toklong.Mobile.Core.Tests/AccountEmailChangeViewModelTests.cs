using System.Reflection;
using Toklong.Mobile.Core;
using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Core.Tests;

public sealed class AccountEmailChangeViewModelTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Account_loads_confirmed_email_and_pending_resume_concurrently()
    {
        var profileCompletion =
            new TaskCompletionSource<MobileProfile>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingCompletion =
            new TaskCompletionSource<PendingEmailChange?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var authentication = new RecordingAuthentication
        {
            GetProfile = () => profileCompletion.Task,
            GetPending = () => pendingCompletion.Task
        };
        var viewModel = Account(authentication);

        var load = viewModel.LoadAsync();

        Assert.Equal(1, authentication.ProfileCalls);
        Assert.Equal(1, authentication.PendingCalls);
        profileCompletion.SetResult(Profile("old@example.com"));
        pendingCompletion.SetResult(Pending());
        await load;

        Assert.Equal("old@example.com", viewModel.Email);
        Assert.True(viewModel.HasPendingEmailChange);
        Assert.Equal("รอยืนยัน", viewModel.EmailStatus);
        Assert.Equal("ยืนยันต่อ", viewModel.EmailActionText);
        Assert.Contains("n••", viewModel.EmailNote);
        Assert.Contains(
            "old@example.com",
            viewModel.EmailSemanticDescription);
    }

    [Fact]
    public async Task Account_routes_to_request_or_pending_step_from_server_state()
    {
        Shell.Current = new Shell();
        var noPendingAuthentication = new RecordingAuthentication
        {
            GetPending = () =>
                Task.FromResult<PendingEmailChange?>(null)
        };
        var noPending = Account(noPendingAuthentication);
        await noPending.LoadAsync();

        await noPending.OpenEmailChangeAsync();

        Assert.Equal(
            ["ChangeEmailPage"],
            Shell.Current.Routes);
        Assert.Equal("ยืนยันแล้ว", noPending.EmailStatus);
        Assert.Equal("แก้ไข", noPending.EmailActionText);

        Shell.Current = new Shell();
        var restoredPending = Pending();
        var restoredAuthentication = new RecordingAuthentication
        {
            GetPending = () =>
                Task.FromResult<PendingEmailChange?>(
                    restoredPending)
        };
        var restoredAfterRestart = Account(restoredAuthentication);
        await restoredAfterRestart.LoadAsync();

        await restoredAfterRestart.OpenEmailChangeAsync();

        Assert.Equal(
            ["VerifyEmailChangePage"],
            Shell.Current.Routes);
        var restoredNavigation = Assert.Single(
            Shell.Current.ParameterizedRoutes);
        Assert.Equal(
            "VerifyEmailChangePage",
            restoredNavigation.Route);
        Assert.Same(
            restoredPending,
            restoredNavigation.Parameters["Pending"]);
    }

    [Fact]
    public async Task Seller_only_account_ignores_expected_pending_email_forbidden_response()
    {
        var authentication = new RecordingAuthentication
        {
            GetProfile = () =>
                Task.FromResult(Profile(
                    email: null,
                    canBuy: false,
                    canSell: true)),
            GetPending = () =>
                Task.FromException<PendingEmailChange?>(
                    new InvalidOperationException(
                        "บัญชีนี้ไม่มีสิทธิ์เปลี่ยนอีเมล"))
        };
        var viewModel = Account(authentication);

        await viewModel.LoadAsync();

        Assert.False(viewModel.CanBuy);
        Assert.False(viewModel.HasPendingEmailChange);
        Assert.False(viewModel.HasMessage);
    }

    [Fact]
    public async Task Account_applies_successful_profile_and_clears_stale_pending_when_pending_refresh_fails()
    {
        var authentication = new RecordingAuthentication
        {
            GetPending = () =>
                Task.FromResult<PendingEmailChange?>(Pending())
        };
        var viewModel = Account(authentication);
        await viewModel.LoadAsync();

        authentication.GetProfile = () =>
            Task.FromResult(Profile("new@example.com"));
        authentication.GetPending = () =>
            Task.FromException<PendingEmailChange?>(
                new HttpRequestException("private network detail"));

        await viewModel.LoadAsync();

        Assert.Equal("new@example.com", viewModel.Email);
        Assert.False(viewModel.HasPendingEmailChange);
        Assert.True(viewModel.HasMessage);
        Assert.DoesNotContain("private", viewModel.Message);
    }

    [Fact]
    public async Task Sign_out_clears_only_local_email_navigation_state()
    {
        Shell.Current = new Shell();
        var authentication = new RecordingAuthentication
        {
            GetPending = () =>
                Task.FromResult<PendingEmailChange?>(Pending())
        };
        var viewModel = Account(authentication);
        await viewModel.LoadAsync();

        await viewModel.SignOutAsync();

        Assert.False(viewModel.HasPendingEmailChange);
        Assert.True(authentication.SignedOut);
        Assert.Empty(authentication.RequestCalls);
        Assert.Empty(authentication.ResendCalls);
        Assert.Empty(authentication.VerifyCalls);
        Assert.Equal(["//welcome"], Shell.Current.Routes);
    }

    [Fact]
    public async Task Late_account_load_after_sign_out_cannot_restore_pending_state()
    {
        Shell.Current = new Shell();
        var profileCompletion =
            new TaskCompletionSource<MobileProfile>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingCompletion =
            new TaskCompletionSource<PendingEmailChange?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var authentication = new RecordingAuthentication
        {
            GetProfile = () => profileCompletion.Task,
            GetPending = () => pendingCompletion.Task
        };
        var viewModel = Account(authentication);
        var load = viewModel.LoadAsync();

        await viewModel.SignOutAsync();
        profileCompletion.SetResult(Profile("old@example.com"));
        pendingCompletion.SetResult(Pending());
        await load;

        Assert.Equal("", viewModel.Email);
        Assert.False(viewModel.HasPendingEmailChange);
        Assert.Equal(["//welcome"], Shell.Current.Routes);
    }

    [Fact]
    public async Task Step_one_shows_syntax_feedback_without_requesting_a_code()
    {
        var analytics = new RecordingAnalytics();
        var authentication = new RecordingAuthentication();
        var viewModel = Change(authentication, analytics);

        viewModel.Email = "not-an-email";
        await viewModel.SubmitAsync();

        Assert.Equal("กรอกอีเมลให้ถูกต้อง", viewModel.EmailError);
        Assert.True(viewModel.HasEmailError);
        Assert.Empty(authentication.RequestCalls);
        AssertFailedReason(analytics, "invalid");
    }

    [Fact]
    public async Task Step_one_reuses_key_after_network_failure_and_replaces_it_after_success()
    {
        Shell.Current = new Shell();
        var attempt = 0;
        var authentication = new RecordingAuthentication
        {
            RequestEmail = (_, _) =>
                ++attempt == 1
                    ? Task.FromException<PendingEmailChange>(
                        new HttpRequestException("private network detail"))
                    : Task.FromResult(Pending())
        };
        var analytics = new RecordingAnalytics();
        var viewModel = Change(authentication, analytics);
        viewModel.Email = "new@example.com";

        await viewModel.SubmitAsync();
        await viewModel.SubmitAsync();
        await viewModel.SubmitAsync();

        Assert.Equal(3, authentication.RequestCalls.Count);
        Assert.Equal(
            authentication.RequestCalls[0].Key,
            authentication.RequestCalls[1].Key);
        Assert.NotEqual(
            authentication.RequestCalls[1].Key,
            authentication.RequestCalls[2].Key);
        Assert.All(
            authentication.RequestCalls,
            call => Assert.Equal("new@example.com", call.Email));
        Assert.Equal(
            2,
            analytics.Events.Count(
                value =>
                    value.Name ==
                    "account_email_change_started"));
        Assert.DoesNotContain(
            analytics.Events.SelectMany(
                value => value.Properties.Values),
            value =>
                value.Contains('@') ||
                value.Contains("private", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Changing_a_valid_email_replaces_the_request_key()
    {
        var authentication = new RecordingAuthentication
        {
            RequestEmail = (_, _) =>
                Task.FromException<PendingEmailChange>(
                    new HttpRequestException())
        };
        var viewModel = Change(
            authentication,
            new RecordingAnalytics());
        viewModel.Email = "first@example.com";
        await viewModel.SubmitAsync();

        viewModel.Email = "second@example.com";
        await viewModel.SubmitAsync();

        Assert.Equal(2, authentication.RequestCalls.Count);
        Assert.NotEqual(
            authentication.RequestCalls[0].Key,
            authentication.RequestCalls[1].Key);
    }

    [Fact]
    public async Task Step_one_allows_only_one_request_in_flight()
    {
        var completion =
            new TaskCompletionSource<PendingEmailChange>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var authentication = new RecordingAuthentication
        {
            RequestEmail = (_, _) => completion.Task
        };
        var viewModel = Change(
            authentication,
            new RecordingAnalytics());
        viewModel.Email = "new@example.com";

        var first = viewModel.SubmitAsync();
        var second = viewModel.SubmitAsync();

        Assert.Single(authentication.RequestCalls);
        completion.SetResult(Pending());
        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task Step_one_uses_plain_sender_and_network_errors()
    {
        var analytics = new RecordingAnalytics();
        var response = 0;
        var authentication = new RecordingAuthentication
        {
            RequestEmail = (_, _) =>
                ++response == 1
                    ? Task.FromException<PendingEmailChange>(
                        new InvalidOperationException(
                            "ยังส่งอีเมลไม่ได้ กรุณาลองอีกครั้ง"))
                    : Task.FromException<PendingEmailChange>(
                        new HttpRequestException(
                            "host and email must not escape"))
        };
        var viewModel = Change(authentication, analytics);
        viewModel.Email = "new@example.com";

        await viewModel.SubmitAsync();

        Assert.Equal(
            "ยังส่งอีเมลไม่ได้ กรุณาลองอีกครั้ง",
            viewModel.Message);
        AssertFailedReason(analytics, "sender");

        await viewModel.SubmitAsync();

        Assert.Equal(
            "เชื่อมต่อไม่สำเร็จ กรุณาลองอีกครั้ง",
            viewModel.Message);
        AssertFailedReason(analytics, "network");
        Assert.DoesNotContain("host", viewModel.Message);
        Assert.DoesNotContain("new@example.com", viewModel.Message);
    }

    [Fact]
    public async Task Step_one_success_tracks_started_and_navigates_with_no_sensitive_analytics()
    {
        Shell.Current = new Shell();
        var analytics = new RecordingAnalytics();
        var pending = Pending();
        var authentication = new RecordingAuthentication
        {
            RequestEmail = (_, _) =>
                Task.FromResult(pending)
        };
        var viewModel = Change(authentication, analytics);
        viewModel.Email = "new@example.com";

        await viewModel.SubmitAsync();

        Assert.Equal(
            ["VerifyEmailChangePage"],
            Shell.Current.Routes);
        var navigation = Assert.Single(
            Shell.Current.ParameterizedRoutes);
        Assert.Same(
            pending,
            navigation.Parameters["Pending"]);
        var started = Assert.Single(
            analytics.Events,
            value =>
                value.Name ==
                "account_email_change_started");
        Assert.Empty(started.Properties);
    }

    [Fact]
    public async Task Step_two_filters_to_ascii_digits_and_rejects_non_six_digit_code()
    {
        var authentication = new RecordingAuthentication();
        var viewModel = Verify(authentication);
        viewModel.Apply(Pending());

        viewModel.Code = "12a٣45678";

        Assert.Equal("124567", viewModel.Code);
        viewModel.Code = "12345";
        await viewModel.ConfirmAsync();

        Assert.Empty(authentication.VerifyCalls);
        Assert.Equal("กรอกรหัสยืนยัน 6 หลัก", viewModel.Message);
    }

    [Fact]
    public async Task Verification_reuses_key_for_the_same_code_and_replaces_it_when_code_changes()
    {
        var response = 0;
        var authentication = new RecordingAuthentication
        {
            VerifyEmail = (_, _, _) =>
                ++response < 3
                    ? Task.FromException<string>(
                        new HttpRequestException())
                    : Task.FromResult("new@example.com")
        };
        var viewModel = Verify(authentication);
        viewModel.Apply(Pending());
        viewModel.Code = "123456";

        await viewModel.ConfirmAsync();
        await viewModel.ConfirmAsync();
        viewModel.Code = "654321";
        await viewModel.ConfirmAsync();

        Assert.Equal(3, authentication.VerifyCalls.Count);
        Assert.Equal(
            authentication.VerifyCalls[0].Key,
            authentication.VerifyCalls[1].Key);
        Assert.NotEqual(
            authentication.VerifyCalls[1].Key,
            authentication.VerifyCalls[2].Key);
    }

    [Fact]
    public void Resend_countdown_uses_server_timestamp_and_injected_time()
    {
        var time = new MutableTimeProvider(Now);
        var viewModel = Verify(
            new RecordingAuthentication(),
            time: time);

        viewModel.Apply(Pending(
            resendAvailableAt: Now.AddSeconds(60)));

        Assert.Equal(60, viewModel.ResendSecondsRemaining);
        Assert.False(viewModel.CanResend);
        Assert.Equal(
            "ส่งรหัสใหม่ได้ใน 60 วินาที",
            viewModel.ResendButtonText);
        Assert.Contains("60 วินาที", viewModel.ResendSemanticDescription);

        time.Advance(TimeSpan.FromSeconds(1));
        viewModel.RefreshCountdown();

        Assert.Equal(59, viewModel.ResendSecondsRemaining);

        time.Advance(TimeSpan.FromSeconds(59));
        viewModel.RefreshCountdown();

        Assert.Equal(0, viewModel.ResendSecondsRemaining);
        Assert.True(viewModel.CanResend);
        Assert.Equal("ส่งรหัสใหม่", viewModel.ResendButtonText);
    }

    [Fact]
    public async Task Resend_reuses_key_then_replaces_pending_code_and_timing_on_success()
    {
        var time = new MutableTimeProvider(Now);
        var replacement = Pending(
            challengeId:
                Guid.Parse(
                    "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            maskedEmail: "r••@example.com",
            resendAvailableAt: Now.AddSeconds(60));
        var response = 0;
        var authentication = new RecordingAuthentication
        {
            ResendEmail = (_, _) =>
                ++response == 1
                    ? Task.FromException<PendingEmailChange>(
                        new HttpRequestException())
                    : Task.FromResult(replacement)
        };
        var analytics = new RecordingAnalytics();
        var viewModel = Verify(
            authentication,
            analytics,
            time);
        viewModel.Apply(Pending(
            resendAvailableAt: Now));
        viewModel.Code = "123456";

        await viewModel.ResendAsync();
        await viewModel.ResendAsync();

        Assert.Equal(2, authentication.ResendCalls.Count);
        Assert.Equal(
            authentication.ResendCalls[0].Key,
            authentication.ResendCalls[1].Key);
        Assert.Equal("", viewModel.Code);
        Assert.Equal("r••@example.com", viewModel.MaskedEmail);
        Assert.Equal(60, viewModel.ResendSecondsRemaining);
        time.Advance(TimeSpan.FromSeconds(60));
        viewModel.RefreshCountdown();

        await viewModel.ResendAsync();

        Assert.Equal(3, authentication.ResendCalls.Count);
        Assert.Equal(
            replacement.ChallengeId,
            authentication.ResendCalls[2].ChallengeId);
        Assert.NotEqual(
            authentication.ResendCalls[1].Key,
            authentication.ResendCalls[2].Key);
        var resent = analytics.Events
            .Where(value =>
                value.Name ==
                "account_email_change_code_resent")
            .ToArray();
        Assert.Equal(2, resent.Length);
        Assert.All(
            resent,
            value => Assert.Empty(value.Properties));

        viewModel.Code = "123456";
        await viewModel.ConfirmAsync();

        Assert.Equal(
            replacement.ChallengeId,
            Assert.Single(
                authentication.VerifyCalls)
                .ChallengeId);
    }

    [Fact]
    public async Task Successful_resend_restarts_an_active_server_countdown()
    {
        var time = new CountingTimeProvider(Now);
        var authentication = new RecordingAuthentication
        {
            ResendEmail = (_, _) =>
                Task.FromResult(Pending(
                    resendAvailableAt: Now.AddSeconds(60)))
        };
        var viewModel = Verify(
            authentication,
            time: time);
        viewModel.Apply(Pending(
            resendAvailableAt: Now));
        viewModel.Activate();

        Assert.Equal(0, time.TimerCreationCount);

        await viewModel.ResendAsync();

        Assert.Equal(60, viewModel.ResendSecondsRemaining);
        Assert.Equal(1, time.TimerCreationCount);
        viewModel.Deactivate();
    }

    [Theory]
    [InlineData(
        "รหัสหมดอายุแล้ว กรุณาขอรหัสใหม่",
        "รหัสหมดอายุแล้ว กรุณาขอรหัสใหม่",
        "expired")]
    [InlineData(
        "กรอกรหัสไม่ถูกต้องครบจำนวนแล้ว กรุณาขอรหัสใหม่",
        "กรอกรหัสไม่ถูกต้องครบจำนวนแล้ว กรุณาขอรหัสใหม่",
        "locked")]
    [InlineData(
        "database text that must not escape",
        "รหัสไม่ถูกต้อง ลองตรวจสอบแล้วกรอกอีกครั้ง",
        "invalid")]
    public async Task Verification_uses_plain_copy_and_coarse_failure_analytics(
        string exceptionMessage,
        string expectedMessage,
        string expectedReason)
    {
        var authentication = new RecordingAuthentication
        {
            VerifyEmail = (_, _, _) =>
                Task.FromException<string>(
                    new InvalidOperationException(
                        exceptionMessage))
        };
        var analytics = new RecordingAnalytics();
        var viewModel = Verify(authentication, analytics);
        viewModel.Apply(Pending());
        viewModel.Code = "123456";

        await viewModel.ConfirmAsync();

        Assert.Equal(expectedMessage, viewModel.Message);
        AssertFailedReason(analytics, expectedReason);
        Assert.DoesNotContain(
            analytics.Events.SelectMany(
                value => value.Properties.Values),
            value =>
                value.Contains("database", StringComparison.Ordinal) ||
                value.Contains("123456", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Successful_verification_refreshes_server_profile_and_returns_to_account()
    {
        Shell.Current = new Shell();
        var authentication = new RecordingAuthentication();
        var analytics = new RecordingAnalytics();
        var viewModel = Verify(authentication, analytics);
        viewModel.Apply(Pending());
        viewModel.Code = "123456";

        await viewModel.ConfirmAsync();

        Assert.Single(authentication.VerifyCalls);
        Assert.Equal(1, authentication.ProfileCalls);
        Assert.Equal(
            ["//main/account"],
            Shell.Current.Routes);
        var verified = Assert.Single(
            analytics.Events,
            value =>
                value.Name ==
                "account_email_change_verified");
        Assert.Empty(verified.Properties);
        Assert.DoesNotContain(
            analytics.Events.SelectMany(
                value => value.Properties.Values),
            value =>
                value.Contains('@') ||
                value.Contains("123456", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Successful_verification_replaces_the_verification_key()
    {
        Shell.Current = new Shell();
        var authentication = new RecordingAuthentication();
        var viewModel = Verify(authentication);
        viewModel.Apply(Pending());
        viewModel.Code = "123456";

        await viewModel.ConfirmAsync();
        await viewModel.ConfirmAsync();

        Assert.Equal(2, authentication.VerifyCalls.Count);
        Assert.NotEqual(
            authentication.VerifyCalls[0].Key,
            authentication.VerifyCalls[1].Key);
    }

    [Fact]
    public async Task Step_two_allows_only_one_verify_or_resend_action_in_flight()
    {
        Shell.Current = new Shell();
        var completion =
            new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var authentication = new RecordingAuthentication
        {
            VerifyEmail = (_, _, _) => completion.Task
        };
        var viewModel = Verify(authentication);
        viewModel.Apply(Pending(
            resendAvailableAt: Now.AddMinutes(-1)));
        viewModel.Code = "123456";

        var verification = viewModel.ConfirmAsync();
        var resend = viewModel.ResendAsync();

        Assert.Single(authentication.VerifyCalls);
        Assert.Empty(authentication.ResendCalls);
        completion.SetResult("new@example.com");
        await Task.WhenAll(verification, resend);
    }

    [Fact]
    public void Analytics_constructors_accept_only_coarse_values()
    {
        var publicFactories = typeof(AccountEmailChangeAnalytics)
            .GetMethods(
                BindingFlags.Public |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly);

        Assert.DoesNotContain(
            publicFactories.SelectMany(
                method => method.GetParameters()),
            parameter =>
                parameter.ParameterType == typeof(string));

        var events = new[]
        {
            AccountEmailChangeAnalytics.Started(),
            AccountEmailChangeAnalytics.CodeResent(),
            AccountEmailChangeAnalytics.Verified(),
            AccountEmailChangeAnalytics.Failed(
                AccountEmailChangeFailureReason.Invalid),
            AccountEmailChangeAnalytics.Failed(
                AccountEmailChangeFailureReason.Expired),
            AccountEmailChangeAnalytics.Failed(
                AccountEmailChangeFailureReason.Locked),
            AccountEmailChangeAnalytics.Failed(
                AccountEmailChangeFailureReason.Network),
            AccountEmailChangeAnalytics.Failed(
                AccountEmailChangeFailureReason.Sender)
        };

        Assert.Equal(
            [
                "account_email_change_started",
                "account_email_change_code_resent",
                "account_email_change_verified",
                "account_email_change_failed",
                "account_email_change_failed",
                "account_email_change_failed",
                "account_email_change_failed",
                "account_email_change_failed"
            ],
            events.Select(value => value.Name));
        Assert.All(
            events.Take(3),
            value => Assert.Empty(value.Properties));
        Assert.Equal(
            ["invalid", "expired", "locked", "network", "sender"],
            events.Skip(3)
                .Select(value => value.Properties["reason"]));
        Assert.All(
            events.SelectMany(value => value.Properties),
            property =>
            {
                Assert.DoesNotContain("@", property.Value);
                Assert.DoesNotContain("123456", property.Value);
                Assert.DoesNotContain("exception", property.Value);
                Assert.DoesNotContain("phone", property.Key);
                Assert.DoesNotContain("email", property.Key);
                Assert.DoesNotContain("code", property.Key);
            });
    }

    private static AccountViewModel Account(
        RecordingAuthentication authentication) =>
        new(
            authentication,
            new AuthenticatedSessionBoundary());

    private static ChangeEmailViewModel Change(
        RecordingAuthentication authentication,
        RecordingAnalytics analytics) =>
        new(authentication, analytics);

    private static VerifyEmailChangeViewModel Verify(
        RecordingAuthentication authentication,
        RecordingAnalytics? analytics = null,
        TimeProvider? time = null) =>
        new(
            authentication,
            analytics ?? new RecordingAnalytics(),
            time ?? new MutableTimeProvider(Now));

    private static MobileProfile Profile(
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

    private static PendingEmailChange Pending(
        Guid? challengeId = null,
        string maskedEmail = "n••@example.com",
        DateTimeOffset? resendAvailableAt = null) =>
        new(
            challengeId ??
            Guid.Parse(
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            maskedEmail,
            Now.AddMinutes(10),
            resendAvailableAt ?? Now.AddSeconds(60),
            5);

    private static void AssertFailedReason(
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

    private sealed class MutableTimeProvider(
        DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan value) => now += value;
    }

    private sealed class CountingTimeProvider(
        DateTimeOffset now) : TimeProvider
    {
        public int TimerCreationCount { get; private set; }

        public override DateTimeOffset GetUtcNow() => now;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            TimerCreationCount++;
            return new PassiveTimer();
        }

        private sealed class PassiveTimer : ITimer
        {
            public bool Change(
                TimeSpan dueTime,
                TimeSpan period) =>
                true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() =>
                ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingAnalytics : IMobileAnalytics
    {
        public List<MobileAnalyticsEvent> Events { get; } = [];

        public void Track(MobileAnalyticsEvent value) =>
            Events.Add(value);
    }

    private sealed class RecordingAuthentication :
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
            return RequestEmail(email, idempotencyKey);
        }

        public Task<PendingEmailChange> ResendEmailChangeAsync(
            Guid challengeId,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            ResendCalls.Add((challengeId, idempotencyKey));
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
