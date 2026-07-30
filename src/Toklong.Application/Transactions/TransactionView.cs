using Toklong.Domain.Transactions;

namespace Toklong.Application.Transactions;

public sealed record TransactionView(
    Guid Id,
    string PublicToken,
    string SellerAccessToken,
    string? BuyerAccessToken,
    Guid? BuyerId,
    TransactionState State,
    string StateLabel,
    InitiatorRole InitiatorRole,
    string SellerDisplayName,
    string SellerContact,
    FulfillmentType FulfillmentType,
    string ProductName,
    string Category,
    ConditionCode Condition,
    string Description,
    string KnownDefects,
    string? PhotoUrl,
    long PriceSatang,
    long ShippingFeeSatang,
    long ParcelInsuranceFeeSatang,
    long ShippingDeclaredValueSatang,
    string? ShippingInsuranceCode,
    long BuyerTotalSatang,
    string Currency,
    int ShipByDurationHours,
    int InspectionWindowDurationHours,
    string TermsVersion,
    DateTimeOffset? SellerAcceptedAt,
    DateTimeOffset SellerAcceptanceDeadlineAt,
    DateTimeOffset? BuyerPaymentDeadlineAt,
    TransactionExpirationReason? ExpirationReason,
    string? BuyerDisplayName,
    string? BuyerContact,
    string? DeliveryProvinceName,
    string? DeliveryDistrictName,
    string? DeliverySubdistrictName,
    string? DeliveryPostalCode,
    string? DeliveryAddress,
    string? DeliveryAddressLine,
    string? ShippingOriginAddress,
    string? ShippingOriginAddressLine,
    string? ShippingOriginProvinceName,
    string? ShippingOriginDistrictName,
    string? ShippingOriginSubdistrictName,
    string? ShippingOriginPostalCode,
    int? PackageWeightGrams,
    int? PackageWidthCentimeters,
    int? PackageLengthCentimeters,
    int? PackageHeightCentimeters,
    string? ShippingQuoteProvider,
    string? ShippingQuoteReference,
    DateTimeOffset? ShippingQuoteExpiresAt,
    string? ShippingServiceCode,
    string? ShippingServiceName,
    string? ShippingPurchaseReference,
    string? ShippingProviderTrackingCode,
    string? ShippingCourierTrackingCode,
    DateTimeOffset? ShippingReservedAt,
    DateTimeOffset? ShippingConfirmedAt,
    string? ShippingLastProviderStatus,
    DateTimeOffset? ShippingLastReconciledAt,
    DateTimeOffset? ShippingCancelledAt,
    DateTimeOffset? BuyerAcceptedAt,
    string? AgreementCoreSnapshotHash,
    DateTimeOffset? AgreementCoreSnapshotCreatedAt,
    string? ProductSnapshotHash,
    int? SnapshotSchemaVersion,
    DateTimeOffset? AgreementSnapshotCreatedAt,
    DateTimeOffset? AgreementSnapshotSealedAt,
    string? TermsSnapshotHash,
    DateTimeOffset? ShipByAt,
    string PaymentProvider,
    string? PaymentReference,
    long BuyerProtectionFeeSatang,
    long PlatformFeeSatang,
    long SellerExpectedNetSatang,
    string FeePolicyVersion,
    DateTimeOffset? PaymentConfirmedAt,
    string? CarrierCode,
    string? TrackingNumber,
    TrackingVerificationStatus? TrackingVerificationStatus,
    DateTimeOffset? TrackingSubmittedAt,
    DateTimeOffset? FirstCarrierScanAt,
    DateTimeOffset? InTransitAt,
    string? DigitalDeliveryStatement,
    DateTimeOffset? DigitalDeliverySubmittedAt,
    DateTimeOffset? DeliveredAt,
    string? DeliveryEventId,
    DateTimeOffset? DeliveryEventReceivedAt,
    DateTimeOffset? DisputeWindowStartsAt,
    DateTimeOffset? DisputeWindowEndsAt,
    DateTimeOffset? BuyerConfirmedAt,
    PayoutReleaseReason? PayoutReleaseReason,
    DisputeReason? DisputeReason,
    string? DisputeStatement,
    DateTimeOffset? DisputeOpenedAt,
    string? PayoutReference,
    DateTimeOffset? PayoutConfirmedAt,
    string? RefundProviderStatus,
    DateTimeOffset? RefundActionRequiredAt,
    DateTimeOffset? RefundActionExpiresAt,
    DateTimeOffset? RefundInstructionsSentAt,
    IReadOnlyList<AgreementAcceptanceView> AgreementAcceptances,
    IReadOnlyList<AuditView> AuditEvents,
    DateTimeOffset CreatedAt)
{
    public ShippingOperationStatus? ShippingOperationStatus
    {
        get;
        init;
    }
    public bool ReturnShippingLabelAvailable
    {
        get;
        init;
    }
    public bool ParcelProtectionBookingReady { get; init; }

    public bool IsProviderManagedShipment =>
        FulfillmentType ==
            Toklong.Domain.Transactions.FulfillmentType
                .PhysicalShipment &&
        !string.IsNullOrWhiteSpace(
            ShippingPurchaseReference) &&
        !string.IsNullOrWhiteSpace(
            ShippingProviderTrackingCode);
    public bool HasTimelyTrustedCarrierAcceptance =>
        IsProviderManagedShipment &&
        FirstCarrierScanAt.HasValue &&
        ShipByAt.HasValue &&
        FirstCarrierScanAt.Value <= ShipByAt.Value;

    public static TransactionView From(SaleTransaction transaction) => new(
        transaction.Id,
        transaction.PublicToken,
        transaction.SellerAccessToken,
        transaction.BuyerAccessToken,
        transaction.BuyerId,
        transaction.State,
        ThaiStateLabel(
            transaction.State,
            transaction.ExpirationReason),
        transaction.InitiatorRole,
        transaction.SellerDisplayName,
        transaction.SellerContact,
        transaction.FulfillmentType,
        transaction.ProductName,
        transaction.Category,
        transaction.Condition,
        transaction.Description,
        transaction.KnownDefects,
        transaction.PhotoUrl,
        transaction.PriceSatang,
        transaction.ShippingFeeSatang,
        transaction.ParcelInsuranceFeeSatang,
        transaction.ShippingDeclaredValueSatang,
        transaction.ShippingInsuranceCode,
        transaction.BuyerTotalSatang,
        transaction.Currency,
        transaction.ShipByDurationHours,
        transaction.InspectionWindowDurationHours,
        transaction.TermsVersion,
        transaction.SellerAcceptedAt,
        transaction.SellerAcceptanceDeadlineAt,
        transaction.BuyerPaymentDeadlineAt,
        transaction.ExpirationReason,
        transaction.BuyerDisplayName,
        transaction.BuyerContact,
        transaction.DeliveryProvinceName,
        transaction.DeliveryDistrictName,
        transaction.DeliverySubdistrictName,
        transaction.DeliveryPostalCode,
        transaction.DeliveryAddress,
        transaction.DeliveryAddressLine,
        transaction.ShippingOriginAddress,
        transaction.ShippingOriginAddressLine,
        transaction.ShippingOriginProvinceName,
        transaction.ShippingOriginDistrictName,
        transaction.ShippingOriginSubdistrictName,
        transaction.ShippingOriginPostalCode,
        transaction.PackageWeightGrams,
        transaction.PackageWidthCentimeters,
        transaction.PackageLengthCentimeters,
        transaction.PackageHeightCentimeters,
        transaction.ShippingQuoteProvider,
        transaction.ShippingQuoteReference,
        transaction.ShippingQuoteExpiresAt,
        transaction.ShippingServiceCode,
        transaction.ShippingServiceName,
        transaction.ShippingPurchaseReference,
        transaction.ShippingProviderTrackingCode,
        transaction.ShippingCourierTrackingCode,
        transaction.ShippingReservedAt,
        transaction.ShippingConfirmedAt,
        transaction.ShippingLastProviderStatus,
        transaction.ShippingLastReconciledAt,
        transaction.ShippingCancelledAt,
        transaction.BuyerAcceptedAt,
        transaction.AgreementCoreSnapshotHash,
        transaction.AgreementCoreSnapshotCreatedAt,
        transaction.ProductSnapshotHash,
        transaction.SnapshotSchemaVersion,
        transaction.AgreementSnapshotCreatedAt,
        transaction.AgreementSnapshotSealedAt,
        transaction.TermsSnapshotHash,
        transaction.ShipByAt,
        transaction.PaymentProvider,
        transaction.PaymentReference,
        transaction.BuyerProtectionFeeSatang,
        transaction.PlatformFeeSatang,
        transaction.SellerExpectedNetSatang,
        transaction.FeePolicyVersion,
        transaction.PaymentConfirmedAt,
        transaction.CarrierCode,
        transaction.TrackingNumber,
        transaction.TrackingVerificationStatus,
        transaction.TrackingSubmittedAt,
        transaction.FirstCarrierScanAt,
        transaction.InTransitAt,
        transaction.DigitalDeliveryStatement,
        transaction.DigitalDeliverySubmittedAt,
        transaction.DeliveredAt,
        transaction.DeliveryEventId,
        transaction.DeliveryEventReceivedAt,
        transaction.DisputeWindowStartsAt,
        transaction.DisputeWindowEndsAt,
        transaction.BuyerConfirmedAt,
        transaction.PayoutReleaseReason,
        transaction.DisputeReason,
        transaction.DisputeStatement,
        transaction.DisputeOpenedAt,
        transaction.PayoutReference,
        transaction.PayoutConfirmedAt,
        transaction.RefundProviderStatus,
        transaction.RefundActionRequiredAt,
        transaction.RefundActionExpiresAt,
        transaction.RefundInstructionsSentAt,
        transaction.AgreementAcceptances
            .OrderBy(x => x.AcceptedAt)
            .Select(x => new AgreementAcceptanceView(
                x.Role,
                x.AuthenticationMethod,
                x.AgreementCoreSnapshotHash,
                x.TermsVersion,
                x.AcceptedAt))
            .ToList(),
        transaction.AuditEvents.OrderByDescending(x => x.CreatedAt)
            .Select(x => new AuditView(x.Name, x.FromState, x.ToState, x.ActorRole, x.CreatedAt))
            .ToList(),
        transaction.CreatedAt)
    {
        ShippingOperationStatus = transaction.ShippingOperations
            .OrderByDescending(operation => operation.CreatedAt)
            .Select(operation =>
                (ShippingOperationStatus?)operation.Status)
            .FirstOrDefault(),
        ReturnShippingLabelAvailable =
            transaction.ReturnRequired &&
            transaction.ManagedShipments.Any(shipment =>
                shipment.Direction == ShipmentDirection.Return &&
                !string.IsNullOrWhiteSpace(
                    shipment.PurchaseReference) &&
                !string.IsNullOrWhiteSpace(
                    shipment.CourierTrackingCode) &&
                shipment.Status is
                    ManagedShipmentStatus.Confirmed or
                    ManagedShipmentStatus.CarrierAccepted or
                    ManagedShipmentStatus.InTransit or
                    ManagedShipmentStatus.TrackingUnverified or
                    ManagedShipmentStatus.CarrierException or
                    ManagedShipmentStatus.Delivered),
        ParcelProtectionBookingReady = transaction.ParcelProtectionBookingReady
    };

    private static string ThaiStateLabel(
        TransactionState state,
        TransactionExpirationReason? expirationReason) => state switch
    {
        TransactionState.BuyerOfferDraft => "กำลังสร้างข้อเสนอ",
        TransactionState.AwaitingSellerAcceptance => "รอผู้ขายยืนยัน",
        TransactionState.SellerAcceptedAwaitingPayment => "ผู้ขายยืนยันแล้ว · รอผู้ซื้อชำระ",
        TransactionState.LinkActive => "รอผู้ซื้อชำระ",
        TransactionState.CheckoutStarted or TransactionState.PaymentPending => "กำลังตรวจสอบการชำระ",
        TransactionState.PaidAwaitingShipment => "ชำระแล้ว · ส่งสินค้าได้",
        TransactionState.PaidAwaitingDigitalDelivery => "ชำระแล้ว · ส่งมอบข้อมูลได้",
        TransactionState.DigitalDeliverySubmitted => "ผู้ขายแจ้งว่าส่งมอบแล้ว · รอผู้ซื้อยืนยัน",
        TransactionState.TrackingSubmitted => "รอขนส่งตรวจสอบ Tracking",
        TransactionState.TrackingUnverified => "Tracking ยังตรวจสอบไม่ได้",
        TransactionState.InTransit => "กำลังจัดส่ง",
        TransactionState.CarrierException =>
            "การจัดส่งต้องตรวจสอบ",
        TransactionState.DeliveredDisputeWindow => "พัสดุถึงแล้ว · อยู่ในช่วงตรวจสินค้า",
        TransactionState.BuyerConfirmedReceipt => "ผู้ซื้อยืนยันรับสินค้าแล้ว",
        TransactionState.Disputed or TransactionState.ResolutionPending => "พักการจ่ายระหว่างตรวจสอบ",
        TransactionState.PayoutEligible => "พร้อมเริ่มจ่ายเงิน",
        TransactionState.PayoutPending => "กำลังดำเนินการโอนให้ผู้ขาย",
        TransactionState.PaidOut => "โอนเงินให้ผู้ขายแล้ว",
        TransactionState.ShipmentOverdue => "เลยกำหนดส่ง",
        TransactionState.RefundPending => "กำลังคืนเงิน",
        TransactionState.Refunded => "คืนเงินแล้ว",
        TransactionState.Expired when
            expirationReason ==
            TransactionExpirationReason.SellerDidNotRespond =>
                "ผู้ขายไม่ได้ตอบภายในเวลา",
        TransactionState.Expired when
            expirationReason ==
            TransactionExpirationReason.BuyerDidNotPay =>
                "หมดเวลาชำระ",
        TransactionState.Expired => "รายการหมดอายุ",
        TransactionState.Cancelled => "ยกเลิกแล้ว",
        _ => "ฉบับร่าง"
    };
}

public sealed record AgreementAcceptanceView(
    AgreementAcceptanceRole Role,
    string AuthenticationMethod,
    string AgreementCoreSnapshotHash,
    string TermsVersion,
    DateTimeOffset AcceptedAt);

public sealed record AuditView(
    string Name,
    TransactionState FromState,
    TransactionState ToState,
    ActorRole ActorRole,
    DateTimeOffset CreatedAt);
