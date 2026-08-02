using Toklong.Domain.Common;

namespace Toklong.Domain.Transactions;

public enum CounterQrResourceStatus
{
    Pending,
    Ready,
    RetryableError,
    Unavailable
}

public enum CounterQrRepresentation
{
    ProviderPng,
    ProviderCounterPayload
}

public sealed class ShipmentCounterQrResource
{
    private const int MaximumProtectedArtifactBytes =
        2 * 1024 * 1024;

    private ShipmentCounterQrResource() { }

    public Guid Id { get; private set; }
    public Guid TransactionId { get; private set; }
    public Guid ManagedShipmentId { get; private set; }
    public CounterQrResourceStatus Status { get; private set; }
    public CounterQrRepresentation? Representation { get; private set; }
    public byte[]? ProtectedArtifact { get; private set; }
    public string? ProtectionVersion { get; private set; }
    public string? ArtifactSha256 { get; private set; }
    public string? ProviderResourceDigest { get; private set; }
    public DateTimeOffset? ProviderExpiresAt { get; private set; }
    public DateTimeOffset? FetchedAt { get; private set; }
    public string? LastSanitizedErrorCode { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Version { get; private set; }

    internal static ShipmentCounterQrResource Queue(
        Guid transactionId,
        Guid managedShipmentId,
        DateTimeOffset now)
    {
        if (transactionId == Guid.Empty ||
            managedShipmentId == Guid.Empty ||
            now == default)
            throw new DomainException(
                "ข้อมูล QR สำหรับจัดส่งไม่ถูกต้อง");
        return new ShipmentCounterQrResource
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            ManagedShipmentId = managedShipmentId,
            Status = CounterQrResourceStatus.Pending,
            NextAttemptAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Claim(
        string workerId,
        DateTimeOffset now,
        TimeSpan leaseDuration)
    {
        var duePending =
            (Status is CounterQrResourceStatus.Pending or
                CounterQrResourceStatus.RetryableError) &&
            NextAttemptAt.HasValue &&
            NextAttemptAt <= now;
        var dueExpiredReady =
            Status == CounterQrResourceStatus.Ready &&
            ProviderExpiresAt.HasValue &&
            ProviderExpiresAt <= now;
        if ((!duePending && !dueExpiredReady) ||
            LeaseExpiresAt > now ||
            leaseDuration < TimeSpan.FromSeconds(30) ||
            leaseDuration > TimeSpan.FromMinutes(15))
            throw new DomainException(
                "งาน QR นี้ยังไม่พร้อมให้ดำเนินการ");
        if (dueExpiredReady)
        {
            ClearArtifact();
            LastSanitizedErrorCode = null;
            NextAttemptAt = now;
            Status = CounterQrResourceStatus.Pending;
        }
        LeaseOwner = Required(
            workerId,
            "ผู้ประมวลผล QR",
            120);
        LeaseExpiresAt = now.Add(leaseDuration);
        AttemptCount++;
        UpdatedAt = now;
        Version++;
    }

    public void RecordReady(
        CounterQrRepresentation representation,
        byte[] protectedArtifact,
        string protectionVersion,
        string artifactSha256,
        string providerResourceDigest,
        DateTimeOffset? providerExpiresAt,
        DateTimeOffset fetchedAt,
        string workerId)
    {
        EnsureLease(workerId, fetchedAt);
        if (protectedArtifact is null ||
            protectedArtifact.Length is < 32 or
                > MaximumProtectedArtifactBytes)
            throw new DomainException(
                "ข้อมูล QR ที่ป้องกันไว้ไม่ถูกต้อง");
        if (providerExpiresAt.HasValue &&
            providerExpiresAt <= fetchedAt)
            throw new DomainException(
                "QR จากผู้ให้บริการหมดอายุแล้ว");

        Representation = representation;
        ProtectedArtifact = [.. protectedArtifact];
        ProtectionVersion = Required(
            protectionVersion,
            "รุ่นการป้องกัน QR",
            32);
        ArtifactSha256 = Sha256(artifactSha256);
        ProviderResourceDigest = Sha256(providerResourceDigest);
        ProviderExpiresAt = providerExpiresAt;
        FetchedAt = fetchedAt;
        LastSanitizedErrorCode = null;
        NextAttemptAt = null;
        Status = CounterQrResourceStatus.Ready;
        ClearLease();
        UpdatedAt = fetchedAt;
        Version++;
    }

    public void RecordRetryableError(
        string sanitizedErrorCode,
        DateTimeOffset nextAttemptAt,
        DateTimeOffset now,
        string workerId)
    {
        EnsureLease(workerId, now);
        if (nextAttemptAt <= now ||
            nextAttemptAt > now.AddHours(1))
            throw new DomainException(
                "เวลาลองโหลด QR ใหม่ไม่ถูกต้อง");
        ClearArtifact();
        LastSanitizedErrorCode = SafeCode(sanitizedErrorCode);
        NextAttemptAt = nextAttemptAt;
        Status = CounterQrResourceStatus.RetryableError;
        ClearLease();
        UpdatedAt = now;
        Version++;
    }

    public void RecordUnavailable(
        string sanitizedErrorCode,
        DateTimeOffset now,
        string workerId)
    {
        EnsureLease(workerId, now);
        ClearArtifact();
        LastSanitizedErrorCode = SafeCode(sanitizedErrorCode);
        NextAttemptAt = null;
        Status = CounterQrResourceStatus.Unavailable;
        ClearLease();
        UpdatedAt = now;
        Version++;
    }

    public bool RequestRetry(DateTimeOffset now)
    {
        if (Status == CounterQrResourceStatus.Pending)
            return false;
        if (Status == CounterQrResourceStatus.Ready &&
            (!ProviderExpiresAt.HasValue ||
             ProviderExpiresAt > now))
            return false;

        ClearArtifact();
        LastSanitizedErrorCode = null;
        NextAttemptAt = now;
        Status = CounterQrResourceStatus.Pending;
        ClearLease();
        UpdatedAt = now;
        Version++;
        return true;
    }

    private void EnsureLease(
        string workerId,
        DateTimeOffset now)
    {
        var cleanWorker = Required(
            workerId,
            "ผู้ประมวลผล QR",
            120);
        if (!string.Equals(
                LeaseOwner,
                cleanWorker,
                StringComparison.Ordinal) ||
            !LeaseExpiresAt.HasValue ||
            LeaseExpiresAt < now)
            throw new DomainException(
                "สิทธิ์ประมวลผล QR หมดอายุแล้ว");
    }

    private void ClearArtifact()
    {
        Representation = null;
        ProtectedArtifact = null;
        ProtectionVersion = null;
        ArtifactSha256 = null;
        ProviderResourceDigest = null;
        ProviderExpiresAt = null;
        FetchedAt = null;
    }

    private void ClearLease()
    {
        LeaseOwner = null;
        LeaseExpiresAt = null;
    }

    private static string Sha256(string value)
    {
        var clean = (value ?? "").Trim().ToLowerInvariant();
        if (clean.Length != 64 ||
            !clean.All(Uri.IsHexDigit))
            throw new DomainException(
                "ข้อมูลตรวจสอบ QR ไม่ถูกต้อง");
        return clean;
    }

    private static string SafeCode(string value)
    {
        var clean = Required(
            value,
            "รหัสข้อผิดพลาด QR",
            100).ToLowerInvariant();
        if (clean.Any(character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character is not ('-' or '_' or '.')))
            throw new DomainException(
                "รหัสข้อผิดพลาด QR ไม่ถูกต้อง");
        return clean;
    }

    private static string Required(
        string? value,
        string label,
        int maximumLength)
    {
        var clean = value?.Trim() ?? "";
        if (clean.Length == 0 ||
            clean.Length > maximumLength)
            throw new DomainException(
                $"{label}ไม่ถูกต้อง");
        return clean;
    }
}
