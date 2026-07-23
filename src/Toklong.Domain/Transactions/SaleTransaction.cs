using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Toklong.Domain.Common;

namespace Toklong.Domain.Transactions;

public sealed class SaleTransaction
{
    private readonly List<AuditEvent> _auditEvents = [];
    private readonly List<ExternalEvent> _externalEvents = [];

    private SaleTransaction() { }

    public Guid Id { get; private set; }
    public string PublicToken { get; private set; } = "";
    public string SellerAccessToken { get; private set; } = "";
    public string? BuyerAccessToken { get; private set; }
    public Guid SellerId { get; private set; }
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
    public string PhotoUrl { get; private set; } = "";
    public long PriceSatang { get; private set; }
    public long ShippingFeeSatang { get; private set; }
    public string Currency { get; private set; } = "THB";
    public int ShipByDurationHours { get; private set; }
    public string TermsVersion { get; private set; } = "";
    public DateTimeOffset SellerAcceptedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ActivatedAt { get; private set; }
    public string? BuyerDisplayName { get; private set; }
    public string? BuyerContact { get; private set; }
    public string? DeliveryAddress { get; private set; }
    public DateTimeOffset? BuyerAcceptedAt { get; private set; }
    public string? ProductSnapshotJson { get; private set; }
    public string? ProductSnapshotHash { get; private set; }
    public DateTimeOffset? ShipByAt { get; private set; }
    public string PaymentProvider { get; private set; } = "manual-bank";
    public string? PaymentReference { get; private set; }
    public DateTimeOffset? PaymentConfirmedAt { get; private set; }
    public string? CarrierCode { get; private set; }
    public string? TrackingNumber { get; private set; }
    public DateTimeOffset? TrackingSubmittedAt { get; private set; }
    public string? DigitalDeliveryStatement { get; private set; }
    public DateTimeOffset? DigitalDeliverySubmittedAt { get; private set; }
    public string? DigitalManualReviewReference { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public string? DeliveryEventId { get; private set; }
    public DateTimeOffset? DisputeWindowEndsAt { get; private set; }
    public DateTimeOffset? BuyerConfirmedAt { get; private set; }
    public DisputeReason? DisputeReason { get; private set; }
    public string? DisputeStatement { get; private set; }
    public DateTimeOffset? DisputeOpenedAt { get; private set; }
    public string? PayoutReference { get; private set; }
    public DateTimeOffset? PayoutConfirmedAt { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyCollection<AuditEvent> AuditEvents => _auditEvents;
    public IReadOnlyCollection<ExternalEvent> ExternalEvents => _externalEvents;
    public long BuyerTotalSatang => checked(PriceSatang + ShippingFeeSatang);

    public static SaleTransaction CreateAndActivate(
        Guid sellerId,
        string sellerDisplayName,
        string sellerContact,
        string payoutBankCode,
        string payoutAccountName,
        string payoutAccountNumber,
        FulfillmentType fulfillmentType,
        string productName,
        string category,
        ConditionCode condition,
        string description,
        string knownDefects,
        string photoUrl,
        long priceSatang,
        long shippingFeeSatang,
        int shipByDurationHours,
        string termsVersion,
        DateTimeOffset now,
        TransactionTransitionService transitions)
    {
        if (string.IsNullOrWhiteSpace(productName) || string.IsNullOrWhiteSpace(description))
            throw new DomainException("กรุณาระบุรายการและรายละเอียดข้อตกลง");
        if (string.IsNullOrWhiteSpace(payoutBankCode) || string.IsNullOrWhiteSpace(payoutAccountName))
            throw new DomainException("กรุณากรอกข้อมูลบัญชีรับเงินของผู้ขาย");
        var normalizedAccountNumber = new string(payoutAccountNumber.Where(char.IsDigit).ToArray());
        if (normalizedAccountNumber.Length is < 10 or > 15)
            throw new DomainException("เลขบัญชีรับเงินต้องมีตัวเลข 10–15 หลัก");
        if (string.IsNullOrWhiteSpace(photoUrl))
            throw new DomainException("กรุณาเพิ่มรูปสินค้าอย่างน้อย 1 รูป");
        if (priceSatang <= 0 || shippingFeeSatang < 0)
            throw new DomainException("ยอดเงินไม่ถูกต้อง");
        if (fulfillmentType == FulfillmentType.DigitalHandoff && shippingFeeSatang != 0)
            throw new DomainException("สินค้าดิจิทัลต้องไม่มีค่าจัดส่ง");
        if (fulfillmentType == FulfillmentType.DigitalHandoff &&
            ContainsDigitalSecret(description))
            throw new DomainException("ห้ามใส่รหัสผ่าน รหัสกู้คืน private key หรือข้อมูลลับในรายละเอียดข้อตกลง");
        if (shipByDurationHours is < 24 or > 168)
            throw new DomainException("กำหนดส่งต้องอยู่ระหว่าง 24 ถึง 168 ชั่วโมง");
        var policy = ProductPolicy.Evaluate(fulfillmentType, category, productName, description);
        if (!policy.Allowed)
            throw new DomainException(policy.UserMessage);

        var sale = new SaleTransaction
        {
            Id = Guid.NewGuid(),
            SellerId = sellerId,
            PublicToken = Token(),
            SellerAccessToken = Token(),
            State = TransactionState.SellerDraft,
            SellerDisplayName = sellerDisplayName.Trim(),
            SellerContact = sellerContact.Trim(),
            PayoutBankCode = payoutBankCode.Trim(),
            PayoutAccountName = payoutAccountName.Trim(),
            PayoutAccountNumber = normalizedAccountNumber,
            FulfillmentType = fulfillmentType,
            ProductName = productName.Trim(),
            Category = category.Trim(),
            Condition = condition,
            Description = description.Trim(),
            KnownDefects = knownDefects.Trim(),
            PhotoUrl = photoUrl.Trim(),
            PriceSatang = priceSatang,
            ShippingFeeSatang = shippingFeeSatang,
            ShipByDurationHours = shipByDurationHours,
            TermsVersion = termsVersion,
            SellerAcceptedAt = now,
            CreatedAt = now
        };

        transitions.Transition(sale, TransactionState.LinkActive, ActorRole.Seller, sale.SellerAccessToken,
            "sale_link.activated", now, sale.Id.ToString("N"), $"activate:{sale.Id:N}");
        sale.ActivatedAt = now;
        return sale;
    }

    public void BeginCheckout(
        string buyerDisplayName,
        string buyerContact,
        string deliveryAddress,
        DateTimeOffset now,
        TransactionTransitionService transitions)
    {
        BuyerAccessToken ??= Token();
        BuyerDisplayName = Required(buyerDisplayName, "ชื่อผู้ซื้อ");
        BuyerContact = Required(buyerContact, "ช่องทางติดต่อ");
        DeliveryAddress = FulfillmentType == FulfillmentType.PhysicalShipment
            ? Required(deliveryAddress, "ที่อยู่จัดส่ง")
            : null;
        BuyerAcceptedAt = now;

        var snapshot = new
        {
            ProductName,
            FulfillmentType,
            Category,
            Condition,
            Description,
            KnownDefects,
            PhotoUrl,
            PriceSatang,
            ShippingFeeSatang,
            Currency,
            ShipByDurationHours,
            TermsVersion,
            SellerAcceptedAt,
            BuyerAcceptedAt
        };
        ProductSnapshotJson = JsonSerializer.Serialize(snapshot);
        ProductSnapshotHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ProductSnapshotJson))).ToLowerInvariant();
        PaymentReference = $"TK-{DateTime.UtcNow:yyMMdd}-{Id.ToString("N")[..8].ToUpperInvariant()}";

