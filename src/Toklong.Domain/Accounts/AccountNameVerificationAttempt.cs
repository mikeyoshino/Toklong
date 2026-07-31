using Toklong.Domain.Common;

namespace Toklong.Domain.Accounts;

public enum AccountNameVerificationAttemptOutcome
{
    Verified,
    Incorrect,
    Locked,
    Expired
}

public sealed class AccountNameVerificationAttempt
{
    private const int MaximumAttempts = 5;

    private AccountNameVerificationAttempt() { }

    public AccountNameVerificationAttempt(
        Guid id,
        Guid? buyerId,
        Guid? sellerId,
        Guid sessionId,
        Guid challengeId,
        string idempotencyKey,
        string submittedDigest,
        AccountNameVerificationAttemptOutcome outcome,
        int remainingAttempts,
        DateTimeOffset createdAt,
        DateTimeOffset? completedAt)
    {
        if (id == Guid.Empty)
            throw new DomainException("รหัสบันทึกการยืนยันไม่ถูกต้อง");
        if (!buyerId.HasValue && !sellerId.HasValue)
            throw new DomainException("บัญชีผู้ใช้ไม่ถูกต้อง");
        if (buyerId == Guid.Empty || sellerId == Guid.Empty)
            throw new DomainException("บัญชีผู้ใช้ไม่ถูกต้อง");
        if (sessionId == Guid.Empty)
            throw new DomainException("เซสชันไม่ถูกต้อง");
        if (challengeId == Guid.Empty)
            throw new DomainException("รหัสคำขอเปลี่ยนชื่อไม่ถูกต้อง");
        if (remainingAttempts is < 0 or > MaximumAttempts)
            throw new DomainException("จำนวนครั้งที่ยืนยันได้ไม่ถูกต้อง");
        if (outcome == AccountNameVerificationAttemptOutcome.Verified &&
            completedAt is null)
            throw new DomainException("เวลายืนยันสำเร็จไม่ถูกต้อง");
        if (outcome != AccountNameVerificationAttemptOutcome.Verified &&
            completedAt is not null)
            throw new DomainException("เวลายืนยันสำเร็จไม่ถูกต้อง");

        Id = id;
        BuyerId = buyerId;
        SellerId = sellerId;
        SessionId = sessionId;
        ChallengeId = challengeId;
        IdempotencyKey = NormalizeIdempotencyKey(idempotencyKey);
        SubmittedDigest = NormalizeDigest(submittedDigest);
        Outcome = outcome;
        RemainingAttempts = remainingAttempts;
        CreatedAt = createdAt;
        CompletedAt = completedAt;
    }

    public Guid Id { get; private set; }
    public Guid? BuyerId { get; private set; }
    public Guid? SellerId { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid ChallengeId { get; private set; }
    public string IdempotencyKey { get; private set; } = "";
    public string SubmittedDigest { get; private set; } = "";
    public AccountNameVerificationAttemptOutcome Outcome { get; private set; }
    public int RemainingAttempts { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    private static string NormalizeDigest(string value)
    {
        var clean = (value ?? "").Trim();
        if (clean.Length != 64 ||
            clean.Any(character => !Uri.IsHexDigit(character)))
            throw new DomainException("ข้อมูลอ้างอิงรหัสยืนยันไม่ถูกต้อง");
        return clean.ToLowerInvariant();
    }

    private static string NormalizeIdempotencyKey(string value)
    {
        var clean = (value ?? "").Trim();
        if (clean.Length != 32 ||
            !Guid.TryParseExact(clean, "N", out var parsed))
            throw new DomainException("รหัสคำขอไม่ถูกต้อง");
        return parsed.ToString("N");
    }
}
