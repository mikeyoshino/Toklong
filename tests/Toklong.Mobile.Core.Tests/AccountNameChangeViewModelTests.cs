using Toklong.Mobile.Core;
using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Core.Tests;

public sealed class AccountNameChangeViewModelTests :
    AccountNameChangeViewModelTestBase
{
    [Fact]
    public void Cooldown_modal_formats_the_exact_server_instant_in_Bangkok()
    {
        var modal = AccountNameChangeModalPresenter.Cooldown(
            new AccountNameChangeBlockedNotice(
                DateTimeOffset.Parse("2026-09-30T02:45:00Z")));

        Assert.Equal("ยังเปลี่ยนชื่อไม่ได้", modal.Title);
        Assert.Contains(
            "เพื่อความปลอดภัย ชื่อบัญชีเปลี่ยนได้ทุก 2 เดือน",
            modal.Message);
        Assert.Contains("30 ก.ย. 2569 · 09:45 น.", modal.Message);
        Assert.Equal("เข้าใจแล้ว", modal.AcceptText);
    }

    [Fact]
    public async Task Eligible_account_opens_prefilled_name_form_and_tracks_only_a_bounded_event()
    {
        Shell.Current = new Shell();
        var authentication = new RecordingAuthentication();
        var analytics = new RecordingAnalytics();
        var session = new AuthenticatedSessionBoundary();
        var viewModel = Account(authentication, analytics, session);
        await viewModel.LoadAsync();

        await viewModel.OpenNameChangeAsync();

        Assert.Equal(["ChangeNamePage"], Shell.Current.Routes);
        var navigation = Assert.Single(Shell.Current.ParameterizedRoutes);
        Assert.Equal("สมชาย", navigation.Parameters["FirstName"]);
        Assert.Equal("ใจดี", navigation.Parameters["LastName"]);
        Assert.Single(analytics.Events, value =>
            value.Name == "account_name_change_opened" &&
            value.Properties.Count == 0);
        Assert.DoesNotContain(
            analytics.Events.SelectMany(value => value.Properties.Values),
            value => value.Contains("สมชาย", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Blocked_account_emits_the_exact_server_notice_without_opening_the_form()
    {
        Shell.Current = new Shell();
        var nextAllowedAt =
            DateTimeOffset.Parse("2026-09-30T09:45:00+07:00");
        var authentication = new RecordingAuthentication
        {
            GetEligibility = () => Task.FromResult(
                new AccountNameChangeEligibility(false, nextAllowedAt))
        };
        var analytics = new RecordingAnalytics();
        var session = new AuthenticatedSessionBoundary();
        var viewModel = Account(authentication, analytics, session);
        await viewModel.LoadAsync();
        AccountNameChangeBlockedNotice? notice = null;
        viewModel.NameChangeBlocked += (_, value) => notice = value;

        await viewModel.OpenNameChangeAsync();

        Assert.Equal(nextAllowedAt, notice!.NextAllowedAt);
        Assert.Empty(Shell.Current.Routes);
        var blocked = Assert.Single(analytics.Events, value =>
            value.Name == "account_name_change_blocked");
        Assert.Equal("cooldown", blocked.Properties["reason"]);
    }

    [Fact]
    public async Task Existing_pending_challenge_resumes_verification_after_eligibility_check()
    {
        Shell.Current = new Shell();
        var pending = Pending();
        var authentication = new RecordingAuthentication
        {
            GetPendingName = () =>
                Task.FromResult<PendingAccountNameChange?>(pending)
        };
        var session = new AuthenticatedSessionBoundary();
        var viewModel = Account(
            authentication,
            new RecordingAnalytics(),
            session);
        await viewModel.LoadAsync();

        await viewModel.OpenNameChangeAsync();

        Assert.Equal(["VerifyNameChangePage"], Shell.Current.Routes);
        Assert.Same(
            pending,
            Assert.Single(Shell.Current.ParameterizedRoutes)
                .Parameters["Pending"]);
    }

    [Fact]
    public async Task Late_eligibility_response_after_sign_out_never_opens_another_accounts_flow()
    {
        Shell.Current = new Shell();
        var eligibility =
            new TaskCompletionSource<AccountNameChangeEligibility>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var authentication = new RecordingAuthentication
        {
            GetEligibility = () => eligibility.Task
        };
        var session = new AuthenticatedSessionBoundary();
        var viewModel = Account(
            authentication,
            new RecordingAnalytics(),
            session);
        await viewModel.LoadAsync();

        var opening = viewModel.OpenNameChangeAsync();
        await viewModel.SignOutAsync();
        eligibility.SetResult(new(true, null));
        await opening;

        Assert.Equal(["//welcome"], Shell.Current.Routes);
        Assert.Equal(0, authentication.PendingNameCalls);
    }

    [Fact]
    public async Task Form_reports_each_required_field_without_sending_a_code()
    {
        var authentication = new RecordingAuthentication();
        var viewModel = Change(
            authentication,
            new RecordingAnalytics());
        viewModel.ApplyCurrentName("สมชาย", "ใจดี");
        viewModel.FirstName = "";
        viewModel.LastName = "";

        await viewModel.SubmitAsync();

        Assert.Equal("กรุณากรอกชื่อ", viewModel.FirstNameError);
        Assert.Equal("กรุณากรอกนามสกุล", viewModel.LastNameError);
        Assert.Empty(authentication.RequestNameCalls);
    }

    [Fact]
    public async Task Normalized_unchanged_name_is_inline_and_does_not_consume_a_send()
    {
        var authentication = new RecordingAuthentication();
        var analytics = new RecordingAnalytics();
        var viewModel = Change(authentication, analytics);
        viewModel.ApplyCurrentName("สมชาย", "ใจดี");
        viewModel.FirstName = "  สมชาย ";
        viewModel.LastName = "ใจดี  ";

        await viewModel.SubmitAsync();

        Assert.Contains("ชื่อปัจจุบัน", viewModel.Message);
        Assert.Empty(authentication.RequestNameCalls);
        Assert.Equal(
            "unchanged",
            Assert.Single(analytics.Events, value =>
                value.Name == "account_name_change_failed")
                .Properties["reason"]);
    }

    [Fact]
    public async Task Form_collapses_whitespace_and_rejects_unsupported_characters_before_send()
    {
        var authentication = new RecordingAuthentication();
        var viewModel = Change(
            authentication,
            new RecordingAnalytics());
        viewModel.ApplyCurrentName("สมชาย", "ใจดี");
        viewModel.FirstName = "มารี  แอนน์";
        viewModel.LastName = "โอ'นีล";

        await viewModel.SubmitAsync();

        Assert.Equal(
            ("มารี แอนน์", "โอ'นีล"),
            Assert.Single(authentication.RequestNameCalls));

        var invalid = Change(
            new RecordingAuthentication(),
            new RecordingAnalytics());
        invalid.ApplyCurrentName("สมชาย", "ใจดี");
        invalid.FirstName = "สมชาย1";

        await invalid.SubmitAsync();

        Assert.Contains("อักขระ", invalid.FirstNameError);
    }

    [Fact]
    public async Task Combined_display_name_over_120_characters_is_rejected_before_send()
    {
        var authentication = new RecordingAuthentication();
        var viewModel = Change(
            authentication,
            new RecordingAnalytics());
        viewModel.ApplyCurrentName("สมชาย", "ใจดี");
        viewModel.FirstName = new string('ก', 60);
        viewModel.LastName = new string('ข', 60);

        await viewModel.SubmitAsync();

        Assert.Empty(authentication.RequestNameCalls);
        Assert.Contains("120", viewModel.LastNameError);
    }

    [Fact]
    public async Task Accepted_request_navigates_with_pending_and_navigation_failure_retries_no_send()
    {
        var pending = Pending();
        Shell.Current = new Shell
        {
            Navigate = _ => Task.FromException(
                new InvalidOperationException("private navigation detail"))
        };
        var authentication = new RecordingAuthentication
        {
            RequestName = (_, _) => Task.FromResult(pending)
        };
        var analytics = new RecordingAnalytics();
        var viewModel = Change(authentication, analytics);
        viewModel.ApplyCurrentName("สมชาย", "ใจดี");
        viewModel.FirstName = " สมศักดิ์ ";

        await viewModel.SubmitAsync();

        Assert.Single(authentication.RequestNameCalls);
        Assert.Contains("ส่งรหัสยืนยันแล้ว", viewModel.Message);
        Assert.Equal("ไปกรอกรหัสยืนยัน", viewModel.SubmitButtonText);
        Assert.False(viewModel.CanEditName);

        Shell.Current.Navigate = null;
        await viewModel.SubmitAsync();

        Assert.Single(authentication.RequestNameCalls);
        Assert.Equal(["VerifyNameChangePage"], Shell.Current.Routes);
        Assert.Same(
            pending,
            Assert.Single(Shell.Current.ParameterizedRoutes)
                .Parameters["Pending"]);
        Assert.Single(analytics.Events, value =>
            value.Name == "account_name_change_started");
    }

    [Fact]
    public async Task Server_recheck_cooldown_raises_the_same_modal_notice()
    {
        var nextAllowedAt =
            DateTimeOffset.Parse("2026-09-30T09:45:00+07:00");
        var authentication = new RecordingAuthentication
        {
            RequestName = (_, _) =>
                Task.FromException<PendingAccountNameChange>(
                    Problem("name_change_cooldown", nextAllowedAt))
        };
        var viewModel = Change(
            authentication,
            new RecordingAnalytics());
        viewModel.ApplyCurrentName("สมชาย", "ใจดี");
        viewModel.FirstName = "สมศักดิ์";
        AccountNameChangeBlockedNotice? notice = null;
        viewModel.NameChangeBlocked += (_, value) => notice = value;

        await viewModel.SubmitAsync();

        Assert.Equal(nextAllowedAt, notice!.NextAllowedAt);
        Assert.DoesNotContain("private", viewModel.Message);
        Assert.Empty(Shell.Current.Routes);
    }

    [Fact]
    public async Task Daily_send_limit_is_presented_only_as_an_action_modal()
    {
        var retryAt = DateTimeOffset.Parse("2026-08-01T12:00:00+07:00");
        var authentication = new RecordingAuthentication
        {
            RequestName = (_, _) =>
                Task.FromException<PendingAccountNameChange>(
                    Problem("name_change_send_limit", retryAt))
        };
        var viewModel = Change(
            authentication,
            new RecordingAnalytics());
        viewModel.ApplyCurrentName("สมชาย", "ใจดี");
        viewModel.FirstName = "สมศักดิ์";
        AccountNameChangeModalNotice? modal = null;
        viewModel.ActionBlocked += (_, value) => modal = value;

        Assert.Null(modal);
        Assert.False(viewModel.HasMessage);

        await viewModel.SubmitAsync();

        Assert.Equal("ขอรหัสยืนยันไม่ได้ในตอนนี้", modal!.Title);
        Assert.Contains("1 ส.ค. 2569 · 12:00 น.", modal.Message);
        Assert.False(viewModel.HasMessage);
    }

    [Fact]
    public async Task Provider_send_throttle_is_presented_as_a_modal_after_submit()
    {
        var authentication = new RecordingAuthentication
        {
            RequestName = (_, _) =>
                Task.FromException<PendingAccountNameChange>(
                    Problem(
                        "name_change_provider_throttled",
                        retryAfter: TimeSpan.FromSeconds(22)))
        };
        var viewModel = Change(
            authentication,
            new RecordingAnalytics());
        viewModel.ApplyCurrentName("สมชาย", "ใจดี");
        viewModel.FirstName = "สมศักดิ์";
        AccountNameChangeModalNotice? modal = null;
        viewModel.ActionBlocked += (_, value) => modal = value;

        await viewModel.SubmitAsync();

        Assert.Contains("22 วินาที", modal!.Message);
        Assert.False(viewModel.HasMessage);
    }

    private static AccountViewModel Account(
        RecordingAuthentication authentication,
        RecordingAnalytics analytics,
        AuthenticatedSessionBoundary session) =>
        new(
            authentication,
            analytics,
            session,
            new AccountEmailChangeCompletionState(session),
            new AccountNameChangeCompletionState(session));

    private static ChangeNameViewModel Change(
        RecordingAuthentication authentication,
        RecordingAnalytics analytics)
    {
        var viewModel = new ChangeNameViewModel(
            authentication,
            analytics,
            new AuthenticatedSessionBoundary());
        viewModel.Activate();
        return viewModel;
    }
}
