using Toklong.Domain.Common;

namespace Toklong.Domain.Accounts;

public sealed class AccountNameChangeAuditEvent
{
    private AccountNameChangeAuditEvent() { }

    public AccountNameChangeAuditEvent(
        Guid? buyerId,
        Guid? sellerId,
        Guid sessionId,
        Guid challengeId,
        byte[] protectedNameEvidence,
        string protectionVersion,
        DateTimeOffset createdAt,
        string name,
        string result)
    {
        if (!buyerId.HasValue && !sellerId.HasValue)
            throw new DomainException("บัญชีผู้ใช้ไม่ถูกต้อง");
        if (buyerId == Guid.Empty || sellerId == Guid.Empty)
            throw new DomainException("บัญชีผู้ใช้ไม่ถูกต้อง");
        if (sessionId == Guid.Empty)
            throw new DomainException("เซสชันไม่ถูกต้อง");
        if (challengeId == Guid.Empty)
            throw new DomainException("รหัสคำขอเปลี่ยนชื่อไม่ถูกต้อง");
        BuyerId = buyerId;
        SellerId = sellerId;
        SessionId = sessionId;
        ChallengeId = challengeId;
        if (protectedNameEvidence is null ||
            protectedNameEvidence.Length is < 32 or > 4096)
            throw new DomainException(
                "หลักฐานชื่อที่ป้องกันไว้ไม่ถูกต้อง");
        ProtectedNameEvidence = [.. protectedNameEvidence];
        ProtectionVersion = Required(
            protectionVersion,
            "รุ่นการป้องกันหลักฐานชื่อ",
            32);
        CreatedAt = createdAt;
        Name = Required(name, "ชื่อเหตุการณ์", 100);
        Result = Required(result, "ผลลัพธ์", 100);
    }

    public Guid Id { get; private set; }
    public Guid? BuyerId { get; private set; }
    public Guid? SellerId { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid ChallengeId { get; private set; }
    public byte[]? ProtectedNameEvidence { get; private set; }
    public string ProtectionVersion { get; private set; } = "";
    public string? LegacyOldNameDigest { get; private set; }
    public string? LegacyNewNameDigest { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string Name { get; private set; } = "";
    public string Result { get; private set; } = "";

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
}
