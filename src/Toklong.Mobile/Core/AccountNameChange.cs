using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;

namespace Toklong.Mobile.Core;

public sealed record AccountNameChangeEligibility(
    bool CanChange,
    DateTimeOffset? NextAllowedAt);

public sealed record PendingAccountNameChange(
    Guid ChallengeId,
    string MaskedPhoneNumber,
    string FirstName,
    string LastName,
    DateTimeOffset ExpiresAt,
    DateTimeOffset ResendAvailableAt,
    int RemainingAttempts);

public sealed record VerifiedAccountNameChange(
    string FirstName,
    string LastName,
    string DisplayName,
    DateTimeOffset CompletedAt);

public sealed record AccountNameChangeBlockedNotice(
    DateTimeOffset NextAllowedAt);

public sealed record AccountNameChangeModalNotice(
    string Title,
    string Message,
    string AcceptText);

public static class AccountNameChangeModalPresenter
{
    private static readonly CultureInfo ThaiCulture =
        CultureInfo.GetCultureInfo("th-TH");
    private static readonly TimeZoneInfo Bangkok =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok");

    public static AccountNameChangeModalNotice Cooldown(
        AccountNameChangeBlockedNotice notice) =>
        new(
            "ยังเปลี่ยนชื่อไม่ได้",
            "เพื่อความปลอดภัย ชื่อบัญชีเปลี่ยนได้ทุก 2 เดือน\n\n" +
            "คุณจะเปลี่ยนได้อีกครั้งวันที่ " +
            FormatBangkok(notice.NextAllowedAt),
            "เข้าใจแล้ว");

    public static AccountNameChangeModalNotice SendLimit(
        AccountNameChangeErrorNotice notice) =>
        new(
            "ขอรหัสยืนยันไม่ได้ในตอนนี้",
            notice.NextAllowedAt is { } nextAllowedAt
                ? "คุณขอรหัสยืนยันครบจำนวนแล้ว กรุณาลองใหม่วันที่ " +
                  FormatBangkok(nextAllowedAt)
                : notice.RetryAfter is { } retryAfter
                    ? $"คุณขอรหัสยืนยันบ่อยเกินไป กรุณาลองใหม่ใน {Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))} วินาที"
                    : "คุณขอรหัสยืนยันครบจำนวนแล้ว กรุณาลองใหม่ภายหลัง",
            "เข้าใจแล้ว");

    private static string FormatBangkok(DateTimeOffset value) =>
        TimeZoneInfo.ConvertTime(value, Bangkok)
            .ToString("d MMM yyyy · HH:mm 'น.'", ThaiCulture);
}

public enum AccountNameChangeErrorTarget
{
    FirstNameInput,
    LastNameInput,
    RequestAction,
    CodeInput,
    VerificationAction,
    ResendAction,
    NewRequestAction,
    AccountReturnAction,
    BlockedAction
}

public enum AccountNameChangeErrorKind
{
    Invalid,
    Cooldown,
    SendLimit,
    RateLimited,
    Unchanged,
    Expired,
    Locked,
    Unavailable,
    Network,
    Missing
}

public sealed record AccountNameChangeErrorNotice(
    AccountNameChangeErrorKind Kind,
    AccountNameChangeErrorTarget Target,
    string Message,
    TimeSpan? RetryAfter = null,
    int? RemainingAttempts = null,
    DateTimeOffset? NextAllowedAt = null,
    bool RetryWithSameIdempotencyKey = false);

public static class AccountNameChangeErrorPresentation
{
    public static AccountNameChangeBlockedNotice? BlockedNotice(
        AccountNameChangeEligibility eligibility) =>
        !eligibility.CanChange && eligibility.NextAllowedAt is { } nextAllowedAt
            ? new AccountNameChangeBlockedNotice(nextAllowedAt)
            : null;

    public static AccountNameChangeErrorNotice ForRequest(Exception exception) =>
        Present(exception, AccountNameChangeErrorTarget.RequestAction);

    public static AccountNameChangeErrorNotice ForResend(Exception exception) =>
        Present(exception, AccountNameChangeErrorTarget.ResendAction);

    public static AccountNameChangeErrorNotice ForVerification(Exception exception) =>
        Present(exception, AccountNameChangeErrorTarget.VerificationAction);

