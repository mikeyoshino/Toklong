using System.Net;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class VerifyEmailChangeTimingTests :
    EmailChangeViewModelTestBase
{
    [Fact]
    public async Task Resend_countdown_uses_server_timestamp_and_injected_time()
    {
        var time = new ManualTimeProvider(Now);
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
        Assert.Contains(
            "60 วินาที",
            viewModel.ResendSemanticDescription);

        await time.AdvanceAsync(
            TimeSpan.FromSeconds(1));

        Assert.Equal(59, viewModel.ResendSecondsRemaining);

        await time.AdvanceAsync(
            TimeSpan.FromSeconds(59));

        Assert.Equal(0, viewModel.ResendSecondsRemaining);
        Assert.True(viewModel.CanResend);
        Assert.Equal(
            "ส่งรหัสใหม่",
            viewModel.ResendButtonText);
    }

    [Fact]
    public async Task Server_expiry_disables_obsolete_actions_and_requires_a_new_request()
    {
        var time = new ManualTimeProvider(Now);
        var authentication = new RecordingAuthentication();
        var viewModel = Verify(
            authentication,
            time: time);
        viewModel.Apply(Pending(
            expiresAt: Now.AddSeconds(2),
            resendAvailableAt: Now));
        viewModel.Code = "123456";

        Assert.False(viewModel.IsExpired);
        Assert.True(viewModel.CanConfirm);
        Assert.True(viewModel.CanResend);

        await time.AdvanceAsync(
            TimeSpan.FromSeconds(2));

        Assert.True(viewModel.IsExpired);
        Assert.True(viewModel.RequiresNewRequest);
        Assert.False(viewModel.CanConfirm);
        Assert.False(viewModel.CanResend);
        Assert.Contains(
            "หมดอายุ",
            viewModel.Message);

        await viewModel.ConfirmAsync();
        await viewModel.ResendAsync();

        Assert.Empty(authentication.VerifyCalls);
        Assert.Empty(authentication.ResendCalls);
    }

    [Fact]
    public async Task Expiry_remains_terminal_when_an_older_verification_fails_late()
    {
        var time = new ManualTimeProvider(Now);
        var completion =
            new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var authentication = new RecordingAuthentication
        {
            VerifyEmail = (_, _, _) => completion.Task
        };
        var viewModel = Verify(
            authentication,
            time: time);
        viewModel.Apply(Pending(
            expiresAt: Now.AddSeconds(1)));
        viewModel.Code = "123456";

        var verification = viewModel.ConfirmAsync();
        await time.AdvanceAsync(
            TimeSpan.FromSeconds(1));
        var terminalMessage = viewModel.Message;
        completion.SetException(
            new HttpRequestException(
                "late private network detail"));
        await verification;

        Assert.True(viewModel.IsExpired);
        Assert.True(viewModel.RequiresNewRequest);
        Assert.False(viewModel.CanConfirm);
        Assert.False(viewModel.CanResend);
        Assert.Equal(
            terminalMessage,
            viewModel.Message);
    }

    [Fact]
    public async Task Expiry_remains_terminal_when_an_older_resend_fails_late()
    {
        var time = new ManualTimeProvider(Now);
        var completion =
            new TaskCompletionSource<PendingEmailChange>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var authentication = new RecordingAuthentication
        {
            ResendEmail = (_, _) => completion.Task
        };
        var viewModel = Verify(
            authentication,
            time: time);
        viewModel.Apply(Pending(
            expiresAt: Now.AddSeconds(1),
            resendAvailableAt: Now));

        var resend = viewModel.ResendAsync();
        await time.AdvanceAsync(
            TimeSpan.FromSeconds(1));
        var terminalMessage = viewModel.Message;
        completion.SetException(
            new HttpRequestException(
                "late private network detail"));
        await resend;

        Assert.True(viewModel.IsExpired);
        Assert.True(viewModel.RequiresNewRequest);
        Assert.False(viewModel.CanConfirm);
        Assert.False(viewModel.CanResend);
        Assert.Equal(
            terminalMessage,
            viewModel.Message);
    }

    [Fact]
    public async Task Successful_resend_replaces_expired_state_and_clears_its_message()
    {
        var time = new ManualTimeProvider(Now);
        var completion =
            new TaskCompletionSource<PendingEmailChange>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var authentication = new RecordingAuthentication
        {
            ResendEmail = (_, _) => completion.Task
        };
        var viewModel = Verify(
            authentication,
            time: time);
        viewModel.Apply(Pending(
            expiresAt: Now.AddSeconds(1),
            resendAvailableAt: Now));

        var resend = viewModel.ResendAsync();
        await time.AdvanceAsync(
            TimeSpan.FromSeconds(1));
        Assert.True(viewModel.IsExpired);
        Assert.True(viewModel.HasMessage);

        completion.SetResult(Pending(
            challengeId:
                Guid.Parse(
                    "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            expiresAt: Now.AddMinutes(10),
            resendAvailableAt: Now.AddSeconds(60)));
        await resend;

        Assert.False(viewModel.IsExpired);
        Assert.False(viewModel.RequiresNewRequest);
        Assert.True(viewModel.CanConfirm);
        Assert.False(viewModel.HasMessage);
    }

    [Fact]
    public void Activating_an_already_expired_challenge_presents_the_terminal_action()
    {
        var session =
            new AuthenticatedSessionBoundary();
        var viewModel =
            new Toklong.Mobile.ViewModels.VerifyEmailChangeViewModel(
            new RecordingAuthentication(),
            new RecordingAnalytics(),
            new ManualTimeProvider(Now),
            session,
            new AccountEmailChangeCompletionState(
                session));
        viewModel.Apply(Pending(
            expiresAt: Now.AddSeconds(-1)));
        EmailChangeErrorNotice? notice = null;
        viewModel.ErrorPresented += (_, value) =>
            notice = value;

        viewModel.Activate();

        Assert.True(viewModel.IsExpired);
        Assert.Equal(
            EmailChangeErrorTarget.NewRequestAction,
            notice?.Target);
        Assert.Equal(
            viewModel.Message,
            notice?.Message);
    }

    [Fact]
    public async Task Countdown_cancels_without_updates_after_page_deactivation()
    {
        var time = new ManualTimeProvider(Now);
        var viewModel = Verify(
            new RecordingAuthentication(),
            time: time);
        viewModel.Apply(Pending(
            expiresAt: Now.AddSeconds(10),
            resendAvailableAt: Now.AddSeconds(3)));

        Assert.Equal(1, time.ActiveTimerCount);
        await time.AdvanceAsync(
            TimeSpan.FromSeconds(1));
        Assert.Equal(2, viewModel.ResendSecondsRemaining);

        viewModel.Deactivate();
        Assert.Equal(0, time.ActiveTimerCount);
        await time.AdvanceAsync(
            TimeSpan.FromSeconds(5));

        Assert.Equal(2, viewModel.ResendSecondsRemaining);
        Assert.False(viewModel.IsExpired);
    }

    [Fact]
    public async Task Resend_reuses_key_then_replaces_pending_code_and_timing_on_success()
    {
        var time = new ManualTimeProvider(Now);
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
        Assert.Equal(
            "r••@example.com",
            viewModel.MaskedEmail);
        Assert.Equal(60, viewModel.ResendSecondsRemaining);
        await time.AdvanceAsync(
            TimeSpan.FromSeconds(60));

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
        var time = new ManualTimeProvider(Now);
        var authentication = new RecordingAuthentication
        {
            ResendEmail = (_, _) =>
                Task.FromResult(Pending(
                    resendAvailableAt:
                        Now.AddSeconds(60)))
        };
        var viewModel = Verify(
            authentication,
            time: time);
        viewModel.Apply(Pending(
            resendAvailableAt: Now));

        var timersBeforeResend =
            time.CreatedTimerCount;

        await viewModel.ResendAsync();

        Assert.Equal(60, viewModel.ResendSecondsRemaining);
        Assert.True(
            time.CreatedTimerCount >
            timersBeforeResend);
        Assert.Equal(1, time.ActiveTimerCount);
        viewModel.Deactivate();
    }

    [Fact]
    public async Task Late_resend_does_not_mutate_a_dismissed_verification_page()
    {
        var completion =
            new TaskCompletionSource<PendingEmailChange>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var authentication = new RecordingAuthentication
        {
            ResendEmail = (_, _) => completion.Task
        };
        var viewModel = Verify(authentication);
        viewModel.Apply(Pending(
            resendAvailableAt: Now));

        var resend = viewModel.ResendAsync();
        viewModel.Deactivate();
        completion.SetResult(Pending(
            challengeId:
                Guid.Parse(
                    "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            maskedEmail: "r••@example.com"));
        await resend;

        Assert.Equal(
            "n••@example.com",
            viewModel.MaskedEmail);
        Assert.True(
            Assert.Single(
                    authentication.ResendTokens)
                .IsCancellationRequested);
        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.HasMessage);
    }

    [Fact]
    public async Task Resend_cooldown_uses_wait_copy_without_invalidating_the_challenge()
    {
        var time = new ManualTimeProvider(Now);
        var authentication = new RecordingAuthentication
        {
            ResendEmail = (_, _) =>
                Task.FromException<PendingEmailChange>(
                    new InvalidOperationException(
                        "กรุณารอสักครู่ก่อนส่งรหัสอีกครั้ง"))
        };
        var analytics = new RecordingAnalytics();
        var viewModel = Verify(
            authentication,
            analytics,
            time);
        viewModel.Apply(Pending(
            resendAvailableAt: Now));

        await viewModel.ResendAsync();

        Assert.Equal(
            "กรุณารอสักครู่ก่อนส่งรหัสอีกครั้ง",
            viewModel.Message);
        Assert.Equal(1, viewModel.ResendSecondsRemaining);
        Assert.False(viewModel.CanResend);
        AssertFailedReason(analytics, "invalid");

        await time.AdvanceAsync(
            TimeSpan.FromSeconds(1));

        Assert.True(viewModel.CanResend);
    }

    [Fact]
    public async Task Resend_rate_limit_uses_retry_after_to_disable_repeat_submission()
    {
        var time = new ManualTimeProvider(Now);
        var authentication = new RecordingAuthentication
        {
            ResendEmail = (_, _) =>
                Task.FromException<PendingEmailChange>(
                    new MobileApiRequestException(
                        HttpStatusCode.TooManyRequests,
                        "เชื่อมต่อ TOKLONG ไม่สำเร็จ กรุณาลองอีกครั้ง",
                        TimeSpan.FromSeconds(17)))
        };
        var viewModel = Verify(
            authentication,
            time: time);
        viewModel.Apply(Pending(
            resendAvailableAt: Now));

        await viewModel.ResendAsync();

        Assert.Contains(
            "17 วินาที",
            viewModel.Message);
        Assert.Equal(17, viewModel.ResendSecondsRemaining);
        Assert.False(viewModel.CanResend);
    }

    [Fact]
    public async Task Resend_rate_limit_without_retry_after_uses_a_local_guard_without_inventing_server_timing()
    {
        var time = new ManualTimeProvider(Now);
        var authentication = new RecordingAuthentication
        {
            ResendEmail = (_, _) =>
                Task.FromException<PendingEmailChange>(
                    new MobileApiRequestException(
                        HttpStatusCode.TooManyRequests,
                        "private provider detail",
                        retryAfter: null))
        };
        var viewModel = Verify(
            authentication,
            time: time);
        viewModel.Apply(Pending(
            resendAvailableAt: Now));

        await viewModel.ResendAsync();

        Assert.Equal(
            "กรุณารอสักครู่ก่อนลองอีกครั้ง",
            viewModel.Message);
        Assert.DoesNotContain(
            "1 วินาที",
            viewModel.Message);
        Assert.Equal(1, viewModel.ResendSecondsRemaining);
        Assert.False(viewModel.CanResend);
    }
}
