using Toklong.Domain.Common;

namespace Toklong.Domain.Transactions;

public enum ShipmentDirection
{
    Outbound,
    Return
}

public enum ManagedShipmentStatus
{
    PendingBooking,
    Reserved,
    Confirmed,
    CarrierAccepted,
    InTransit,
    Delivered,
    Cancelled,
    TrackingUnverified,
    CarrierException
}

public sealed record ManagedShipmentDraft(
    string Provider,
    string OriginPrivateSnapshotReference,
    string DestinationPrivateSnapshotReference,
    string ParcelName,
    int WeightGrams,
    int WidthCentimeters,
    int LengthCentimeters,
    int HeightCentimeters,
    string CarrierCode,
    string ServiceCode,
    string ServiceName,
    long BaseShippingFeeSatang,
    long InsuranceFeeSatang,
    long DeclaredValueSatang,
    string InsuranceCode,
    string QuoteReference,
    DateTimeOffset QuoteExpiresAt);

public sealed class ManagedShipment
{
    private ManagedShipment() { }

    public Guid Id { get; private set; }
    public Guid TransactionId { get; private set; }
    public ShipmentDirection Direction { get; private set; }
    public string Provider { get; private set; } = "";
    public ManagedShipmentStatus Status { get; private set; }
    public string OriginPrivateSnapshotReference { get; private set; } = "";
    public string DestinationPrivateSnapshotReference { get; private set; } = "";
    public string ParcelName { get; private set; } = "";
    public int WeightGrams { get; private set; }
    public int WidthCentimeters { get; private set; }
    public int LengthCentimeters { get; private set; }
    public int HeightCentimeters { get; private set; }
    public string CarrierCode { get; private set; } = "";
    public string ServiceCode { get; private set; } = "";
    public string ServiceName { get; private set; } = "";
    public string HandoffMode { get; private set; } = "DropOff";
    public long BaseShippingFeeSatang { get; private set; }
    public long InsuranceFeeSatang { get; private set; }
    public long DeclaredValueSatang { get; private set; }
    public string InsuranceCode { get; private set; } = "";
    public string QuoteReference { get; private set; } = "";
    public DateTimeOffset QuoteExpiresAt { get; private set; }
    public string? PurchaseReference { get; private set; }
    public string? ProviderTrackingCode { get; private set; }
    public string? CourierTrackingCode { get; private set; }
    public DateTimeOffset? ReservedAt { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset? FirstCarrierScanAt { get; private set; }
    public DateTimeOffset? InTransitAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public string? LastProviderStatus { get; private set; }
    public DateTimeOffset? LastReconciledAt { get; private set; }
    public string? ExceptionResolvedBy { get; private set; }
    public string? ExceptionResolutionReference { get; private set; }
    public DateTimeOffset? ExceptionResolvedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; }
    public bool HasOpenException =>
        (Status is ManagedShipmentStatus.TrackingUnverified or
            ManagedShipmentStatus.CarrierException) &&
        !ExceptionResolvedAt.HasValue;

    public static ManagedShipment CreateOutbound(
        Guid transactionId,
        ManagedShipmentDraft draft,
        DateTimeOffset now) =>
        Create(
            transactionId,
            ShipmentDirection.Outbound,
            draft,
            now);

    public static ManagedShipment CreateReturn(
        Guid transactionId,
        ManagedShipmentDraft draft,
        DateTimeOffset now) =>
        Create(
            transactionId,
            ShipmentDirection.Return,
            draft,
            now);

    private static ManagedShipment Create(
        Guid transactionId,
        ShipmentDirection direction,
        ManagedShipmentDraft draft,
        DateTimeOffset now)
    {
        if (transactionId == Guid.Empty)
            throw new DomainException(
                "ไม่พบรายการซื้อขายสำหรับการจัดส่ง");
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.WeightGrams is < 1 or > 30_000 ||
            draft.WidthCentimeters is < 1 or > 200 ||
            draft.LengthCentimeters is < 1 or > 200 ||
            draft.HeightCentimeters is < 1 or > 200)
            throw new DomainException(
                "น้ำหนักหรือขนาดพัสดุไม่ถูกต้อง");
        if (draft.BaseShippingFeeSatang <= 0 ||
            draft.InsuranceFeeSatang <= 0 ||
            draft.DeclaredValueSatang <= 0)
            throw new DomainException(
                "ค่าจัดส่งหรือประกันพัสดุไม่ถูกต้อง");

