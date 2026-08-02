using System.Globalization;
using System.Text.Json.Serialization;

namespace Toklong.Mobile.Core;

[JsonConverter(typeof(JsonStringEnumConverter<AppTransactionRole>))]
public enum AppTransactionRole
{
    Buyer,
    Seller
}

[JsonConverter(typeof(JsonStringEnumConverter<AppFulfillmentType>))]
public enum AppFulfillmentType
{
    Physical,
    Digital
}

public enum TransactionBucket
{
    ActionRequired,
    InProgress,
    Completed
}

public enum TransactionAction
{
    None,
    ShareWithSeller,
    ReviewSellerOffer,
    ReviewAndPay,
    AddTracking,
    ConfirmDigitalHandoff,
    ConfirmReceipt,
    ViewStatus
}

public sealed record TransactionPresentation(
    string StatusLabel,
    TransactionBucket Bucket,
    string StatusColor,
    string StatusBackground,
    TransactionAction PrimaryAction,
    string PrimaryActionLabel);

public sealed record AppTransaction(
    Guid Id,
    string ProductName,
    long AmountSatang,
    string Currency,
    AppTransactionRole Role,
    AppFulfillmentType FulfillmentType,
    string State,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ActionDeadline,
    string CounterpartyName,
    string? PhotoUrl = null,
    string AgreementDetails = "",
    string TermsVersion = "terms-mvp-v1",
    long BuyerProtectionFeeSatang = 0,
    long PlatformFeeSatang = 0,
    long SellerExpectedNetSatang = 0,
    string FeePolicyVersion = "",
    string? ExpirationReason = null,
    string? SellerInvitationUrl = null,
    string? AgreementCoreSnapshotHash = null,
    DateTimeOffset? SellerAcceptedAt = null,
    DateTimeOffset? BuyerAcceptedAt = null,
    string? DeliveryProvinceName = null,
    string? DeliveryPostalCode = null,
    string? DeliveryAddress = null,
    string? CarrierCode = null,
    string? ShippingServiceName = null,
    AppCondition Condition = AppCondition.New,
    string KnownDefects = "",
    long ItemPriceSatang = 0,
    long ShippingFeeSatang = 0,
    bool ShippingManagedByProvider = false,
    string? TrackingNumber = null,
    bool ShippingLabelAvailable = false,
    bool ReturnShippingLabelAvailable = false,
    DateTimeOffset? ShipByAt = null,
    DateTimeOffset? FirstCarrierScanAt = null,
    string? RefundProviderStatus = null,
    DateTimeOffset? RefundActionRequiredAt = null,
    DateTimeOffset? RefundActionExpiresAt = null,
    DateTimeOffset? RefundInstructionsSentAt = null,
    DateTimeOffset CreatedAt = default,
    long ParcelInsuranceFeeSatang = 0,
    long ShippingDeclaredValueSatang = 0,
    string? ShippingOperationStatus = null)
{
    private static readonly CultureInfo ThaiCulture =
        CultureInfo.GetCultureInfo("th-TH");

    public TransactionPresentation Presentation
    {
        get
        {
            var presentation =
                TransactionStatePresenter.Present(
            State,
            Role,
            FulfillmentType,
            ExpirationReason);
            if (ShippingOperationStatus is
                "Pending" or "Processing" or
                "RetryScheduled")
                return presentation with
                {
                    StatusLabel =
                        "กำลังเตรียมรายการจัดส่ง",
                    Bucket = TransactionBucket.InProgress,
                    PrimaryAction = TransactionAction.ViewStatus,
                    PrimaryActionLabel = "ดูสถานะ"
                };
            if (ShippingOperationStatus is
                "OutcomeUnknown" or "NeedsReview")
                return presentation with
                {
                    StatusLabel =
                        "การจัดส่งต้องตรวจสอบ",
                    Bucket = TransactionBucket.InProgress,
                    PrimaryAction = TransactionAction.ViewStatus,
                    PrimaryActionLabel = "ดูรายละเอียด"
                };
            if (ShippingManagedByProvider &&
                Role == AppTransactionRole.Seller &&
                State == "PaidAwaitingShipment")
                return presentation with
                {
                    Bucket = TransactionBucket.InProgress,
                    PrimaryAction = TransactionAction.ViewStatus,
                    PrimaryActionLabel =
                        "ระบบกำลังออกเลขพัสดุ"
                };
            if (ShippingManagedByProvider &&
                State == "TrackingUnverified")
                return presentation with
                {
                    StatusLabel =
                        HasTimelyTrustedCarrierAcceptance
                            ? Role == AppTransactionRole.Seller
                                ? "ส่งพัสดุทันเวลา · กำลังตรวจขนส่ง"
                                : "การนำส่งมีปัญหา"
                            : presentation.StatusLabel,
                    Bucket = TransactionBucket.InProgress,
                    PrimaryAction = TransactionAction.ViewStatus,
                    PrimaryActionLabel = "ดูสถานะ"
                };
            if (ShippingManagedByProvider &&
                Role == AppTransactionRole.Seller &&
                State == "ShipmentOverdue")
                return presentation with
                {
                    Bucket = TransactionBucket.InProgress,
                    PrimaryAction = TransactionAction.ViewStatus,
                    PrimaryActionLabel = "ดูสถานะ"
                };
            if (State == "RefundPending" &&
                Role == AppTransactionRole.Buyer &&
                RefundProviderStatus == "requires_action")
                return presentation with
                {
                    StatusLabel =
                        "ต้องยืนยันข้อมูลรับเงินคืน",
                    Bucket = TransactionBucket.ActionRequired,
                    StatusColor = "#8A5100",
                    StatusBackground = "#FFF4DC",
                    PrimaryAction =
                        TransactionAction.ViewStatus,
                    PrimaryActionLabel = "ดูวิธีดำเนินการ"
                };
            return presentation;
        }
    }

    public string RoleLabel => Role == AppTransactionRole.Buyer ? "ซื้อ" : "ขาย";

    public string ProductTypeLabel =>
        FulfillmentType == AppFulfillmentType.Physical
            ? "สินค้าที่จัดส่ง"
            : "ไอดีเกม";

    public string RoleAndProductTypeLabel =>
        $"{RoleLabel} · {ProductTypeLabel}";

    public string ProductTypeColor =>
        FulfillmentType == AppFulfillmentType.Physical
            ? "#145FC7"
            : "#5144BF";

    public string ProductTypeBackground =>
        FulfillmentType == AppFulfillmentType.Physical
            ? "#EEF7FF"
            : "#F3F1FF";

    public bool ShowShippingProgress =>
        FulfillmentType == AppFulfillmentType.Physical &&
        (ShippingManagedByProvider ||
         ShippingOperationStatus is not null);

    public int ShippingProgressCompletedThrough =>
        State is "DeliveredDisputeWindow" or
            "BuyerConfirmedReceipt" or "PayoutEligible" or
            "PayoutPending" or "PaidOut"
            ? 4
            : State == "InTransit" ||
              (State == "CarrierException" &&
               FirstCarrierScanAt.HasValue)
                ? 2
                : FirstCarrierScanAt.HasValue
                    ? 1
                    : 0;

    public int ShippingProgressActiveStep =>
        ShippingProgressCompletedThrough switch
        {
            >= 4 => 0,
            2 => 3,
            1 => 2,
            _ => 1
        };

    public string RoleColor =>
        Role == AppTransactionRole.Buyer
            ? "#145FC7"
            : SellerColorPalette.Role;

    public string RoleBackground =>
        Role == AppTransactionRole.Buyer
            ? "#EAF4FF"
            : SellerColorPalette.Surface;

    public string RoleHeaderStart =>
        Role == AppTransactionRole.Buyer
            ? "#3C8AF1"
            : SellerColorPalette.HeaderStart;

    public string RoleHeaderMiddle =>
        Role == AppTransactionRole.Buyer
            ? "#236DCE"
            : SellerColorPalette.HeaderMiddle;

    public string RoleHeaderEnd =>
        Role == AppTransactionRole.Buyer
            ? "#185CB9"
            : SellerColorPalette.HeaderEnd;

    public string RolePageTint =>
        Role == AppTransactionRole.Buyer
            ? "#DCEFFF"
            : SellerColorPalette.Surface;

    public string RolePageMiddle =>
        Role == AppTransactionRole.Buyer
            ? "#F6FAFF"
            : SellerColorPalette.BadgeSurface;

    public string RoleHeaderSecondary =>
        Role == AppTransactionRole.Buyer
            ? "#D8E7FF"
            : SellerColorPalette.Secondary;

    public string RoleDot =>
        Role == AppTransactionRole.Buyer
            ? "#9CEBD9"
            : SellerColorPalette.Accent;

    public string StatusLabel => Presentation.StatusLabel;

    public string StatusColor => Presentation.StatusColor;

    public string StatusBackground => Presentation.StatusBackground;

    public string PrimaryActionLabel => Presentation.PrimaryActionLabel;

    public string ListSemanticDescription =>
        $"{RoleLabel} {ProductName}. ประเภท {ProductTypeLabel}. " +
        $"{CounterpartyLabel}. " +
        $"{StatusLabel}. {RoleAmountLabel} {RoleAmountText}. " +
        $"{DeadlineText}. {PrimaryActionLabel}";

    public string FormattedAmount => MoneyFormatter.Format(AmountSatang, Currency);

    public string RoleAmountLabel =>
        IsSellerRole ? "ยอดที่จะได้รับ" : "ยอดรวม";

    public string RoleAmountText =>
        IsSellerRole ? SellerNetText : FormattedAmount;

    public string ItemPriceText =>
        MoneyFormatter.Format(
            ItemPriceSatang > 0
                ? ItemPriceSatang
                : AmountSatang,
            Currency);

    public string ShippingFeeText =>
        MoneyFormatter.Format(ShippingFeeSatang, Currency);

    public bool HasShippingFee =>
        FulfillmentType == AppFulfillmentType.Physical &&
        ShippingFeeSatang > 0;

    public bool HasParcelInsurance =>
        FulfillmentType == AppFulfillmentType.Physical &&
        ParcelInsuranceFeeSatang > 0;

    public string ParcelInsuranceFeeText =>
        MoneyFormatter.Format(
            ParcelInsuranceFeeSatang,
            Currency);

    public string ShippingDeclaredValueText =>
        MoneyFormatter.Format(
            ShippingDeclaredValueSatang,
            Currency);

    public string ShippingServiceText =>
        string.IsNullOrWhiteSpace(ShippingServiceName)
            ? CarrierCode ?? ""
            : ShippingServiceName;

    public bool HasTrackingNumber =>
        !string.IsNullOrWhiteSpace(
            TrackingNumber);

    public string TrackingNumberText =>
        TrackingNumber ?? "";

    public bool HasTimelyTrustedCarrierAcceptance =>
        ShippingManagedByProvider &&
        ShipByAt.HasValue &&
        FirstCarrierScanAt.HasValue &&
        FirstCarrierScanAt.Value <= ShipByAt.Value;

    public bool IsBuyerRole => Role == AppTransactionRole.Buyer;

    public bool IsSellerRole => Role == AppTransactionRole.Seller;

    public string SellerNetText =>
        MoneyFormatter.Format(SellerExpectedNetSatang, Currency);

    public string FeeText =>
        MoneyFormatter.Format(
            BuyerProtectionFeeSatang,
            Currency);

    public string CounterpartyLabel =>
        Role == AppTransactionRole.Buyer
            ? $"ผู้ขาย · {CounterpartyName}"
            : $"ผู้ซื้อ · {CounterpartyName}";

    public string DeadlineText
    {
        get
        {
            if (ActionDeadline is null)
                return $"อัปเดต {UpdatedAt.ToLocalTime().ToString("d MMM · HH:mm", ThaiCulture)}";

            var deadline = ActionDeadline.Value.ToLocalTime()
                .ToString("d MMM yyyy · HH:mm", ThaiCulture);
            if (State == "Expired")
                return $"หมดเวลา {deadline}";

            return State switch
            {
                "AwaitingSellerAcceptance" when IsBuyerRole =>
                    $"ผู้ขายตอบได้ถึง {deadline}",
                "AwaitingSellerAcceptance" =>
                    $"ตอบภายใน {deadline}",
                "SellerAcceptedAwaitingPayment" or
                    "CheckoutStarted" or
                    "PaymentPending" when IsBuyerRole =>
                        $"จ่ายภายใน {deadline}",
                "SellerAcceptedAwaitingPayment" or
                    "CheckoutStarted" or
                    "PaymentPending" =>
                        $"รอผู้ซื้อจ่ายถึง {deadline}",
                "PaidAwaitingShipment" or
                    "TrackingSubmitted" when IsSellerRole =>
                        $"ส่งภายใน {deadline}",
                "PaidAwaitingShipment" or
                    "TrackingSubmitted" =>
                        $"ผู้ขายต้องส่งภายใน {deadline}",
                "DeliveredDisputeWindow" when IsBuyerRole =>
                    $"แจ้งปัญหาได้ถึง {deadline}",
                "DeliveredDisputeWindow" =>
                    $"คาดว่าจะเริ่มจ่ายหลัง {deadline}",
                _ => $"ภายใน {deadline}"
            };
        }
    }

    public string DisplayAgreementDetails =>
        string.IsNullOrWhiteSpace(AgreementDetails)
            ? ProductName
            : AgreementDetails;

    public bool HasAdditionalAgreementDetails =>
        !string.IsNullOrWhiteSpace(AgreementDetails) &&
        !string.Equals(
            AgreementDetails.Trim(),
            ProductName.Trim(),
            StringComparison.OrdinalIgnoreCase);

    public string ConditionLabel => Condition switch
    {
        AppCondition.New => "ใหม่",
        AppCondition.UsedGood => "มือสอง สภาพดี",
        _ => "มือสอง มีตำหนิ"
    };

    public bool HasKnownDefects =>
        !string.IsNullOrWhiteSpace(KnownDefects) &&
        !string.Equals(
            KnownDefects.Trim(),
            QuickDealSnapshotComposer.NoBuyerReportedDefects,
            StringComparison.Ordinal);

    public string FulfillmentConsumerLabel =>
        FulfillmentType == AppFulfillmentType.Physical
            ? "จัดส่งพัสดุ"
            : "ส่งมอบดิจิทัล";

    public string FulfillmentLabel =>
        FulfillmentType == AppFulfillmentType.Physical
            ? "สินค้าที่จับต้องได้"
            : "สินค้าดิจิทัล";

    public bool HasDeliveryRegion =>
        FulfillmentType ==
            AppFulfillmentType.Physical &&
        !string.IsNullOrWhiteSpace(
            DeliveryProvinceName) &&
        !string.IsNullOrWhiteSpace(
            DeliveryPostalCode);

    public string DeliveryRegionText =>
        HasDeliveryRegion
            ? $"{DeliveryProvinceName} {DeliveryPostalCode}"
            : "";

    public bool HasDeliveryAddress =>
        FulfillmentType ==
            AppFulfillmentType.Physical &&
        !string.IsNullOrWhiteSpace(
            DeliveryAddress);

    public string DeliveryAddressText =>
        DeliveryAddress ?? "";

    public string TermsDisplayText =>
        $"ข้อตกลงการใช้บริการ · {TermsVersion}";

    public bool HasAgreementEvidence =>
        !string.IsNullOrWhiteSpace(
            AgreementCoreSnapshotHash);

    public string AgreementEvidenceHash =>
        AgreementCoreSnapshotHash ?? "";

    public string SellerAcceptanceText =>
        AcceptanceText("ผู้ขาย", SellerAcceptedAt);

    public string BuyerAcceptanceText =>
        AcceptanceText("ผู้ซื้อ", BuyerAcceptedAt);

    public string StatusGuidance => State switch
    {
        "AwaitingSellerAcceptance" =>
            $"รอผู้ขายตรวจสอบและเตรียมขายถึง {ExactDeadline()} ยังไม่มีการเก็บเงิน",
        "SellerAcceptedAwaitingPayment" when Role == AppTransactionRole.Buyer =>
            $"ผู้ขายพร้อมขายแล้ว ชำระภายใน {ExactDeadline()}",
        "SellerAcceptedAwaitingPayment" =>
            $"รอผู้ซื้อจ่ายถึง {ExactDeadline()} ยังไม่ต้องส่งสินค้า",
        "CheckoutStarted" or "PaymentPending" =>
            "กำลังตรวจสอบยอดชำระ ผู้ขายจะส่งของได้เมื่อระบบยืนยันยอดแล้ว",
        "PaidAwaitingShipment" when
            ShippingManagedByProvider =>
            "ระบบยืนยันยอดชำระแล้ว กำลังยืนยันการจัดส่งและออกเลขพัสดุให้อัตโนมัติ",
        "PaidAwaitingShipment" when Role == AppTransactionRole.Seller =>
            $"ผู้ซื้อจ่ายแล้ว ส่งสินค้าและเพิ่มเลขพัสดุภายใน {ExactDeadline()}",
        "PaidAwaitingShipment" =>
            $"ระบบยืนยันยอดชำระแล้ว รอผู้ขายส่งสินค้าภายใน {ExactDeadline()}",
        "PaidAwaitingDigitalDelivery" when Role == AppTransactionRole.Seller =>
            "ผู้ซื้อจ่ายแล้ว ส่งมอบผ่านช่องทางที่ตกลง แล้วบันทึกเฉพาะช่องทางและเวลา",
        "PaidAwaitingDigitalDelivery" =>
            "ระบบยืนยันยอดชำระแล้ว รอผู้ขายส่งมอบผ่านช่องทางที่ตกลง",
        "DigitalDeliverySubmitted" when Role == AppTransactionRole.Buyer =>
            "ผู้ขายแจ้งว่าส่งมอบแล้ว ตรวจรายการก่อนยืนยันหรือแจ้งปัญหา ไม่มีการจ่ายอัตโนมัติจากเวลา",
        "DigitalDeliverySubmitted" =>
            "คุณแจ้งการส่งมอบแล้ว รอผู้ซื้อตรวจและยืนยัน ไม่มีการจ่ายอัตโนมัติจากเวลา",
        "TrackingSubmitted" when
            ShippingManagedByProvider =>
            $"ออกเลขพัสดุแล้ว เปิดใบปะหน้าและส่งให้ขนส่งภายใน {ExactDeadline()}",
        "InTransit" =>
            "พัสดุกำลังเดินทาง เราจะอัปเดตจากบริษัทขนส่ง",
        "TrackingUnverified" when
            HasTimelyTrustedCarrierAcceptance &&
            Role == AppTransactionRole.Seller =>
            "ขนส่งรับพัสดุของคุณภายในกำหนดแล้ว ระบบพักการจ่ายไว้ระหว่างตรวจสอบปัญหาการนำส่ง สถานะนี้ยังไม่ใช่การยืนยันว่าจะได้รับเงิน",
        "TrackingUnverified" when
            HasTimelyTrustedCarrierAcceptance =>
            "ขนส่งรับพัสดุแล้วแต่การนำส่งมีปัญหา ระบบพักการจ่ายไว้และช่วงตรวจสินค้า 72 ชั่วโมงยังไม่เริ่ม",
        "TrackingUnverified" =>
            "ยังยืนยันสถานะพัสดุไม่ได้ ระบบพักการจ่ายไว้ระหว่างตรวจสอบ",
        "DeliveredDisputeWindow" =>
            "บริษัทขนส่งแจ้งว่าถึงแล้ว ผู้ซื้อยังกดรับหรือแจ้งปัญหาได้",
        "Disputed" or "ResolutionPending" =>
            "มีการแจ้งปัญหา จึงหยุดจ่ายเงินให้ผู้ขายไว้ก่อน",
        "BuyerConfirmedReceipt" when Role == AppTransactionRole.Buyer =>
            "คุณยืนยันรับของแล้ว ตอนนี้ไม่มีอะไรต้องทำ",
        "BuyerConfirmedReceipt" =>
            "ผู้ซื้อยืนยันรับของแล้ว รายการพร้อมเข้าสู่ขั้นตอนรับเงิน",
        "PayoutEligible" when Role == AppTransactionRole.Buyer =>
            "คุณยืนยันรับของแล้ว เรากำลังเตรียมจ่ายเงินให้ผู้ขาย",
        "PayoutEligible" =>
            "ผู้ซื้อยืนยันรับของแล้ว เรากำลังเตรียมจ่ายเงินเข้าบัญชีของคุณ",
        "PayoutPending" when Role == AppTransactionRole.Buyer =>
            "คุณยืนยันรับของแล้ว เรากำลังดำเนินการจ่ายเงินให้ผู้ขาย",
        "PayoutPending" =>
            "ผู้ซื้อยืนยันรับของแล้ว กำลังดำเนินการโอนเงินเข้าบัญชีของคุณ",
        "PaidOut" when Role == AppTransactionRole.Buyer =>
            "รายการเสร็จแล้ว ผู้ขายได้รับเงินแล้ว",
        "PaidOut" =>
            "รายการเสร็จแล้ว เงินเข้าบัญชีของคุณแล้ว",
        "RefundPending" when
            Role == AppTransactionRole.Buyer &&
            RefundProviderStatus == "requires_action" =>
            RefundActionExpiresAt.HasValue
                ? $"Stripe ส่งอีเมลขอข้อมูลรับเงินคืนแล้ว กรุณาเปิดอีเมลและส่งบัญชีที่ใช้ชำระให้ Stripe โดยตรงภายใน {RefundActionExpiresAt.Value.ToLocalTime():d MMM yyyy HH:mm} น. TOKLONG จะไม่ขอเลขบัญชีในแอป"
                : "Stripe ส่งอีเมลขอข้อมูลรับเงินคืนแล้ว กรุณาเปิดอีเมลและส่งบัญชีที่ใช้ชำระให้ Stripe โดยตรง TOKLONG จะไม่ขอเลขบัญชีในแอป",
        "RefundPending" when
            RefundProviderStatus == "requires_action" =>
            "รอผู้ซื้อยืนยันข้อมูลรับเงินคืนกับ Stripe โดยตรง การจ่ายเงินให้ผู้ขายยังถูกบล็อก",
        "RefundPending" when
            RefundProviderStatus == "pending" =>
            "Stripe ได้รับข้อมูลแล้วและกำลังดำเนินการคืนเงิน สถานะจะเปลี่ยนเมื่อผู้ให้บริการยืนยัน",
        "RefundPending" =>
            "กำลังเริ่มคืนเงิน สถานะจะเปลี่ยนเมื่อผู้ให้บริการยืนยัน",
        "Refunded" =>
            "คืนเงินสำเร็จแล้ว",
        "Expired" when ExpirationReason == "SellerDidNotRespond" =>
            "ผู้ขายไม่ได้ตอบภายในเวลาที่กำหนด และไม่มีการเก็บเงิน ส่งข้อเสนอใหม่ได้หากยังต้องการซื้อ",
        "Expired" when ExpirationReason == "BuyerDidNotPay" &&
                       Role == AppTransactionRole.Seller =>
            "ผู้ซื้อไม่ได้จ่ายภายในเวลาที่กำหนด คุณไม่ต้องจองสินค้าไว้แล้ว",
        "Expired" when ExpirationReason == "BuyerDidNotPay" =>
            "หมดเวลาชำระและรายการนี้ปิดแล้ว หากยังต้องการซื้อให้ส่งข้อเสนอใหม่",
        _ =>
            "กลับมาเช็กสถานะรายการนี้ได้ทุกเมื่อ"
    };

    public string StatusGuidanceBackground => State switch
    {
        "PaidOut" or "Refunded" => "#EAFBF7",
        "Disputed" or "ResolutionPending" => "#FFF4DC",
        _ => RoleBackground
    };

    public string StatusGuidanceColor => State switch
    {
        "PaidOut" or "Refunded" => "#087C68",
        "Disputed" or "ResolutionPending" => "#8A5100",
        _ => RoleColor
    };

    public string StatusGuidanceIcon => State switch
    {
        "AwaitingSellerAcceptance" =>
            "ui_offer.png",
        "SellerAcceptedAwaitingPayment" or "CheckoutStarted" or
            "PaymentPending" =>
                "ui_money.png",
        "PaidAwaitingShipment" or "InTransit" or "TrackingSubmitted" or
            "TrackingUnverified" =>
            "ui_truck.png",
        "PaidAwaitingDigitalDelivery" =>
            "ui_offer.png",
        "DigitalDeliverySubmitted" or "DeliveredDisputeWindow" =>
            "ui_receipt_check.png",
        "PaidOut" or "Refunded" =>
            "ui_check_money.png",
        "PayoutEligible" or "PayoutPending" or "BuyerConfirmedReceipt"
            when Role == AppTransactionRole.Buyer =>
                "ui_check_money.png",
        "PayoutEligible" or "PayoutPending" or "BuyerConfirmedReceipt" =>
            "ui_bank.png",
        "Disputed" or "ResolutionPending" =>
            "ui_shield.png",
        _ =>
            "ui_bell.png"
    };

    public string FulfillmentIcon =>
        FulfillmentType == AppFulfillmentType.Physical
            ? "ui_truck.png"
            : "ui_offer.png";

    public int ProgressCompletedThrough => Role switch
    {
        AppTransactionRole.Buyer => State switch
        {
            "BuyerOfferDraft" => 0,
            "AwaitingSellerAcceptance" or
                "SellerAcceptedAwaitingPayment" or "LinkActive" or
                "CheckoutStarted" or "PaymentPending" => 1,
            "PaidAwaitingShipment" or "PaidAwaitingDigitalDelivery" or
                "TrackingSubmitted" or "TrackingUnverified" or "InTransit" or
                "DigitalDeliverySubmitted" or "ShipmentOverdue" or
                "DeliveredDisputeWindow" or "Disputed" or
                "ResolutionPending" or "CarrierException" => 2,
            "BuyerConfirmedReceipt" or "PayoutEligible" or "PayoutPending" or
                "PaidOut" => 3,
            "RefundPending" or "Refunded" => 2,
            _ => 1
        },
        AppTransactionRole.Seller => State switch
        {
            "AwaitingSellerAcceptance" or "BuyerOfferDraft" => 0,
            "SellerAcceptedAwaitingPayment" or "LinkActive" or
                "CheckoutStarted" or "PaymentPending" or
                "PaidAwaitingShipment" or "PaidAwaitingDigitalDelivery" or
                "ShipmentOverdue" => 1,
            "TrackingSubmitted" or "TrackingUnverified" or "InTransit" or
                "DigitalDeliverySubmitted" or "DeliveredDisputeWindow" or
                "BuyerConfirmedReceipt" or "Disputed" or
                "ResolutionPending" or "CarrierException" or
                "PayoutEligible" or
                "PayoutPending" => 2,
            "PaidOut" => 3,
            _ => ExpirationReason == "BuyerDidNotPay" ? 1 : 0
        },
        _ => 0
    };

    public int ProgressActiveStep => Role switch
    {
        AppTransactionRole.Buyer => State switch
        {
            "BuyerOfferDraft" => 1,
            "SellerAcceptedAwaitingPayment" or "LinkActive" or
                "CheckoutStarted" or "PaymentPending" => 2,
            "PaidAwaitingShipment" or "PaidAwaitingDigitalDelivery" or
                "TrackingSubmitted" or "TrackingUnverified" or "InTransit" or
                "DigitalDeliverySubmitted" or "ShipmentOverdue" or
                "DeliveredDisputeWindow" or "Disputed" or
                "ResolutionPending" or "CarrierException" => 3,
            _ => 0
        },
        AppTransactionRole.Seller => State switch
        {
            "AwaitingSellerAcceptance" or "BuyerOfferDraft" => 1,
            "PaidAwaitingShipment" or "PaidAwaitingDigitalDelivery" or
                "ShipmentOverdue" => 2,
            "TrackingSubmitted" or "TrackingUnverified" or "InTransit" or
                "DigitalDeliverySubmitted" or "DeliveredDisputeWindow" or
                "BuyerConfirmedReceipt" or "CarrierException" or
                "PayoutEligible" or
                "PayoutPending" => 3,
            _ => 0
        },
        _ => 0
    };

    public string ProgressOneLabel =>
        Role == AppTransactionRole.Buyer
            ? "สร้างข้อตกลง"
            : "ยอมรับข้อตกลง";
    public string ProgressTwoLabel =>
        Role == AppTransactionRole.Buyer
            ? "จ่ายเงิน"
            : "ส่งของ";
    public string ProgressThreeLabel =>
        Role == AppTransactionRole.Buyer
            ? "ได้รับของ"
            : "รับเงิน";
    public TransactionProgressStep ProgressOne =>
        CreateProgressStep(
            1,
            ProgressOneLabel,
            Role == AppTransactionRole.Seller
                ? TransactionProgressGlyph.SellerAgreementProof
                : TransactionProgressGlyph.Agreement);
    public TransactionProgressStep ProgressTwo =>
        CreateProgressStep(
            2,
            ProgressTwoLabel,
            Role == AppTransactionRole.Buyer
                ? TransactionProgressGlyph.Payment
                : FulfillmentType == AppFulfillmentType.Physical
                    ? TransactionProgressGlyph.SellerPhysicalShipmentProof
                    : TransactionProgressGlyph.DigitalHandoff);
    public TransactionProgressStep ProgressThree =>
        CreateProgressStep(
            3,
            ProgressThreeLabel,
            Role == AppTransactionRole.Seller
                ? TransactionProgressGlyph.SellerPayoutProof
                : FulfillmentType == AppFulfillmentType.Physical
                    ? TransactionProgressGlyph.PhysicalReceipt
                    : TransactionProgressGlyph.DigitalHandoff);
    public string ProgressConnectorOneColor =>
        ProgressCompletedThrough >= 2
            ? CompletedProgressColor
            : ProgressIncomplete;
    public string ProgressConnectorTwoColor =>
        ProgressCompletedThrough >= 3
            ? CompletedProgressColor
            : ProgressIncomplete;

    public string ProductIcon =>
        FulfillmentType == AppFulfillmentType.Physical
            ? "product_physical.png"
            : "product_digital.png";

    public string ProductVisual =>
        string.IsNullOrWhiteSpace(PhotoUrl) ? ProductIcon : PhotoUrl;

    private string ExactDeadline() =>
        ActionDeadline?.ToLocalTime().ToString(
            "d MMM yyyy · HH:mm",
            ThaiCulture) ?? "เวลาที่แสดงในรายการ";

    private static string AcceptanceText(
        string role,
        DateTimeOffset? acceptedAt) =>
        acceptedAt is null
            ? $"{role}ยังไม่ได้ยอมรับ"
            : $"{role}ยอมรับเมื่อ " +
              acceptedAt.Value.ToLocalTime().ToString(
                  "d MMM yyyy · HH:mm",
                  ThaiCulture) +
              " · บัญชีที่ยืนยันด้วยเบอร์โทร";

    private const string BuyerProgress = "#145FC7";
    private const string BuyerProgressBackground = "#EAF4FF";
    private const string SellerProgress = SellerColorPalette.Role;
    private const string SellerProgressBackground =
        SellerColorPalette.Surface;
    private const string ProgressIncomplete = "#E4EAF1";
    private const string ProgressMuted = "#98A2B3";

    private string CompletedProgressColor =>
        Role == AppTransactionRole.Buyer
            ? BuyerProgress
            : SellerProgress;

    private string CompletedProgressBackground =>
        Role == AppTransactionRole.Buyer
            ? BuyerProgressBackground
            : SellerProgressBackground;

    private string CompletedProgressVariant =>
        Role == AppTransactionRole.Buyer
            ? "buyer_completed"
            : "seller_completed";

    private TransactionProgressStep CreateProgressStep(
        int step,
        string label,
        TransactionProgressGlyph glyph)
    {
        var completed = step <= ProgressCompletedThrough;
        var current =
            Role == AppTransactionRole.Seller &&
            !completed &&
            step == ProgressActiveStep;
        var suffix = completed
            ? CompletedProgressVariant
            : current
                ? "seller_current"
                : "disabled";
        var assetName = glyph switch
        {
            TransactionProgressGlyph.Agreement => "agreement",
            TransactionProgressGlyph.Payment => "payment",
            TransactionProgressGlyph.PhysicalHandoff =>
                "physical_handoff",
            TransactionProgressGlyph.PhysicalReceipt =>
                "physical_receipt",
            TransactionProgressGlyph.DigitalHandoff =>
                "digital_handoff",
            TransactionProgressGlyph.Payout => "payout",
            TransactionProgressGlyph.SellerAgreementProof =>
                "seller_agreement_proof",
            TransactionProgressGlyph.SellerPhysicalShipmentProof =>
                "seller_physical_shipment_proof",
            TransactionProgressGlyph.SellerPayoutProof =>
                "seller_payout_proof",
            _ => throw new ArgumentOutOfRangeException(
                nameof(glyph))
        };
        var background = completed
            ? CompletedProgressBackground
            : current
                ? "#EAFBF7"
                : "#FFFFFF";
        var stroke = completed
            ? CompletedProgressColor
            : current
                ? "#087C68"
                : ProgressIncomplete;
        var labelColor = completed
            ? CompletedProgressColor
            : current
                ? "#087C68"
                : ProgressMuted;
        var semanticState = completed
            ? "เสร็จแล้ว"
            : current
                ? "ขั้นปัจจุบัน"
                : "ยังไม่เสร็จ";

        return new TransactionProgressStep(
            label,
            $"progress_{assetName}_{suffix}.png",
            glyph,
            background,
            stroke,
            labelColor,
            $"{label} {semanticState}");
    }

}

public static class MoneyFormatter
{
    public static string Format(long amountSatang, string currency)
    {
        if (amountSatang < 0)
            throw new ArgumentOutOfRangeException(nameof(amountSatang));

        var amount = amountSatang / 100m;
        var format = amountSatang % 100 == 0 ? "#,##0" : "#,##0.00";
        return string.Equals(currency, "THB", StringComparison.OrdinalIgnoreCase)
            ? $"฿{amount.ToString(format, CultureInfo.GetCultureInfo("th-TH"))}"
            : $"{currency.ToUpperInvariant()} {amount.ToString(format, CultureInfo.InvariantCulture)}";
    }
}
