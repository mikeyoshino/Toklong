using System.Security.Cryptography;
using Toklong.Domain.Common;

namespace Toklong.Domain.Buyers;

public enum BuyerEmailChangeStatus
{
    PendingSend,
    Active,
    Verified,
    Expired,
    Locked,
    Superseded,
    SendFailed
}

public enum BuyerEmailVerificationOutcome
{
    Verified,
    ExactReplay,
    Incorrect,
    Locked
}

public sealed class BuyerEmailChangeChallenge
{
    private const int MaximumIncorrectAttempts = 5;

    private BuyerEmailChangeChallenge() { }

    public Guid Id { get; private set; }
    public Guid BuyerId { get; private set; }
    public string PendingEmail { get; private set; } = "";
    public string MaskedPendingEmail { get; private set; } = "";
    public string CodeDigest { get; private set; } = "";
    public string RequestIdempotencyKey { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset ResendAvailableAt { get; private set; }
    public DateTimeOffset? SendAcceptedAt { get; private set; }
    public DateTimeOffset? SendFailedAt { get; private set; }
    public DateTimeOffset? VerifiedAt { get; private set; }
    public DateTimeOffset? SupersededAt { get; private set; }
    public string? VerificationIdempotencyKey { get; private set; }
    public int IncorrectAttempts { get; private set; }
    public int RemainingAttempts =>
        Math.Max(0, MaximumIncorrectAttempts - IncorrectAttempts);
    public BuyerEmailChangeStatus Status { get; private set; }
    public long Version { get; private set; }

    public static BuyerEmailChangeChallenge Create(
        Guid id,
        Guid buyerId,
        string pendingEmail,
        string maskedPendingEmail,
        string codeDigest,
        string requestIdempotencyKey,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
            throw new DomainException("รหัสการยืนยันอีเมลไม่ถูกต้อง");
        if (buyerId == Guid.Empty)
            throw new DomainException("บัญชีผู้ใช้ไม่ถูกต้อง");

        var normalizedPendingEmail = BuyerAccount.NormalizeEmail(pendingEmail);

        return new BuyerEmailChangeChallenge
        {
            Id = id,
            BuyerId = buyerId,
            PendingEmail = normalizedPendingEmail,
            MaskedPendingEmail = BuyerEmailChangeMask.ValidateForPendingEmail(
                normalizedPendingEmail,
                maskedPendingEmail),
            CodeDigest = ValidDigest(codeDigest),
            RequestIdempotencyKey = NormalizedIdempotencyKey(requestIdempotencyKey),
            CreatedAt = createdAt,
            ExpiresAt = createdAt.AddMinutes(10),
            ResendAvailableAt = createdAt.AddSeconds(60),
            Status = BuyerEmailChangeStatus.PendingSend
        };
    }

    public void MarkSendAccepted(DateTimeOffset acceptedAt)
    {
        EnsureStatus(BuyerEmailChangeStatus.PendingSend);
        SendAcceptedAt = acceptedAt;
        Status = BuyerEmailChangeStatus.Active;
        Version++;
    }

    public void MarkSendFailed(DateTimeOffset failedAt)
    {
        EnsureStatus(BuyerEmailChangeStatus.PendingSend);
        SendFailedAt = failedAt;
        Status = BuyerEmailChangeStatus.SendFailed;
        Version++;
    }

    public void EnsureCanResend(DateTimeOffset now)
    {
        EnsureStatus(BuyerEmailChangeStatus.Active);
        if (ExpiresAt <= now)
            throw new DomainException("รหัสยืนยันอีเมลหมดอายุแล้ว");
        if (now < ResendAvailableAt)
            throw new DomainException("กรุณารอก่อนขอรหัสยืนยันอีกครั้ง");
    }

    public void Supersede(DateTimeOffset supersededAt)
    {
        EnsureStatus(
            BuyerEmailChangeStatus.PendingSend,
            BuyerEmailChangeStatus.Active);
        SupersededAt = supersededAt;
        Status = BuyerEmailChangeStatus.Superseded;
        Version++;
    }

    public BuyerEmailVerificationOutcome Verify(
        string submittedDigest,
        string verificationIdempotencyKey,
        DateTimeOffset now)
    {
        var normalizedSubmittedDigest = ValidDigest(submittedDigest);
        var normalizedVerificationKey =
            NormalizedIdempotencyKey(verificationIdempotencyKey);

        if (Status == BuyerEmailChangeStatus.Verified)
        {
            return VerificationIdempotencyKey == normalizedVerificationKey &&
                   DigestsMatch(CodeDigest, normalizedSubmittedDigest)
                ? BuyerEmailVerificationOutcome.ExactReplay
                : throw new DomainException("รหัสยืนยันอีเมลนี้ถูกใช้แล้ว");
        }

        EnsureStatus(BuyerEmailChangeStatus.Active);
        if (ExpiresAt <= now)
        {
            Status = BuyerEmailChangeStatus.Expired;
            Version++;
            throw new DomainException("รหัสยืนยันอีเมลหมดอายุแล้ว");
        }

        if (DigestsMatch(CodeDigest, normalizedSubmittedDigest))
        {
            VerificationIdempotencyKey = normalizedVerificationKey;
            VerifiedAt = now;
            Status = BuyerEmailChangeStatus.Verified;
            Version++;
            return BuyerEmailVerificationOutcome.Verified;
        }

        IncorrectAttempts++;
        Version++;
        if (IncorrectAttempts == MaximumIncorrectAttempts)
        {
            Status = BuyerEmailChangeStatus.Locked;
            return BuyerEmailVerificationOutcome.Locked;
        }

        return BuyerEmailVerificationOutcome.Incorrect;
    }

    private void EnsureStatus(params BuyerEmailChangeStatus[] allowedStatuses)
    {
        if (!allowedStatuses.Contains(Status))
            throw new DomainException("รหัสยืนยันอีเมลไม่อยู่ในสถานะที่ใช้งานได้");
    }

    private static bool DigestsMatch(string expected, string submitted)
    {
        var expectedBytes = Convert.FromHexString(expected);
        var submittedBytes = Convert.FromHexString(submitted);
        return expectedBytes.Length == submittedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, submittedBytes);
    }

