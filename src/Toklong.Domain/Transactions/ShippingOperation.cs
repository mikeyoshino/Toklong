using Toklong.Domain.Common;

namespace Toklong.Domain.Transactions;

public enum ShippingOperationType
{
    BookOutbound,
    ConfirmOutbound,
    CancelOutbound,
    BookReturn,
    ConfirmReturn,
    CancelReturn
}

public enum ShippingOperationStatus
{
    Pending,
    Processing,
    RetryScheduled,
    OutcomeUnknown,
    Succeeded,
    NeedsReview,
    Superseded
}

public sealed class ShippingOperation
{
    private ShippingOperation() { }

    public Guid Id { get; private set; }
    public Guid TransactionId { get; private set; }
    public Guid ManagedShipmentId { get; private set; }
    public ShippingOperationType OperationType { get; private set; }
    public ShippingOperationStatus Status { get; private set; }
    public string IdempotencyKey { get; private set; } = "";
    public string RequestFingerprint { get; private set; } = "";
    public string? ProviderPurchaseReference { get; private set; }
    public string? ProviderTrackingReference { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public string? LastSanitizedErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public long Version { get; private set; }

    public static ShippingOperation Queue(
        Guid transactionId,
        Guid managedShipmentId,
        ShippingOperationType operationType,
        string idempotencyKey,
        string requestFingerprint,
        DateTimeOffset now)
    {
        if (transactionId == Guid.Empty ||
            managedShipmentId == Guid.Empty)
            throw new DomainException(
                "ข้อมูลรายการจัดส่งไม่ครบ");
        var cleanKey = Required(
            idempotencyKey,
            "idempotency key",
            160);
        var cleanFingerprint =
            requestFingerprint?.Trim() ?? "";
        if (cleanFingerprint.Length != 64 ||
            cleanFingerprint.Any(character =>
                !char.IsAsciiHexDigit(character) ||
                char.IsAsciiLetterUpper(character)))
            throw new DomainException(
                "request fingerprint ไม่ถูกต้อง");

        return new ShippingOperation
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            ManagedShipmentId = managedShipmentId,
            OperationType = operationType,
            Status = ShippingOperationStatus.Pending,
            IdempotencyKey = cleanKey,
            RequestFingerprint = cleanFingerprint,
            NextAttemptAt = now,
            CreatedAt = now
        };
    }

    public void Claim(
        string workerId,
        DateTimeOffset now,
        TimeSpan leaseDuration)
    {
        var cleanWorker = Required(
            workerId,
            "worker",
            120);
        if (leaseDuration <= TimeSpan.Zero)
            throw new DomainException(
                "ระยะเวลา lease ไม่ถูกต้อง");
        if (Status == ShippingOperationStatus.Processing &&
            LeaseExpiresAt > now)
            throw new DomainException(
                "operation นี้มี worker กำลังทำอยู่");
        if (Status is not (
                ShippingOperationStatus.Pending or
                ShippingOperationStatus.RetryScheduled or
                ShippingOperationStatus.Processing))
            throw new DomainException(
                "operation นี้ยัง claim ไม่ได้");
        if (Status != ShippingOperationStatus.Processing &&
            NextAttemptAt > now)
            throw new DomainException(
                "operation นี้ยังไม่ถึงเวลาทำงาน");

        Status = ShippingOperationStatus.Processing;
        LeaseOwner = cleanWorker;
        LeaseExpiresAt = now.Add(leaseDuration);
        StartedAt ??= now;
        AttemptCount++;
        Version++;
    }

    public void MarkOutcomeUnknown(
        string workerId,
        string sanitizedErrorCode,
        DateTimeOffset now)
    {
        EnsureProcessingLease(workerId, now);
        Status = ShippingOperationStatus.OutcomeUnknown;
        LastSanitizedErrorCode = Required(
            sanitizedErrorCode,
            "error code",
            100);
        ClearLease();
        Version++;
    }

    public void ScheduleRetry(
        string actorId,
        DateTimeOffset nextAttemptAt,
        string sanitizedErrorCode,
        bool providerReplayProvenSafe,
        DateTimeOffset now)
    {
        Required(actorId, "ผู้สั่ง retry", 120);
        if (Status is (
                ShippingOperationStatus.OutcomeUnknown or
                ShippingOperationStatus.NeedsReview) &&
            !providerReplayProvenSafe)
            throw new DomainException(
                "ยัง retry ไม่ได้จนกว่าจะพิสูจน์ผลเดิมจากผู้ให้บริการ");
        if (Status is not (
                ShippingOperationStatus.Processing or
                ShippingOperationStatus.OutcomeUnknown or
                ShippingOperationStatus.NeedsReview))
            throw new DomainException(
                "operation นี้ยังตั้ง retry ไม่ได้");
        if (nextAttemptAt <= now)
            throw new DomainException(
                "เวลา retry ต้องอยู่ในอนาคต");

        Status = ShippingOperationStatus.RetryScheduled;
        NextAttemptAt = nextAttemptAt;
        LastSanitizedErrorCode = Required(
            sanitizedErrorCode,
            "error code",
            100);
        ClearLease();
        Version++;
    }

    public void Succeed(
        string workerId,
        string? providerPurchaseReference,
        string? providerTrackingReference,
        DateTimeOffset now)
    {
        EnsureProcessingLease(workerId, now);
        Status = ShippingOperationStatus.Succeeded;
        ProviderPurchaseReference = Optional(
            providerPurchaseReference,
            160);
        ProviderTrackingReference = Optional(
            providerTrackingReference,
            160);
        CompletedAt = now;
        LastSanitizedErrorCode = null;
        ClearLease();
        Version++;
    }

    public void SendToReview(
        string workerId,
        string sanitizedErrorCode,
        DateTimeOffset now)
    {
        EnsureProcessingLease(workerId, now);
        Status = ShippingOperationStatus.NeedsReview;
        LastSanitizedErrorCode = Required(
            sanitizedErrorCode,
            "error code",
            100);
        CompletedAt = now;
        ClearLease();
        Version++;
    }

    public void Supersede(
        string workerId,
        string sanitizedReasonCode,
        DateTimeOffset now)
    {
        EnsureProcessingLease(workerId, now);
        Status = ShippingOperationStatus.Superseded;
        LastSanitizedErrorCode = Required(
            sanitizedReasonCode,
            "reason code",
            100);
        CompletedAt = now;
        ClearLease();
        Version++;
    }

    private void EnsureProcessingLease(
        string workerId,
        DateTimeOffset now)
    {
        if (Status != ShippingOperationStatus.Processing ||
            !string.Equals(
                LeaseOwner,
                workerId?.Trim(),
                StringComparison.Ordinal) ||
            LeaseExpiresAt <= now)
            throw new DomainException(
                "worker ไม่มี lease สำหรับ operation นี้");
    }

    private void ClearLease()
    {
        LeaseOwner = null;
        LeaseExpiresAt = null;
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

    private static string? Optional(
        string? value,
        int maximumLength)
    {
        var clean = value?.Trim();
        if (string.IsNullOrWhiteSpace(clean))
            return null;
        if (clean.Length > maximumLength)
            throw new DomainException(
                "เลขอ้างอิงผู้ให้บริการยาวเกินกำหนด");
        return clean;
    }
}