    private static AccountNameChangeErrorNotice Present(
        Exception exception,
        AccountNameChangeErrorTarget defaultTarget)
    {
        if (exception is HttpRequestException or TimeoutException or TaskCanceledException)
            return Notice(
                AccountNameChangeErrorKind.Network,
                defaultTarget,
                "เชื่อมต่อไม่สำเร็จ กรุณาลองอีกครั้ง",
                retryWithSameIdempotencyKey: true);

        if (exception is not MobileApiRequestException api)
            return Notice(
                AccountNameChangeErrorKind.Invalid,
                defaultTarget,
                "เปลี่ยนชื่อไม่สำเร็จ กรุณาลองอีกครั้ง");

        var notice = api.Code switch
        {
            "name_change_cooldown" =>
                Notice(
                    AccountNameChangeErrorKind.Cooldown,
                    AccountNameChangeErrorTarget.BlockedAction,
                    "ยังเปลี่ยนชื่อไม่ได้ กรุณาลองใหม่เมื่อถึงเวลาที่แจ้ง"),
            "name_change_first_name_invalid" =>
                Notice(
                    AccountNameChangeErrorKind.Invalid,
                    AccountNameChangeErrorTarget.FirstNameInput,
                    "กรุณาตรวจสอบชื่อ"),
            "name_change_last_name_invalid" =>
                Notice(
                    AccountNameChangeErrorKind.Invalid,
                    AccountNameChangeErrorTarget.LastNameInput,
                    "กรุณาตรวจสอบนามสกุล"),
            "name_change_unchanged" =>
                Notice(
                    AccountNameChangeErrorKind.Unchanged,
                    defaultTarget,
                    "ชื่อนี้เป็นชื่อปัจจุบันของคุณแล้ว"),
            "name_change_code_incorrect" =>
                Notice(
                    AccountNameChangeErrorKind.Invalid,
                    AccountNameChangeErrorTarget.CodeInput,
                    "รหัสยืนยันไม่ถูกต้อง ลองตรวจสอบแล้วกรอกอีกครั้ง",
                    api.RetryAfter,
                    api.RemainingAttempts),
            "name_change_code_invalid" =>
                Notice(
                    AccountNameChangeErrorKind.Invalid,
                    AccountNameChangeErrorTarget.CodeInput,
                    "กรอกรหัสยืนยัน 6 หลัก"),
            "name_change_expired" =>
                Notice(
                    AccountNameChangeErrorKind.Expired,
                    AccountNameChangeErrorTarget.NewRequestAction,
                    "รหัสยืนยันหมดอายุแล้ว กรุณาขอรหัสใหม่"),
            "name_change_locked" =>
                Notice(
                    AccountNameChangeErrorKind.Locked,
                    AccountNameChangeErrorTarget.NewRequestAction,
                    "กรอกรหัสไม่ถูกต้องครบจำนวนแล้ว กรุณาขอรหัสใหม่"),
            "name_change_challenge_unavailable" or "name_change_challenge_inactive" =>
                Notice(
                    AccountNameChangeErrorKind.Missing,
                    AccountNameChangeErrorTarget.AccountReturnAction,
                    "คำขอเปลี่ยนชื่อนี้ใช้ไม่ได้แล้ว กรุณากลับไปหน้าบัญชี"),
            "name_change_provider_unavailable" =>
                Notice(
                    AccountNameChangeErrorKind.Unavailable,
                    defaultTarget,
                    "บริการยืนยันชื่อยังไม่พร้อมใช้งาน กรุณาลองใหม่ภายหลัง"),
            "name_change_provider_outcome_unknown" =>
                Notice(
                    AccountNameChangeErrorKind.Unavailable,
                    defaultTarget,
                    "กำลังตรวจสอบผลการยืนยัน กรุณาลองอีกครั้งด้วยคำขอเดิม",
                    api.RetryAfter,
                    retryWithSameIdempotencyKey: true),
            "name_change_provider_throttled" or "name_change_resend_cooldown" =>
                Notice(
                    AccountNameChangeErrorKind.Cooldown,
                    defaultTarget,
                    "กรุณารอก่อนขอรหัสยืนยันอีกครั้ง"),
            "name_change_send_limit" =>
                Notice(
                    AccountNameChangeErrorKind.SendLimit,
                    defaultTarget,
                    "ขอรหัสยืนยันครบจำนวนแล้ว กรุณาลองใหม่ภายหลัง"),
            "name_change_rate_limited" =>
                Notice(
                    AccountNameChangeErrorKind.RateLimited,
                    defaultTarget,
                    "มีการทำรายการบ่อยเกินไป กรุณารอสักครู่ก่อนลองอีกครั้ง"),
            "name_change_idempotency_invalid" =>
                Notice(
                    AccountNameChangeErrorKind.Invalid,
                    defaultTarget,
                    "คำขอไม่ถูกต้อง กรุณาลองใหม่"),
            "name_change_idempotency_conflict" =>
                Notice(
                    AccountNameChangeErrorKind.Invalid,
                    defaultTarget,
                    "คำขอนี้ไม่ตรงกับข้อมูลเดิม กรุณาลองใหม่"),
            "name_change_invalid_request" =>
                Notice(
                    AccountNameChangeErrorKind.Invalid,
                    defaultTarget,
                    "ไม่สามารถทำรายการเปลี่ยนชื่อได้ กรุณาตรวจสอบข้อมูลแล้วลองใหม่"),
            _ when api.StatusCode == HttpStatusCode.TooManyRequests =>
                Notice(
                    AccountNameChangeErrorKind.Cooldown,
                    defaultTarget,
                    "กรุณารอสักครู่ก่อนลองอีกครั้ง",
                    api.RetryAfter),
            _ => Notice(
                AccountNameChangeErrorKind.Invalid,
                defaultTarget,
                "เปลี่ยนชื่อไม่สำเร็จ กรุณาลองอีกครั้ง")
        };

        return notice with
        {
            RetryAfter = notice.RetryAfter ?? api.RetryAfter,
            RemainingAttempts = notice.RemainingAttempts ?? api.RemainingAttempts,
            NextAllowedAt = notice.NextAllowedAt ?? api.NextAllowedAt
        };
    }

