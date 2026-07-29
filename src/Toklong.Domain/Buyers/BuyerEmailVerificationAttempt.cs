using Toklong.Domain.Common;

namespace Toklong.Domain.Buyers;

public enum BuyerEmailVerificationAttemptOutcome
{
    Verified,
    Incorrect,
    Locked,
    Expired
}

public sealed class BuyerEmailVerificationAttempt
{
    private const int MaximumAttempts = 5;

    private BuyerEmailVerificationAttempt() { }

    public BuyerEmailVerificationAttempt(
        Guid id,
        Guid buyerId,
        Guid challengeId,
        string idempotencyKey,
        string submittedDigest,
        BuyerEmailVerificationAttemptOutcome outcome,
        int remainingAttempts,
        DateTimeOffset createdAt,
        DateTimeOffset? completedAt)
    {
        if (id == Guid.Empty)
            throw new DomainException(
                "รหัสบันทึกการยืนยันอีเมลไม่ถูกต้อง");
        if (buyerId == Guid.Empty)
            throw new DomainException("บัญชีผู้ใช้ไม่ถูกต้อง");
        if (challengeId == Guid.Empty)
            throw new DomainException(
                "รหัสการยืนยันอีเมลไม่ถูกต้อง");
        if (remainingAttempts is < 0 or > MaximumAttempts)
            throw new DomainException(
                "จำนวนครั้งที่ยืนยันอีเมลได้ไม่ถูกต้อง");
        if (outcome == BuyerEmailVerificationAttemptOutcome.Verified &&
            completedAt is null)
            throw new DomainException(
                "เวลายืนยันอีเมลสำเร็จไม่ถูกต้อง");
        if (outcome != BuyerEmailVerificationAttemptOutcome.Verified &&
            completedAt is not null)
            throw new DomainException(
                "เวลายืนยันอีเมลสำเร็จไม่ถูกต้อง");

        Id = id;
        BuyerId = buyerId;
        ChallengeId = challengeId;
        IdempotencyKey = NormalizeIdempotencyKey(idempotencyKey);
        SubmittedDigest = ValidDigest(submittedDigest);
        Outcome = outcome;
        RemainingAttempts = remainingAttempts;
        CreatedAt = createdAt;
        CompletedAt = completedAt;
    }

    public Guid Id { get; private set; }
    public Guid BuyerId { get; private set; }
    public Guid ChallengeId { get; private set; }
    public string IdempotencyKey { get; private set; } = "";
    public string SubmittedDigest { get; private set; } = "";
    public BuyerEmailVerificationAttemptOutcome Outcome
    {
        get;
        private set;
    }
    public int RemainingAttempts { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    private static string ValidDigest(string value)
    {
        var clean = (value ?? "").Trim();
        if (clean.Length != 64 ||
            clean.Any(character => !Uri.IsHexDigit(character)))
            throw new DomainException(
                "ข้อมูลอ้างอิงรหัสยืนยันอีเมลไม่ถูกต้อง");
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