        transitions.Transition(this, TransactionState.CheckoutStarted, ActorRole.Buyer, BuyerAccessToken,
            "checkout.started", now, Id.ToString("N"), $"checkout:{Id:N}");
        transitions.Transition(this, TransactionState.PaymentPending, ActorRole.Buyer, BuyerAccessToken,
            "payment.awaiting_verification", now, Id.ToString("N"), $"payment-pending:{Id:N}");
    }

    public void ConfirmPayment(string eventId, DateTimeOffset confirmedAt, TransactionTransitionService transitions)
    {
        EnsureExternalEventIsNew("manual-bank", eventId, "payment.confirmed", confirmedAt, confirmedAt);
        ShipByAt = confirmedAt.AddHours(ShipByDurationHours);
        PaymentConfirmedAt = confirmedAt;
        var target = FulfillmentType == FulfillmentType.PhysicalShipment
            ? TransactionState.PaidAwaitingShipment
            : TransactionState.PaidAwaitingDigitalDelivery;
        transitions.Transition(this, target, ActorRole.Reconciliation, "manual-bank",
            "payment.confirmed", confirmedAt, eventId, $"manual-bank:{eventId}");
    }

    public void SubmitTracking(string sellerToken, string carrierCode, string trackingNumber, DateTimeOffset now, TransactionTransitionService transitions)
    {
        EnsureSeller(sellerToken);
        if (FulfillmentType != FulfillmentType.PhysicalShipment)
            throw new DomainException("รายการดิจิทัลไม่ใช้ Tracking");
        if (ShipByAt is not null && now > ShipByAt)
            throw new DomainException("เลยกำหนดส่งแล้ว กรุณาติดต่อฝ่ายช่วยเหลือ");
        CarrierCode = Required(carrierCode, "ขนส่ง").ToUpperInvariant();
        TrackingNumber = Required(trackingNumber, "หมายเลขติดตาม").ToUpperInvariant();
        TrackingSubmittedAt = now;
        transitions.Transition(this, TransactionState.TrackingSubmitted, ActorRole.Seller, sellerToken,
            "shipment.tracking_submitted", now, Id.ToString("N"), $"tracking:{Id:N}:{TrackingNumber}");
    }

    public void RecordCarrierEvent(string eventId, string eventType, DateTimeOffset occurredAt, DateTimeOffset receivedAt, TransactionTransitionService transitions)
    {
        if (FulfillmentType != FulfillmentType.PhysicalShipment)
            throw new DomainException("รายการดิจิทัลไม่รับสถานะจากขนส่ง");
        EnsureExternalEventIsNew(CarrierCode ?? "carrier", eventId, eventType, occurredAt, receivedAt);
        var target = eventType.ToLowerInvariant() switch
        {
            "in_transit" => TransactionState.InTransit,
            "unverified" => TransactionState.TrackingUnverified,
            "delivered" => TransactionState.DeliveredDisputeWindow,
            _ => throw new DomainException("ไม่รองรับสถานะขนส่งนี้")
        };

        if (target == TransactionState.DeliveredDisputeWindow)
        {
            DeliveredAt = occurredAt;
            DeliveryEventId = eventId;
            DisputeWindowEndsAt = occurredAt.AddHours(168);
        }

        transitions.Transition(this, target, ActorRole.CarrierProvider, CarrierCode ?? "carrier",
            $"carrier.{eventType.ToLowerInvariant()}", occurredAt, eventId, $"carrier:{eventId}");
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
        BuyerConfirmedAt = now;
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
        transitions.Transition(this, TransactionState.Disputed, ActorRole.Buyer, buyerToken,
            "dispute.opened", now, Id.ToString("N"), $"dispute:{Id:N}");
    }

    public void EvaluateDeadline(DateTimeOffset now, TransactionTransitionService transitions)
    {
        if (FulfillmentType == FulfillmentType.DigitalHandoff)
            return;
        if (State != TransactionState.DeliveredDisputeWindow || DisputeWindowEndsAt is null || now < DisputeWindowEndsAt)
            return;
        if (DisputeOpenedAt is not null)
            throw new DomainException("ข้อโต้แย้งที่เปิดอยู่บล็อกการจ่ายเงิน");
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
        if (DisputeOpenedAt is not null)
            throw new DomainException("ข้อโต้แย้งที่เปิดอยู่บล็อกการจ่ายเงิน");
        DigitalManualReviewReference = Required(reviewReference, "เลขอ้างอิงการตรวจสอบ");
        transitions.Transition(this, TransactionState.PayoutEligible, ActorRole.Reconciliation, "manual-review",
            "payout.eligible_digital_manual_review", now, reviewReference, $"digital-review:{Id:N}:{reviewReference}");
    }

    public void StartPayout(string reference, DateTimeOffset now, TransactionTransitionService transitions)
    {
        if (DisputeOpenedAt is not null)
            throw new DomainException("ข้อโต้แย้งที่เปิดอยู่บล็อกการจ่ายเงิน");
        if (string.IsNullOrWhiteSpace(PayoutBankCode) ||
            string.IsNullOrWhiteSpace(PayoutAccountName) ||
            string.IsNullOrWhiteSpace(PayoutAccountNumber))
            throw new DomainException("ยังไม่มีบัญชีรับเงินของผู้ขาย จึงยังเริ่มจ่ายเงินไม่ได้");
        PayoutReference = Required(reference, "เลขอ้างอิงการจ่าย");
        transitions.Transition(this, TransactionState.PayoutPending, ActorRole.Reconciliation, "manual-bank",
            "payout.instruction_created", now, reference, $"payout:{Id:N}");
    }

    public void ConfirmPayout(string eventId, DateTimeOffset confirmedAt, TransactionTransitionService transitions)
    {
        EnsureExternalEventIsNew("manual-bank", eventId, "payout.confirmed", confirmedAt, confirmedAt);
        PayoutConfirmedAt = confirmedAt;
        transitions.Transition(this, TransactionState.PaidOut, ActorRole.Reconciliation, "manual-bank",
            "payout.confirmed", confirmedAt, eventId, $"manual-payout:{eventId}");
    }

    public bool HasExternalEvent(string provider, string eventId) =>
        _externalEvents.Any(x => x.Provider == provider && x.EventId == eventId);

    internal void ApplyTransition(TransactionState state)
    {
        State = state;
        Version++;
    }

    internal void AddAudit(AuditEvent auditEvent) => _auditEvents.Add(auditEvent);

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
