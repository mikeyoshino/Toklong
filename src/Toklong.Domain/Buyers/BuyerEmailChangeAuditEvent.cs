using Toklong.Domain.Common;

namespace Toklong.Domain.Buyers;

public sealed class BuyerEmailChangeAuditEvent
{
    private BuyerEmailChangeAuditEvent() { }

    public BuyerEmailChangeAuditEvent(
        Guid buyerId,
        Guid challengeId,
        string name,
        string destinationHash,
        string maskedDestination,
        DateTimeOffset createdAt,
        string result)
    {
        if (buyerId == Guid.Empty)
            throw new DomainException("บัญชีผู้ใช้ไม่ถูกต้อง");
        if (challengeId == Guid.Empty)
            throw new DomainException("รหัสการยืนยันอีเมลไม่ถูกต้อง");

        BuyerId = buyerId;
        ChallengeId = challengeId;
        Name = Required(name, "ชื่อเหตุการณ์", 100);
        DestinationHash = ValidDigest(destinationHash);
        MaskedDestination = MaskedEmail(maskedDestination);
        CreatedAt = createdAt;
        Result = Required(result, "ผลลัพธ์", 100);
    }

    public Guid Id { get; private set; }
    public Guid BuyerId { get; private set; }
    public Guid ChallengeId { get; private set; }
    public string Name { get; private set; } = "";
    public string DestinationHash { get; private set; } = "";
    public string MaskedDestination { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public string Result { get; private set; } = "";

    private static string ValidDigest(string value)
    {
        var clean = (value ?? "").Trim();
        if (clean.Length != 64 || clean.Any(character => !Uri.IsHexDigit(character)))
            throw new DomainException("ข้อมูลอ้างอิงปลายทางไม่ถูกต้อง");
        return clean.ToLowerInvariant();
    }

    private static string MaskedEmail(string value)
    {
        var clean = (value ?? "").Trim();
        var at = clean.IndexOf('@');
        if (clean.Length is 0 or > 254 || at is < 2 or >= 254)
            throw new DomainException("อีเมลที่ปกปิดแล้วไม่ถูกต้อง");

        var local = clean[..at];
        var domain = clean[(at + 1)..];
        var maskStart = local.IndexOfAny(['*', '•']);
        if (domain.Length == 0 ||
            domain.Any(character => char.IsWhiteSpace(character) ||
                                    character is '*' or '•') ||
            maskStart is < 1 or > 2 ||
            local[maskStart..].Any(character => character != local[maskStart]))
            throw new DomainException("อีเมลที่ปกปิดแล้วไม่ถูกต้อง");

        return clean;
    }

    private static string Required(string value, string label, int maximumLength)
    {
        var clean = (value ?? "").Trim();
        if (clean.Length == 0 || clean.Length > maximumLength)
            throw new DomainException($"{label}ไม่ถูกต้อง");
        return clean;
    }
}
