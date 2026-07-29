using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class VerifyEmailChangeInputAndErrorsTests :
    EmailChangeViewModelTestBase
{
    [Fact]
    public void Applying_active_challenge_notifies_action_bindings()
    {
        var viewModel = Verify(
            new RecordingAuthentication());
        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, eventArgs) =>
            changes.Add(eventArgs.PropertyName);

        viewModel.Apply(Pending());

        Assert.True(viewModel.CanUseChallenge);
        Assert.True(viewModel.CanConfirm);
        Assert.Contains(
            nameof(viewModel.CanUseChallenge),
            changes);
        Assert.Contains(
            nameof(viewModel.CanConfirm),
            changes);
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
        Assert.Equal(
            "กรอกรหัสยืนยัน 6 หลัก",
            viewModel.Message);
    }

    [Fact]
    public async Task Step_two_requests_accessible_code_focus_for_invalid_code()
    {
        var viewModel = Verify(
            new RecordingAuthentication());
        viewModel.Apply(Pending());
        EmailChangeErrorNotice? notice = null;
        viewModel.ErrorPresented += (_, value) =>
            notice = value;
        viewModel.Code = "12345";

        await viewModel.ConfirmAsync();

        Assert.Equal(
            EmailChangeErrorTarget.CodeInput,
            notice?.Target);
        Assert.Equal(
            viewModel.Message,
            notice?.Message);
    }

    [Fact]
    public async Task Verification_reuses_key_after_ambiguous_network_failure_and_replaces_it_when_code_changes()
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
    public async Task Definitive_incorrect_verification_uses_a_fresh_key_for_an_explicit_retry()
    {
        var authentication = new RecordingAuthentication
        {
            VerifyEmail = (_, _, _) =>
                Task.FromException<string>(
                    new InvalidOperationException(
                        "รหัสไม่ถูกต้อง ลองตรวจสอบแล้วกรอกอีกครั้ง"))
        };
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
        "รหัสไม่ถูกต้อง ลองตรวจสอบแล้วกรอกอีกครั้ง",
        "รหัสไม่ถูกต้อง ลองตรวจสอบแล้วกรอกอีกครั้ง",
        "invalid")]
    [InlineData(
        "database text that must not escape",
        "ยืนยันอีเมลไม่สำเร็จ กรุณาลองอีกครั้ง",
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
                value.Contains(
                    "database",
                    StringComparison.Ordinal) ||
                value.Contains(
                    "123456",
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task Superseded_verification_disables_actions_and_returns_to_account_for_latest_pending()
    {
        Shell.Current = new Shell();
        var authentication = new RecordingAuthentication
        {
            VerifyEmail = (_, _, _) =>
                Task.FromException<string>(
                    new InvalidOperationException(
                        "มีการส่งรหัสใหม่แล้ว กรุณาใช้รหัสล่าสุด"))
        };
        var viewModel = Verify(authentication);
        viewModel.Apply(Pending(
            resendAvailableAt: Now));
        EmailChangeErrorNotice? notice = null;
        viewModel.ErrorPresented += (_, value) =>
            notice = value;
        viewModel.Code = "123456";

        await viewModel.ConfirmAsync();

        Assert.Equal(
            "มีการส่งรหัสใหม่แล้ว กรุณากลับไปยืนยันรหัสล่าสุดจากหน้าบัญชี",
            viewModel.Message);
        Assert.True(viewModel.RequiresPendingRefresh);
        Assert.False(viewModel.RequiresNewRequest);
        Assert.True(viewModel.CanReturnToAccount);
        Assert.Equal(
            "กลับไปยืนยันรหัสล่าสุด",
            viewModel.AccountReturnButtonText);
        Assert.False(viewModel.CanConfirm);
        Assert.False(viewModel.CanResend);
        Assert.Equal(
            EmailChangeErrorTarget.AccountReturnAction,
            notice?.Target);

        await viewModel.ReturnToAccountAsync();

        Assert.Equal(
            ["//main/account"],
            Shell.Current.Routes);
        Assert.Empty(
            Shell.Current.ParameterizedRoutes);
    }

    [Fact]
    public async Task Missing_challenge_returns_to_account_without_claiming_a_latest_pending_code()
    {
        Shell.Current = new Shell();
        var authentication = new RecordingAuthentication
        {
            VerifyEmail = (_, _, _) =>
                Task.FromException<string>(
                    new InvalidOperationException(
                        "ไม่พบคำขอเปลี่ยนอีเมล"))
        };
        var viewModel = Verify(authentication);
        viewModel.Apply(Pending(
            resendAvailableAt: Now));
        EmailChangeErrorNotice? notice = null;
        viewModel.ErrorPresented += (_, value) =>
            notice = value;
        viewModel.Code = "123456";

        await viewModel.ConfirmAsync();

        Assert.Equal(
            "คำขอเปลี่ยนอีเมลนี้ใช้ไม่ได้แล้ว กรุณากลับไปหน้าบัญชี",
            viewModel.Message);
        Assert.True(viewModel.RequiresPendingRefresh);
        Assert.False(viewModel.RequiresNewRequest);
        Assert.True(viewModel.CanReturnToAccount);
        Assert.Equal(
            "กลับไปหน้าบัญชี",
            viewModel.AccountReturnButtonText);
        Assert.Equal(
            "กลับไปหน้าบัญชีเพื่อตรวจสอบข้อมูลล่าสุด",
            viewModel.AccountReturnSemanticDescription);
        Assert.Equal(
            EmailChangeErrorTarget.AccountReturnAction,
            notice?.Target);

        await viewModel.ReturnToAccountAsync();

        Assert.Equal(
            ["//main/account"],
            Shell.Current.Routes);
        Assert.Empty(
            Shell.Current.ParameterizedRoutes);
    }

    [Fact]
    public async Task Locked_verification_disables_confirm_and_resend_actions()
    {
        var authentication = new RecordingAuthentication
        {
            VerifyEmail = (_, _, _) =>
                Task.FromException<string>(
                    new InvalidOperationException(
                        "กรอกรหัสไม่ถูกต้องครบจำนวนแล้ว กรุณาขอรหัสใหม่"))
        };
        var viewModel = Verify(authentication);
        viewModel.Apply(Pending(
            resendAvailableAt: Now));
        viewModel.Code = "123456";

        await viewModel.ConfirmAsync();

        Assert.True(viewModel.IsLocked);
        Assert.True(viewModel.RequiresNewRequest);
        Assert.False(viewModel.CanConfirm);
        Assert.False(viewModel.CanResend);

        await viewModel.ConfirmAsync();
        await viewModel.ResendAsync();

        Assert.Single(authentication.VerifyCalls);
        Assert.Empty(authentication.ResendCalls);
    }
}
