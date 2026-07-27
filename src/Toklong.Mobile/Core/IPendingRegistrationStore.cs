namespace Toklong.Mobile.Core;

public sealed record PendingMobileRegistration
{
    public PendingMobileRegistration(
        string registrationTicket,
        DateTimeOffset expiresAt,
        string maskedPhoneNumber,
        string installationId,
        string completionIdempotencyKey)
    {
        RegistrationTicket = Required(
            registrationTicket,
            "Registration ticket",
            512);
        ExpiresAt = expiresAt;
        MaskedPhoneNumber = Required(
            maskedPhoneNumber,
            "หมายเลขโทรศัพท์",
            32);
        InstallationId = NormalizedGuid(
            installationId,
            "รหัสการติดตั้งแอป");
        CompletionIdempotencyKey = NormalizedGuid(
            completionIdempotencyKey,
            "รหัสคำขอ");
    }

    public string RegistrationTicket { get; }
    public DateTimeOffset ExpiresAt { get; }
    public string MaskedPhoneNumber { get; }
    public string InstallationId { get; }
    public string CompletionIdempotencyKey { get; }

    private static string Required(
        string value,
        string label,
        int maximumLength)
    {
        var clean = (value ?? "").Trim();
        if (clean.Length is 0 || clean.Length > maximumLength)
            throw new ArgumentException($"{label}ไม่ถูกต้อง");
        return clean;
    }

    private static string NormalizedGuid(
        string value,
        string label)
    {
        var clean = (value ?? "").Trim();
        if (clean.Length != 32 ||
            !Guid.TryParseExact(clean, "N", out var id) ||
            id == Guid.Empty)
            throw new ArgumentException($"{label}ไม่ถูกต้อง");
        return id.ToString("N");
    }
}

public interface IPendingRegistrationStore
{
    Task<PendingMobileRegistration?> GetValidAsync(
        DateTimeOffset now);

    Task SaveAsync(PendingMobileRegistration pending);

    void Clear();
}
