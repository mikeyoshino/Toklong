using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class AccountEmailChangeViewModelTests :
    EmailChangeViewModelTestBase
{
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
        profileCompletion.SetResult(
            Profile("old@example.com"));
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
    public async Task Latest_account_load_wins_when_an_older_load_completes_last()
    {
        var oldProfile =
            new TaskCompletionSource<MobileProfile>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var oldPending =
            new TaskCompletionSource<PendingEmailChange?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var latestProfile =
            new TaskCompletionSource<MobileProfile>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var latestPending =
            new TaskCompletionSource<PendingEmailChange?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var profileCall = 0;
        var pendingCall = 0;
        var authentication = new RecordingAuthentication
        {
            GetProfile = () =>
                ++profileCall == 1
                    ? oldProfile.Task
                    : latestProfile.Task,
            GetPending = () =>
                ++pendingCall == 1
                    ? oldPending.Task
                    : latestPending.Task
        };
        var viewModel = Account(authentication);

        var olderLoad = viewModel.LoadAsync();
        var latestLoad = viewModel.LoadAsync();
        latestProfile.SetResult(
            Profile("new@example.com"));
        latestPending.SetResult(null);
        await latestLoad;

        oldProfile.SetResult(
            Profile("old@example.com"));
        oldPending.SetResult(Pending());
        await olderLoad;

        Assert.Equal("new@example.com", viewModel.Email);
        Assert.False(viewModel.HasPendingEmailChange);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task Account_routes_to_request_or_pending_step_from_server_state()
    {
        Shell.Current = new Shell();
        var noPendingAuthentication =
            new RecordingAuthentication
            {
                GetPending = () =>
                    Task.FromResult<PendingEmailChange?>(
                        null)
            };
        var noPending = Account(
            noPendingAuthentication);
        await noPending.LoadAsync();

        await noPending.OpenEmailChangeAsync();

        Assert.Equal(
            ["ChangeEmailPage"],
            Shell.Current.Routes);
        Assert.Equal("ยืนยันแล้ว", noPending.EmailStatus);
        Assert.Equal("แก้ไข", noPending.EmailActionText);

        Shell.Current = new Shell();
        var restoredPending = Pending();
        var restoredAuthentication =
            new RecordingAuthentication
            {
                GetPending = () =>
                    Task.FromResult<PendingEmailChange?>(
                        restoredPending)
            };
        var restoredAfterRestart = Account(
            restoredAuthentication);
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
                Task.FromResult<PendingEmailChange?>(
                    Pending())
        };
        var viewModel = Account(authentication);
        await viewModel.LoadAsync();

        authentication.GetProfile = () =>
            Task.FromResult(
                Profile("new@example.com"));
        authentication.GetPending = () =>
            Task.FromException<PendingEmailChange?>(
                new HttpRequestException(
                    "private network detail"));

        await viewModel.LoadAsync();

        Assert.Equal("new@example.com", viewModel.Email);
        Assert.False(viewModel.HasPendingEmailChange);
        Assert.True(viewModel.HasMessage);
        Assert.DoesNotContain(
            "private",
            viewModel.Message);
    }

    [Fact]
    public async Task Email_change_success_confirmation_survives_account_reload()
    {
        var viewModel = Account(
            new RecordingAuthentication
            {
                GetProfile = () =>
                    Task.FromResult(
                        Profile("new@example.com"))
            });
        viewModel.ShowEmailChangeSuccess();

        await viewModel.LoadAsync();

        Assert.True(viewModel.HasSuccessMessage);
        Assert.Equal(
            "เปลี่ยนอีเมลเรียบร้อยแล้ว",
            viewModel.SuccessMessage);
        Assert.False(viewModel.HasMessage);
    }

    [Fact]
    public async Task Sign_out_clears_only_local_email_navigation_state()
    {
        Shell.Current = new Shell();
        var authentication = new RecordingAuthentication
        {
            GetPending = () =>
                Task.FromResult<PendingEmailChange?>(
                    Pending())
        };
        var viewModel = Account(authentication);
        await viewModel.LoadAsync();

        await viewModel.SignOutAsync();

        Assert.False(viewModel.HasPendingEmailChange);
        Assert.True(authentication.SignedOut);
        Assert.Empty(authentication.RequestCalls);
        Assert.Empty(authentication.ResendCalls);
        Assert.Empty(authentication.VerifyCalls);
        Assert.Equal(
            ["//welcome"],
            Shell.Current.Routes);
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
        profileCompletion.SetResult(
            Profile("old@example.com"));
        pendingCompletion.SetResult(Pending());
        await load;

        Assert.Equal("", viewModel.Email);
        Assert.False(viewModel.HasPendingEmailChange);
        Assert.Equal(
            ["//welcome"],
            Shell.Current.Routes);
    }
}