    private static AccountNameChangeErrorNotice Notice(
        AccountNameChangeErrorKind kind,
        AccountNameChangeErrorTarget target,
        string message,
        TimeSpan? retryAfter = null,
        int? remainingAttempts = null,
        DateTimeOffset? nextAllowedAt = null,
        bool retryWithSameIdempotencyKey = false) =>
        new(
            kind,
            target,
            message,
            retryAfter,
            remainingAttempts,
            nextAllowedAt,
            retryWithSameIdempotencyKey);
}

public enum AccountNameChangeOperationKind
{
    Request,
    Resend,
    Verification
}

public sealed class AccountNameChangeOperationLease
{
    internal AccountNameChangeOperationLease(
        AccountNameChangeOperationKind kind,
        string fingerprint,
        string idempotencyKey,
        long slotGeneration,
        long sessionGeneration)
    {
        Kind = kind;
        Fingerprint = fingerprint;
        IdempotencyKey = idempotencyKey;
        SlotGeneration = slotGeneration;
        SessionGeneration = sessionGeneration;
    }

    internal AccountNameChangeOperationKind Kind { get; }
    internal string Fingerprint { get; }
    internal string IdempotencyKey { get; }
    internal long SlotGeneration { get; }
    internal long SessionGeneration { get; }
}

public sealed class AccountNameChangeOperationState
{
    private readonly AuthenticatedSessionBoundary session;
    private readonly object sync = new();
    private readonly HashSet<string> issuedKeys = new(StringComparer.Ordinal);
    private OperationSlot? request;
    private OperationSlot? resend;
    private OperationSlot? verification;
    private long requestGeneration;
    private long resendGeneration;
    private long verificationGeneration;

    public AccountNameChangeOperationState(AuthenticatedSessionBoundary session)
    {
        this.session = session;
        session.ResetRequested += (_, _) => Reset();
    }

    public AccountNameChangeOperationLease BeginRequest(
        string firstName,
        string lastName) =>
        Begin(
            AccountNameChangeOperationKind.Request,
            Fingerprint(
                AccountNameChangeOperationKind.Request,
                Normalize(firstName),
                Normalize(lastName)));

    public AccountNameChangeOperationLease BeginResend(Guid challengeId) =>
        Begin(
            AccountNameChangeOperationKind.Resend,
            Fingerprint(
                AccountNameChangeOperationKind.Resend,
                challengeId.ToString("N")));

    public AccountNameChangeOperationLease BeginVerification(
        Guid challengeId,
        string code) =>
        Begin(
            AccountNameChangeOperationKind.Verification,
            Fingerprint(
                AccountNameChangeOperationKind.Verification,
                challengeId.ToString("N"),
                Normalize(code)));

    public void RecordRequestSuccess(AccountNameChangeOperationLease lease) =>
        Complete(lease, success: true);

    public void RecordRequestFailure(
        AccountNameChangeOperationLease lease,
        Exception exception) =>
        Complete(lease, success: false, exception);

    public void RecordResendSuccess(AccountNameChangeOperationLease lease) =>
        Complete(lease, success: true);

    public void RecordResendFailure(
        AccountNameChangeOperationLease lease,
        Exception exception) =>
        Complete(lease, success: false, exception);

