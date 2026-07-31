using Toklong.Mobile.Core;
using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Core.Tests;

public sealed class VerifyNameChangeViewModelTests :
    AccountNameChangeViewModelTestBase
{
    [Fact]
    public void Pending_challenge_exposes_masked_phone_name_summary_and_server_timing()
    {
        var viewModel = Verify(new RecordingAuthentication());

        viewModel.Apply(Pending());

        Assert.Equal("08x-xxx-1234", viewModel.MaskedPhoneNumber);
        Assert.Equal("สมศักดิ์ ใจดี", viewModel.PendingDisplayName);
        Assert.Equal(60, viewModel.ResendSecondsRemaining);
        Assert.False(viewModel.CanResend);
        Assert.True(viewModel.CanUseChallenge);
        Assert.True(viewModel.CanConfirm);
        viewModel.Deactivate();
    }

    [Fact]
    public async Task Incorrect_code_uses_shared_error_target_and_bounded_remaining_attempts()
    {
        var authentication = new RecordingAuthentication
        {
            VerifyName = (_, _) =>
                Task.FromException<VerifiedAccountNameChange>(
                    Problem(
                        "name_change_code_incorrect",
                        remainingAttempts: 3))
        };
        var viewModel = Verify(authentication);
        AccountNameChangeErrorNotice? notice = null;
        viewModel.ErrorPresented += (_, value) => notice = value;
        viewModel.Apply(Pending());
        viewModel.Code = "123456";

        await viewModel.ConfirmAsync();

        Assert.Equal(AccountNameChangeErrorTarget.CodeInput, notice!.Target);
        Assert.Equal(3, viewModel.RemainingAttempts);
        Assert.Contains("เหลือ 3 ครั้ง", viewModel.Message);
        Assert.True(viewModel.CanUseChallenge);
        viewModel.Deactivate();
    }

    [Fact]
    public void Expired_or_locked_pending_cannot_verify_and_offers_a_fresh_request()
    {
        var expired = Verify(new RecordingAuthentication());
        expired.Apply(Pending(expiresAt: Now.AddSeconds(-1)));

        Assert.True(expired.IsExpired);
        Assert.True(expired.RequiresNewRequest);
        Assert.False(expired.CanConfirm);
        expired.Deactivate();

        var locked = Verify(new RecordingAuthentication());
        locked.Apply(Pending(remainingAttempts: 0));

        Assert.True(locked.IsLocked);
        Assert.True(locked.RequiresNewRequest);
        Assert.False(locked.CanUseChallenge);
        locked.Deactivate();
    }

    [Fact]
    public async Task Resend_replaces_the_challenge_clears_code_and_tracks_no_personal_data()
    {
        var replacement = Pending(
            challengeId: Guid.Parse(
                "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            resendAvailableAt: Now.AddSeconds(60));
        var authentication = new RecordingAuthentication
        {
            ResendName = _ => Task.FromResult(replacement)
        };
        var analytics = new RecordingAnalytics();
        var viewModel = Verify(authentication, analytics);
        viewModel.Apply(Pending(resendAvailableAt: Now.AddSeconds(-1)));
        viewModel.Code = "123456";

        await viewModel.ResendAsync();

        Assert.Equal("", viewModel.Code);
        Assert.Equal(60, viewModel.ResendSecondsRemaining);
        Assert.Single(authentication.ResendNameCalls);
        var resent = Assert.Single(analytics.Events, value =>
            value.Name == "account_name_change_code_resent");
        Assert.Empty(resent.Properties);
        viewModel.Deactivate();
    }

    [Fact]
    public async Task Resend_daily_limit_is_modal_only_and_keeps_the_pending_challenge()
    {
        var retryAt = Now.AddHours(12);
        var authentication = new RecordingAuthentication
        {
            ResendName = _ =>
                Task.FromException<PendingAccountNameChange>(
                    Problem("name_change_send_limit", retryAt))
        };
        var viewModel = Verify(authentication);
        viewModel.Apply(Pending(resendAvailableAt: Now.AddSeconds(-1)));
        AccountNameChangeModalNotice? modal = null;
        viewModel.ActionBlocked += (_, value) => modal = value;

        await viewModel.ResendAsync();

        Assert.NotNull(modal);
        Assert.False(viewModel.HasMessage);
        Assert.True(viewModel.CanUseChallenge);
        Assert.Equal("สมศักดิ์ ใจดี", viewModel.PendingDisplayName);
        viewModel.Deactivate();
    }

    [Fact]
    public async Task Success_refreshes_profile_returns_to_account_and_is_consumed_once()
    {
        Shell.Current = new Shell();
        var authentication = new RecordingAuthentication
        {
            GetProfile = () => Task.FromResult(
                Profile("สมศักดิ์", "ใจดี"))
        };
        var analytics = new RecordingAnalytics();
        var session = new AuthenticatedSessionBoundary();
        var completion = new AccountNameChangeCompletionState(session);
        var viewModel = new VerifyNameChangeViewModel(
            authentication,
            analytics,
            new FixedTimeProvider(Now),
            session,
            completion);
        viewModel.Activate();
        viewModel.Apply(Pending());
        viewModel.Code = "123456";

        await viewModel.ConfirmAsync();

        Assert.Equal(["//main/account"], Shell.Current.Routes);
        Assert.Equal(1, authentication.ProfileCalls);
        Assert.Single(analytics.Events, value =>
            value.Name == "account_name_change_verified");

        var account = new AccountViewModel(
            authentication,
            analytics,
            session,
            new AccountEmailChangeCompletionState(session),
            completion);
        await account.LoadAsync();

        Assert.Equal("สมศักดิ์ ใจดี", account.DisplayName);
        Assert.Equal(
            "เปลี่ยนชื่อเรียบร้อยแล้ว ชื่อใหม่จะใช้กับรายการใหม่",
            account.SuccessMessage);
        account.DismissSuccessMessage();
        await account.LoadAsync();
        Assert.False(account.HasSuccessMessage);
        viewModel.Deactivate();
    }

    [Fact]
    public async Task Navigation_failure_after_verified_result_never_verifies_twice()
    {
        Shell.Current = new Shell
        {
            Navigate = _ => Task.FromException(
                new InvalidOperationException("private navigation detail"))
        };
        var authentication = new RecordingAuthentication();
        var analytics = new RecordingAnalytics();
        var viewModel = Verify(authentication, analytics);
        viewModel.Apply(Pending());
        viewModel.Code = "123456";

        await viewModel.ConfirmAsync();

        Assert.True(viewModel.RequiresAccountReturn);
        Assert.Contains("บันทึกชื่อสำเร็จแล้ว", viewModel.Message);
        Assert.Single(authentication.VerifyNameCalls);

        Shell.Current.Navigate = null;
        await viewModel.ReturnToAccountAsync();

        Assert.Equal(["//main/account"], Shell.Current.Routes);
        Assert.Single(authentication.VerifyNameCalls);
        Assert.Single(analytics.Events, value =>
            value.Name == "account_name_change_verified");
        viewModel.Deactivate();
    }

    [Fact]
    public async Task Late_verification_after_session_reset_cannot_refresh_navigate_or_emit_success()
    {
        Shell.Current = new Shell();
        var verification =
            new TaskCompletionSource<VerifiedAccountNameChange>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var authentication = new RecordingAuthentication
        {
            VerifyName = (_, _) => verification.Task
        };
        var analytics = new RecordingAnalytics();
        var session = new AuthenticatedSessionBoundary();
        var completion = new AccountNameChangeCompletionState(session);
        var viewModel = new VerifyNameChangeViewModel(
            authentication,
            analytics,
            new FixedTimeProvider(Now),
            session,
            completion);
        viewModel.Activate();
        viewModel.Apply(Pending());
        viewModel.Code = "123456";

        var confirming = viewModel.ConfirmAsync();
        session.Reset();
        verification.SetResult(Verified());
        await confirming;

        Assert.Equal(0, authentication.ProfileCalls);
        Assert.Empty(Shell.Current.Routes);
        Assert.Empty(analytics.Events);
        Assert.False(completion.TryConsume(session.Capture()));
    }

    private static VerifyNameChangeViewModel Verify(
        RecordingAuthentication authentication,
        RecordingAnalytics? analytics = null)
    {
        var session = new AuthenticatedSessionBoundary();
        var viewModel = new VerifyNameChangeViewModel(
            authentication,
            analytics ?? new RecordingAnalytics(),
            new FixedTimeProvider(Now),
            session,
            new AccountNameChangeCompletionState(session));
        viewModel.Activate();
        return viewModel;
    }
}
