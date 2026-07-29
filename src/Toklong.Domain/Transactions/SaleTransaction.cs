using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Toklong.Domain.Common;
using Toklong.Domain.Notifications;

namespace Toklong.Domain.Transactions;

public sealed class SaleTransaction
{
    public const int FixedFulfillmentDurationHours = 72;
    public const int PhysicalInspectionWindowHours = 72;
    public const int EvidenceRetentionYears = 5;
    public const int FinancialRetentionYears = 7;
    public const int SellerAcceptanceWindowHours = 24;
    public const int BuyerPaymentWindowHours = 1;
    public const int AgreementSnapshotSchemaVersion = 8;
    public const long MinimumProtectedItemPriceSatang = 100_000;
    public const long MaximumProtectedItemPriceSatang = 99_999_900;

    private readonly List<AuditEvent> _auditEvents = [];
    private readonly List<AgreementAcceptance> _agreementAcceptances = [];
    private readonly List<ExternalEvent> _externalEvents = [];
    private readonly List<NotificationOutboxMessage> _notifications = [];
    private readonly List<DisputeEvidence> _disputeEvidence = [];

    private SaleTransaction() { }

    public Guid Id { get; private set; }
    public string PublicToken { get; private set; } = "";
    public string SellerAccessToken { get; private set; } = "";
    public string? BuyerAccessToken { get; private set; }
    public Guid? BuyerId { get; private set; }
    public Guid? SellerId { get; private set; }
    public InitiatorRole InitiatorRole { get; private set; }
    public TransactionState State { get; private set; }
    public string SellerDisplayName { get; private set; } = "";
    public string SellerContact { get; private set; } = "";
    public string PayoutBankCode { get; private set; } = "";
    public string PayoutAccountName { get; private set; } = "";
    public string PayoutAccountNumber { get; private set; } = "";
    public FulfillmentType FulfillmentType { get; private set; }
    public string ProductName { get; private set; } = "";
    public string Category { get; private set; } = "";
    public ConditionCode Condition { get; private set; }
    public string Description { get; private set; } = "";
    public string KnownDefects { get; private set; } = "";
    public string? PhotoUrl { get; private set; }
    public long PriceSatang { get; private set; }
    public long ShippingFeeSatang { get; private set; }
    public long BuyerTotalSatang { get; private set; }
    public string Currency { get; private set; } = "THB";
    public int ShipByDurationHours { get; private set; }
    public int InspectionWindowDurationHours { get; private set; }
    public string TermsVersion { get; private set; } = "";
    public DateTimeOffset? SellerAcceptedAt { get; private set; }
    public DateTimeOffset SellerAcceptanceDeadlineAt { get; private set; }
    public DateTimeOffset? BuyerPaymentDeadlineAt { get; private set; }
    public TransactionExpirationReason? ExpirationReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ActivatedAt { get; private set; }
    public string? BuyerDisplayName { get; private set; }
    public string? BuyerContact { get; private set; }
    public string? DeliveryProvinceName { get; private set; }
    public string? DeliveryDistrictName { get; private set; }
    public string? DeliverySubdistrictName { get; private set; }
    public string? DeliveryPostalCode { get; private set; }
    public string? DeliveryAddress { get; private set; }
    public string? DeliveryAddressLine { get; private set; }
    public string? ShippingOriginAddress { get; private set; }
    public string? ShippingOriginAddressLine { get; private set; }
    public string? ShippingOriginProvinceName { get; private set; }
    public string? ShippingOriginDistrictName { get; private set; }
    public string? ShippingOriginSubdistrictName { get; private set; }
    public string? ShippingOriginPostalCode { get; private set; }
    public int? PackageWeightGrams { get; private set; }
    public int? PackageWidthCentimeters { get; private set; }
    public int? PackageLengthCentimeters { get; private set; }
    public int? PackageHeightCentimeters { get; private set; }
    public string? ShippingQuoteProvider { get; private set; }
    public string? ShippingQuoteReference { get; private set; }
    public DateTimeOffset? ShippingQuoteExpiresAt { get; private set; }
    public string? ShippingServiceCode { get; private set; }
    public string? ShippingServiceName { get; private set; }
    public string? ShippingPurchaseReference { get; private set; }
    public string? ShippingProviderTrackingCode { get; private set; }
    public string? ShippingCourierTrackingCode { get; private set; }
    public DateTimeOffset? ShippingReservedAt { get; private set; }
    public DateTimeOffset? ShippingConfirmedAt { get; private set; }
    public string? ShippingLastProviderStatus { get; private set; }
    public DateTimeOffset? ShippingLastReconciledAt { get; private set; }
    public DateTimeOffset? ShippingCancelledAt { get; private set; }
    public DateTimeOffset? BuyerAcceptedAt { get; private set; }
    public string? AgreementCoreSnapshotJson { get; private set; }
    public string? AgreementCoreSnapshotHash { get; private set; }
    public DateTimeOffset? AgreementCoreSnapshotCreatedAt { get; private set; }
    public string? ProductSnapshotJson { get; private set; }
    public string? ProductSnapshotHash { get; private set; }
    public int? SnapshotSchemaVersion { get; private set; }
    public DateTimeOffset? AgreementSnapshotCreatedAt { get; private set; }
    public DateTimeOffset? AgreementSnapshotSealedAt { get; private set; }
    public string? TermsSnapshotJson { get; private set; }
    public string? TermsSnapshotHash { get; private set; }
    public DateTimeOffset? ShipByAt { get; private set; }
    public string PaymentProvider { get; private set; } = "manual-bank";
    public string? PaymentReference { get; private set; }
    public long BuyerProtectionFeeSatang { get; private set; }
    public long PlatformFeeSatang { get; private set; }
    public long SellerExpectedNetSatang { get; private set; }
    public string FeePolicyVersion { get; private set; } = "";
    public DateTimeOffset? PaymentConfirmedAt { get; private set; }
    public string? CarrierCode { get; private set; }
    public string? TrackingNumber { get; private set; }
    public TrackingVerificationStatus? TrackingVerificationStatus { get; private set; }
    public DateTimeOffset? TrackingSubmittedAt { get; private set; }
    public DateTimeOffset? FirstCarrierScanAt { get; private set; }
    public DateTimeOffset? InTransitAt { get; private set; }
    public string? DigitalDeliveryStatement { get; private set; }
    public DateTimeOffset? DigitalDeliverySubmittedAt { get; private set; }
    public string? DigitalManualReviewReference { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public string? DeliveryEventId { get; private set; }
    public DateTimeOffset? DeliveryEventReceivedAt { get; private set; }
    public DateTimeOffset? DisputeWindowStartsAt { get; private set; }
    public DateTimeOffset? DisputeWindowEndsAt { get; private set; }
    public DateTimeOffset? BuyerConfirmedAt { get; private set; }
    public PayoutReleaseReason? PayoutReleaseReason { get; private set; }
    public DisputeReason? DisputeReason { get; private set; }
    public string? DisputeStatement { get; private set; }
    public DateTimeOffset? DisputeOpenedAt { get; private set; }
    public DateTimeOffset? DisputeResolvedAt { get; private set; }
    public string? DisputeResolutionReference { get; private set; }
    public DateTimeOffset? RetentionStartsAt { get; private set; }
    public DateTimeOffset? RetentionExpiresAt { get; private set; }
    public DateTimeOffset? LegalHoldPlacedAt { get; private set; }
    public string? LegalHoldReference { get; private set; }
    public string? LegalHoldReason { get; private set; }
    public string? PayoutReference { get; private set; }
    public string PayoutProvider { get; private set; } = "manual-bank";
    public DateTimeOffset? PayoutConfirmedAt { get; private set; }
    public string? RefundReference { get; private set; }
    public DateTimeOffset? RefundRequestedAt { get; private set; }
    public DateTimeOffset? RefundConfirmedAt { get; private set; }
    public string? RefundProviderStatus { get; private set; }
    public DateTimeOffset? RefundActionRequiredAt { get; private set; }
    public DateTimeOffset? RefundActionExpiresAt { get; private set; }
    public DateTimeOffset? RefundInstructionsSentAt { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyCollection<AuditEvent> AuditEvents => _auditEvents;
    public IReadOnlyCollection<AgreementAcceptance> AgreementAcceptances =>
        _agreementAcceptances;
    public IReadOnlyCollection<ExternalEvent> ExternalEvents => _externalEvents;
    public IReadOnlyCollection<NotificationOutboxMessage> Notifications =>
        _notifications;
    public IReadOnlyCollection<DisputeEvidence> DisputeEvidence =>
        _disputeEvidence;
    public bool IsProviderManagedShipment =>
        FulfillmentType == FulfillmentType.PhysicalShipment &&
        !string.IsNullOrWhiteSpace(ShippingPurchaseReference) &&
        !string.IsNullOrWhiteSpace(ShippingProviderTrackingCode);
    public bool RequiresShippingCancellationBeforeRefund =>
        State == TransactionState.RefundPending &&
        IsProviderManagedShipment &&
        !ShippingCancelledAt.HasValue &&
        !FirstCarrierScanAt.HasValue;
    public bool HasTimelyTrustedCarrierAcceptance =>
        IsProviderManagedShipment &&
        FirstCarrierScanAt.HasValue &&
        ShipByAt.HasValue &&
        FirstCarrierScanAt.Value <= ShipByAt.Value;
    public static SaleTransaction CreateBuyerOffer(
        Guid buyerId,
        string buyerDisplayName,
        string buyerContact,
        FulfillmentType fulfillmentType,
        string productName,
        string proposedDescription,
        ConditionCode condition,
        string knownDefects,
        string? photoUrl,
        long priceSatang,
        string? deliveryAddress,
        string? deliveryProvinceName,
        string? deliveryPostalCode,
        string termsVersion,
        DateTimeOffset now,
        TransactionTransitionService transitions,
        string? deliveryDistrictName = null,
        string? deliverySubdistrictName = null,
        string? deliveryAddressLine = null) =>
        CreateBuyerOffer(
            buyerId,
            buyerDisplayName,
            buyerContact,
            buyerContact,
            fulfillmentType,
            productName,
            proposedDescription,
            condition,
            knownDefects,
            photoUrl,
            priceSatang,
            deliveryAddress,
            deliveryProvinceName,
            deliveryPostalCode,
            termsVersion,
            now,
            transitions,
            deliveryDistrictName,
            deliverySubdistrictName,
            deliveryAddressLine);

    public static SaleTransaction CreateBuyerOffer(
        Guid buyerId,
        string buyerDisplayName,
        string buyerContact,
        string intendedSellerContact,
        FulfillmentType fulfillmentType,
        string productName,
        string proposedDescription,
        ConditionCode condition,
        string knownDefects,
        string? photoUrl,
        long priceSatang,
        string? deliveryAddress,
        string? deliveryProvinceName,
        string? deliveryPostalCode,
        string termsVersion,
        DateTimeOffset now,
        TransactionTransitionService transitions,
        string? deliveryDistrictName = null,
        string? deliverySubdistrictName = null,
        string? deliveryAddressLine = null)
    {
        var cleanBuyerName = Required(buyerDisplayName, "ชื่อผู้ซื้อ");
        var cleanBuyerContact = Required(buyerContact, "เบอร์โทรผู้ซื้อ");
        var cleanSellerContact = Required(
            intendedSellerContact, "เบอร์โทรผู้ขาย");
        var cleanProductName = Required(productName, "ชื่อสินค้า");
        var cleanDescription = Required(
            proposedDescription, "รายละเอียด สภาพ อุปกรณ์ และตำหนิ");
        var cleanPhotoUrl = string.IsNullOrWhiteSpace(photoUrl)
            ? null
            : photoUrl.Trim();
        if (buyerId == Guid.Empty)
            throw new DomainException("กรุณาเข้าสู่ระบบผู้ซื้อก่อนสร้างข้อเสนอ");
        if (condition == ConditionCode.AsDescribed)
            throw new DomainException("กรุณาเลือกสภาพสินค้า");
        if (priceSatang is <
                MinimumProtectedItemPriceSatang or >
                MaximumProtectedItemPriceSatang)
            throw new DomainException(
                "ราคาสินค้าต้องอยู่ระหว่าง 1,000–999,999 บาทตามขอบเขตระบบ");
        if (fulfillmentType == FulfillmentType.DigitalHandoff &&
            ContainsDigitalSecret(cleanDescription))
            throw new DomainException("ห้ามใส่รหัสผ่าน รหัสกู้คืน private key หรือข้อมูลลับในข้อเสนอ");
        var cleanDeliveryProvinceName =
            fulfillmentType == FulfillmentType.PhysicalShipment
                ? Required(
                    deliveryProvinceName ?? "",
                    "จังหวัดปลายทาง")
                : null;
        var cleanDeliveryPostalCode =
            fulfillmentType == FulfillmentType.PhysicalShipment
                ? RequiredPostalCode(
                    deliveryPostalCode ?? "")
                : null;
        var cleanDeliveryDistrictName =
            fulfillmentType == FulfillmentType.PhysicalShipment
                ? OptionalAddressRegion(
                    deliveryDistrictName)
                : null;
        var cleanDeliverySubdistrictName =
            fulfillmentType == FulfillmentType.PhysicalShipment
                ? OptionalAddressRegion(
                    deliverySubdistrictName)
                : null;
        var cleanDeliveryAddress =
            fulfillmentType == FulfillmentType.PhysicalShipment
                ? Required(
                    deliveryAddress ?? "",
                    "ที่อยู่จัดส่ง")
                : null;

        var policy = ProductPolicy.Evaluate(
            fulfillmentType,
            fulfillmentType == FulfillmentType.DigitalHandoff
                ? "สินค้าดิจิทัล"
                : "งานอดิเรกและของใช้",
            cleanProductName,
            cleanDescription);
        if (!policy.Allowed)
            throw new DomainException(policy.UserMessage);

        var offer = new SaleTransaction
        {
            Id = Guid.NewGuid(),
            PublicToken = Token(),
            SellerAccessToken = Token(),
            BuyerAccessToken = Token(),
            BuyerId = buyerId,
            State = TransactionState.BuyerOfferDraft,
            InitiatorRole = InitiatorRole.Buyer,
            BuyerDisplayName = cleanBuyerName,
            BuyerContact = cleanBuyerContact,
            SellerContact = cleanSellerContact,
            DeliveryProvinceName =
                cleanDeliveryProvinceName,
            DeliveryDistrictName =
                cleanDeliveryDistrictName,
            DeliverySubdistrictName =
                cleanDeliverySubdistrictName,
            DeliveryPostalCode =
                cleanDeliveryPostalCode,
            DeliveryAddress =
                cleanDeliveryAddress,
            DeliveryAddressLine =
                fulfillmentType ==
                    FulfillmentType.PhysicalShipment
                        ? Required(
                            deliveryAddressLine ??
                            cleanDeliveryAddress!,
                            "บ้านเลขที่และรายละเอียดที่อยู่")
                        : null,
            FulfillmentType = fulfillmentType,
            ProductName = cleanProductName,
            Category = fulfillmentType == FulfillmentType.DigitalHandoff
                ? "สินค้าดิจิทัล"
                : "งานอดิเรกและของใช้",
            Condition = condition,
            Description = cleanDescription,
            KnownDefects = knownDefects.Trim(),
            PhotoUrl = cleanPhotoUrl,
            PriceSatang = priceSatang,
            BuyerTotalSatang = priceSatang,
            ShipByDurationHours = FixedFulfillmentDurationHours,
            InspectionWindowDurationHours =
                PhysicalInspectionWindowHours,
            TermsVersion = termsVersion,
            CreatedAt = now,
            ActivatedAt = now,
            SellerAcceptanceDeadlineAt =
                now.AddHours(SellerAcceptanceWindowHours)
        };

        transitions.Transition(
            offer,
            TransactionState.AwaitingSellerAcceptance,
            ActorRole.Buyer,
            buyerId.ToString("N"),
            "buyer_offer.created",
            now,
            offer.Id.ToString("N"),
            $"buyer-offer:{offer.Id:N}");
        return offer;
    }

    public void EnsureIntendedSeller(string verifiedPhoneNumber)
    {
        var phone = Required(
            verifiedPhoneNumber, "เบอร์โทรที่ยืนยันแล้ว");
        if (!string.Equals(
                SellerContact,
                phone,
                StringComparison.Ordinal))
            throw new DomainException(
                "ข้อเสนอนี้ส่งให้ผู้ขายหมายเลขอื่น");
    }

    public bool IsIntendedSeller(string verifiedPhoneNumber) =>
        !string.IsNullOrWhiteSpace(verifiedPhoneNumber) &&
        string.Equals(
            SellerContact,
            verifiedPhoneNumber.Trim(),
            StringComparison.Ordinal);

    public void AcceptBuyerOffer(
        Guid sellerId,
        string sellerDisplayName,
        string sellerContact,
        string payoutBankCode,
        string payoutAccountName,
        string payoutAccountNumber,
        bool transferRightsAttested,
        DateTimeOffset now,
        TransactionTransitionService transitions,
        long buyerProtectionFeeSatang = 0,
        long platformFeeSatang = 0,
        long? sellerExpectedNetSatang = null,
        string feePolicyVersion = "manual-unconfigured",
        AcceptedShippingQuote? shipping = null)
    {
        if (InitiatorRole != InitiatorRole.Buyer ||
            State != TransactionState.AwaitingSellerAcceptance)
            throw new DomainException("ข้อเสนอนี้ไม่อยู่ในสถานะที่ผู้ขายยอมรับได้");
        EnsureSellerAcceptanceWindowOpen(now);
        if (string.IsNullOrWhiteSpace(Description))
            throw new DomainException(
                "ข้อเสนอนี้มีรายละเอียดสินค้าไม่ครบ กรุณาปฏิเสธและให้ผู้ซื้อสร้างข้อเสนอใหม่");
        if (!transferRightsAttested)
            throw new DomainException("กรุณายืนยันว่าคุณครอบครอง มีสิทธิ์โอน และรายการไม่ต้องห้าม");
        if (sellerId == Guid.Empty)
            throw new DomainException(
                "ไม่พบบัญชีผู้ขายที่ยืนยันตัวตน");
        if (!string.IsNullOrWhiteSpace(BuyerContact) &&
            SecureEquals(BuyerContact, sellerContact))
            throw new DomainException(
                "ผู้ซื้อและผู้ขายต้องเป็นคนละบัญชี");

        var normalizedAccountNumber = new string(
            payoutAccountNumber.Where(char.IsDigit).ToArray());
        if (normalizedAccountNumber.Length is < 10 or > 15)
            throw new DomainException("เลขบัญชีรับเงินต้องมีตัวเลข 10–15 หลัก");
        SellerId = sellerId;
        SellerDisplayName = Required(sellerDisplayName, "ชื่อผู้ขาย");
        SellerContact = Required(sellerContact, "ช่องทางติดต่อผู้ขาย");
        PayoutBankCode = Required(payoutBankCode, "ธนาคารรับเงิน");
        PayoutAccountName = Required(payoutAccountName, "ชื่อบัญชีรับเงิน");
        PayoutAccountNumber = normalizedAccountNumber;
        if (buyerProtectionFeeSatang < 0)
            throw new DomainException(
                "ค่าคุ้มครองผู้ซื้อไม่ถูกต้อง");
        if (platformFeeSatang < 0 || platformFeeSatang > PriceSatang)
            throw new DomainException("ค่าบริการไม่ถูกต้อง");
        var expectedNet = sellerExpectedNetSatang ??
                          PriceSatang - platformFeeSatang;
        var acceptedFeePolicyVersion = Required(
            feePolicyVersion,
            "เวอร์ชันค่าบริการ");
        if (expectedNet < 0 ||
            expectedNet + platformFeeSatang != PriceSatang)
            throw new DomainException("ยอดรับสุทธิของผู้ขายไม่ถูกต้อง");
        BuyerProtectionFeeSatang =
            buyerProtectionFeeSatang;
        PlatformFeeSatang = platformFeeSatang;
        SellerExpectedNetSatang = expectedNet;
        FeePolicyVersion = acceptedFeePolicyVersion;
        ApplyAcceptedShippingQuote(
            shipping,
            now);
        SellerAcceptedAt = now;
        BuyerPaymentDeadlineAt =
            now.AddHours(BuyerPaymentWindowHours);
        CreateSellerAcceptanceEvidence(
            sellerId,
            now);

        transitions.Transition(
            this,
            TransactionState.SellerAcceptedAwaitingPayment,
            ActorRole.Seller,
            sellerId.ToString("N"),
            "buyer_offer.seller_accepted",
            now,
            Id.ToString("N"),
            $"buyer-offer-accept:{Id:N}",
            AcceptanceAuditMetadata(
                AgreementAcceptanceRole.Seller));
    }

    public void DeclineBuyerOffer(
        Guid sellerId,
        DateTimeOffset now,
        TransactionTransitionService transitions)
    {
        if (InitiatorRole != InitiatorRole.Buyer ||
            State != TransactionState.AwaitingSellerAcceptance)
            throw new DomainException("ข้อเสนอนี้ไม่อยู่ในสถานะที่ปฏิเสธได้");
        EnsureSellerAcceptanceWindowOpen(now);
        transitions.Transition(
            this,
            TransactionState.Cancelled,
            ActorRole.Seller,
            sellerId.ToString("N"),
            "buyer_offer.seller_declined",
            now,
            Id.ToString("N"),
            $"buyer-offer-decline:{Id:N}");
    }

    private void ApplyAcceptedShippingQuote(
        AcceptedShippingQuote? shipping,
        DateTimeOffset acceptedAt)
    {
        if (FulfillmentType == FulfillmentType.DigitalHandoff)
        {
            if (shipping is not null)
                throw new DomainException(
                    "รายการดิจิทัลไม่ใช้ค่าจัดส่ง");
            ShippingFeeSatang = 0;
            BuyerTotalSatang = checked(
                PriceSatang +
                BuyerProtectionFeeSatang);
            return;
        }

        if (shipping is null)
            throw new DomainException(
                "กรุณาระบุต้นทาง ขนาดพัสดุ และเลือกค่าจัดส่งก่อนยืนยัน");
        if (shipping.FeeSatang <= 0)
            throw new DomainException(
                "ค่าจัดส่งไม่ถูกต้อง");
        if (shipping.ExpiresAt <
            acceptedAt.AddHours(
                BuyerPaymentWindowHours))
            throw new DomainException(
                "ราคาค่าจัดส่งมีเวลาไม่พอสำหรับการชำระ กรุณาดูราคาใหม่");
        if (shipping.WeightGrams is < 1 or > 30_000)
            throw new DomainException(
                "น้ำหนักพัสดุต้องอยู่ระหว่าง 1 กรัมถึง 30 กิโลกรัม");
        if (shipping.WidthCentimeters is < 1 or > 200 ||
            shipping.LengthCentimeters is < 1 or > 200 ||
            shipping.HeightCentimeters is < 1 or > 200)
            throw new DomainException(
                "ขนาดพัสดุแต่ละด้านต้องอยู่ระหว่าง 1–200 ซม.");

        ShippingOriginAddress = Required(
            shipping.OriginAddress,
            "ที่อยู่ต้นทางจัดส่ง");
        ShippingOriginAddressLine = Required(
            shipping.OriginAddressLine ??
            shipping.OriginAddress,
            "บ้านเลขที่และรายละเอียดต้นทาง");
        ShippingOriginProvinceName = Required(
            shipping.OriginProvinceName,
            "จังหวัดต้นทาง");
        ShippingOriginDistrictName = OptionalAddressRegion(
            shipping.OriginDistrictName);
        ShippingOriginSubdistrictName = OptionalAddressRegion(
            shipping.OriginSubdistrictName);
        ShippingOriginPostalCode = RequiredPostalCode(
            shipping.OriginPostalCode);
        PackageWeightGrams = shipping.WeightGrams;
        PackageWidthCentimeters =
            shipping.WidthCentimeters;
        PackageLengthCentimeters =
            shipping.LengthCentimeters;
        PackageHeightCentimeters =
            shipping.HeightCentimeters;
        ShippingQuoteProvider = Required(
            shipping.Provider,
            "ผู้ให้บริการราคาขนส่ง");
        ShippingQuoteReference = Required(
            shipping.QuoteReference,
            "เลขอ้างอิงราคาขนส่ง");
        ShippingQuoteExpiresAt = shipping.ExpiresAt;
        CarrierCode = Required(
            shipping.CarrierCode,
            "บริษัทขนส่ง").ToUpperInvariant();
        ShippingServiceCode = Required(
            shipping.ServiceCode,
            "บริการขนส่ง");
        ShippingServiceName = Required(
            shipping.ServiceName,
            "ชื่อบริการขนส่ง");
        if (string.Equals(
                shipping.Provider,
                "shippop",
                StringComparison.Ordinal) &&
            (string.IsNullOrWhiteSpace(
                 shipping.PurchaseReference) ||
             string.IsNullOrWhiteSpace(
                 shipping.ProviderTrackingCode) ||
             !shipping.ReservedAt.HasValue))
            throw new DomainException(
                "ยังสร้างรายการจัดส่ง SHIPPOP ไม่สำเร็จ");
        ShippingPurchaseReference =
            CleanOptional(
                shipping.PurchaseReference,
                160,
                "เลขอ้างอิงรายการขนส่ง");
        ShippingProviderTrackingCode =
            CleanOptional(
                shipping.ProviderTrackingCode,
                120,
                "หมายเลขติดตามของผู้ให้บริการ");
        ShippingCourierTrackingCode =
            CleanOptional(
                shipping.CourierTrackingCode,
                120,
                "หมายเลขพัสดุ");
        ShippingReservedAt = shipping.ReservedAt;
        ShippingLastProviderStatus =
            ShippingReservedAt.HasValue
                ? "wait"
                : null;
        ShippingLastReconciledAt =
            ShippingReservedAt;
        ShippingFeeSatang = shipping.FeeSatang;
        BuyerTotalSatang = checked(
            PriceSatang +
            ShippingFeeSatang +
            BuyerProtectionFeeSatang);
    }

    public void BeginCheckout(
        string buyerDisplayName,
        string buyerContact,
        DateTimeOffset now,
        TransactionTransitionService transitions,
        string paymentProvider = "manual-bank",
        string? paymentReference = null,
        long buyerProtectionFeeSatang = 0,
        long platformFeeSatang = 0,
        long? sellerExpectedNetSatang = null,
        string feePolicyVersion = "manual-unconfigured")
    {
        if (InitiatorRole == InitiatorRole.Buyer &&
            State != TransactionState.SellerAcceptedAwaitingPayment)
            throw new DomainException("ผู้ขายยังไม่ได้ยอมรับข้อเสนอ จึงยังชำระไม่ได้");
        EnsureBuyerPaymentWindowOpen(now);
        EnsureAgreementCoreSnapshotIntegrity();
        BuyerAccessToken ??= Token();
        var acceptedBuyerName =
            Required(buyerDisplayName, "ชื่อผู้ซื้อ");
        var acceptedBuyerContact =
            Required(buyerContact, "ช่องทางติดต่อ");
        if (!SecureEquals(
                BuyerDisplayName ?? "",
                acceptedBuyerName) ||
            !SecureEquals(
                BuyerContact ?? "",
                acceptedBuyerContact))
            throw new DomainException(
                "ข้อมูลผู้ซื้อเปลี่ยนหลังผู้ขายยอมรับ กรุณาสร้างข้อเสนอใหม่");
        if (FulfillmentType ==
                FulfillmentType.PhysicalShipment &&
            string.IsNullOrWhiteSpace(DeliveryAddress))
            throw new DomainException(
                "ข้อเสนอเดิมไม่มีที่อยู่จัดส่ง กรุณาสร้างข้อเสนอใหม่");
        if (buyerProtectionFeeSatang < 0)
            throw new DomainException(
                "ค่าคุ้มครองผู้ซื้อไม่ถูกต้อง");
        if (platformFeeSatang < 0 || platformFeeSatang > PriceSatang)
            throw new DomainException("ค่าบริการไม่ถูกต้อง");
        var expectedNet = sellerExpectedNetSatang ??
                          PriceSatang - platformFeeSatang;
        var acceptedFeePolicyVersion = Required(
            feePolicyVersion,
            "เวอร์ชันค่าบริการ");
        if (expectedNet < 0 ||
            expectedNet + platformFeeSatang != PriceSatang)
            throw new DomainException("ยอดรับสุทธิของผู้ขายไม่ถูกต้อง");
        if (buyerProtectionFeeSatang !=
                BuyerProtectionFeeSatang ||
            platformFeeSatang != PlatformFeeSatang ||
            expectedNet != SellerExpectedNetSatang ||
            !string.Equals(
                acceptedFeePolicyVersion,
                FeePolicyVersion,
                StringComparison.Ordinal))
            throw new DomainException(
                "ค่าบริการเปลี่ยนหลังผู้ขายยอมรับ กรุณาสร้างข้อเสนอใหม่");
        PaymentProvider = Required(paymentProvider, "ผู้ให้บริการชำระเงิน");
        PaymentReference = string.IsNullOrWhiteSpace(paymentReference)
            ? $"TK-{now:yyMMdd}-{Id.ToString("N")[..8].ToUpperInvariant()}"
            : paymentReference.Trim();
        BuyerAcceptedAt = now;
        CreateBuyerAcceptanceEvidence(
            BuyerId ??
            throw new DomainException(
                "ไม่พบบัญชีผู้ซื้อของข้อเสนอนี้"),
            now);
        CreateAgreementSnapshot(now);

        transitions.Transition(this, TransactionState.CheckoutStarted, ActorRole.Buyer, BuyerAccessToken,
            "checkout.started", now, Id.ToString("N"), $"checkout:{Id:N}",
            SnapshotAuditMetadata());
        transitions.Transition(this, TransactionState.PaymentPending, ActorRole.Buyer, BuyerAccessToken,
            "payment.awaiting_verification", now, Id.ToString("N"), $"payment-pending:{Id:N}");
    }

    public void ConfirmPayment(string eventId, DateTimeOffset confirmedAt, TransactionTransitionService transitions)
    {
        ConfirmProviderPayment(
            "manual-bank",
            eventId,
            PaymentReference ?? "",
            confirmedAt,
            transitions);
    }

    public void ConfirmStripePayment(
        string eventId,
        string paymentIntentId,
        long amountSatang,
        string currency,
        DateTimeOffset confirmedAt,
        DateTimeOffset receivedAt,
        TransactionTransitionService transitions)
    {
        if (amountSatang != BuyerTotalSatang ||
            !string.Equals(
                currency,
                Currency,
                StringComparison.OrdinalIgnoreCase))
            throw new DomainException(
                "ยอดหรือสกุลเงินจาก Stripe ไม่ตรงกับรายการ");
        if (PaymentConfirmedAt is not null &&
            string.Equals(
                PaymentProvider,
                "stripe",
                StringComparison.Ordinal) &&
            SecureEquals(
                PaymentReference ?? "",
                paymentIntentId))
        {
            EnsureExternalEventIsNew(
                "stripe",
                eventId,
                "payment.reconciled_duplicate_confirmation",
                confirmedAt,
                receivedAt);
            return;
        }
        ConfirmProviderPayment(
            "stripe",
            eventId,
            paymentIntentId,
            confirmedAt,
            transitions,
            receivedAt);
    }

    private void ConfirmProviderPayment(
        string provider,
        string eventId,
        string paymentReference,
        DateTimeOffset confirmedAt,
        TransactionTransitionService transitions,
        DateTimeOffset? receivedAt = null)
    {
        var isExpiredPaymentWindow =
            State == TransactionState.Expired &&
            ExpirationReason ==
            TransactionExpirationReason.BuyerDidNotPay;
        if (State != TransactionState.PaymentPending &&
            !isExpiredPaymentWindow)
            throw new DomainException(
                "รายการนี้ไม่อยู่ในสถานะที่ยืนยันการชำระได้");
        if (!string.Equals(
                PaymentProvider,
                provider,
                StringComparison.Ordinal) ||
            !SecureEquals(
                PaymentReference ?? "",
                paymentReference))
            throw new DomainException(
                "ข้อมูลการชำระไม่ตรงกับรายการ");

        EnsureAgreementSnapshotIntegrity();
        EnsureExternalEventIsNew(
            provider,
            eventId,
            "payment.confirmed",
            confirmedAt,
            receivedAt ?? confirmedAt);
        PaymentConfirmedAt = confirmedAt;
        AgreementSnapshotSealedAt ??= confirmedAt;
        var role = provider == "stripe"
            ? ActorRole.PaymentProvider
            : ActorRole.Reconciliation;
        if (BuyerPaymentDeadlineAt is not null &&
            confirmedAt > BuyerPaymentDeadlineAt)
        {
            transitions.Transition(
                this,
                TransactionState.RefundPending,
                role,
                provider,
                "payment.confirmed_after_deadline_refund_required",
                receivedAt ?? confirmedAt,
                eventId,
                $"{provider}:late-payment:{eventId}",
                SnapshotAuditMetadata());
            return;
        }

        ExpirationReason = null;
        ShipByAt = confirmedAt.AddHours(ShipByDurationHours);
        var target = FulfillmentType == FulfillmentType.PhysicalShipment
            ? TransactionState.PaidAwaitingShipment
            : TransactionState.PaidAwaitingDigitalDelivery;
        transitions.Transition(
            this,
            target,
            role,
            provider,
            "payment.confirmed",
            confirmedAt,
            eventId,
            $"{provider}:{eventId}",
            SnapshotAuditMetadata());
    }

    public bool ExpireIfDue(
        DateTimeOffset now,
        TransactionTransitionService transitions)
    {
        if (State == TransactionState.AwaitingSellerAcceptance &&
            now >= SellerAcceptanceDeadlineAt)
        {
            ExpirationReason =
                TransactionExpirationReason.SellerDidNotRespond;
            transitions.Transition(
                this,
                TransactionState.Expired,
                ActorRole.System,
                "offer-deadline-job",
                "buyer_offer.seller_response_expired",
                now,
                Id.ToString("N"),
                $"offer-expired:seller-response:{Id:N}");
            return true;
        }

        if ((State is TransactionState.SellerAcceptedAwaitingPayment or
             TransactionState.CheckoutStarted or
             TransactionState.PaymentPending) &&
            BuyerPaymentDeadlineAt is not null &&
            now >= BuyerPaymentDeadlineAt)
        {
            ExpirationReason =
                TransactionExpirationReason.BuyerDidNotPay;
            transitions.Transition(
                this,
                TransactionState.Expired,
                ActorRole.System,
                "payment-deadline-job",
                "buyer_offer.payment_window_expired",
                now,
                Id.ToString("N"),
                $"offer-expired:buyer-payment:{Id:N}");
            return true;
        }

        return false;
    }

    public void EnsureSellerAcceptanceWindowOpen(DateTimeOffset now)
    {
        if (now >= SellerAcceptanceDeadlineAt)
            throw new DomainException(
                "ข้อเสนอนี้หมดเวลาตอบรับแล้ว กรุณาให้ผู้ซื้อส่งข้อเสนอใหม่");
    }

    public void EnsureBuyerPaymentWindowOpen(DateTimeOffset now)
    {
        if (BuyerPaymentDeadlineAt is null ||
            now >= BuyerPaymentDeadlineAt)
            throw new DomainException(
                "หมดเวลาชำระแล้ว กรุณาส่งข้อเสนอใหม่ให้ผู้ขายยืนยัน");
    }

    public void SubmitTracking(string sellerToken, string carrierCode, string trackingNumber, DateTimeOffset now, TransactionTransitionService transitions)
    {
        EnsureSeller(sellerToken);
        if (FulfillmentType != FulfillmentType.PhysicalShipment)
            throw new DomainException("รายการดิจิทัลไม่ใช้ Tracking");
        if (IsProviderManagedShipment)
            throw new DomainException(
                "รายการนี้ออกเลขพัสดุผ่านระบบขนส่งอัตโนมัติ ไม่ต้องกรอก Tracking");
        if (ShipByAt is not null && now > ShipByAt)
            throw new DomainException("เลยกำหนดส่งแล้ว กรุณาติดต่อฝ่ายช่วยเหลือ");
        var carrier = SupportedCarrierCatalog.RequireValid(
            carrierCode,
            trackingNumber);
        if (!string.IsNullOrWhiteSpace(ShippingQuoteReference) &&
            !string.Equals(
                CarrierCode,
                carrier.Code,
                StringComparison.Ordinal))
            throw new DomainException(
                "บริษัทขนส่งไม่ตรงกับบริการที่เลือกและชำระไว้");
        var cleanTracking =
            SupportedCarrierCatalog.NormalizeTracking(trackingNumber);
        CarrierCode = carrier.Code;
        TrackingNumber = cleanTracking;
        TrackingVerificationStatus =
            Toklong.Domain.Transactions.TrackingVerificationStatus.Submitted;
        TrackingSubmittedAt = now;
        transitions.Transition(this, TransactionState.TrackingSubmitted, ActorRole.Seller, sellerToken,
            "shipment.tracking_submitted", now, Id.ToString("N"), $"tracking:{Id:N}:{TrackingNumber}");
    }

    public void ConfirmProviderManagedShipment(
        string provider,
        string providerTrackingCode,
        string courierTrackingCode,
        string carrierCode,
        string providerStatus,
        DateTimeOffset confirmedAt,
        TransactionTransitionService transitions)
    {
        if (!IsProviderManagedShipment ||
            !string.Equals(
                ShippingQuoteProvider,
                provider,
                StringComparison.Ordinal) ||
            !SecureEquals(
                ShippingProviderTrackingCode ?? "",
                providerTrackingCode.Trim()))
            throw new DomainException(
                "รายการจัดส่งจากผู้ให้บริการไม่ตรงกับรายการ");
        if (!string.Equals(
                CarrierCode,
                carrierCode.Trim().ToUpperInvariant(),
                StringComparison.Ordinal))
            throw new DomainException(
                "บริษัทขนส่งจากผู้ให้บริการไม่ตรงกับรายการ");
        var cleanTracking = NormalizeProviderTracking(
            courierTrackingCode);
        if (State == TransactionState.TrackingSubmitted &&
            SecureEquals(
                TrackingNumber ?? "",
                cleanTracking))
            return;
        if (State != TransactionState.PaidAwaitingShipment)
            throw new DomainException(
                "รายการนี้ไม่อยู่ในสถานะที่ยืนยันการจัดส่งได้");

        ShippingCourierTrackingCode = cleanTracking;
        ShippingConfirmedAt = confirmedAt;
        ShippingLastProviderStatus = Required(
            providerStatus,
            "สถานะผู้ให้บริการ").ToLowerInvariant();
        ShippingLastReconciledAt = confirmedAt;
        TrackingNumber = cleanTracking;
        TrackingVerificationStatus =
            Toklong.Domain.Transactions
                .TrackingVerificationStatus.Submitted;
        TrackingSubmittedAt = confirmedAt;
        transitions.Transition(
            this,
            TransactionState.TrackingSubmitted,
            ActorRole.Reconciliation,
            provider,
            "shipment.provider_confirmed",
            confirmedAt,
            ShippingProviderTrackingCode!,
            $"shipment-provider-confirmed:{provider}:{ShippingProviderTrackingCode}",
            JsonSerializer.Serialize(new
            {
                Provider = provider,
                ShippingPurchaseReference,
                ShippingProviderTrackingCode,
                ShippingCourierTrackingCode,
                CarrierCode,
                ShippingServiceCode
            }));
    }

    public void RecordShippingProviderReconciliation(
        string provider,
        string providerStatus,
        DateTimeOffset reconciledAt)
    {
        if (!IsProviderManagedShipment ||
            !string.Equals(
                ShippingQuoteProvider,
                provider,
                StringComparison.Ordinal))
            throw new DomainException(
                "ผู้ให้บริการขนส่งไม่ตรงกับรายการ");
        var cleanStatus = Required(
            providerStatus,
            "สถานะผู้ให้บริการ").ToLowerInvariant();
        if (string.Equals(
                ShippingLastProviderStatus,
                cleanStatus,
                StringComparison.Ordinal))
        {
            // Poll frequently enough to surface carrier changes, but avoid a
            // database write and aggregate-version bump for every unchanged
            // provider response.
            if (ShippingLastReconciledAt.HasValue &&
                reconciledAt <
                ShippingLastReconciledAt.Value.AddMinutes(5))
                return;
            ShippingLastReconciledAt = reconciledAt;
            Version++;
            return;
        }
        ShippingLastReconciledAt = reconciledAt;
        ShippingLastProviderStatus = cleanStatus;
        Version++;
        _auditEvents.Add(
            new AuditEvent(
                Id,
                ActorRole.Reconciliation,
                provider,
                "shipment.provider_status_reconciled",
                State,
                State,
                reconciledAt,
                ShippingProviderTrackingCode!,
                $"shipment-provider-status:{provider}:{ShippingProviderTrackingCode}:{cleanStatus}",
                JsonSerializer.Serialize(new
                {
                    ProviderStatus =
                        cleanStatus,
                    ShippingProviderTrackingCode,
                    ShippingCourierTrackingCode
                })));
    }

    public void RecordShippingCancellation(
        string provider,
        DateTimeOffset cancelledAt)
    {
        if (State != TransactionState.RefundPending ||
            !IsProviderManagedShipment ||
            !string.Equals(
                ShippingQuoteProvider,
                provider,
                StringComparison.Ordinal))
            throw new DomainException(
                "รายการนี้ไม่อยู่ในสถานะที่ยกเลิกการจัดส่งได้");
        if (FirstCarrierScanAt.HasValue)
            throw new DomainException(
                "พัสดุเริ่มเดินทางแล้ว จึงยกเลิกผ่านระบบอัตโนมัติไม่ได้");
        if (ShippingCancelledAt.HasValue)
            return;
        ShippingCancelledAt = cancelledAt;
        ShippingLastProviderStatus = "cancel";
        ShippingLastReconciledAt = cancelledAt;
        Version++;
        _auditEvents.Add(
            new AuditEvent(
                Id,
                ActorRole.Reconciliation,
                provider,
                "shipment.provider_cancelled",
                State,
                State,
                cancelledAt,
                ShippingProviderTrackingCode!,
                $"shipment-provider-cancelled:{provider}:{ShippingProviderTrackingCode}",
                "{}"));
    }

    public void RecordShipmentScanDuringRefund(
        string provider,
        string providerStatus,
        DateTimeOffset occurredAt,
        DateTimeOffset reconciledAt,
        TransactionTransitionService transitions)
    {
        if (State != TransactionState.RefundPending ||
            !IsProviderManagedShipment ||
            !string.Equals(
                ShippingQuoteProvider,
                provider,
                StringComparison.Ordinal))
            throw new DomainException(
                "รายการนี้ไม่อยู่ในสถานะตรวจสอบการยกเลิกขนส่ง");
        FirstCarrierScanAt ??= occurredAt;
        ShippingLastProviderStatus = Required(
            providerStatus,
            "สถานะผู้ให้บริการ").ToLowerInvariant();
        ShippingLastReconciledAt = reconciledAt;

        if (HasTimelyTrustedCarrierAcceptance)
        {
            transitions.Transition(
                this,
                TransactionState.TrackingUnverified,
                ActorRole.Reconciliation,
                provider,
                "shipment.timely_acceptance_recovered",
                reconciledAt,
                ShippingProviderTrackingCode!,
                $"shipment-timely-acceptance-recovered:{provider}:{ShippingProviderTrackingCode}",
                JsonSerializer.Serialize(new
                {
                    ProviderStatus =
                        ShippingLastProviderStatus,
                    FirstCarrierScanAt,
                    ShipByAt
                }));
            return;
        }

        Version++;
        _auditEvents.Add(
            new AuditEvent(
                Id,
                ActorRole.Reconciliation,
                provider,
                "shipment.cancellation_skipped_after_carrier_scan",
                State,
                State,
                reconciledAt,
                ShippingProviderTrackingCode!,
                $"shipment-carrier-scan-during-refund:{provider}:{ShippingProviderTrackingCode}",
                JsonSerializer.Serialize(new
                {
                    ProviderStatus =
                        ShippingLastProviderStatus,
                    OccurredAt = occurredAt
                })));
    }

    public void RecordCarrierEvent(
        string eventId,
        string eventType,
        DateTimeOffset occurredAt,
        DateTimeOffset receivedAt,
        TransactionTransitionService transitions,
        string? reportedCarrierCode = null,
        string? reportedTrackingNumber = null)
    {
        if (FulfillmentType != FulfillmentType.PhysicalShipment)
            throw new DomainException("รายการดิจิทัลไม่รับสถานะจากขนส่ง");
        if (!string.IsNullOrWhiteSpace(reportedCarrierCode) &&
            !SecureEquals(
                CarrierCode ?? "",
                reportedCarrierCode.Trim().ToUpperInvariant()))
            throw new DomainException(
                "บริษัทขนส่งจาก event ไม่ตรงกับรายการ");
        if (!string.IsNullOrWhiteSpace(reportedTrackingNumber))
        {
            var cleanTracking = new string(
                reportedTrackingNumber
                    .Where(char.IsAsciiLetterOrDigit)
                    .Select(char.ToUpperInvariant)
                    .ToArray());
            if (!SecureEquals(
                    TrackingNumber ?? "",
                    cleanTracking))
                throw new DomainException(
                    "หมายเลขติดตามจาก event ไม่ตรงกับรายการ");
        }
        var target = eventType.ToLowerInvariant() switch
        {
            "in_transit" => TransactionState.InTransit,
            "unverified" => TransactionState.TrackingUnverified,
            "delivered" => TransactionState.DeliveredDisputeWindow,
            _ => throw new DomainException("ไม่รองรับสถานะขนส่งนี้")
        };
        if (target == TransactionState.DeliveredDisputeWindow)
            EnsureAgreementSnapshotIntegrity();
        EnsureExternalEventIsNew(
            CarrierCode ?? "carrier",
            eventId,
            eventType,
            occurredAt,
            receivedAt);

        if (target == TransactionState.DeliveredDisputeWindow)
        {
            TrackingVerificationStatus =
                Toklong.Domain.Transactions.TrackingVerificationStatus.Delivered;
            FirstCarrierScanAt ??= occurredAt;
            DeliveredAt = occurredAt;
            DeliveryEventId = eventId;
            DeliveryEventReceivedAt = receivedAt;
            DisputeWindowStartsAt = occurredAt;
            DisputeWindowEndsAt =
                occurredAt.AddHours(InspectionWindowDurationHours);
        }
        else if (target == TransactionState.InTransit)
        {
            TrackingVerificationStatus =
                Toklong.Domain.Transactions.TrackingVerificationStatus.VerifiedInTransit;
            FirstCarrierScanAt ??= occurredAt;
            InTransitAt ??= occurredAt;
        }
        else if (target == TransactionState.TrackingUnverified)
        {
            TrackingVerificationStatus =
                Toklong.Domain.Transactions.TrackingVerificationStatus.Unverified;
        }

        transitions.Transition(this, target, ActorRole.CarrierProvider, CarrierCode ?? "carrier",
            $"carrier.{eventType.ToLowerInvariant()}", occurredAt, eventId, $"carrier:{eventId}");
    }

    public void RecordUnverifiedCarrierEvidence(
        string provider,
        string eventId,
        string providerStatus,
        DateTimeOffset receivedAt,
        TransactionTransitionService transitions)
    {
        if (!IsProviderManagedShipment ||
            !string.Equals(
                ShippingQuoteProvider,
                provider,
                StringComparison.Ordinal))
            throw new DomainException(
                "ผู้ให้บริการขนส่งไม่ตรงกับรายการ");
        if (State is not (
                TransactionState.TrackingSubmitted or
                TransactionState.InTransit or
                TransactionState.RefundPending))
            throw new DomainException(
                "รายการนี้ไม่อยู่ในสถานะตรวจสอบการจัดส่ง");

        var cleanStatus = Required(
            providerStatus,
            "สถานะผู้ให้บริการ").ToLowerInvariant();
        EnsureExternalEventIsNew(
            CarrierCode ?? provider,
            eventId,
            "unverified",
            receivedAt,
            receivedAt);
        ShippingLastProviderStatus = cleanStatus;
        ShippingLastReconciledAt = receivedAt;
        TrackingVerificationStatus =
            Toklong.Domain.Transactions
                .TrackingVerificationStatus.Unverified;
        transitions.Transition(
            this,
            TransactionState.TrackingUnverified,
            ActorRole.Reconciliation,
            provider,
            "shipment.delivery_time_unverified",
            receivedAt,
            eventId,
            $"shipment-delivery-time-unverified:{provider}:{eventId}");
    }

    public void SubmitDigitalDelivery(
        string sellerToken,
        string statement,
        DateTimeOffset now,
        TransactionTransitionService transitions)
    {
        EnsureSeller(sellerToken);
        if (FulfillmentType != FulfillmentType.DigitalHandoff)
            throw new DomainException("รายการนี้ต้องส่งผ่านขนส่งและ Tracking");
        if (ShipByAt is not null && now > ShipByAt)
            throw new DomainException("เลยกำหนดส่งมอบแล้ว กรุณาติดต่อฝ่ายช่วยเหลือ");

        var clean = Required(statement, "รายละเอียดการส่งมอบ");
        if (clean.Length > 500)
            throw new DomainException("รายละเอียดการส่งมอบยาวเกิน 500 ตัวอักษร");
        if (ContainsDigitalSecret(clean))
            throw new DomainException("ห้ามใส่รหัสผ่าน รหัสกู้คืน private key หรือข้อมูลลับใน TOKLONG กรุณาส่งผ่านช่องทางที่ตกลงกัน");

        DigitalDeliveryStatement = clean;
        DigitalDeliverySubmittedAt = now;
        transitions.Transition(this, TransactionState.DigitalDeliverySubmitted, ActorRole.Seller, sellerToken,
            "digital_delivery.submitted", now, Id.ToString("N"), $"digital-delivery:{Id:N}");
    }

    public void ConfirmReceipt(string buyerToken, DateTimeOffset now, TransactionTransitionService transitions)
    {
        EnsureBuyer(buyerToken);
        if (DisputeOpenedAt is not null)
            throw new DomainException("รายการนี้มีข้อโต้แย้งอยู่ จึงยังจ่ายเงินไม่ได้");
        EnsureAgreementSnapshotIntegrity();
        BuyerConfirmedAt = now;
        PayoutReleaseReason =
            Toklong.Domain.Transactions.PayoutReleaseReason
                .BuyerConfirmedAfterInspection;
        transitions.Transition(this, TransactionState.BuyerConfirmedReceipt, ActorRole.Buyer, buyerToken,
            "buyer.receipt_confirmed", now, Id.ToString("N"), $"receipt:{Id:N}");
        transitions.Transition(this, TransactionState.PayoutEligible, ActorRole.Buyer, buyerToken,
            "payout.eligible_buyer_confirmation", now, Id.ToString("N"), $"eligibility:buyer:{Id:N}");
    }

    public void OpenDispute(string buyerToken, DisputeReason reason, string statement, DateTimeOffset now, TransactionTransitionService transitions)
    {
        EnsureBuyer(buyerToken);
        if (FulfillmentType == FulfillmentType.PhysicalShipment &&
            (DisputeWindowEndsAt is null || now >= DisputeWindowEndsAt))
            throw new DomainException("หมดเวลาการแจ้งปัญหาปกติแล้ว");
        var cleanStatement = Required(statement, "รายละเอียดปัญหา");
        if (FulfillmentType == FulfillmentType.DigitalHandoff &&
            ContainsDigitalSecret(cleanStatement))
            throw new DomainException("ห้ามใส่รหัสผ่าน รหัสกู้คืน private key หรือข้อมูลลับในหลักฐาน");
        DisputeReason = reason;
        DisputeStatement = cleanStatement;
        DisputeOpenedAt = now;
        DisputeResolvedAt = null;
        DisputeResolutionReference = null;
        transitions.Transition(this, TransactionState.Disputed, ActorRole.Buyer, buyerToken,
            "dispute.opened", now, Id.ToString("N"), $"dispute:{Id:N}");
    }

    public void BeginDisputeResolution(
        string reviewReference,
        string actorId,
        string metadataJson,
        string idempotencyKey,
        DateTimeOffset now,
        TransactionTransitionService transitions)
    {
        var cleanReference = Required(
            reviewReference,
            "เลขอ้างอิงการตรวจสอบ");
        transitions.Transition(
            this,
            TransactionState.ResolutionPending,
            ActorRole.Reconciliation,
            Required(actorId, "ผู้ตรวจเคส"),
            "dispute.review_started",
            now,
            cleanReference,
            Required(idempotencyKey, "idempotency key"),
            Required(metadataJson, "audit metadata"));
    }

    public DisputeEvidence RecordDisputeEvidence(
        Guid evidenceId,
        DisputeEvidenceParty party,
        Guid submittedById,
        DisputeEvidenceType evidenceType,
        string description,
        string storageReference,
        string contentType,
        long lengthBytes,
        string sha256,
        string idempotencyKey,
        DateTimeOffset now)
    {
        if (State is not
            (TransactionState.Disputed or
             TransactionState.ResolutionPending))
            throw new DomainException(
                "ส่งหลักฐานได้เฉพาะระหว่างตรวจสอบข้อโต้แย้ง");
        if (evidenceId == Guid.Empty ||
            submittedById == Guid.Empty)
            throw new DomainException(
                "ข้อมูลผู้ส่งหลักฐานไม่ถูกต้อง");
        var cleanIdempotencyKey = Required(
            idempotencyKey,
            "idempotency key");
        var existing = _disputeEvidence.SingleOrDefault(
            item =>
                item.Party == party &&
                string.Equals(
                    item.IdempotencyKey,
                    cleanIdempotencyKey,
                    StringComparison.Ordinal));
        if (existing is not null)
            return existing;
        if (_disputeEvidence.Count(item => item.Party == party) >= 10)
            throw new DomainException(
                "ส่งหลักฐานได้ไม่เกิน 10 ภาพต่อฝ่าย");
        var cleanDescription = ReusableCredentialGuard.Reject(
            Required(description, "คำอธิบายหลักฐาน"));
        var cleanStorageReference = Required(
            storageReference,
            "ที่เก็บหลักฐาน");
        var cleanContentType = Required(
            contentType,
            "ชนิดไฟล์");
        if (!string.Equals(
                cleanContentType,
                "image/jpeg",
                StringComparison.Ordinal))
            throw new DomainException(
                "หลักฐานที่จัดเก็บต้องเป็นภาพ JPEG ที่ผ่านการแปลงแล้ว");
        if (lengthBytes is < 1 or > 8_000_000)
            throw new DomainException(
                "ขนาดหลักฐานไม่ถูกต้อง");
        var cleanHash = Required(sha256, "SHA-256");
        if (cleanHash.Length != 64 ||
            !cleanHash.All(Uri.IsHexDigit))
            throw new DomainException(
                "SHA-256 ของหลักฐานไม่ถูกต้อง");

        var evidence = new DisputeEvidence(
            evidenceId,
            Id,
            party,
            submittedById,
            evidenceType,
            cleanDescription,
            cleanStorageReference,
            cleanContentType,
            lengthBytes,
            cleanHash.ToLowerInvariant(),
            cleanIdempotencyKey,
            now);
        _disputeEvidence.Add(evidence);
        var actorRole = party == DisputeEvidenceParty.Buyer
            ? ActorRole.Buyer
            : ActorRole.Seller;
        _auditEvents.Add(
            new AuditEvent(
                Id,
                actorRole,
                submittedById.ToString("N"),
                "dispute.evidence_submitted",
                State,
                State,
                now,
                evidenceId.ToString("N"),
                $"dispute-evidence:{Id:N}:{party}:{cleanIdempotencyKey}",
                JsonSerializer.Serialize(new
                {
                    evidenceId,
                    party = party.ToString(),
                    evidenceType = evidenceType.ToString(),
                    sha256 = evidence.Sha256
                })));
        Version++;
        return evidence;
    }

    public bool RequestDisputeEvidence(
        Guid requestId,
        DisputeEvidenceParty party,
        Guid requestedByUserId,
        string requiredEvidence,
        DateTimeOffset dueAt,
        DateTimeOffset now)
    {
        if (State is not
            (TransactionState.Disputed or
             TransactionState.ResolutionPending))
            throw new DomainException(
                "ขอหลักฐานเพิ่มได้เฉพาะระหว่างตรวจสอบข้อโต้แย้ง");
        if (requestId == Guid.Empty ||
            requestedByUserId == Guid.Empty)
            throw new DomainException(
                "ข้อมูลคำขอหลักฐานไม่ถูกต้อง");
        var idempotencyKey =
            $"dispute-evidence-request:{requestId:N}:{party}";
        if (_auditEvents.Any(item =>
                string.Equals(
                    item.IdempotencyKey,
                    idempotencyKey,
                    StringComparison.Ordinal)))
            return false;
        if (dueAt <= now ||
            dueAt > now.AddDays(7))
            throw new DomainException(
                "กำหนดส่งหลักฐานต้องอยู่ภายใน 7 วัน");
        var cleanEvidence = ReusableCredentialGuard.Reject(
            Required(
                requiredEvidence,
                "หลักฐานที่ต้องการ"));
        if (cleanEvidence.Length > 500)
            throw new DomainException(
                "รายละเอียดหลักฐานยาวเกิน 500 ตัวอักษร");
        var recipient = party == DisputeEvidenceParty.Buyer
            ? BuyerContact
            : SellerContact;
        if (string.IsNullOrWhiteSpace(recipient))
            throw new DomainException(
                "ไม่พบช่องทางแจ้งเตือนฝ่ายที่ต้องส่งหลักฐาน");

        _auditEvents.Add(
            new AuditEvent(
                Id,
                ActorRole.Reconciliation,
                requestedByUserId.ToString("N"),
                "dispute.evidence_requested",
                State,
                State,
                now,
                requestId.ToString("N"),
                idempotencyKey,
                JsonSerializer.Serialize(new
                {
                    requestId,
                    party = party.ToString(),
                    requiredEvidence = cleanEvidence,
                    dueAt
                })));
        _notifications.Add(
            NotificationOutboxMessage.Create(
                Id,
                party == DisputeEvidenceParty.Buyer
                    ? "buyer"
                    : "seller",
                recipient,
                "dispute_evidence_requested",
                now,
                now,
                cleanEvidence,
                dueAt));
        Version++;
        return true;
    }

    public void ResolveDisputeForPayout(
        string reviewReference,
        string actorId,
        string metadataJson,
        string idempotencyKey,
        DateTimeOffset now,
        TransactionTransitionService transitions)
    {
        if (DisputeOpenedAt is null)
            throw new DomainException("รายการนี้ไม่มีข้อโต้แย้ง");
        DisputeResolvedAt = now;
        DisputeResolutionReference = Required(
            reviewReference,
            "เลขอ้างอิงการตัดสิน");
        EnsureAgreementSnapshotIntegrity();
        PayoutReleaseReason =
            Toklong.Domain.Transactions.PayoutReleaseReason
                .DisputeResolvedForSeller;
        transitions.Transition(
            this,
            TransactionState.PayoutEligible,
            ActorRole.Reconciliation,
            Required(actorId, "ผู้อนุมัติ"),
            "dispute.resolved_for_seller",
            now,
            DisputeResolutionReference,
            Required(idempotencyKey, "idempotency key"),
            Required(metadataJson, "audit metadata"));
    }

    public void ResolveDisputeForRefund(
        string reviewReference,
        string actorId,
        string metadataJson,
        string idempotencyKey,
        DateTimeOffset now,
        TransactionTransitionService transitions)
    {
        if (DisputeOpenedAt is null)
            throw new DomainException("รายการนี้ไม่มีข้อโต้แย้ง");
        DisputeResolvedAt = now;
        DisputeResolutionReference = Required(
            reviewReference,
            "เลขอ้างอิงการตัดสิน");
        transitions.Transition(
            this,
            TransactionState.RefundPending,
            ActorRole.Reconciliation,
            Required(actorId, "ผู้อนุมัติ"),
            "dispute.resolved_for_buyer",
            now,
            DisputeResolutionReference,
            Required(idempotencyKey, "idempotency key"),
            Required(metadataJson, "audit metadata"));
    }

    public bool MarkShipmentOverdue(
        DateTimeOffset now,
        TransactionTransitionService transitions)
    {
        var awaitingFulfillment =
            State is TransactionState.PaidAwaitingShipment or
                TransactionState.PaidAwaitingDigitalDelivery ||
            (IsProviderManagedShipment &&
             FirstCarrierScanAt is null &&
             (State is TransactionState.TrackingSubmitted or
                 TransactionState.TrackingUnverified));
        if (!awaitingFulfillment ||
            ShipByAt is null ||
            now < ShipByAt)
            return false;
        transitions.Transition(
            this,
            TransactionState.ShipmentOverdue,
            ActorRole.System,
            "fulfillment-deadline-job",
            "fulfillment.deadline_missed",
            now,
            Id.ToString("N"),
            $"fulfillment-overdue:{Id:N}");
        transitions.Transition(
            this,
            TransactionState.RefundPending,
            ActorRole.System,
            "fulfillment-deadline-job",
            "refund.required_fulfillment_overdue",
            now,
            Id.ToString("N"),
            $"refund-required:fulfillment-overdue:{Id:N}");
        return true;
    }

    public void EvaluateDeadline(DateTimeOffset now, TransactionTransitionService transitions)
    {
        if (FulfillmentType == FulfillmentType.DigitalHandoff)
            return;
        if (State != TransactionState.DeliveredDisputeWindow || DisputeWindowEndsAt is null || now < DisputeWindowEndsAt)
            return;
        if (DisputeOpenedAt is not null &&
            DisputeResolvedAt is null)
            throw new DomainException("ข้อโต้แย้งที่เปิดอยู่บล็อกการจ่ายเงิน");
        EnsureAgreementSnapshotIntegrity();
        PayoutReleaseReason =
            Toklong.Domain.Transactions.PayoutReleaseReason
                .PhysicalInspectionWindowElapsed;
        transitions.Transition(this, TransactionState.PayoutEligible, ActorRole.System, "release-job",
            "payout.eligible_deadline", now, Id.ToString("N"), $"eligibility:deadline:{Id:N}");
    }

    public void AuthorizeDigitalRelease(
        string reviewReference,
        DateTimeOffset now,
        TransactionTransitionService transitions)
    {
        if (FulfillmentType != FulfillmentType.DigitalHandoff)
            throw new DomainException("manual digital release ใช้ได้เฉพาะรายการดิจิทัล");
        if (DisputeOpenedAt is not null &&
            DisputeResolvedAt is null)
            throw new DomainException("ข้อโต้แย้งที่เปิดอยู่บล็อกการจ่ายเงิน");
        EnsureAgreementSnapshotIntegrity();
        DigitalManualReviewReference = Required(reviewReference, "เลขอ้างอิงการตรวจสอบ");
        PayoutReleaseReason =
            Toklong.Domain.Transactions.PayoutReleaseReason
                .DigitalManualReview;
        transitions.Transition(this, TransactionState.PayoutEligible, ActorRole.Reconciliation, "manual-review",
            "payout.eligible_digital_manual_review", now, reviewReference, $"digital-review:{Id:N}:{reviewReference}");
    }

    public void StartPayout(
        string reference,
        DateTimeOffset now,
        TransactionTransitionService transitions,
        string provider = "manual-bank")
    {
        if (DisputeOpenedAt is not null &&
            DisputeResolvedAt is null)
            throw new DomainException("ข้อโต้แย้งที่เปิดอยู่บล็อกการจ่ายเงิน");
        EnsureAgreementSnapshotIntegrity();
        if (string.IsNullOrWhiteSpace(PayoutBankCode) ||
            string.IsNullOrWhiteSpace(PayoutAccountName) ||
            string.IsNullOrWhiteSpace(PayoutAccountNumber))
            throw new DomainException("ยังไม่มีบัญชีรับเงินของผู้ขาย จึงยังเริ่มจ่ายเงินไม่ได้");
        PayoutReference = Required(reference, "เลขอ้างอิงการจ่าย");
        PayoutProvider = Required(provider, "ผู้ให้บริการโอนเงิน");
        transitions.Transition(this, TransactionState.PayoutPending, ActorRole.Reconciliation, PayoutProvider,
            "payout.instruction_created", now, reference, $"payout:{Id:N}");
    }

    public void ConfirmPayout(string eventId, DateTimeOffset confirmedAt, TransactionTransitionService transitions)
    {
        EnsureExternalEventIsNew(PayoutProvider, eventId, "payout.confirmed", confirmedAt, confirmedAt);
        PayoutConfirmedAt = confirmedAt;
        transitions.Transition(this, TransactionState.PaidOut, ActorRole.Reconciliation, PayoutProvider,
            "payout.confirmed", confirmedAt, eventId, $"payout-confirmed:{PayoutProvider}:{eventId}");
    }

    public void RecordRefundInstruction(
        string provider,
        string refundReference,
        DateTimeOffset requestedAt,
        string providerStatus = "pending")
    {
        if (State != TransactionState.RefundPending)
            throw new DomainException(
                "รายการนี้ไม่อยู่ในสถานะที่เริ่มคืนเงินได้");
        if (!string.Equals(
                PaymentProvider,
                provider,
                StringComparison.Ordinal))
            throw new DomainException(
                "ผู้ให้บริการคืนเงินไม่ตรงกับรายการ");
        var cleanReference = Required(
            refundReference,
            "เลขอ้างอิงการคืนเงิน");
        if (!string.IsNullOrWhiteSpace(RefundReference))
        {
            if (!SecureEquals(RefundReference, cleanReference))
                throw new DomainException(
                    "รายการนี้มีคำขอคืนเงินอื่นอยู่แล้ว");
            return;
        }

        RefundReference = cleanReference;
        RefundRequestedAt = requestedAt;
        RefundProviderStatus = NormalizeRefundProgressStatus(
            providerStatus);
        Version++;
        _auditEvents.Add(new AuditEvent(
            Id,
            ActorRole.System,
            "refund-worker",
            "refund.instruction_created",
            State,
            State,
            requestedAt,
            cleanReference,
            $"refund-instruction:{Id:N}",
            "{}"));
    }

    public void RecordRefundProgress(
        string provider,
        string eventId,
        string refundReference,
        string paymentReference,
        long amountSatang,
        string currency,
        string providerStatus,
        DateTimeOffset occurredAt,
        DateTimeOffset receivedAt,
        DateTimeOffset? actionExpiresAt = null,
        DateTimeOffset? instructionsSentAt = null)
    {
        if (State != TransactionState.RefundPending)
            throw new DomainException(
                "รายการนี้ไม่อยู่ในสถานะติดตามการคืนเงิน");
        ValidateRefundProviderData(
            provider,
            refundReference,
            paymentReference,
            amountSatang,
            currency);
        var cleanStatus = NormalizeRefundProgressStatus(
            providerStatus);
        if (cleanStatus == "succeeded")
            throw new DomainException(
                "ต้องยืนยัน refund succeeded ผ่านขั้นตอนยืนยันคืนเงิน");

        EnsureExternalEventIsNew(
            provider,
            eventId,
            $"refund.{cleanStatus}",
            occurredAt,
            receivedAt);
        var enteredActionRequired =
            cleanStatus == "requires_action" &&
            (!string.Equals(
                 RefundProviderStatus,
                 "requires_action",
                 StringComparison.Ordinal) ||
             !RefundActionRequiredAt.HasValue);
        RefundReference ??= Required(
            refundReference,
            "เลขอ้างอิงการคืนเงิน");
        RefundRequestedAt ??= occurredAt;
        RefundProviderStatus = cleanStatus;
        if (cleanStatus == "requires_action")
        {
            RefundActionRequiredAt = occurredAt;
            RefundActionExpiresAt = actionExpiresAt;
            RefundInstructionsSentAt = instructionsSentAt;
        }
        Version++;
        _auditEvents.Add(new AuditEvent(
            Id,
            ActorRole.PaymentProvider,
            provider,
            cleanStatus == "requires_action"
                ? "refund.action_required"
                : "refund.processing",
            State,
            State,
            occurredAt,
            eventId,
            $"{provider}:refund-progress:{eventId}",
            JsonSerializer.Serialize(new
            {
                ProviderStatus = cleanStatus,
                ActionExpiresAt = actionExpiresAt,
                InstructionsSentAt = instructionsSentAt
            })));

        if (enteredActionRequired &&
            !string.IsNullOrWhiteSpace(BuyerContact))
            _notifications.Add(NotificationOutboxMessage.Create(
                Id,
                "buyer",
                BuyerContact,
                "refund_action_required",
                occurredAt,
                occurredAt,
                actionDeadlineAt: actionExpiresAt));
    }

    public void ConfirmRefund(
        string provider,
        string eventId,
        string refundReference,
        string paymentReference,
        long amountSatang,
        string currency,
        DateTimeOffset confirmedAt,
        DateTimeOffset receivedAt,
        TransactionTransitionService transitions)
    {
        if (State != TransactionState.RefundPending)
            throw new DomainException(
                "รายการนี้ไม่อยู่ในสถานะที่ยืนยันการคืนเงินได้");
        ValidateRefundProviderData(
            provider,
            refundReference,
            paymentReference,
            amountSatang,
            currency);

        EnsureExternalEventIsNew(
            provider,
            eventId,
            "refund.succeeded",
            confirmedAt,
            receivedAt);
        RefundReference = Required(
            refundReference,
            "เลขอ้างอิงการคืนเงิน");
        RefundRequestedAt ??= confirmedAt;
        RefundConfirmedAt = confirmedAt;
        RefundProviderStatus = "succeeded";
        transitions.Transition(
            this,
            TransactionState.Refunded,
            ActorRole.PaymentProvider,
            provider,
            "refund.confirmed",
            confirmedAt,
            eventId,
            $"{provider}:refund:{eventId}");
    }

    private void ValidateRefundProviderData(
        string provider,
        string refundReference,
        string paymentReference,
        long amountSatang,
        string currency)
    {
        if (!string.Equals(
                PaymentProvider,
                provider,
                StringComparison.Ordinal) ||
            !SecureEquals(
                PaymentReference ?? "",
                paymentReference) ||
            amountSatang != BuyerTotalSatang ||
            !string.Equals(
                Currency,
                currency,
                StringComparison.OrdinalIgnoreCase))
            throw new DomainException(
                "ข้อมูลการคืนเงินไม่ตรงกับรายการ");
        if (!string.IsNullOrWhiteSpace(RefundReference) &&
            !SecureEquals(RefundReference, refundReference))
            throw new DomainException(
                "เลขอ้างอิงการคืนเงินไม่ตรงกับรายการ");
    }

    private static string NormalizeRefundProgressStatus(
        string value)
    {
        var clean = Required(
                value,
                "สถานะการคืนเงิน")
            .ToLowerInvariant();
        return clean is "pending" or "requires_action" or
            "succeeded" or "failed" or "canceled"
            ? clean
            : throw new DomainException(
                "ไม่รองรับสถานะการคืนเงินนี้");
    }

    public bool HasExternalEvent(string provider, string eventId) =>
        _externalEvents.Any(x => x.Provider == provider && x.EventId == eventId);

    public bool HasActiveLegalHold =>
        LegalHoldPlacedAt.HasValue;

    public void PlaceLegalHold(
        string reference,
        string reason,
        DateTimeOffset now)
    {
        var cleanReference = Required(
            reference,
            "เลขอ้างอิง legal hold");
        var cleanReason = Required(
            reason,
            "เหตุผล legal hold");
        if (cleanReference.Length > 160)
            throw new DomainException(
                "เลขอ้างอิง legal hold ยาวเกิน 160 ตัวอักษร");
        if (cleanReason.Length > 500)
            throw new DomainException(
                "เหตุผล legal hold ยาวเกิน 500 ตัวอักษร");
        var idempotencyKey =
            $"legal-hold-place:{Id:N}:{cleanReference}";
        if (_auditEvents.Any(
                audit =>
                    string.Equals(
                        audit.IdempotencyKey,
                        idempotencyKey,
                        StringComparison.Ordinal)))
            return;
        if (HasActiveLegalHold)
        {
            if (SecureEquals(
                    LegalHoldReference ?? "",
                    cleanReference))
                return;
            throw new DomainException(
                "รายการนี้มี legal hold อื่นอยู่แล้ว");
        }

        LegalHoldPlacedAt = now;
        LegalHoldReference = cleanReference;
        LegalHoldReason = cleanReason;
        Version++;
        _auditEvents.Add(new AuditEvent(
            Id,
            ActorRole.Reconciliation,
            "privacy-operator",
            "retention.legal_hold_placed",
            State,
            State,
            now,
            cleanReference,
            idempotencyKey,
            JsonSerializer.Serialize(new
            {
                Reference = cleanReference,
                Reason = cleanReason
            })));
    }

    public void ReleaseLegalHold(
        string reference,
        DateTimeOffset now)
    {
        var cleanReference = Required(
            reference,
            "เลขอ้างอิง legal hold");
        if (cleanReference.Length > 160)
            throw new DomainException(
                "เลขอ้างอิง legal hold ยาวเกิน 160 ตัวอักษร");
        if (!HasActiveLegalHold)
            return;
        if (!SecureEquals(
                LegalHoldReference ?? "",
                cleanReference))
            throw new DomainException(
                "เลขอ้างอิง legal hold ไม่ตรงกับรายการ");

        LegalHoldPlacedAt = null;
        LegalHoldReference = null;
        LegalHoldReason = null;
        Version++;
        _auditEvents.Add(new AuditEvent(
            Id,
            ActorRole.Reconciliation,
            "privacy-operator",
            "retention.legal_hold_released",
            State,
            State,
            now,
            cleanReference,
            $"legal-hold-release:{Id:N}:{cleanReference}",
            "{}"));
    }

    internal void ApplyTransition(
        TransactionState state,
        DateTimeOffset occurredAt)
    {
        State = state;
        if (IsRetentionTerminalState(state))
        {
            var retentionStart =
                DisputeResolvedAt.HasValue &&
                DisputeResolvedAt.Value > occurredAt
                    ? DisputeResolvedAt.Value
                    : occurredAt;
            RetentionStartsAt = retentionStart;
            RetentionExpiresAt =
                retentionStart.AddYears(
                    EvidenceRetentionYears);
        }
        Version++;
    }

    internal void AddAudit(AuditEvent auditEvent) => _auditEvents.Add(auditEvent);

    internal void QueueTransitionNotifications(
        string eventName,
        DateTimeOffset occurredAt)
    {
        var messages = eventName switch
        {
            "buyer_offer.created" =>
                SellerNotice("buyer_offer_received"),
            "buyer_offer.seller_accepted" =>
                BuyerNotice("seller_accepted"),
            "payment.confirmed" =>
                SellerNotice("payment_confirmed"),
            "shipment.tracking_submitted" =>
                BuyerNotice("tracking_submitted"),
            "carrier.delivered" =>
                BuyerNotice("delivered"),
            "dispute.opened" =>
                BuyerNotice("dispute_opened")
                    .Concat(
                        SellerNotice("dispute_opened"))
                    .ToArray(),
            "dispute.resolved_for_seller" =>
                BuyerNotice("dispute_resolved_for_seller")
                    .Concat(
                        SellerNotice(
                            "dispute_resolved_for_seller"))
                    .ToArray(),
            "payout.instruction_created" =>
                SellerNotice("payout_started"),
            "payout.confirmed" =>
                SellerNotice("payout_confirmed"),
            "refund.required_fulfillment_overdue" or
            "payment.confirmed_after_deadline_refund_required" =>
                BuyerNotice("refund_started"),
            "dispute.resolved_for_buyer" =>
                BuyerNotice("refund_started")
                    .Concat(
                        SellerNotice(
                            "dispute_resolved_for_buyer"))
                    .ToArray(),
            "refund.confirmed" =>
                BuyerNotice("refund_confirmed"),
            _ => []
        };
        foreach (var message in messages)
            _notifications.Add(NotificationOutboxMessage.Create(
                Id,
                message.Audience,
                message.Recipient,
                message.Template,
                occurredAt,
                occurredAt));

        if (eventName == "carrier.delivered" &&
            DisputeWindowEndsAt is { } deadline)
        {
            var reminderAt = deadline.AddHours(-24);
            if (reminderAt > occurredAt &&
                !string.IsNullOrWhiteSpace(BuyerContact))
                _notifications.Add(NotificationOutboxMessage.Create(
                    Id,
                    "buyer",
                    BuyerContact,
                    "payout_reminder_24h",
                    occurredAt,
                    reminderAt));
        }
    }

    private NotificationTarget[] BuyerNotice(string template) =>
        string.IsNullOrWhiteSpace(BuyerContact)
            ? []
            : [new("buyer", BuyerContact, template)];

    private NotificationTarget[] SellerNotice(string template) =>
        string.IsNullOrWhiteSpace(SellerContact)
            ? []
            : [new("seller", SellerContact, template)];

    private sealed record NotificationTarget(
        string Audience,
        string Recipient,
        string Template);

    public bool HasValidAgreementCoreSnapshot()
    {
        if (AgreementCoreSnapshotCreatedAt is null ||
            string.IsNullOrWhiteSpace(AgreementCoreSnapshotJson) ||
            string.IsNullOrWhiteSpace(AgreementCoreSnapshotHash) ||
            string.IsNullOrWhiteSpace(TermsSnapshotJson) ||
            string.IsNullOrWhiteSpace(TermsSnapshotHash))
            return false;

        var schemaVersion = ReadSchemaVersion(
            AgreementCoreSnapshotJson);
        if (schemaVersion is not (3 or 4 or 5 or
            AgreementSnapshotSchemaVersion))
            return false;

        return SecureEquals(
                   AgreementCoreSnapshotHash,
                   Hash(AgreementCoreSnapshotJson)) &&
               SecureEquals(
                   TermsSnapshotHash,
                   Hash(TermsSnapshotJson)) &&
               SecureEquals(
                   TermsSnapshotJson,
                   BuildTermsSnapshotJson(
                       schemaVersion.Value)) &&
               SecureEquals(
                   AgreementCoreSnapshotJson,
                   BuildAgreementCoreSnapshotJson(
                       schemaVersion.Value,
                       AgreementCoreSnapshotCreatedAt.Value,
                       TermsSnapshotHash));
    }

    public bool HasMatchingPartyAcceptances()
    {
        if (string.IsNullOrWhiteSpace(
                AgreementCoreSnapshotHash) ||
            BuyerId is null ||
            SellerId is null ||
            BuyerAcceptedAt is null ||
            SellerAcceptedAt is null)
            return false;

        if (_agreementAcceptances.Count != 2)
            return false;

        var sellerAcceptance =
            _agreementAcceptances.SingleOrDefault(
                acceptance =>
                    acceptance.Role ==
                    AgreementAcceptanceRole.Seller);
        var buyerAcceptance =
            _agreementAcceptances.SingleOrDefault(
                acceptance =>
                    acceptance.Role ==
                    AgreementAcceptanceRole.Buyer);
        return AcceptanceMatches(
                   sellerAcceptance,
                   SellerId.Value,
                   SellerContact,
                   SellerAcceptedAt.Value) &&
               AcceptanceMatches(
                   buyerAcceptance,
                   BuyerId.Value,
                   BuyerContact ?? "",
                   BuyerAcceptedAt.Value);
    }

    private bool AcceptanceMatches(
        AgreementAcceptance? acceptance,
        Guid expectedActorUserId,
        string expectedPhoneNumber,
        DateTimeOffset expectedAcceptedAt) =>
        acceptance is not null &&
        acceptance.ActorUserId == expectedActorUserId &&
        SecureEquals(
            acceptance.VerifiedPhoneNumber,
            expectedPhoneNumber) &&
        string.Equals(
            acceptance.AuthenticationMethod,
            "verified-phone-session",
            StringComparison.Ordinal) &&
        SecureEquals(
            acceptance.AgreementCoreSnapshotHash,
            AgreementCoreSnapshotHash ?? "") &&
        SecureEquals(
            acceptance.TermsSnapshotHash,
            TermsSnapshotHash ?? "") &&
        string.Equals(
            acceptance.TermsVersion,
            TermsVersion,
            StringComparison.Ordinal) &&
        acceptance.AcceptedAt == expectedAcceptedAt;

    public bool HasValidAgreementSnapshot()
    {
        if (SnapshotSchemaVersion == 4)
            return HasValidVersionFourSnapshot();
        if (SnapshotSchemaVersion == 5)
            return HasValidVersionFiveSnapshot();

        if (SnapshotSchemaVersion !=
                AgreementSnapshotSchemaVersion ||
            AgreementSnapshotCreatedAt is null ||
            string.IsNullOrWhiteSpace(ProductSnapshotJson) ||
            string.IsNullOrWhiteSpace(ProductSnapshotHash) ||
            !HasValidAgreementCoreSnapshot() ||
            !HasMatchingPartyAcceptances())
            return false;

        return SecureEquals(
                   ProductSnapshotHash,
                   Hash(ProductSnapshotJson)) &&
               SecureEquals(
                   ProductSnapshotJson,
                   BuildProductSnapshotJson(
                       AgreementSnapshotSchemaVersion,
                       AgreementSnapshotCreatedAt.Value,
                       TermsSnapshotHash!,
                       AgreementCoreSnapshotHash!));
    }

    private void CreateSellerAcceptanceEvidence(
        Guid sellerId,
        DateTimeOffset acceptedAt)
    {
        if (sellerId == Guid.Empty)
            throw new DomainException(
                "ไม่พบบัญชีผู้ขายที่ยืนยันตัวตน");
        if (AgreementCoreSnapshotJson is not null ||
            AgreementCoreSnapshotHash is not null ||
            TermsSnapshotJson is not null ||
            TermsSnapshotHash is not null ||
            AgreementCoreSnapshotCreatedAt is not null)
            throw new DomainException(
                "รายการนี้มี agreement core snapshot แล้ว");

        TermsSnapshotJson = BuildTermsSnapshotJson(
            AgreementSnapshotSchemaVersion);
        TermsSnapshotHash = Hash(TermsSnapshotJson);
        AgreementCoreSnapshotCreatedAt = acceptedAt;
        AgreementCoreSnapshotJson =
            BuildAgreementCoreSnapshotJson(
                AgreementSnapshotSchemaVersion,
                acceptedAt,
                TermsSnapshotHash);
        AgreementCoreSnapshotHash =
            Hash(AgreementCoreSnapshotJson);
        AddAgreementAcceptance(
            AgreementAcceptanceRole.Seller,
            sellerId,
            SellerContact,
            acceptedAt);

        if (!HasValidAgreementCoreSnapshot())
            throw new DomainException(
                "ไม่สามารถสร้าง agreement core snapshot ได้");
    }

    private void CreateBuyerAcceptanceEvidence(
        Guid buyerId,
        DateTimeOffset acceptedAt)
    {
        EnsureAgreementCoreSnapshotIntegrity();
        AddAgreementAcceptance(
            AgreementAcceptanceRole.Buyer,
            buyerId,
            BuyerContact ?? "",
            acceptedAt);
        if (!HasMatchingPartyAcceptances())
            throw new DomainException(
                "หลักฐานการยอมรับของทั้งสองฝ่ายไม่ครบ");
    }

    private void AddAgreementAcceptance(
        AgreementAcceptanceRole role,
        Guid actorUserId,
        string verifiedPhoneNumber,
        DateTimeOffset acceptedAt)
    {
        if (_agreementAcceptances.Any(
                acceptance =>
                    acceptance.Role == role))
            throw new DomainException(
                "ฝ่ายนี้ยอมรับข้อตกลงแล้ว");
        _agreementAcceptances.Add(
            AgreementAcceptance.Create(
                Id,
                role,
                actorUserId,
                Required(
                    verifiedPhoneNumber,
                    "เบอร์โทรที่ยืนยันแล้ว"),
                AgreementCoreSnapshotHash!,
                TermsVersion,
                TermsSnapshotHash!,
                acceptedAt,
                Id.ToString("N"),
                $"agreement-acceptance:{Id:N}:{role.ToString().ToLowerInvariant()}"));
    }

    private void CreateAgreementSnapshot(
        DateTimeOffset createdAt)
    {
        if (ProductSnapshotJson is not null ||
            ProductSnapshotHash is not null ||
            AgreementSnapshotCreatedAt is not null)
            throw new DomainException(
                "รายการนี้มี paid snapshot แล้วและสร้างซ้ำไม่ได้");
        EnsureAgreementCoreSnapshotIntegrity();
        if (!HasMatchingPartyAcceptances())
            throw new DomainException(
                "ผู้ซื้อและผู้ขายยังยอมรับข้อตกลงไม่ครบ");

        SnapshotSchemaVersion =
            AgreementSnapshotSchemaVersion;
        AgreementSnapshotCreatedAt = createdAt;
        ProductSnapshotJson = BuildProductSnapshotJson(
            AgreementSnapshotSchemaVersion,
            createdAt,
            TermsSnapshotHash!,
            AgreementCoreSnapshotHash!);
        ProductSnapshotHash = Hash(ProductSnapshotJson);

        if (!HasValidAgreementSnapshot())
            throw new DomainException(
                "ไม่สามารถสร้าง paid snapshot ของข้อตกลงได้");
    }

    private void EnsureAgreementCoreSnapshotIntegrity()
    {
        if (!HasValidAgreementCoreSnapshot())
            throw new DomainException(
                "agreement core snapshot ไม่ครบหรือไม่ตรงกับ hash");
    }

    private void EnsureAgreementSnapshotIntegrity()
    {
        if (SnapshotSchemaVersion is null)
        {
            if (!HasValidLegacyProductSnapshot())
                throw new DomainException(
                    "snapshot เดิมของข้อตกลงไม่ครบหรือไม่ตรงกับ hash จึงไม่สามารถดำเนินการด้านการเงินได้");
            return;
        }

        if (SnapshotSchemaVersion == 1)
        {
            if (!HasValidVersionOneSnapshot())
                throw new DomainException(
                    "snapshot เวอร์ชัน 1 ไม่ครบหรือไม่ตรงกับ hash");
            return;
        }

        if (SnapshotSchemaVersion == 2)
        {
            if (!HasValidVersionTwoSnapshot())
                throw new DomainException(
                    "snapshot เวอร์ชัน 2 ไม่ครบหรือไม่ตรงกับ hash");
            return;
        }

        if (SnapshotSchemaVersion == 3)
        {
            if (!HasValidVersionThreeSnapshot())
                throw new DomainException(
                    "snapshot เวอร์ชัน 3 ไม่ครบหรือไม่ตรงกับ hash");
            return;
        }

        if (SnapshotSchemaVersion == 4)
        {
            if (!HasValidVersionFourSnapshot())
                throw new DomainException(
                    "snapshot เวอร์ชัน 4 ไม่ครบหรือไม่ตรงกับ hash");
            return;
        }

        if (SnapshotSchemaVersion == 5)
        {
            if (!HasValidVersionFiveSnapshot())
                throw new DomainException(
                    "snapshot เวอร์ชัน 5 ไม่ครบหรือไม่ตรงกับ hash");
            return;
        }

        if (!HasValidAgreementSnapshot())
            throw new DomainException(
                "snapshot ของข้อตกลงไม่ครบหรือไม่ตรงกับ hash จึงไม่สามารถดำเนินการด้านการเงินได้");
    }

    private bool HasValidLegacyProductSnapshot() =>
        !string.IsNullOrWhiteSpace(ProductSnapshotJson) &&
        !string.IsNullOrWhiteSpace(ProductSnapshotHash) &&
        SecureEquals(
            ProductSnapshotHash,
            Hash(ProductSnapshotJson));

    private bool HasValidVersionOneSnapshot() =>
        AgreementSnapshotCreatedAt is not null &&
        !string.IsNullOrWhiteSpace(ProductSnapshotJson) &&
        !string.IsNullOrWhiteSpace(ProductSnapshotHash) &&
        !string.IsNullOrWhiteSpace(TermsSnapshotJson) &&
        !string.IsNullOrWhiteSpace(TermsSnapshotHash) &&
        SecureEquals(
            ProductSnapshotHash,
            Hash(ProductSnapshotJson)) &&
        SecureEquals(
            TermsSnapshotHash,
            Hash(TermsSnapshotJson));

    private bool HasValidVersionTwoSnapshot() =>
        AgreementCoreSnapshotCreatedAt is not null &&
        AgreementSnapshotCreatedAt is not null &&
        !string.IsNullOrWhiteSpace(
            AgreementCoreSnapshotJson) &&
        !string.IsNullOrWhiteSpace(
            AgreementCoreSnapshotHash) &&
        !string.IsNullOrWhiteSpace(ProductSnapshotJson) &&
        !string.IsNullOrWhiteSpace(ProductSnapshotHash) &&
        !string.IsNullOrWhiteSpace(TermsSnapshotJson) &&
        !string.IsNullOrWhiteSpace(TermsSnapshotHash) &&
        SecureEquals(
            AgreementCoreSnapshotHash,
            Hash(AgreementCoreSnapshotJson)) &&
        SecureEquals(
            ProductSnapshotHash,
            Hash(ProductSnapshotJson)) &&
        SecureEquals(
            TermsSnapshotHash,
            Hash(TermsSnapshotJson)) &&
        HasMatchingPartyAcceptances();

    private bool HasValidVersionThreeSnapshot() =>
        SnapshotSchemaVersion == 3 &&
        AgreementSnapshotCreatedAt is not null &&
        !string.IsNullOrWhiteSpace(
            ProductSnapshotJson) &&
        !string.IsNullOrWhiteSpace(
            ProductSnapshotHash) &&
        HasValidAgreementCoreSnapshot() &&
        HasMatchingPartyAcceptances() &&
        SecureEquals(
            ProductSnapshotHash,
            Hash(ProductSnapshotJson)) &&
        SecureEquals(
            ProductSnapshotJson,
            BuildProductSnapshotJson(
                3,
                AgreementSnapshotCreatedAt.Value,
                TermsSnapshotHash!,
                AgreementCoreSnapshotHash!));

    private bool HasValidVersionFourSnapshot() =>
        SnapshotSchemaVersion == 4 &&
        AgreementSnapshotCreatedAt is not null &&
        !string.IsNullOrWhiteSpace(
            ProductSnapshotJson) &&
        !string.IsNullOrWhiteSpace(
            ProductSnapshotHash) &&
        HasValidAgreementCoreSnapshot() &&
        HasMatchingPartyAcceptances() &&
        SecureEquals(
            ProductSnapshotHash,
            Hash(ProductSnapshotJson)) &&
        SecureEquals(
            ProductSnapshotJson,
            BuildProductSnapshotJson(
                4,
                AgreementSnapshotCreatedAt.Value,
                TermsSnapshotHash!,
                AgreementCoreSnapshotHash!));

    private bool HasValidVersionFiveSnapshot() =>
        SnapshotSchemaVersion == 5 &&
        AgreementSnapshotCreatedAt is not null &&
        !string.IsNullOrWhiteSpace(
            ProductSnapshotJson) &&
        !string.IsNullOrWhiteSpace(
            ProductSnapshotHash) &&
        HasValidAgreementCoreSnapshot() &&
        HasMatchingPartyAcceptances() &&
        SecureEquals(
            ProductSnapshotHash,
            Hash(ProductSnapshotJson)) &&
        SecureEquals(
            ProductSnapshotJson,
            BuildProductSnapshotJson(
                5,
                AgreementSnapshotCreatedAt.Value,
                TermsSnapshotHash!,
                AgreementCoreSnapshotHash!));

    private string BuildTermsSnapshotJson(
        int schemaVersion)
    {
        if (schemaVersion == 3)
            return JsonSerializer.Serialize(new
            {
                SchemaVersion = schemaVersion,
                TermsVersion,
                FeePolicyVersion,
                ProductPolicyVersion =
                    "mvp-product-policy-2026-07",
                FulfillmentType =
                    FulfillmentType.ToString(),
                Rules = new
                {
                    MoneyUnit = "satang",
                    Currency,
                    ProviderConfirmationRequiredForPayment = true,
                    ProviderConfirmationRequiredForRefund = true,
                    ProviderConfirmationRequiredForPayout = true,
                    MaterialChangeRequiresCancellationAndNewOffer = true,
                    ShipByDurationHours,
                    BuyerSuppliesPhysicalDeliveryAddressAtCheckout =
                        FulfillmentType ==
                        FulfillmentType.PhysicalShipment,
                    PhysicalDeliveryRegionLockedBeforeSellerAcceptance =
                        FulfillmentType ==
                        FulfillmentType.PhysicalShipment,
                    PhysicalInspectionWindowHours =
                        FulfillmentType ==
                        FulfillmentType.PhysicalShipment
                            ? InspectionWindowDurationHours
                            : (int?)null,
                    BuyerConfirmationMayReleaseEarly = true,
                    OpenDisputeBlocksPayout = true,
                    TrustedCarrierDeliveryRequiredForAutomaticRelease =
                        FulfillmentType ==
                        FulfillmentType.PhysicalShipment,
                    DigitalAutomaticReleaseFromElapsedTime = false
                }
            });

        if (schemaVersion < 6)
            return JsonSerializer.Serialize(new
            {
                SchemaVersion = schemaVersion,
                TermsVersion,
                FeePolicyVersion,
                ProductPolicyVersion =
                    "mvp-product-policy-2026-07",
                FulfillmentType = FulfillmentType.ToString(),
                Rules = new
                {
                    MoneyUnit = "satang",
                    Currency,
                    ProviderConfirmationRequiredForPayment = true,
                    ProviderConfirmationRequiredForRefund = true,
                    ProviderConfirmationRequiredForPayout = true,
                    MaterialChangeRequiresCancellationAndNewOffer = true,
                    ShipByDurationHours,
                    BuyerSuppliesPhysicalDeliveryAddressAtOfferCreation =
                        FulfillmentType ==
                        FulfillmentType.PhysicalShipment,
                    PhysicalDeliveryAddressLockedBeforeSellerAcceptance =
                        FulfillmentType ==
                        FulfillmentType.PhysicalShipment,
                    PhysicalDeliveryRegionLockedBeforeSellerAcceptance =
                        FulfillmentType ==
                        FulfillmentType.PhysicalShipment,
                    PhysicalInspectionWindowHours =
                        FulfillmentType ==
                        FulfillmentType.PhysicalShipment
                            ? InspectionWindowDurationHours
                            : (int?)null,
                    BuyerConfirmationMayReleaseEarly = true,
                    OpenDisputeBlocksPayout = true,
                    TrustedCarrierDeliveryRequiredForAutomaticRelease =
                        FulfillmentType ==
                        FulfillmentType.PhysicalShipment,
                    DigitalAutomaticReleaseFromElapsedTime = false
                }
            });

        var currentJson = JsonSerializer.Serialize(new
        {
            SchemaVersion = schemaVersion,
            TermsVersion,
            FeePolicyVersion,
            ProductPolicyVersion =
                "mvp-product-policy-2026-07",
            FulfillmentType = FulfillmentType.ToString(),
            Rules = new
            {
                MoneyUnit = "satang",
                Currency,
                ProviderConfirmationRequiredForPayment = true,
                ProviderConfirmationRequiredForRefund = true,
                ProviderConfirmationRequiredForPayout = true,
                MaterialChangeRequiresCancellationAndNewOffer = true,
                ShipByDurationHours,
                BuyerSuppliesPhysicalDeliveryAddressAtOfferCreation =
                    FulfillmentType ==
                    FulfillmentType.PhysicalShipment,
                PhysicalDeliveryAddressLockedBeforeSellerAcceptance =
                    FulfillmentType ==
                    FulfillmentType.PhysicalShipment,
                PhysicalDeliveryRegionLockedBeforeSellerAcceptance =
                    FulfillmentType ==
                    FulfillmentType.PhysicalShipment,
                ShippingQuoteLockedBeforeSellerAcceptance =
                    FulfillmentType ==
                    FulfillmentType.PhysicalShipment,
                BuyerPaysShippingCharge =
                    FulfillmentType ==
                    FulfillmentType.PhysicalShipment,
                ShippingChargeExcludedFromSellerNet = true,
                PhysicalInspectionWindowHours =
                    FulfillmentType ==
                    FulfillmentType.PhysicalShipment
                        ? InspectionWindowDurationHours
                        : (int?)null,
                BuyerConfirmationMayReleaseEarly = true,
                OpenDisputeBlocksPayout = true,
                TrustedCarrierDeliveryRequiredForAutomaticRelease =
                    FulfillmentType ==
                    FulfillmentType.PhysicalShipment,
                DigitalAutomaticReleaseFromElapsedTime = false
            }
        });
        if (schemaVersion < 8)
            return currentJson;
        var currentSnapshot =
            JsonNode.Parse(currentJson)!.AsObject();
        currentSnapshot[nameof(BuyerProtectionFeeSatang)] =
            BuyerProtectionFeeSatang;
        return currentSnapshot.ToJsonString();
    }

    private string BuildAgreementCoreSnapshotJson(
        int schemaVersion,
        DateTimeOffset createdAt,
        string termsSnapshotHash)
    {
        if (schemaVersion < 6)
            return JsonSerializer.Serialize(new
            {
                SchemaVersion = schemaVersion,
                TransactionId = Id,
                InitiatorRole = InitiatorRole.ToString(),
                Buyer = new
                {
                    BuyerId,
                    DisplayName = BuyerDisplayName,
                    Contact = BuyerContact
                },
                Seller = new
                {
                    SellerId,
                    DisplayName = SellerDisplayName,
                    Contact = SellerContact
                },
                ProductName,
                FulfillmentType =
                    FulfillmentType.ToString(),
                Category,
                Condition = Condition.ToString(),
                Description,
                KnownDefects,
                PhotoUrl,
                PriceSatang,
                PlatformFeeSatang,
                SellerExpectedNetSatang,
                Currency,
                DeliveryRegion =
                    FulfillmentType ==
                    FulfillmentType.PhysicalShipment
                        ? new
                        {
                            ProvinceName =
                                DeliveryProvinceName,
                            PostalCode =
                                DeliveryPostalCode
                        }
                        : null,
                ShipByDurationHours,
                PhysicalInspectionWindowHours =
                    FulfillmentType ==
                    FulfillmentType.PhysicalShipment
                        ? InspectionWindowDurationHours
                        : (int?)null,
                TermsVersion,
                TermsSnapshotHash =
                    termsSnapshotHash,
                FeePolicyVersion,
                SellerAcceptedAt,
                BuyerPaymentDeadlineAt,
                SnapshotCreatedAt = createdAt
            });

        if (schemaVersion == 6)
            return JsonSerializer.Serialize(new
            {
                SchemaVersion = schemaVersion,
                TransactionId = Id,
                InitiatorRole = InitiatorRole.ToString(),
                Buyer = new
                {
                    BuyerId,
                    DisplayName = BuyerDisplayName,
                    Contact = BuyerContact
                },
                Seller = new
                {
                    SellerId,
                    DisplayName = SellerDisplayName,
                    Contact = SellerContact
                },
                ProductName,
                FulfillmentType =
                    FulfillmentType.ToString(),
                Category,
                Condition = Condition.ToString(),
                Description,
                KnownDefects,
                PhotoUrl,
                PriceSatang,
                ShippingFeeSatang,
                BuyerTotalSatang,
                PlatformFeeSatang,
                SellerExpectedNetSatang,
                Currency,
                DeliveryRegion =
                    FulfillmentType ==
                    FulfillmentType.PhysicalShipment
                        ? new
                        {
                            ProvinceName =
                                DeliveryProvinceName,
                            PostalCode =
                                DeliveryPostalCode
                        }
                        : null,
                Shipping =
                    FulfillmentType ==
                    FulfillmentType.PhysicalShipment
                        ? new
                        {
                            OriginProvinceName =
                                ShippingOriginProvinceName,
                            OriginPostalCode =
                                ShippingOriginPostalCode,
                            PackageWeightGrams,
                            PackageWidthCentimeters,
                            PackageLengthCentimeters,
                            PackageHeightCentimeters,
                            ShippingQuoteProvider,
                            ShippingQuoteReference,
                            ShippingQuoteExpiresAt,
                            CarrierCode,
                            ShippingServiceCode,
                            ShippingServiceName
                        }
                        : null,
                ShipByDurationHours,
                PhysicalInspectionWindowHours =
                    FulfillmentType ==
                    FulfillmentType.PhysicalShipment
                        ? InspectionWindowDurationHours
                        : (int?)null,
                TermsVersion,
                TermsSnapshotHash =
                    termsSnapshotHash,
                FeePolicyVersion,
                SellerAcceptedAt,
                BuyerPaymentDeadlineAt,
                SnapshotCreatedAt = createdAt
            });

        var currentJson = JsonSerializer.Serialize(new
        {
            SchemaVersion = schemaVersion,
            TransactionId = Id,
            InitiatorRole = InitiatorRole.ToString(),
            Buyer = new
            {
                BuyerId,
                DisplayName = BuyerDisplayName,
                Contact = BuyerContact
            },
            Seller = new
            {
                SellerId,
                DisplayName = SellerDisplayName,
                Contact = SellerContact
            },
            ProductName,
            FulfillmentType =
                FulfillmentType.ToString(),
            Category,
            Condition = Condition.ToString(),
            Description,
            KnownDefects,
            PhotoUrl,
            PriceSatang,
            ShippingFeeSatang,
            BuyerTotalSatang,
            PlatformFeeSatang,
            SellerExpectedNetSatang,
            Currency,
            DeliveryRegion =
                FulfillmentType ==
                FulfillmentType.PhysicalShipment
                    ? new
                    {
                        ProvinceName =
                            DeliveryProvinceName,
                        PostalCode =
                            DeliveryPostalCode
                    }
                    : null,
            Shipping =
                FulfillmentType ==
                FulfillmentType.PhysicalShipment
                    ? new
                    {
                        OriginProvinceName =
                            ShippingOriginProvinceName,
                        OriginPostalCode =
                            ShippingOriginPostalCode,
                        PackageWeightGrams,
                        PackageWidthCentimeters,
                        PackageLengthCentimeters,
                        PackageHeightCentimeters,
                        ShippingQuoteProvider,
                        ShippingQuoteReference,
                        ShippingQuoteExpiresAt,
                        CarrierCode,
                        ShippingServiceCode,
                        ShippingServiceName,
                        ShippingPurchaseReference,
                        ShippingProviderTrackingCode,
                        ShippingCourierTrackingCode,
                        ShippingReservedAt
                    }
                    : null,
            ShipByDurationHours,
            PhysicalInspectionWindowHours =
                FulfillmentType ==
                FulfillmentType.PhysicalShipment
                    ? InspectionWindowDurationHours
                    : (int?)null,
            TermsVersion,
            TermsSnapshotHash =
                termsSnapshotHash,
            FeePolicyVersion,
            SellerAcceptedAt,
            BuyerPaymentDeadlineAt,
            SnapshotCreatedAt = createdAt
        });
        if (schemaVersion < 8)
            return currentJson;
        var currentSnapshot =
            JsonNode.Parse(currentJson)!.AsObject();
        currentSnapshot[nameof(BuyerProtectionFeeSatang)] =
            BuyerProtectionFeeSatang;
        return currentSnapshot.ToJsonString();
    }

    private string BuildProductSnapshotJson(
        int schemaVersion,
        DateTimeOffset createdAt,
        string termsSnapshotHash,
        string agreementCoreSnapshotHash)
    {
        if (schemaVersion < 6)
            return JsonSerializer.Serialize(new
            {
                SchemaVersion = schemaVersion,
                TransactionId = Id,
                InitiatorRole = InitiatorRole.ToString(),
                Buyer = new
                {
                    BuyerId,
                    DisplayName = BuyerDisplayName,
                    Contact = BuyerContact
                },
                Seller = new
                {
                    SellerId,
                    DisplayName = SellerDisplayName,
                    Contact = SellerContact
                },
                ProductName,
                FulfillmentType =
                    FulfillmentType.ToString(),
                Category,
                Condition = Condition.ToString(),
                Description,
                KnownDefects,
                PhotoUrl,
                PriceSatang,
                PlatformFeeSatang,
                SellerExpectedNetSatang,
                Currency,
                DeliveryRegion =
                    FulfillmentType ==
                    FulfillmentType.PhysicalShipment
                        ? new
                        {
                            ProvinceName =
                                DeliveryProvinceName,
                            PostalCode =
                                DeliveryPostalCode
                        }
                        : null,
                ShipByDurationHours,
                PhysicalInspectionWindowHours =
                    FulfillmentType ==
                    FulfillmentType.PhysicalShipment
                        ? InspectionWindowDurationHours
                        : (int?)null,
                DeliveryAddress,
                TermsVersion,
                TermsSnapshotHash =
                    termsSnapshotHash,
                AgreementCoreSnapshotHash =
                    agreementCoreSnapshotHash,
                FeePolicyVersion,
                SellerAcceptedAt,
                BuyerPaymentDeadlineAt,
                BuyerAcceptedAt,
                SnapshotCreatedAt = createdAt
            });

        if (schemaVersion == 6)
            return JsonSerializer.Serialize(new
            {
                SchemaVersion = schemaVersion,
                TransactionId = Id,
                InitiatorRole = InitiatorRole.ToString(),
                Buyer = new
                {
                    BuyerId,
                    DisplayName = BuyerDisplayName,
                    Contact = BuyerContact
                },
                Seller = new
                {
                    SellerId,
                    DisplayName = SellerDisplayName,
                    Contact = SellerContact
                },
                ProductName,
                FulfillmentType =
                    FulfillmentType.ToString(),
                Category,
                Condition = Condition.ToString(),
                Description,
                KnownDefects,
                PhotoUrl,
                PriceSatang,
                ShippingFeeSatang,
                BuyerTotalSatang,
                PlatformFeeSatang,
                SellerExpectedNetSatang,
                Currency,
                DeliveryRegion =
                    FulfillmentType ==
                    FulfillmentType.PhysicalShipment
                        ? new
                        {
                            ProvinceName =
                                DeliveryProvinceName,
                            PostalCode =
                                DeliveryPostalCode
                        }
                        : null,
                Shipping =
                    FulfillmentType ==
                    FulfillmentType.PhysicalShipment
                        ? new
                        {
                            ShippingOriginAddress,
                            ShippingOriginProvinceName,
                            ShippingOriginPostalCode,
                            PackageWeightGrams,
                            PackageWidthCentimeters,
                            PackageLengthCentimeters,
                            PackageHeightCentimeters,
                            ShippingQuoteProvider,
                            ShippingQuoteReference,
                            ShippingQuoteExpiresAt,
                            CarrierCode,
                            ShippingServiceCode,
                            ShippingServiceName
                        }
                        : null,
                ShipByDurationHours,
                PhysicalInspectionWindowHours =
                    FulfillmentType ==
                    FulfillmentType.PhysicalShipment
                        ? InspectionWindowDurationHours
                        : (int?)null,
                DeliveryAddress,
                TermsVersion,
                TermsSnapshotHash =
                    termsSnapshotHash,
                AgreementCoreSnapshotHash =
                    agreementCoreSnapshotHash,
                FeePolicyVersion,
                SellerAcceptedAt,
                BuyerPaymentDeadlineAt,
                BuyerAcceptedAt,
                SnapshotCreatedAt = createdAt
            });

        var currentJson = JsonSerializer.Serialize(new
        {
            SchemaVersion = schemaVersion,
            TransactionId = Id,
            InitiatorRole = InitiatorRole.ToString(),
            Buyer = new
            {
                BuyerId,
                DisplayName = BuyerDisplayName,
                Contact = BuyerContact
            },
            Seller = new
            {
                SellerId,
                DisplayName = SellerDisplayName,
                Contact = SellerContact
            },
            ProductName,
            FulfillmentType =
                FulfillmentType.ToString(),
            Category,
            Condition = Condition.ToString(),
            Description,
            KnownDefects,
            PhotoUrl,
            PriceSatang,
            ShippingFeeSatang,
            BuyerTotalSatang,
            PlatformFeeSatang,
            SellerExpectedNetSatang,
            Currency,
            DeliveryRegion =
                FulfillmentType ==
                FulfillmentType.PhysicalShipment
                    ? new
                    {
                        AddressLine =
                            DeliveryAddressLine,
                        SubdistrictName =
                            DeliverySubdistrictName,
                        DistrictName =
                            DeliveryDistrictName,
                        ProvinceName =
                            DeliveryProvinceName,
                        PostalCode =
                            DeliveryPostalCode
                    }
                    : null,
            Shipping =
                FulfillmentType ==
                FulfillmentType.PhysicalShipment
                    ? new
                    {
                        ShippingOriginAddress,
                        ShippingOriginAddressLine,
                        ShippingOriginSubdistrictName,
                        ShippingOriginDistrictName,
                        ShippingOriginProvinceName,
                        ShippingOriginPostalCode,
                        PackageWeightGrams,
                        PackageWidthCentimeters,
                        PackageLengthCentimeters,
                        PackageHeightCentimeters,
                        ShippingQuoteProvider,
                        ShippingQuoteReference,
                        ShippingQuoteExpiresAt,
                        CarrierCode,
                        ShippingServiceCode,
                        ShippingServiceName,
                        ShippingPurchaseReference,
                        ShippingProviderTrackingCode,
                        ShippingCourierTrackingCode,
                        ShippingReservedAt
                    }
                    : null,
            ShipByDurationHours,
            PhysicalInspectionWindowHours =
                FulfillmentType ==
                FulfillmentType.PhysicalShipment
                    ? InspectionWindowDurationHours
                    : (int?)null,
            DeliveryAddress,
            TermsVersion,
            TermsSnapshotHash =
                termsSnapshotHash,
            AgreementCoreSnapshotHash =
                agreementCoreSnapshotHash,
            FeePolicyVersion,
            SellerAcceptedAt,
            BuyerPaymentDeadlineAt,
            BuyerAcceptedAt,
            SnapshotCreatedAt = createdAt
        });
        if (schemaVersion < 8)
            return currentJson;
        var currentSnapshot =
            JsonNode.Parse(currentJson)!.AsObject();
        currentSnapshot[nameof(BuyerProtectionFeeSatang)] =
            BuyerProtectionFeeSatang;
        return currentSnapshot.ToJsonString();
    }

    private string SnapshotAuditMetadata() =>
        JsonSerializer.Serialize(new
        {
            SnapshotSchemaVersion,
            AgreementCoreSnapshotHash,
            AgreementSnapshotCreatedAt,
            AgreementSnapshotSealedAt,
            ProductSnapshotHash,
            TermsSnapshotHash,
            AgreementAcceptances =
                _agreementAcceptances
                    .OrderBy(x => x.Role)
                    .Select(x => new
                    {
                        Role = x.Role.ToString(),
                        x.Id,
                        x.AcceptedAt
                    })
                    .ToArray()
        });

    private string AcceptanceAuditMetadata(
        AgreementAcceptanceRole role)
    {
        var acceptance = _agreementAcceptances.Single(
            item => item.Role == role);
        return JsonSerializer.Serialize(new
        {
            Role = acceptance.Role.ToString(),
            acceptance.AuthenticationMethod,
            acceptance.AgreementCoreSnapshotHash,
            acceptance.TermsVersion,
            acceptance.TermsSnapshotHash,
            acceptance.AcceptedAt
        });
    }

    private static int? ReadSchemaVersion(
        string json)
    {
        try
        {
            using var document =
                JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(
                    "SchemaVersion",
                    out var value) &&
                   value.TryGetInt32(
                       out var schemaVersion)
                ? schemaVersion
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private void EnsureExternalEventIsNew(
        string provider,
        string eventId,
        string eventType,
        DateTimeOffset occurredAt,
        DateTimeOffset receivedAt)
    {
        if (HasExternalEvent(provider, eventId))
            throw new DomainException("event นี้ถูกประมวลผลแล้ว");
        _externalEvents.Add(new ExternalEvent(Id, provider, eventId, eventType, occurredAt, receivedAt));
    }

    private void EnsureSeller(string token)
    {
        if (!SecureEquals(SellerAccessToken, token))
            throw new DomainException("ไม่มีสิทธิ์จัดการรายการนี้");
    }

    private void EnsureBuyer(string token)
    {
        if (BuyerAccessToken is null || !SecureEquals(BuyerAccessToken, token))
            throw new DomainException("ไม่มีสิทธิ์จัดการรายการนี้");
    }

    private static bool SecureEquals(string expected, string supplied)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        return expectedBytes.Length == suppliedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }

    private static string Required(string value, string label) =>
        string.IsNullOrWhiteSpace(value) ? throw new DomainException($"กรุณากรอก{label}") : value.Trim();

    private static string? CleanOptional(
        string? value,
        int maximumLength,
        string label)
    {
        var clean = value?.Trim();
        if (string.IsNullOrWhiteSpace(clean))
            return null;
        if (clean.Length > maximumLength)
            throw new DomainException(
                $"{label}ยาวเกิน {maximumLength} ตัวอักษร");
        return clean;
    }

    private static string NormalizeProviderTracking(
        string? value)
    {
        var clean = new string(
            (value ?? "")
            .Where(char.IsAsciiLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
        if (clean.Length is < 8 or > 120)
            throw new DomainException(
                "หมายเลขพัสดุจากผู้ให้บริการไม่ถูกต้อง");
        return clean;
    }

    private static bool IsRetentionTerminalState(
        TransactionState state) =>
        state is
            TransactionState.PaidOut or
            TransactionState.Refunded or
            TransactionState.Cancelled or
            TransactionState.Expired;

    private static string RequiredPostalCode(
        string value)
    {
        var postalCode = value.Trim();
        if (postalCode.Length != 5 ||
            postalCode.Any(character =>
                !char.IsAsciiDigit(character)))
            throw new DomainException(
                "รหัสไปรษณีย์ปลายทางไม่ถูกต้อง");
        return postalCode;
    }

    private static string? OptionalAddressRegion(
        string? value)
    {
        var clean = value?.Trim();
        if (string.IsNullOrWhiteSpace(clean))
            return null;
        if (clean.Length > 100)
            throw new DomainException(
                "ข้อมูลเขตหรือแขวงยาวเกิน 100 ตัวอักษร");
        return clean;
    }

    private static bool ContainsDigitalSecret(string value)
    {
        string[] markers =
        [
            "password", "รหัสผ่าน", "recovery code", "รหัสกู้คืน",
            "private key", "seed phrase", "คำกู้คืน", "otp"
        ];
        return markers.Any(marker =>
            value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static string Token() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
}

public sealed record AcceptedShippingQuote(
    string OriginAddress,
    string OriginProvinceName,
    string OriginPostalCode,
    int WeightGrams,
    int WidthCentimeters,
    int LengthCentimeters,
    int HeightCentimeters,
    string Provider,
    string QuoteReference,
    string CarrierCode,
    string ServiceCode,
    string ServiceName,
    long FeeSatang,
    DateTimeOffset ExpiresAt,
    string? OriginDistrictName = null,
    string? OriginSubdistrictName = null,
    string? PurchaseReference = null,
    string? ProviderTrackingCode = null,
    string? CourierTrackingCode = null,
    DateTimeOffset? ReservedAt = null,
    string? OriginAddressLine = null);