    private static string ValidDigest(string value)
    {
        var clean = (value ?? "").Trim();
        if (clean.Length != 64 || clean.Any(character => !Uri.IsHexDigit(character)))
            throw new DomainException("รหัสยืนยันอีเมลไม่ถูกต้อง");
        return clean;
    }

    private static string NormalizedIdempotencyKey(string value)
    {
        var clean = (value ?? "").Trim();
        if (clean.Length != 32 || !Guid.TryParseExact(clean, "N", out var parsed))
            throw new DomainException("รหัสคำขอไม่ถูกต้อง");
        return parsed.ToString("N");
    }

}

internal static class BuyerEmailChangeMask
{
    public static string ValidateForPendingEmail(
        string pendingEmail,
        string maskedEmail)
    {
        var clean = Validate(maskedEmail);
        var at = clean.IndexOf('@');
        var pendingAt = pendingEmail.IndexOf('@');
        var local = clean[..at];
        var pendingLocal = pendingEmail[..pendingAt];
        var maskStart = local.IndexOfAny(['*', '•']);
        if (pendingLocal.Length < maskStart ||
            !string.Equals(
                pendingEmail[(pendingAt + 1)..],
                clean[(at + 1)..],
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                pendingLocal[..maskStart],
                local[..maskStart],
                StringComparison.OrdinalIgnoreCase))
            throw new DomainException("อีเมลที่ปกปิดแล้วไม่ถูกต้อง");

        return clean;
    }

    public static string Validate(string value)
    {
        var clean = (value ?? "").Trim();
        var at = clean.IndexOf('@');
        if (clean.Length is 0 or > 254 ||
            at is < 2 or >= 254 ||
            clean.IndexOf('@', at + 1) >= 0)
            throw new DomainException("อีเมลที่ปกปิดแล้วไม่ถูกต้อง");

        var local = clean[..at];
        var domain = clean[(at + 1)..];
        var maskStart = local.IndexOfAny(['*', '•']);
        if (Uri.CheckHostName(domain) == UriHostNameType.Unknown ||
            maskStart is < 1 or > 2 ||
            local[maskStart..].Any(character => character != local[maskStart]))
            throw new DomainException("อีเมลที่ปกปิดแล้วไม่ถูกต้อง");

        return clean;
    }
}
