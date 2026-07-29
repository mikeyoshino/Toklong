using System.Net;

namespace Toklong.Mobile.Core;

internal enum AccountEmailChangeErrorKind
{
    Invalid,
    Expired,
    Locked,
    Network,
    Sender,
    Superseded,
    Cooldown
}

internal sealed record AccountEmailChangeError(
    AccountEmailChangeErrorKind Kind,
    AccountEmailChangeFailureReason Reason,
    string Message,
    TimeSpan? RetryAfter = null)
{
    public bool RequiresNewRequest =>
        Kind is
            AccountEmailChangeErrorKind.Expired or
            AccountEmailChangeErrorKind.Locked or
            AccountEmailChangeErrorKind.Superseded;
}

internal static class AccountEmailChangeErrorPresentation
{
    public static AccountEmailChangeError ForRequest(
        Exception exception)
    {
        if (RateLimit(
                exception,
                "ลองขอรหัสอีกครั้งใน") is { } cooldown)
            return cooldown;
        if (IsNetwork(exception))
            return Network();
        if (IsSender(exception))
            return Sender();
        if (Contains(
                exception,
                "อีเมลปัจจุบัน"))
        {
            return Error(
                AccountEmailChangeErrorKind.Invalid,
                AccountEmailChangeFailureReason.Invalid,
                "อีเมลนี้เป็นอีเมลปัจจุบันของคุณแล้ว");
        }

        return Error(
            AccountEmailChangeErrorKind.Invalid,
            AccountEmailChangeFailureReason.Invalid,
            "เปลี่ยนอีเมลไม่สำเร็จ กรุณาตรวจสอบแล้วลองอีกครั้ง");
    }

    public static AccountEmailChangeError ForResend(
        Exception exception)
    {
        if (RateLimit(
                exception,
                "ส่งรหัสใหม่ได้ในอีก") is { } cooldown)
            return cooldown;
        if (IsNetwork(exception))
            return Network();
        if (IsSender(exception))
            return Sender();
        if (Contains(
                exception,
                "กรุณารอสักครู่ก่อนส่งรหัสอีกครั้ง"))
        {
            return Error(
                AccountEmailChangeErrorKind.Cooldown,
                AccountEmailChangeFailureReason.Invalid,
                "กรุณารอสักครู่ก่อนส่งรหัสอีกครั้ง");
        }
        if (IsExpired(exception))
            return Expired();
        if (IsLocked(exception))
            return Locked();
        if (IsSuperseded(exception))
            return Superseded(exception);

        return Error(
            AccountEmailChangeErrorKind.Invalid,
            AccountEmailChangeFailureReason.Invalid,
            "ส่งรหัสใหม่ไม่สำเร็จ กรุณาลองอีกครั้ง");
    }

    public static AccountEmailChangeError ForVerification(
        Exception exception)
    {
        if (RateLimit(
                exception,
                "ลองยืนยันอีกครั้งใน") is { } cooldown)
            return cooldown;
        if (IsNetwork(exception))
            return Network();
        if (IsSender(exception))
            return Sender();
        if (IsExpired(exception))
            return Expired();
        if (IsLocked(exception))
            return Locked();
        if (IsSuperseded(exception))
            return Superseded(exception);
        if (Contains(
                exception,
                "รหัสไม่ถูกต้อง"))
        {
            return Error(
                AccountEmailChangeErrorKind.Invalid,
                AccountEmailChangeFailureReason.Invalid,
                "รหัสไม่ถูกต้อง ลองตรวจสอบแล้วกรอกอีกครั้ง");
        }

        return Error(
            AccountEmailChangeErrorKind.Invalid,
            AccountEmailChangeFailureReason.Invalid,
            "ยืนยันอีเมลไม่สำเร็จ กรุณาลองอีกครั้ง");
    }

    private static bool IsNetwork(Exception exception) =>
        exception is HttpRequestException or
            TimeoutException or
            TaskCanceledException;

    private static bool IsSender(Exception exception) =>
        Contains(exception, "ยังส่งอีเมลไม่ได้");

    private static bool IsExpired(Exception exception) =>
        Contains(exception, "หมดอายุ");

    private static bool IsLocked(Exception exception) =>
        Contains(exception, "ครบจำนวน") ||
        Contains(exception, "ล็อก");

    private static bool IsSuperseded(Exception exception) =>
        Contains(exception, "มีการส่งรหัสใหม่แล้ว") ||
        Contains(exception, "ไม่พบคำขอเปลี่ยนอีเมล");

    private static bool Contains(
        Exception exception,
        string value) =>
        exception.Message.Contains(
            value,
            StringComparison.Ordinal);

    private static AccountEmailChangeError? RateLimit(
        Exception exception,
        string action)
    {
        if (exception is not MobileApiRequestException
            {
                StatusCode: HttpStatusCode.TooManyRequests
            } rateLimited)
        {
            return null;
        }

        var seconds = Math.Max(
            1,
            (int)Math.Ceiling(
                (rateLimited.RetryAfter ??
                 TimeSpan.FromSeconds(1)).TotalSeconds));
        return new AccountEmailChangeError(
            AccountEmailChangeErrorKind.Cooldown,
            AccountEmailChangeFailureReason.Invalid,
            $"{action} {seconds} วินาที",
            TimeSpan.FromSeconds(seconds));
    }

    private static AccountEmailChangeError Network() =>
        Error(
            AccountEmailChangeErrorKind.Network,
            AccountEmailChangeFailureReason.Network,
            "เชื่อมต่อไม่สำเร็จ กรุณาลองอีกครั้ง");

    private static AccountEmailChangeError Sender() =>
        Error(
            AccountEmailChangeErrorKind.Sender,
            AccountEmailChangeFailureReason.Sender,
            "ยังส่งอีเมลไม่ได้ กรุณาลองอีกครั้ง");

    private static AccountEmailChangeError Expired() =>
        Error(
            AccountEmailChangeErrorKind.Expired,
            AccountEmailChangeFailureReason.Expired,
            "รหัสหมดอายุแล้ว กรุณาเริ่มเปลี่ยนอีเมลใหม่");

    private static AccountEmailChangeError Locked() =>
        Error(
            AccountEmailChangeErrorKind.Locked,
            AccountEmailChangeFailureReason.Locked,
            "กรอกรหัสไม่ถูกต้องครบจำนวนแล้ว กรุณาเริ่มเปลี่ยนอีเมลใหม่");

    private static AccountEmailChangeError Superseded(
        Exception exception) =>
        Error(
            AccountEmailChangeErrorKind.Superseded,
            AccountEmailChangeFailureReason.Invalid,
            Contains(exception, "มีการส่งรหัสใหม่แล้ว")
                ? "มีการส่งรหัสใหม่แล้ว กรุณาใช้รหัสล่าสุด"
                : "คำขอเปลี่ยนอีเมลนี้ใช้ไม่ได้แล้ว กรุณาเริ่มใหม่");

    private static AccountEmailChangeError Error(
        AccountEmailChangeErrorKind kind,
        AccountEmailChangeFailureReason reason,
        string message) =>
        new(kind, reason, message);
}
