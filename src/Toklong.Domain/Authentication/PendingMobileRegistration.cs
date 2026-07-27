using Toklong.Domain.Common;

namespace Toklong.Domain.Authentication;

public enum RegistrationCompletionStatus
{
    Ready,
    ExactReplay
}

public sealed class PendingMobileRegistration
{
    private PendingMobileRegistration() { }

    public Guid Id { get; private set; }
    public string TicketHash { get; private set; } = "";
    public string PhoneNumber { get; private set; } = "";
    public string InstallationId { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }
    public string? CompletionIdempotencyKey { get; private set; }
    public Guid? BuyerId { get; private set; }
    public long Version { get; private set; }

    public static PendingMobileRegistration Create(
        string ticketHash,
        string phoneNumber,
        string installationId,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
    {
        if (expiresAt <= now)
            throw new DomainException("วันหมดอายุของการยืนยันเบอร์ไม่ถูกต้อง");

        return new PendingMobileRegistration
        {
            Id = Guid.NewGuid(),
            TicketHash = ValidSha256Hash(ticketHash),
            PhoneNumber = ValidThaiMobilePhone(phoneNumber),
            InstallationId = NormalizedGuid(installationId, "รหัสอุปกรณ์"),
            CreatedAt = now,
            ExpiresAt = expiresAt
        };
    }

    public RegistrationCompletionStatus ValidateCompletion(
        string installationId,
        string idempotencyKey,
        DateTimeOffset now)
    {
        var normalizedInstallationId =
            NormalizedGuid(installationId, "รหัสอุปกรณ์");
        var normalizedIdempotencyKey =
            NormalizedGuid(idempotencyKey, "รหัสคำขอ");

        if (ConsumedAt.HasValue)
        {
            return InstallationId == normalizedInstallationId &&
                   CompletionIdempotencyKey == normalizedIdempotencyKey &&
                   BuyerId.HasValue
                ? RegistrationCompletionStatus.ExactReplay
                : throw new DomainException(
                    "ลิงก์สมัครสมาชิกนี้ถูกใช้แล้ว กรุณาเริ่มใหม่");
        }

        if (ExpiresAt <= now || InstallationId != normalizedInstallationId)
            throw new DomainException(
                "การยืนยันเบอร์หมดอายุ กรุณายืนยันเบอร์ใหม่");

        return RegistrationCompletionStatus.Ready;
    }

    public void Complete(
        Guid buyerId,
        string idempotencyKey,
        DateTimeOffset completedAt)
    {
        if (buyerId == Guid.Empty)
            throw new DomainException("บัญชีผู้ใช้ไม่ถูกต้อง");

        var status = ValidateCompletion(
            InstallationId,
            idempotencyKey,
            completedAt);
        if (status == RegistrationCompletionStatus.ExactReplay)
            return;

        BuyerId = buyerId;
        CompletionIdempotencyKey =
            NormalizedGuid(idempotencyKey, "รหัสคำขอ");
        ConsumedAt = completedAt;
        Version++;
    }

    private static string ValidSha256Hash(string value)
    {
        var clean = (value ?? "").Trim();
        if (clean.Length != 64 ||
            clean.Any(character => !Uri.IsHexDigit(character)))
            throw new DomainException("Registration ticket hash ไม่ถูกต้อง");
        return clean.ToLowerInvariant();
    }

    private static string ValidThaiMobilePhone(string value)
    {
        var clean = (value ?? "").Trim();
        if (clean.Length != 12 ||
            !clean.StartsWith("+66", StringComparison.Ordinal) ||
            clean[3] is not ('6' or '8' or '9') ||
            clean[4..].Any(character => !char.IsDigit(character)))
            throw new DomainException("หมายเลขโทรศัพท์ไม่ถูกต้อง");
        return clean;
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
