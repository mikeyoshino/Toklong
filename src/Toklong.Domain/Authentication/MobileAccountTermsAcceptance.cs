using Toklong.Domain.Common;

namespace Toklong.Domain.Authentication;

public sealed class MobileAccountTermsAcceptance
{
    private MobileAccountTermsAcceptance() { }

    public Guid Id { get; private set; }
    public Guid BuyerId { get; private set; }
    public string TermsVersion { get; private set; } = "";
    public string InstallationId { get; private set; } = "";
    public string IdempotencyKey { get; private set; } = "";
    public DateTimeOffset AcceptedAt { get; private set; }

    public static MobileAccountTermsAcceptance Create(
        Guid buyerId,
        string termsVersion,
        string installationId,
        string idempotencyKey,
        DateTimeOffset acceptedAt)
    {
        if (buyerId == Guid.Empty)
            throw new DomainException("บัญชีผู้ใช้ไม่ถูกต้อง");

        var normalizedTermsVersion = (termsVersion ?? "").Trim();
        if (normalizedTermsVersion.Length is 0 or > 40)
            throw new DomainException("เวอร์ชันข้อกำหนดไม่ถูกต้อง");

        return new MobileAccountTermsAcceptance
        {
            Id = Guid.NewGuid(),
            BuyerId = buyerId,
            TermsVersion = normalizedTermsVersion,
            InstallationId =
                NormalizedGuid(installationId, "รหัสอุปกรณ์"),
            IdempotencyKey =
                NormalizedGuid(idempotencyKey, "รหัสคำขอ"),
            AcceptedAt = acceptedAt
        };
    }

    private static string NormalizedGuid(string value, string label)
    {
        var clean = (value ?? "").Trim();
        if (clean.Length != 32 ||
            !Guid.TryParseExact(clean, "N", out var parsed))
            throw new DomainException($"{label}ไม่ถูกต้อง");
        return parsed.ToString("N");
    }
}
