using System.Net;
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
        var problem = await ParsedApiProblemAsync(
            "name_change_send_limit",
            retryAt,
            TimeSpan.FromHours(12));
        var authentication = new RecordingAuthentication
        {
            ResendName = _ =>
                Task.FromException<PendingAccountNameChange>(
                    problem)
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Another_device_cooldown_invalidates_verify_or_resend_and_opens_exact_return_modal(
        bool duringVerification)
    {
        var nextAllowedAt =
            DateTimeOffset.Parse("2026-09-30T02:45:00Z");
        var blocked = await ParsedApiProblemAsync(
            "name_change_cooldown",
            nextAllowedAt,
            status: HttpStatusCode.Conflict);
        var authentication = new RecordingAuthentication
        {
            VerifyName = (_, _) =>
                Task.FromException<VerifiedAccountNameChange>(blocked),
            ResendName = _ =>
                Task.FromException<PendingAccountNameChange>(blocked)
        };
        var viewModel = Verify(authentication);
        viewModel.Apply(Pending(
            resendAvailableAt: Now.AddSeconds(-1)));
        viewModel.Code = "123456";
        AccountNameChangeModalNotice? modal = null;
        viewModel.ActionBlocked += (_, value) => modal = value;

        if (duringVerification)
            await viewModel.ConfirmAsync();
        else
            await viewModel.ResendAsync();

        Assert.Equal("ยังเปลี่ยนชื่อไม่ได้", modal!.Title);
        Assert.Contains("30 ก.ย. 2569 · 09:45 น.", modal.Message);
        Assert.Equal("เข้าใจแล้ว", modal.AcceptText);
        Assert.False(viewModel.CanUseChallenge);
        Assert.False(viewModel.CanConfirm);
        Assert.False(viewModel.CanResend);
        Assert.False(viewModel.RequiresNewRequest);
        Assert.True(viewModel.RequiresAccountReturn);
        Assert.True(viewModel.CanReturnToAccount);
        Assert.Equal("", viewModel.MaskedPhoneNumber);
        Assert.Equal("", viewModel.PendingDisplayName);
        Assert.Equal("", viewModel.Code);
        Assert.False(viewModel.HasMessage);
        viewModel.Deactivate();
    }

    [Fact]
    public async Task Fresh_request_navigation_failure_is_recoverable_and_keeps_its_primary_action()
    {
        Shell.Current = new Shell
        {
            Navigate = _ => Task.FromException(
                new InvalidOperationException("private navigation detail"))
        };
        var viewModel = Verify(new RecordingAuthentication());
        viewModel.Apply(Pending(expiresAt: Now.AddSeconds(-1)));

        await viewModel.StartNewRequestAsync();

        Assert.True(viewModel.RequiresNewRequest);
        Assert.False(viewModel.CanUseChallenge);
        Assert.Contains("เปิดหน้าแก้ไขชื่อไม่สำเร็จ", viewModel.Message);
        Assert.DoesNotContain("บันทึกชื่อสำเร็จแล้ว", viewModel.Message);
        Assert.DoesNotContain("private", viewModel.Message);
        viewModel.Deactivate();
    }

    [Theory]
    [InlineData("verified", "บันทึกชื่อสำเร็จแล้ว", true)]
    [InlineData("cooldown", "ยังเปลี่ยนชื่อไม่ได้", false)]
    [InlineData("missing", "ไม่พบคำขอเปลี่ยนชื่อ", false)]
    [InlineData("reload", "โหลดคำขอเปลี่ยนชื่อไม่สำเร็จ", false)]
    public async Task Account_return_failure_copy_matches_its_recovery_state(
        string state,
        string expectedCopy,
        bool verified)
    {
        Shell.Current = new Shell
        {
            Navigate = _ => Task.FromException(
                new InvalidOperationException(
                    "private route implementation detail"))
        };
        var session = new AuthenticatedSessionBoundary();
        var authentication = new RecordingAuthentication();
        if (state == "cooldown")
        {
            authentication.VerifyName = (_, _) =>
                Task.FromException<VerifiedAccountNameChange>(
                    Problem(
                        "name_change_cooldown",
                        nextAllowedAt: Now.AddMonths(2)));
        }
        else if (state == "missing")
        {
            authentication.VerifyName = (_, _) =>
                Task.FromException<VerifiedAccountNameChange>(
                    Problem("name_change_challenge_inactive"));
        }
        else if (state == "reload")
        {
            authentication.GetPendingName = () =>
                Task.FromException<PendingAccountNameChange?>(
                    new InvalidOperationException(
                        "private pending lookup detail"));
        }
        var viewModel = new VerifyNameChangeViewModel(
            authentication,
            new RecordingAnalytics(),
            new FixedTimeProvider(Now),
            session,
            new AccountNameChangeCompletionState(session));
        viewModel.Activate();

        if (state == "reload")
        {
            viewModel.Apply(Pending());
            session.Reset();
            viewModel.Activate();
            await viewModel.LoadPendingAfterResetAsync();
        }
        else
        {
            viewModel.Apply(Pending());
            viewModel.Code = "123456";
            await viewModel.ConfirmAsync();
        }

        Assert.True(viewModel.RequiresAccountReturn);
        await viewModel.ReturnToAccountAsync();

        Assert.Contains(expectedCopy, viewModel.Message);
        Assert.DoesNotContain("private", viewModel.Message);
        Assert.Equal(
            verified,
            viewModel.Message.Contains(
                "บันทึกชื่อสำเร็จแล้ว",
                StringComparison.Ordinal));
        Assert.True(viewModel.RequiresAccountReturn);
        viewModel.Dispose();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Expired_or_locked_new_request_route_failure_uses_only_recovery_copy(
        bool expired)
    {
        Shell.Current = new Shell
        {
            Navigate = _ => Task.FromException(
                new InvalidOperationException("private route detail"))
        };
        var viewModel = Verify(new RecordingAuthentication());
        viewModel.Apply(expired
            ? Pending(expiresAt: Now.AddSeconds(-1))
            : Pending(remainingAttempts: 0));

        await viewModel.StartNewRequestAsync();

        Assert.True(viewModel.RequiresNewRequest);
        Assert.Contains("เปิดหน้าแก้ไขชื่อไม่สำเร็จ", viewModel.Message);
        Assert.DoesNotContain("บันทึกชื่อสำเร็จแล้ว", viewModel.Message);
        Assert.DoesNotContain("private", viewModel.Message);
        viewModel.Dispose();
    }

    [Fact]
    public async Task Verification_states_expose_exactly_one_primary_action()
    {
        var active = Verify(new RecordingAuthentication());
        active.Apply(Pending());
        AssertPrimaryAction(active, confirm: true);

        var locked = Verify(new RecordingAuthentication());
        locked.Apply(Pending(remainingAttempts: 0));
        AssertPrimaryAction(locked, newRequest: true);

        var nextAllowedAt =
            DateTimeOffset.Parse("2026-09-30T02:45:00Z");
        var blockedAuthentication = new RecordingAuthentication
        {
            VerifyName = (_, _) =>
                Task.FromException<VerifiedAccountNameChange>(
                    Problem(
                        "name_change_cooldown",
                        nextAllowedAt: nextAllowedAt))
        };
        var blocked = Verify(blockedAuthentication);
        blocked.Apply(Pending());
        blocked.Code = "123456";
        await blocked.ConfirmAsync();
        AssertPrimaryAction(blocked, accountReturn: true);

        Shell.Current = new Shell
        {
            Navigate = _ => Task.FromException(
                new InvalidOperationException("route failed"))
        };
        var completed = Verify(new RecordingAuthentication());
        completed.Apply(Pending());
        completed.Code = "123456";
        await completed.ConfirmAsync();
        AssertPrimaryAction(completed, accountReturn: true);

        active.Deactivate();
        locked.Deactivate();
        blocked.Deactivate();
        completed.Deactivate();
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

    private static void AssertPrimaryAction(
        VerifyNameChangeViewModel viewModel,
        bool confirm = false,
        bool newRequest = false,
        bool accountReturn = false)
    {
        Assert.Equal(confirm, viewModel.CanConfirm);
        Assert.Equal(newRequest, viewModel.RequiresNewRequest);
        Assert.Equal(accountReturn, viewModel.CanReturnToAccount);
        Assert.Equal(
            1,
            new[]
            {
                viewModel.CanConfirm,
                viewModel.RequiresNewRequest,
                viewModel.CanReturnToAccount
            }.Count(value => value));
    }
}