        return new ManagedShipment
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            Direction = direction,
            Provider = Required(
                draft.Provider,
                "ผู้ให้บริการ",
                80),
            Status = ManagedShipmentStatus.PendingBooking,
            OriginPrivateSnapshotReference = Required(
                draft.OriginPrivateSnapshotReference,
                "ต้นทาง",
                160),
            DestinationPrivateSnapshotReference = Required(
                draft.DestinationPrivateSnapshotReference,
                "ปลายทาง",
                160),
            ParcelName = Required(
                draft.ParcelName,
                "ชื่อพัสดุ",
                180),
            WeightGrams = draft.WeightGrams,
            WidthCentimeters = draft.WidthCentimeters,
            LengthCentimeters = draft.LengthCentimeters,
            HeightCentimeters = draft.HeightCentimeters,
            CarrierCode = Required(
                draft.CarrierCode,
                "บริษัทขนส่ง",
                40).ToUpperInvariant(),
            ServiceCode = Required(
                draft.ServiceCode,
                "บริการขนส่ง",
                80),
            ServiceName = Required(
                draft.ServiceName,
                "ชื่อบริการขนส่ง",
                160),
            BaseShippingFeeSatang =
                draft.BaseShippingFeeSatang,
            InsuranceFeeSatang =
                draft.InsuranceFeeSatang,
            DeclaredValueSatang =
                draft.DeclaredValueSatang,
            InsuranceCode = Required(
                draft.InsuranceCode,
                "รหัสประกัน",
                80),
            QuoteReference = Required(
                draft.QuoteReference,
                "ราคาอ้างอิง",
                160),
            QuoteExpiresAt = draft.QuoteExpiresAt,
            CreatedAt = now
        };
    }

    public void RecordReservation(
        string purchaseReference,
        string providerTrackingCode,
        string? courierTrackingCode,
        DateTimeOffset reservedAt)
    {
        if (Status != ManagedShipmentStatus.PendingBooking)
            throw new DomainException(
                "รายการจัดส่งนี้สร้างกับผู้ให้บริการแล้ว");
        PurchaseReference = Required(
            purchaseReference,
            "เลขอ้างอิงรายการขนส่ง",
            160);
        ProviderTrackingCode = Required(
            providerTrackingCode,
            "เลขติดตามจากผู้ให้บริการ",
            120);
        CourierTrackingCode = Optional(
            courierTrackingCode,
            120);
        ReservedAt = reservedAt;
        LastProviderStatus = "wait";
        LastReconciledAt = reservedAt;
        Status = ManagedShipmentStatus.Reserved;
        Version++;
    }

    public void RecordConfirmation(
        string courierTrackingCode,
        string providerStatus,
        DateTimeOffset confirmedAt)
    {
        if (Status != ManagedShipmentStatus.Reserved)
            throw new DomainException(
                "รายการจัดส่งยังยืนยันไม่ได้");
        CourierTrackingCode = Required(
            courierTrackingCode,
            "เลขพัสดุ",
            120);
        LastProviderStatus = Required(
            providerStatus,
            "สถานะผู้ให้บริการ",
            40);
        ConfirmedAt = confirmedAt;
        LastReconciledAt = confirmedAt;
        Status = ManagedShipmentStatus.Confirmed;
        Version++;
    }

    public void RecordCancellation(
        DateTimeOffset cancelledAt)
    {
        if (FirstCarrierScanAt.HasValue ||
            Status is ManagedShipmentStatus.Delivered or
                ManagedShipmentStatus.Cancelled)
            throw new DomainException(
                "รายการจัดส่งนี้ยกเลิกไม่ได้");
        CancelledAt = cancelledAt;
        LastReconciledAt = cancelledAt;
        Status = ManagedShipmentStatus.Cancelled;
        Version++;
    }

    public void RecordCarrierAccepted(
        string providerStatus,
        DateTimeOffset occurredAt,
        DateTimeOffset reconciledAt)
    {
        if (Status is ManagedShipmentStatus.Cancelled or
            ManagedShipmentStatus.Delivered)
            return;
        FirstCarrierScanAt ??= occurredAt;
        LastProviderStatus = Required(
            providerStatus,
            "สถานะผู้ให้บริการ",
            40);
        LastReconciledAt = reconciledAt;
        Status = ManagedShipmentStatus.CarrierAccepted;
        Version++;
    }

    public void RecordInTransit(
        string providerStatus,
        DateTimeOffset occurredAt,
        DateTimeOffset reconciledAt)
    {
        if (Status is ManagedShipmentStatus.Cancelled or
            ManagedShipmentStatus.Delivered)
            return;
        FirstCarrierScanAt ??= occurredAt;
        InTransitAt ??= occurredAt;
        LastProviderStatus = Required(
            providerStatus,
            "สถานะผู้ให้บริการ",
            40);
        LastReconciledAt = reconciledAt;
        Status = ManagedShipmentStatus.InTransit;
        Version++;
    }

    public void RecordTrustedDelivery(
        string providerStatus,
        DateTimeOffset occurredAt,
        DateTimeOffset reconciledAt)
    {
        if (Status == ManagedShipmentStatus.Delivered)
            return;
        if (occurredAt == default ||
            occurredAt > reconciledAt)
            throw new DomainException(
                "เวลาส่งถึงจากขนส่งไม่ถูกต้อง");
        FirstCarrierScanAt ??= occurredAt;
        DeliveredAt = occurredAt;
        LastProviderStatus = Required(
            providerStatus,
            "สถานะผู้ให้บริการ",
            40);
        LastReconciledAt = reconciledAt;
        Status = ManagedShipmentStatus.Delivered;
        Version++;
    }

    public void RecordTrackingUnverified(
        string providerStatus,
        DateTimeOffset reconciledAt)
    {
        if (Status is ManagedShipmentStatus.Cancelled or
            ManagedShipmentStatus.Delivered)
            return;
        LastProviderStatus = Required(
            providerStatus,
            "สถานะผู้ให้บริการ",
            40);
        LastReconciledAt = reconciledAt;
        ClearExceptionResolution();
        Status = ManagedShipmentStatus.TrackingUnverified;
        Version++;
    }

    public void RecordCarrierException(
        string providerStatus,
        DateTimeOffset reconciledAt)
    {
        if (Status is ManagedShipmentStatus.Cancelled or
            ManagedShipmentStatus.Delivered)
            return;
        LastProviderStatus = Required(
            providerStatus,
            "สถานะผู้ให้บริการ",
            40);
        LastReconciledAt = reconciledAt;
        ClearExceptionResolution();
        Status = ManagedShipmentStatus.CarrierException;
        Version++;
    }

    public void ResolveException(
        string actorId,
        string resolutionReference,
        DateTimeOffset resolvedAt)
    {
        if (Status is not (
                ManagedShipmentStatus.TrackingUnverified or
                ManagedShipmentStatus.CarrierException))
            throw new DomainException(
                "รายการจัดส่งนี้ไม่มีข้อยกเว้นที่ต้องปิด");
        if (ExceptionResolvedAt.HasValue)
            return;
        ExceptionResolvedBy = Required(
            actorId,
            "ผู้ปิดข้อยกเว้น",
            120);
        ExceptionResolutionReference = Required(
            resolutionReference,
            "เลขอ้างอิงการปิดข้อยกเว้น",
            160);
        ExceptionResolvedAt = resolvedAt;
        Version++;
    }

    public void ResumeTrackingReview(
        DateTimeOffset resumedAt)
    {
        if (Status != ManagedShipmentStatus.CarrierException)
            throw new DomainException(
                "รายการจัดส่งนี้ไม่ได้อยู่ในเคสขนส่ง");
        ClearExceptionResolution();
        LastReconciledAt = resumedAt;
        Status = ManagedShipmentStatus.TrackingUnverified;
        Version++;
    }

    public void RecordProviderReconciliation(
        string providerStatus,
        DateTimeOffset reconciledAt)
    {
        var cleanStatus = Required(
            providerStatus,
            "สถานะผู้ให้บริการ",
            40);
        if (string.Equals(
                LastProviderStatus,
                cleanStatus,
                StringComparison.Ordinal) &&
            LastReconciledAt.HasValue &&
            reconciledAt <
                LastReconciledAt.Value.AddMinutes(5))
            return;
        LastProviderStatus = cleanStatus;
        LastReconciledAt = reconciledAt;
        Version++;
    }

    private void ClearExceptionResolution()
    {
        ExceptionResolvedBy = null;
        ExceptionResolutionReference = null;
        ExceptionResolvedAt = null;
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
                "ข้อมูลรายการจัดส่งยาวเกินกำหนด");
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
