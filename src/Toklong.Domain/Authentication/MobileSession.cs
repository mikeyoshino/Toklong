using Toklong.Domain.Common;

namespace Toklong.Domain.Authentication;

public sealed class MobileSession
{
    private MobileSession() { }

    public Guid Id { get; private set; }
    public Guid? BuyerId { get; private set; }
    public Guid? SellerId { get; private set; }
    public string DisplayName { get; private set; } = "";
    public string PhoneNumber { get; private set; } = "";
    public string RefreshTokenHash { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public DateTimeOffset LastRotatedAt { get; private set; }
    public long Version { get; private set; }

    public static MobileSession Create(
        Guid? buyerId,
        Guid? sellerId,
        string displayName,
        string phoneNumber,
        string refreshTokenHash,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
    {
        if (!buyerId.HasValue && !sellerId.HasValue)
            throw new DomainException("เซสชันต้องผูกกับบัญชีผู้ใช้");
        if (expiresAt <= now)
            throw new DomainException("วันหมดอายุของเซสชันไม่ถูกต้อง");
        return new MobileSession
        {
            Id = Guid.NewGuid(),
            BuyerId = buyerId,
            SellerId = sellerId,
            DisplayName = Required(displayName, "ชื่อผู้ใช้"),
            PhoneNumber = Required(phoneNumber, "เบอร์โทรศัพท์"),
            RefreshTokenHash = ValidHash(refreshTokenHash),
            CreatedAt = now,
            LastRotatedAt = now,
            ExpiresAt = expiresAt
        };
    }

    public bool IsActive(DateTimeOffset now) =>
        RevokedAt is null && ExpiresAt > now;

    public void RotateRefreshToken(
        string refreshTokenHash,
        DateTimeOffset now)
    {
        if (!IsActive(now))
            throw new DomainException("เซสชันหมดอายุ กรุณาเข้าสู่ระบบอีกครั้ง");
        RefreshTokenHash = ValidHash(refreshTokenHash);
        LastRotatedAt = now;
        Version++;
    }

    public void AttachSeller(
        Guid sellerId,
        string phoneNumber,
        string sellerDisplayName,
        DateTimeOffset now)
    {
        if (!IsActive(now))
            throw new DomainException("เซสชันหมดอายุ กรุณาเข้าสู่ระบบอีกครั้ง");
        if (!string.Equals(
                PhoneNumber,
                Required(phoneNumber, "เบอร์โทรศัพท์"),
                StringComparison.Ordinal))
            throw new DomainException(
                "บัญชีผู้ขายต้องใช้เบอร์เดียวกับเซสชันที่เข้าสู่ระบบ");
        if (SellerId.HasValue && SellerId.Value != sellerId)
            throw new DomainException(
                "เซสชันนี้ผูกกับบัญชีผู้ขายอื่นแล้ว");

        SellerId = sellerId;
        if (!BuyerId.HasValue)
            DisplayName = Required(sellerDisplayName, "ชื่อผู้ขาย");
        Version++;
    }

    public void Revoke(DateTimeOffset now)
    {
        if (RevokedAt is not null)
            return;
        RevokedAt = now;
        Version++;
    }

    private static string Required(string value, string label) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new DomainException($"{label}ไม่ถูกต้อง")
            : value.Trim();

    private static string ValidHash(string value)
    {
        var clean = Required(value, "Refresh token hash");
        if (clean.Length != 64 ||
            clean.Any(character => !Uri.IsHexDigit(character)))
            throw new DomainException("Refresh token hash ไม่ถูกต้อง");
        return clean.ToLowerInvariant();
    }
}
