using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class ChangeEmailViewModelTests :
    EmailChangeViewModelTestBase
{
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
    public async Task Step_one_requests_an_accessible_email_focus_announcement_for_errors()
    {
        var viewModel = Change(
            new RecordingAuthentication(),
            new RecordingAnalytics());
        EmailChangeErrorNotice? notice = null;
        viewModel.ErrorPresented += (_, value) =>
            notice = value;
        viewModel.Email = "not-an-email";

        await viewModel.SubmitAsync();

        Assert.Equal(
            EmailChangeErrorTarget.EmailInput,
            notice?.Target);
        Assert.Equal(
            viewModel.EmailError,
            notice?.Message);
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
                        new HttpRequestException(
                            "private network detail"))
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
            call => Assert.Equal(
                "new@example.com",
                call.Email));
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
                value.Contains(
                    "private",
                    StringComparison.Ordinal));
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
    public async Task Step_one_does_not_navigate_when_request_finishes_after_page_dismissal()
    {
        Shell.Current = new Shell();
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

        var submit = viewModel.SubmitAsync();
        viewModel.Deactivate();
        completion.SetResult(Pending());
        await submit;

        Assert.Empty(Shell.Current.Routes);
        Assert.True(
            Assert.Single(
                    authentication.RequestTokens)
                .IsCancellationRequested);
        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.HasMessage);
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
        Assert.DoesNotContain(
            "new@example.com",
            viewModel.Message);
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
}
