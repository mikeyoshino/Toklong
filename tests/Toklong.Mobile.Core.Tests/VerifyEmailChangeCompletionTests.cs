using Toklong.Mobile.Core;
using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Core.Tests;

public sealed class VerifyEmailChangeCompletionTests :
    EmailChangeViewModelTestBase
{
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
                value.Contains(
                    "123456",
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task Navigation_failure_after_server_verification_reports_success_once_and_can_retry_account_route()
    {
        Shell.Current = new Shell
        {
            Navigate = _ =>
                Task.FromException(
                    new InvalidOperationException(
                        "private navigation detail"))
        };
        var analytics = new RecordingAnalytics();
        var viewModel = Verify(
            new RecordingAuthentication(),
            analytics);
        viewModel.Apply(Pending());
        viewModel.Code = "123456";

        await viewModel.ConfirmAsync();

        Assert.True(viewModel.RequiresAccountReturn);
        Assert.Contains(
            "ยืนยันอีเมลสำเร็จแล้ว",
            viewModel.Message);
        Assert.Single(
            analytics.Events,
            value =>
                value.Name ==
                "account_email_change_verified");
        Assert.DoesNotContain(
            analytics.Events,
            value =>
                value.Name ==
                "account_email_change_failed");
        Assert.DoesNotContain(
            "private",
            viewModel.Message);

        Shell.Current.Navigate = null;
        await viewModel.ReturnToAccountAsync();

        Assert.Equal(
            ["//main/account"],
            Shell.Current.Routes);
    }

    [Fact]
    public async Task Profile_refresh_failure_after_verification_still_returns_to_account_without_failed_analytics()
    {
        Shell.Current = new Shell();
        var authentication = new RecordingAuthentication
        {
            GetProfile = () =>
                Task.FromException<MobileProfile>(
                    new HttpRequestException(
                        "private profile detail"))
        };
        var analytics = new RecordingAnalytics();
        var viewModel = Verify(
            authentication,
            analytics);
        viewModel.Apply(Pending());
        viewModel.Code = "123456";

        await viewModel.ConfirmAsync();

        Assert.Equal(
            ["//main/account"],
            Shell.Current.Routes);
        Assert.Single(
            analytics.Events,
            value =>
                value.Name ==
                "account_email_change_verified");
        Assert.DoesNotContain(
            analytics.Events,
            value =>
                value.Name ==
                "account_email_change_failed");
        Assert.False(viewModel.HasMessage);
    }

    [Fact]
    public async Task Late_verification_does_not_refresh_or_navigate_after_session_reset()
    {
        Shell.Current = new Shell();
        var completion =
            new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var authentication = new RecordingAuthentication
        {
            VerifyEmail = (_, _, _) => completion.Task
        };
        var session = new AuthenticatedSessionBoundary();
        var viewModel = new VerifyEmailChangeViewModel(
            authentication,
            new RecordingAnalytics(),
            new ManualTimeProvider(Now),
            session);
        viewModel.Apply(Pending());
        viewModel.Activate();
        viewModel.Code = "123456";

        var verification = viewModel.ConfirmAsync();
        session.Reset();
        completion.SetResult("new@example.com");
        await verification;

        Assert.Equal(0, authentication.ProfileCalls);
        Assert.Empty(Shell.Current.Routes);
        Assert.False(viewModel.HasMessage);
    }

    [Fact]
    public async Task Successful_verification_replaces_the_verification_key_for_a_fresh_challenge()
    {
        Shell.Current = new Shell();
        var authentication = new RecordingAuthentication();
        var viewModel = Verify(authentication);
        viewModel.Apply(Pending());
        viewModel.Code = "123456";

        await viewModel.ConfirmAsync();
        viewModel.Apply(Pending(
            challengeId: Guid.Parse(
                "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")));
        viewModel.Code = "123456";
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
}
