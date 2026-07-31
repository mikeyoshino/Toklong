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
        string oldName,
        AccountName newName,
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
        ArgumentNullException.ThrowIfNull(newName);

        BuyerId = buyerId;
        SellerId = sellerId;
        SessionId = sessionId;
        ChallengeId = challengeId;
        OldName = Optional(oldName, 120);
        NewName = newName.DisplayName;
        CreatedAt = createdAt;
        Name = Required(name, "ชื่อเหตุการณ์", 100);
        Result = Required(result, "ผลลัพธ์", 100);
    }

    public Guid Id { get; private set; }
    public Guid? BuyerId { get; private set; }
    public Guid? SellerId { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid ChallengeId { get; private set; }
    public string OldName { get; private set; } = "";
    public string NewName { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public string Name { get; private set; } = "";
    public string Result { get; private set; } = "";

    private static string Optional(string value, int maximumLength)
    {
        var clean = (value ?? "").Trim();
        if (clean.Length > maximumLength)
            throw new DomainException("ชื่อเดิมไม่ถูกต้อง");
        return clean;
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
}
