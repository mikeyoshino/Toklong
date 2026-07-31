using System.Net;
using System.Text;

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

public sealed class AccountNameChangeOperationState
{
    private readonly object sync = new();
    private readonly HashSet<string> issuedKeys = new(StringComparer.Ordinal);
    private OperationKey<RequestAssociation>? request;
    private OperationKey<ResendAssociation>? resend;
    private OperationKey<VerificationAssociation>? verification;

    public AccountNameChangeOperationState(AuthenticatedSessionBoundary session)
    {
        session.ResetRequested += (_, _) => Reset();
    }

    public string GetRequestKey(string firstName, string lastName)
    {
        lock (sync)
        {
            var association = new RequestAssociation(
                Normalize(firstName),
                Normalize(lastName));
            return GetOrCreate(ref request, association);
        }
    }

    public string GetResendKey(Guid challengeId)
    {
        lock (sync)
        {
            return GetOrCreate(
                ref resend,
                new ResendAssociation(challengeId));
        }
    }

    public string GetVerificationKey(Guid challengeId, string code)
    {
        lock (sync)
        {
            return GetOrCreate(
                ref verification,
                new VerificationAssociation(
                    challengeId,
                    Normalize(code)));
        }
    }

    public void RecordRequestSuccess() => Clear(ref request);

    public void RecordRequestFailure(Exception exception) =>
        ClearIfAuthoritative(ref request, exception);

    public void RecordResendSuccess() => Clear(ref resend);

    public void RecordResendFailure(Exception exception) =>
        ClearIfAuthoritative(ref resend, exception);

    public void RecordVerificationSuccess() => Clear(ref verification);

    public void RecordVerificationFailure(Exception exception) =>
        ClearIfAuthoritative(ref verification, exception);

    public void Reset()
    {
        lock (sync)
        {
            request = null;
            resend = null;
            verification = null;
            issuedKeys.Clear();
        }
    }

    private string GetOrCreate<TAssociation>(
        ref OperationKey<TAssociation>? operation,
        TAssociation association)
        where TAssociation : notnull
    {
        if (operation is { } current &&
            EqualityComparer<TAssociation>.Default.Equals(
                current.Association,
                association))
            return current.IdempotencyKey;

        var key = NewIdempotencyKey();
        operation = new OperationKey<TAssociation>(association, key);
        return key;
    }

    private void Clear<TAssociation>(ref OperationKey<TAssociation>? operation)
        where TAssociation : notnull
    {
        lock (sync)
        {
            operation = null;
        }
    }

    private void ClearIfAuthoritative<TAssociation>(
        ref OperationKey<TAssociation>? operation,
        Exception exception)
        where TAssociation : notnull
    {
        if (MayRetryWithSameKey(exception))
            return;

        Clear(ref operation);
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
        exception is HttpRequestException or TimeoutException or TaskCanceledException ||
        exception is not MobileApiRequestException { Code: { } code } ||
        string.Equals(
            code,
            "name_change_provider_outcome_unknown",
            StringComparison.Ordinal);

    private static string Normalize(string value) =>
        value.Trim().Normalize(NormalizationForm.FormC);

    private sealed record OperationKey<TAssociation>(
        TAssociation Association,
        string IdempotencyKey)
        where TAssociation : notnull;

    private sealed record RequestAssociation(string FirstName, string LastName);
    private sealed record ResendAssociation(Guid ChallengeId);
    private sealed record VerificationAssociation(Guid ChallengeId, string Code);
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
