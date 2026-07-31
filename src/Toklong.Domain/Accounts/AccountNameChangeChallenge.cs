using System.Security.Cryptography;
using System.Text;
using Toklong.Domain.Common;

namespace Toklong.Domain.Accounts;

public enum AccountNameChangeStatus
{
    PendingSend,
    Active,
    Verified,
    Expired,
    Locked,
    Superseded,
    SendFailed
}

public enum AccountNameVerificationOutcome
{
    Verified,
    ExactReplay,
    Incorrect,
    Locked,
    Expired
}

public enum AccountNameChangeOperationKind
{
    InitialRequest,
    Resend
}

public sealed class AccountNameChangeChallenge
{
    private const int MaximumIncorrectAttempts = 5;
    private const int MaximumProviderChallengeLength = 800;
    private const int MaximumFailureCodeLength = 64;
    private const int MaximumFailureMessageLength = 200;
    private static readonly TimeSpan MaximumFailureRetryAfter =
        TimeSpan.FromHours(24);

    private AccountNameChangeChallenge() { }

    public Guid Id { get; private set; }
    public Guid? BuyerId { get; private set; }
    public Guid? SellerId { get; private set; }
    public Guid SessionId { get; private set; }
    public string PhoneNumber { get; private set; } = "";
    public string MaskedPhoneNumber { get; private set; } = "";
    public string PendingFirstName { get; private set; } = "";
    public string PendingLastName { get; private set; } = "";
    public string RequestIdempotencyKey { get; private set; } = "";
    public string ProviderRequestKey { get; private set; } = "";
    public AccountNameChangeOperationKind OperationKind { get; private set; }
    public Guid? SourceChallengeId { get; private set; }
    public string OperationFingerprint { get; private set; } = "";
    public string? ProviderChallengeId { get; private set; }
    public string? VerificationIdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public DateTimeOffset? ResendAvailableAt { get; private set; }
    public DateTimeOffset? SendAcceptedAt { get; private set; }
    public DateTimeOffset? SendFailedAt { get; private set; }
    public string? SendFailureCode { get; private set; }
    public string? SendFailureMessage { get; private set; }
    public long? SendFailureRetryAfterTicks { get; private set; }
    public DateTimeOffset? VerifiedAt { get; private set; }
    public DateTimeOffset? LockedAt { get; private set; }
    public DateTimeOffset? SupersededAt { get; private set; }
    public int IncorrectAttempts { get; private set; }
    public int RemainingAttempts =>
        Math.Max(0, MaximumIncorrectAttempts - IncorrectAttempts);
    public AccountNameChangeStatus Status { get; private set; }
    public long Version { get; private set; }

    public static AccountNameChangeChallenge Create(
        Guid id,
        Guid? buyerId,
        Guid? sellerId,
        Guid sessionId,
        string phoneNumber,
        string maskedPhoneNumber,
        AccountName pendingName,
        string requestIdempotencyKey,
        DateTimeOffset createdAt,
        Guid? sourceChallengeId = null)
    {
        if (id == Guid.Empty)
            throw new DomainException("รหัสคำขอเปลี่ยนชื่อไม่ถูกต้อง");
        if (!buyerId.HasValue && !sellerId.HasValue)
            throw new DomainException("บัญชีผู้ใช้ไม่ถูกต้อง");
        if (buyerId == Guid.Empty || sellerId == Guid.Empty)
            throw new DomainException("บัญชีผู้ใช้ไม่ถูกต้อง");
        if (sessionId == Guid.Empty)
            throw new DomainException("เซสชันไม่ถูกต้อง");
        if (sourceChallengeId == Guid.Empty ||
            sourceChallengeId == id)
            throw new DomainException(
                "รหัสคำขอเปลี่ยนชื่อต้นทางไม่ถูกต้อง");
        ArgumentNullException.ThrowIfNull(pendingName);
        var normalizedPhone = NormalizePhone(phoneNumber);
        var normalizedRequestKey =
            NormalizeIdempotencyKey(requestIdempotencyKey);
        var operationKind = sourceChallengeId.HasValue
            ? AccountNameChangeOperationKind.Resend
            : AccountNameChangeOperationKind.InitialRequest;

        return new AccountNameChangeChallenge
        {
            Id = id,
            BuyerId = buyerId,
            SellerId = sellerId,
            SessionId = sessionId,
            PhoneNumber = normalizedPhone,
            MaskedPhoneNumber = NormalizeMaskedPhone(maskedPhoneNumber),
            PendingFirstName = pendingName.FirstName,
            PendingLastName = pendingName.LastName,
            RequestIdempotencyKey = normalizedRequestKey,
            ProviderRequestKey = normalizedRequestKey,
            OperationKind = operationKind,
            SourceChallengeId = sourceChallengeId,
            OperationFingerprint = Fingerprint(
                operationKind,
                sourceChallengeId,
                normalizedPhone,
                pendingName),
            CreatedAt = createdAt,
            Status = AccountNameChangeStatus.PendingSend
        };
    }