    public void RecordVerificationSuccess(AccountNameChangeOperationLease lease) =>
        Complete(lease, success: true);

    public void RecordVerificationFailure(
        AccountNameChangeOperationLease lease,
        Exception exception) =>
        Complete(lease, success: false, exception);

    public void Reset()
    {
        lock (sync)
        {
            request = null;
            resend = null;
            verification = null;
            requestGeneration++;
            resendGeneration++;
            verificationGeneration++;
            issuedKeys.Clear();
        }
    }

    private AccountNameChangeOperationLease Begin(
        AccountNameChangeOperationKind kind,
        string fingerprint)
    {
        lock (sync)
        {
            ref var slot = ref Slot(kind);
            ref var generation = ref Generation(kind);
            if (slot is null ||
                !string.Equals(
                    slot.Fingerprint,
                    fingerprint,
                    StringComparison.Ordinal))
            {
                slot = new OperationSlot(
                    fingerprint,
                    NewIdempotencyKey(),
                    ++generation);
            }

            return new AccountNameChangeOperationLease(
                kind,
                slot.Fingerprint,
                slot.IdempotencyKey,
                slot.Generation,
                session.Capture());
        }
    }

    private void Complete(
        AccountNameChangeOperationLease lease,
        bool success,
        Exception? exception = null)
    {
        lock (sync)
        {
            if (!IsCurrent(lease))
                return;

            if (!success && exception is not null && MayRetryWithSameKey(exception))
                return;

            ref var slot = ref Slot(lease.Kind);
            slot = null;
            Generation(lease.Kind)++;
        }
    }

    private bool IsCurrent(AccountNameChangeOperationLease lease)
    {
        if (!session.IsCurrent(lease.SessionGeneration))
            return false;

        var slot = Slot(lease.Kind);
        return slot is not null &&
               slot.Generation == lease.SlotGeneration &&
               string.Equals(
                   slot.Fingerprint,
                   lease.Fingerprint,
                   StringComparison.Ordinal) &&
               string.Equals(
                   slot.IdempotencyKey,
                   lease.IdempotencyKey,
                   StringComparison.Ordinal);
    }

    private ref OperationSlot? Slot(AccountNameChangeOperationKind kind)
    {
        switch (kind)
        {
            case AccountNameChangeOperationKind.Request:
                return ref request;
            case AccountNameChangeOperationKind.Resend:
                return ref resend;
            case AccountNameChangeOperationKind.Verification:
                return ref verification;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private ref long Generation(AccountNameChangeOperationKind kind)
    {
        switch (kind)
        {
            case AccountNameChangeOperationKind.Request:
                return ref requestGeneration;
            case AccountNameChangeOperationKind.Resend:
                return ref resendGeneration;
            case AccountNameChangeOperationKind.Verification:
                return ref verificationGeneration;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private string NewIdempotencyKey()
    {
        string key;
        do
        {
            key = Guid.NewGuid().ToString("N");
        }
        while (!issuedKeys.Add(key));
        return key;
    }

    private static bool MayRetryWithSameKey(Exception exception) =>
        exception is HttpRequestException or TimeoutException or OperationCanceledException ||
        exception is MobileApiRequestException
        {
            Code: "name_change_provider_outcome_unknown"
        };

    private static string Fingerprint(
        AccountNameChangeOperationKind kind,
        params string[] values) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(
                $"{kind}\u001f{string.Join("\u001f", values)}")));

    private static string Normalize(string value) =>
        value.Trim().Normalize(NormalizationForm.FormC);

    private sealed record OperationSlot(
        string Fingerprint,
        string IdempotencyKey,
        long Generation);
}

public sealed class AccountNameChangeCompletionState
{
    private readonly AuthenticatedSessionBoundary session;
    private readonly object sync = new();
    private long? completedSessionGeneration;

    public AccountNameChangeCompletionState(AuthenticatedSessionBoundary session)
    {
        this.session = session;
        session.ResetRequested += OnSessionResetRequested;
    }

    public void RecordCompletion(long sessionGeneration)
    {
        lock (sync)
        {
            completedSessionGeneration = session.IsCurrent(sessionGeneration)
                ? sessionGeneration
                : null;
        }
    }

    public bool TryConsume(long sessionGeneration)
    {
        lock (sync)
        {
            if (completedSessionGeneration != sessionGeneration)
                return false;

            completedSessionGeneration = null;
            return session.IsCurrent(sessionGeneration);
        }
    }

    private void OnSessionResetRequested(object? sender, EventArgs eventArgs)
    {
        lock (sync)
        {
            completedSessionGeneration = null;
        }
    }
}
