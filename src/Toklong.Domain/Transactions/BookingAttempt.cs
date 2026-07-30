using Toklong.Domain.Common;

namespace Toklong.Domain.Transactions;

public enum BookingAttemptStatus
{
    Created,
    CallingProvider,
    Succeeded,
    Failed,
    TimedOut
}

public sealed record BookingAttemptSuccess(
    string ProviderPurchaseId,
    string ProviderTrackingCode,
    string? CourierTrackingCode,
    long ShippingFeeSatang,
    long ProtectionFeeSatang,
    long CoverageLimitSatang,
    string Currency,
    string ProviderResponseFingerprint);

public sealed class BookingAttempt
{
    private BookingAttempt() { }

    public Guid Id { get; private set; }
    public Guid TransactionId { get; private set; }
    public Guid ManagedShipmentId { get; private set; }
    public Guid BuyerId { get; private set; }
    public string IdempotencyKey { get; private set; } = "";
    public string RequestFingerprint { get; private set; } = "";
    public string ProviderReference { get; private set; } = "";
    public BookingAttemptStatus Status { get; private set; }
    public int AttemptNumber { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? ProviderPurchaseId { get; private set; }
    public string? ProviderTrackingCode { get; private set; }
    public string? CourierTrackingCode { get; private set; }
    public long? QuotedShippingFeeSatang { get; private set; }
    public long? QuotedProtectionFeeSatang { get; private set; }
    public long? QuotedCoverageLimitSatang { get; private set; }
    public string? Currency { get; private set; }
    public string? ProviderResponseFingerprint { get; private set; }
    public string? FailureCategory { get; private set; }
    public string? SafeFailureCode { get; private set; }
    public long Version { get; private set; }

    public static BookingAttempt Create(
        Guid transactionId,
        Guid managedShipmentId,
        Guid buyerId,
        string idempotencyKey,
        string requestFingerprint,
        int attemptNumber,
        DateTimeOffset now)
    {
        if (transactionId == Guid.Empty ||
            managedShipmentId == Guid.Empty ||
            buyerId == Guid.Empty)
            throw new DomainException(
                "ข้อมูลการเตรียมจัดส่งไม่ครบ");
        var cleanKey = Required(
            idempotencyKey,
            "รหัสป้องกันการทำซ้ำ",
            160);
        RequireFingerprint(
            requestFingerprint,
            "request fingerprint");
        if (attemptNumber is < 1 or > 3)
            throw new DomainException(
                "จำนวนครั้งที่ลองเตรียมจัดส่งไม่ถูกต้อง");

        var id = Guid.NewGuid();
        return new BookingAttempt
        {
            Id = id,
            TransactionId = transactionId,
            ManagedShipmentId = managedShipmentId,
            BuyerId = buyerId,
            IdempotencyKey = cleanKey,
            RequestFingerprint = requestFingerprint,
            ProviderReference = $"checkout:{id:N}",
            Status = BookingAttemptStatus.Created,
            AttemptNumber = attemptNumber,
            CreatedAt = now
        };
    }

    public void Claim(
        DateTimeOffset now)
    {
        if (Status != BookingAttemptStatus.Created)
            throw new DomainException(
                "รายการเตรียมจัดส่งนี้เริ่มทำงานไม่ได้");

        Status = BookingAttemptStatus.CallingProvider;
        StartedAt = now;
        Version++;
    }

    public void Succeed(
        BookingAttemptSuccess result,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(result);
        RequireCallingProvider();
        var purchaseId = Required(
            result.ProviderPurchaseId,
            "provider purchase id",
            160);
        var providerTrackingCode = Required(
            result.ProviderTrackingCode,
            "provider tracking code",
            120);
        var courierTrackingCode = Optional(
            result.CourierTrackingCode,
            "courier tracking code",
            120);
        if (result.ShippingFeeSatang < 0 ||
            result.ProtectionFeeSatang < 0 ||
            result.CoverageLimitSatang < 0)
            throw new DomainException(
                "ยอดเงินจากผู้ให้บริการไม่ถูกต้อง");
        var currency = Required(
            result.Currency,
            "สกุลเงิน",
            3).ToUpperInvariant();
        if (currency.Length != 3 ||
            currency.Any(character =>
                !char.IsAsciiLetterUpper(character)))
            throw new DomainException(
                "สกุลเงินไม่ถูกต้อง");
        RequireFingerprint(
            result.ProviderResponseFingerprint,
            "provider response fingerprint");

        Status = BookingAttemptStatus.Succeeded;
        ProviderPurchaseId = purchaseId;
        ProviderTrackingCode = providerTrackingCode;
        CourierTrackingCode = courierTrackingCode;
        QuotedShippingFeeSatang =
            result.ShippingFeeSatang;
        QuotedProtectionFeeSatang =
            result.ProtectionFeeSatang;
        QuotedCoverageLimitSatang =
            result.CoverageLimitSatang;
        Currency = currency;
        ProviderResponseFingerprint =
            result.ProviderResponseFingerprint;
        CompletedAt = now;
        Version++;
    }

    public void Fail(
        string safeFailureCode,
        DateTimeOffset now)
    {
        RequireCallingProvider();
        Status = BookingAttemptStatus.Failed;
        FailureCategory = "definite_failure";
        SafeFailureCode = Required(
            safeFailureCode,
            "รหัสข้อผิดพลาด",
            100);
        CompletedAt = now;
        Version++;
    }

    public void TimeOut(
        string safeFailureCode,
        DateTimeOffset now)
    {
        RequireCallingProvider();
        Status = BookingAttemptStatus.TimedOut;
        FailureCategory = "outcome_unknown";
        SafeFailureCode = Required(
            safeFailureCode,
            "รหัสข้อผิดพลาด",
            100);
        CompletedAt = now;
        Version++;
    }

    private void RequireCallingProvider()
    {
        if (Status !=
            BookingAttemptStatus.CallingProvider)
            throw new DomainException(
                "รายการเตรียมจัดส่งนี้ยังบันทึกผลไม่ได้");
    }

    private static void RequireFingerprint(
        string value,
        string label)
    {
        if (value?.Length != 64 ||
            value.Any(character =>
                !char.IsAsciiHexDigit(character) ||
                char.IsAsciiLetterUpper(character)))
            throw new DomainException(
                $"{label} ไม่ถูกต้อง");
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
                $"{label} ไม่ถูกต้อง");
        return clean;
    }

    private static string? Optional(
        string? value,
        string label,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return Required(
            value,
            label,
            maximumLength);
    }
}
