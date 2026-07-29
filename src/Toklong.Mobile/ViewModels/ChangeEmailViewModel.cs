using System.Net.Mail;
using System.Windows.Input;
using Toklong.Mobile.Core;
using Toklong.Mobile.Pages;

namespace Toklong.Mobile.ViewModels;

public sealed class ChangeEmailViewModel(
    IAuthenticationService authentication,
    IMobileAnalytics analytics) : ObservableViewModel
{
    private string email = "";
    private string emailError = "";
    private string message = "";
    private string? requestIdempotencyKey;
    private bool isBusy;

    public string Email
    {
        get => email;
        set
        {
            var replacement = value ?? "";
            if (!SetProperty(ref email, replacement))
                return;

            requestIdempotencyKey = null;
            if (TryNormalizeEmail(replacement, out _))
            {
                requestIdempotencyKey =
                    Guid.NewGuid().ToString("N");
                EmailError = "";
            }
            else
            {
                EmailError = string.IsNullOrWhiteSpace(replacement)
                    ? ""
                    : "กรอกอีเมลให้ถูกต้อง";
            }
        }
    }

    public string EmailError
    {
        get => emailError;
        private set
        {
            if (SetProperty(ref emailError, value))
                OnPropertyChanged(nameof(HasEmailError));
        }
    }

    public bool HasEmailError =>
        !string.IsNullOrWhiteSpace(EmailError);

    public string Message
    {
        get => message;
        private set
        {
            if (SetProperty(ref message, value))
                OnPropertyChanged(nameof(HasMessage));
        }
    }

    public bool HasMessage =>
        !string.IsNullOrWhiteSpace(Message);

    public bool IsBusy
    {
        get => isBusy;
        private set => SetProperty(ref isBusy, value);
    }

    public ICommand SubmitCommand =>
        new AsyncCommand(SubmitAsync);

    public async Task SubmitAsync()
    {
        if (IsBusy)
            return;
        if (!TryNormalizeEmail(Email, out var normalizedEmail))
        {
            EmailError = "กรอกอีเมลให้ถูกต้อง";
            analytics.Track(
                AccountEmailChangeAnalytics.Failed(
                    AccountEmailChangeFailureReason.Invalid));
            return;
        }

        requestIdempotencyKey ??=
            Guid.NewGuid().ToString("N");
        IsBusy = true;
        EmailError = "";
        Message = "";
        try
        {
            var pending =
                await authentication.RequestEmailChangeAsync(
                    normalizedEmail,
                    requestIdempotencyKey);
            requestIdempotencyKey = null;
            analytics.Track(
                AccountEmailChangeAnalytics.Started());
            await Shell.Current.GoToAsync(
                nameof(VerifyEmailChangePage),
                new Dictionary<string, object>
                {
                    ["Pending"] = pending
                });
        }
        catch (Exception exception)
        {
            var error =
                AccountEmailChangeErrorPresentation.ForRequest(
                    exception);
            Message = error.Message;
            analytics.Track(
                AccountEmailChangeAnalytics.Failed(
                    error.Reason));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool TryNormalizeEmail(
        string value,
        out string normalized)
    {
        normalized = (value ?? "").Trim();
        return normalized.Length is > 0 and <= 254 &&
               MailAddress.TryCreate(
                   normalized,
                   out var parsed) &&
               string.Equals(
                   parsed.Address,
                   normalized,
                   StringComparison.OrdinalIgnoreCase);
    }
}

internal static class AccountEmailChangeErrorPresentation
{
    public static AccountEmailChangeError ForRequest(
        Exception exception)
    {
        if (IsNetwork(exception))
            return Network();
        if (IsSender(exception))
            return new(
                AccountEmailChangeFailureReason.Sender,
                "ยังส่งอีเมลไม่ได้ กรุณาลองอีกครั้ง");
        if (exception.Message.Contains(
                "อีเมลปัจจุบัน",
                StringComparison.Ordinal))
            return new(
                AccountEmailChangeFailureReason.Invalid,
                "อีเมลนี้เป็นอีเมลปัจจุบันของคุณแล้ว");
        return new(
            AccountEmailChangeFailureReason.Invalid,
            "เปลี่ยนอีเมลไม่สำเร็จ กรุณาตรวจสอบแล้วลองอีกครั้ง");
    }

    public static AccountEmailChangeError ForVerification(
        Exception exception)
    {
        if (IsNetwork(exception))
            return Network();
        if (IsSender(exception))
            return new(
                AccountEmailChangeFailureReason.Sender,
                "ยังส่งอีเมลไม่ได้ กรุณาลองอีกครั้ง");
        if (exception.Message.Contains(
                "หมดอายุ",
                StringComparison.Ordinal))
            return new(
                AccountEmailChangeFailureReason.Expired,
                "รหัสหมดอายุแล้ว กรุณาขอรหัสใหม่");
        if (exception.Message.Contains(
                "ครบจำนวน",
                StringComparison.Ordinal) ||
            exception.Message.Contains(
                "ล็อก",
                StringComparison.Ordinal))
            return new(
                AccountEmailChangeFailureReason.Locked,
                "กรอกรหัสไม่ถูกต้องครบจำนวนแล้ว กรุณาขอรหัสใหม่");
        return new(
            AccountEmailChangeFailureReason.Invalid,
            "รหัสไม่ถูกต้อง ลองตรวจสอบแล้วกรอกอีกครั้ง");
    }

    private static bool IsNetwork(Exception exception) =>
        exception is HttpRequestException or
            TimeoutException or
            TaskCanceledException;

    private static bool IsSender(Exception exception) =>
        exception.Message.Contains(
            "ยังส่งอีเมลไม่ได้",
            StringComparison.Ordinal);

    private static AccountEmailChangeError Network() =>
        new(
            AccountEmailChangeFailureReason.Network,
            "เชื่อมต่อไม่สำเร็จ กรุณาลองอีกครั้ง");
}

internal sealed record AccountEmailChangeError(
    AccountEmailChangeFailureReason Reason,
    string Message);