    public void MarkSendAccepted(
        string providerChallengeId,
        DateTimeOffset acceptedAt)
    {
        EnsureStatus(AccountNameChangeStatus.PendingSend);
        ProviderChallengeId = Required(
            providerChallengeId,
            "รหัสอ้างอิงผู้ให้บริการ",
            MaximumProviderChallengeLength);
        SendAcceptedAt = acceptedAt;
        ExpiresAt = acceptedAt.AddMinutes(10);
        ResendAvailableAt = acceptedAt.AddSeconds(60);
        Status = AccountNameChangeStatus.Active;
        Version++;
    }

    public void MarkSendFailed(
        DateTimeOffset failedAt,
        string failureCode = "otp_send_failed",
        string failureMessage =
            "ยังส่งรหัสยืนยันไม่สำเร็จ กรุณาลองอีกครั้ง",
        TimeSpan? retryAfter = null)
    {
        EnsureStatus(AccountNameChangeStatus.PendingSend);
        var normalizedCode = Required(
            failureCode,
            "รหัสผลการส่ง",
            MaximumFailureCodeLength);
        if (normalizedCode.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) ||
                  character is '_' or '-' or '.')))
            throw new DomainException(
                "รหัสผลการส่งมีอักขระที่ไม่รองรับ");
        var normalizedMessage = Required(
            failureMessage,
            "ข้อความผลการส่ง",
            MaximumFailureMessageLength);
        if (normalizedMessage.Any(char.IsControl))
            throw new DomainException(
                "ข้อความผลการส่งมีอักขระที่ไม่รองรับ");
        if (retryAfter is { } delay &&
            (delay <= TimeSpan.Zero ||
             delay > MaximumFailureRetryAfter))
            throw new DomainException(
                "ระยะเวลารอก่อนส่งใหม่ไม่ถูกต้อง");

        SendFailedAt = failedAt;
        SendFailureCode = normalizedCode;
        SendFailureMessage = normalizedMessage;
        SendFailureRetryAfterTicks = retryAfter?.Ticks;
        Status = AccountNameChangeStatus.SendFailed;
        Version++;
    }

    public void Expire(DateTimeOffset now)
    {
        EnsureStatus(AccountNameChangeStatus.Active);
        if (!ExpiresAt.HasValue || ExpiresAt > now)
            throw new DomainException(
                "รหัสยืนยันยังไม่หมดอายุ");
        Status = AccountNameChangeStatus.Expired;
        Version++;
    }

    public void EnsureCanResend(DateTimeOffset now)
    {
        EnsureStatus(AccountNameChangeStatus.Active);
        if (ExpiresAt <= now)
            throw new DomainException("รหัสยืนยันหมดอายุแล้ว");
        if (ResendAvailableAt > now)
            throw new DomainException("กรุณารอก่อนขอรหัสยืนยันอีกครั้ง");
    }

    public void EnsureExactOperationReplay(
        string requestIdempotencyKey,
        Guid? sourceChallengeId,
        AccountName pendingName)
    {
        if (sourceChallengeId == Guid.Empty)
            throw new DomainException(
                "รหัสคำขอเปลี่ยนชื่อต้นทางไม่ถูกต้อง");
        ArgumentNullException.ThrowIfNull(pendingName);
        var normalizedRequestKey =
            NormalizeIdempotencyKey(requestIdempotencyKey);
        var operationKind = sourceChallengeId.HasValue
            ? AccountNameChangeOperationKind.Resend
            : AccountNameChangeOperationKind.InitialRequest;
        var fingerprint = Fingerprint(
            operationKind,
            sourceChallengeId,
            PhoneNumber,
            pendingName);
        if (!string.Equals(
                RequestIdempotencyKey,
                normalizedRequestKey,
                StringComparison.Ordinal) ||
            SourceChallengeId != sourceChallengeId ||
            !string.Equals(
                OperationFingerprint,
                fingerprint,
                StringComparison.Ordinal))
            throw new DomainException(
                "รหัสคำขอนี้ถูกใช้กับข้อมูลอื่นแล้ว");
    }

    public void Supersede(DateTimeOffset supersededAt)
    {
        EnsureStatus(
            AccountNameChangeStatus.PendingSend,
            AccountNameChangeStatus.Active);
        SupersededAt = supersededAt;
        Status = AccountNameChangeStatus.Superseded;
        Version++;
    }

    public AccountNameVerificationOutcome RecordVerification(
        string verificationIdempotencyKey,
        bool providerAccepted,
        DateTimeOffset now)
    {
        var key = NormalizeIdempotencyKey(verificationIdempotencyKey);

        if (Status == AccountNameChangeStatus.Verified)
        {
            return VerificationIdempotencyKey == key
                ? AccountNameVerificationOutcome.ExactReplay
                : throw new DomainException("รหัสยืนยันนี้ถูกใช้แล้ว");
        }

        EnsureStatus(AccountNameChangeStatus.Active);
        if (ExpiresAt <= now)
        {
            Expire(now);
            return AccountNameVerificationOutcome.Expired;
        }

        if (providerAccepted)
        {
            VerificationIdempotencyKey = key;
            VerifiedAt = now;
            Status = AccountNameChangeStatus.Verified;
            Version++;
            return AccountNameVerificationOutcome.Verified;
        }

        IncorrectAttempts++;
        Version++;
        if (IncorrectAttempts >= MaximumIncorrectAttempts)
        {
            LockedAt = now;
            Status = AccountNameChangeStatus.Locked;
            return AccountNameVerificationOutcome.Locked;
        }

        return AccountNameVerificationOutcome.Incorrect;
    }

    private void EnsureStatus(params AccountNameChangeStatus[] allowedStatuses)
    {
        if (!allowedStatuses.Contains(Status))
            throw new DomainException("รหัสยืนยันไม่อยู่ในสถานะที่ใช้งานได้");
    }

    private static string NormalizePhone(string value)
    {
        var clean = (value ?? "").Trim();
        if (clean.Length != 12 ||
            !clean.StartsWith("+66", StringComparison.Ordinal) ||
            clean[3..].Any(character => !char.IsAsciiDigit(character)))
            throw new DomainException("เบอร์โทรศัพท์ไม่ถูกต้อง");
        return clean;
    }

    private static string NormalizeMaskedPhone(string value)
    {
        var clean = Required(value, "เบอร์โทรศัพท์ที่ปกปิดแล้ว", 32);
        if (!clean.Contains('*') && !clean.Contains('•'))
            throw new DomainException("เบอร์โทรศัพท์ที่ปกปิดแล้วไม่ถูกต้อง");
        return clean;
    }

    private static string NormalizeIdempotencyKey(string value)
    {
        var clean = (value ?? "").Trim();
        if (clean.Length != 32 ||
            !Guid.TryParseExact(clean, "N", out var parsed))
            throw new DomainException("รหัสคำขอไม่ถูกต้อง");
        return parsed.ToString("N");
    }

    private static string Required(
        string value,
        string label,
        int maximumLength)
    {
        var clean = (value ?? "").Trim();
        if (clean.Length == 0 || clean.Length > maximumLength)
            throw new DomainException($"{label}ไม่ถูกต้อง");
        return clean;
    }

    private static string Fingerprint(
        AccountNameChangeOperationKind operationKind,
        Guid? sourceChallengeId,
        string phoneNumber,
        AccountName pendingName) =>
        Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        $"{operationKind}|{sourceChallengeId?.ToString("N") ?? "-"}|" +
                        $"{phoneNumber}|{pendingName.FirstName}|{pendingName.LastName}")))
            .ToLowerInvariant();

}
