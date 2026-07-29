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
    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; }

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
