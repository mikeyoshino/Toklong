using Toklong.Domain.Common;
using Toklong.Domain.Transactions;

namespace Toklong.Domain.Tests.Transactions;

public sealed class SaleTransactionTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 20, 7, 0, 0, TimeSpan.Zero);
    private readonly TransactionTransitionService _transitions = new();

    [Fact]
    public void Checkout_does_not_mark_transaction_paid()
    {
        var transaction = CreateSale();

        transaction.BeginCheckout("ผู้ซื้อ", "0800000000", "กรุงเทพฯ ประเทศไทย", Start.AddMinutes(5), _transitions);

        Assert.Equal(TransactionState.PaymentPending, transaction.State);
        Assert.Null(transaction.PaymentConfirmedAt);
        Assert.Null(transaction.ShipByAt);
        Assert.NotNull(transaction.ProductSnapshotHash);
    }

    [Fact]
    public void Unsupported_or_prohibited_item_is_blocked_before_activation()
    {
        Assert.Throws<DomainException>(() =>
            SaleTransaction.CreateAndActivate(
                Guid.NewGuid(),
                "ผู้ขาย", "line:seller", "KBANK", "ผู้ขาย ทดสอบ", "1234567890",
                FulfillmentType.PhysicalShipment,
                "บริการรับถ่ายภาพ", "บริการ", ConditionCode.New,
                "รับถ่ายภาพนอกสถานที่", "ไม่มี", "https://example.com/photo.jpg",
                100_000, 0, 48, "mvp-th-2026-07", Start, _transitions));
    }

    [Fact]
    public void Seller_payout_account_is_required_before_activation()
    {
        Assert.Throws<DomainException>(() =>
            SaleTransaction.CreateAndActivate(
                Guid.NewGuid(),
                "ผู้ขาย", "line:seller", "KBANK", "ผู้ขาย ทดสอบ", "",
                FulfillmentType.PhysicalShipment,
                "กล้องฟิล์ม", "กล้องและอุปกรณ์", ConditionCode.UsedGood,
                "ใช้งานได้ปกติพร้อมสายคล้อง", "ไม่มี", "https://example.com/photo.jpg",
                450_000, 6_000, 48, "mvp-th-2026-07", Start, _transitions));
    }

    [Fact]
    public void Verified_payment_is_the_only_path_that_enables_shipping()
    {
        var transaction = Checkout();

        transaction.ConfirmPayment("bank-event-1", Start.AddMinutes(10), _transitions);

        Assert.Equal(TransactionState.PaidAwaitingShipment, transaction.State);
        Assert.Equal(Start.AddHours(48).AddMinutes(10), transaction.ShipByAt);
        Assert.Single(transaction.AuditEvents, x => x.Name == "payment.confirmed");
    }

    [Fact]
    public void Seller_token_is_required_to_submit_tracking()
    {
        var transaction = Paid();

        Assert.Throws<DomainException>(() =>
            transaction.SubmitTracking("buyer-or-random-token", "FLASH", "TH12345678", Start.AddHours(1), _transitions));
        Assert.Equal(TransactionState.PaidAwaitingShipment, transaction.State);
    }

    [Fact]
    public void Unverified_tracking_never_starts_dispute_clock()
    {
        var transaction = Shipped();

        transaction.RecordCarrierEvent("carrier-1", "unverified", Start.AddHours(2), Start.AddHours(2), _transitions);

        Assert.Equal(TransactionState.TrackingUnverified, transaction.State);
        Assert.Null(transaction.DeliveredAt);
        Assert.Null(transaction.DisputeWindowEndsAt);
    }

    [Fact]
    public void Trusted_delivery_starts_exact_168_hour_window()
    {
        var transaction = Shipped();
        transaction.RecordCarrierEvent("carrier-move", "in_transit", Start.AddHours(2), Start.AddHours(2), _transitions);
        var delivered = new DateTimeOffset(2026, 7, 20, 7, 18, 0, TimeSpan.Zero);

        transaction.RecordCarrierEvent("carrier-delivered", "delivered", delivered, delivered.AddMinutes(1), _transitions);

        Assert.Equal(TransactionState.DeliveredDisputeWindow, transaction.State);
        Assert.Equal(delivered, transaction.DeliveredAt);
        Assert.Equal(delivered.AddHours(168), transaction.DisputeWindowEndsAt);
    }

    [Fact]
    public void Open_dispute_blocks_buyer_confirmation_and_payout()
    {
        var transaction = Delivered();
        var buyerToken = transaction.BuyerAccessToken!;

        transaction.OpenDispute(buyerToken, DisputeReason.NotAsDescribed, "สินค้าไม่ตรงรายละเอียดและมีรอยแตก", Start.AddHours(4), _transitions);

        Assert.Equal(TransactionState.Disputed, transaction.State);
        Assert.Throws<DomainException>(() =>
            transaction.ConfirmReceipt(buyerToken, Start.AddHours(5), _transitions));
        Assert.Null(transaction.PayoutReference);
    }

    [Fact]
    public void Buyer_confirmation_creates_eligibility_but_not_payout_success()
    {
        var transaction = Delivered();

        transaction.ConfirmReceipt(transaction.BuyerAccessToken!, Start.AddHours(4), _transitions);
        transaction.StartPayout("PAYOUT-1", Start.AddHours(4), _transitions);

        Assert.Equal(TransactionState.PayoutPending, transaction.State);
        Assert.Null(transaction.PayoutConfirmedAt);
    }

    [Fact]
    public void Deadline_release_requires_verified_delivery_and_exact_deadline()
    {
        var transaction = Delivered();
        var deadline = transaction.DisputeWindowEndsAt!.Value;

        transaction.EvaluateDeadline(deadline.AddTicks(-1), _transitions);
        Assert.Equal(TransactionState.DeliveredDisputeWindow, transaction.State);

        transaction.EvaluateDeadline(deadline, _transitions);
        Assert.Equal(TransactionState.PayoutEligible, transaction.State);
        Assert.Null(transaction.PayoutConfirmedAt);
    }

    [Fact]
    public void Payout_is_complete_only_after_confirmed_external_event()
    {
        var transaction = Delivered();
        transaction.ConfirmReceipt(transaction.BuyerAccessToken!, Start.AddHours(4), _transitions);
        transaction.StartPayout("PAYOUT-1", Start.AddHours(4), _transitions);

        transaction.ConfirmPayout("payout-event-1", Start.AddHours(5), _transitions);

        Assert.Equal(TransactionState.PaidOut, transaction.State);
        Assert.Equal(Start.AddHours(5), transaction.PayoutConfirmedAt);
        Assert.Single(transaction.ExternalEvents, x => x.EventType == "payout.confirmed");
    }

    [Fact]
    public void Duplicate_external_event_is_rejected_by_aggregate()
    {
        var transaction = Checkout();
        transaction.ConfirmPayment("same-event", Start.AddMinutes(10), _transitions);

        Assert.Throws<DomainException>(() =>
            transaction.ConfirmPayment("same-event", Start.AddMinutes(11), _transitions));
        Assert.Single(transaction.AuditEvents, x => x.Name == "payment.confirmed");
    }

    [Fact]
    public void Digital_payment_enables_handoff_without_requiring_an_address()
    {
        var transaction = CreateDigitalSale();
        transaction.BeginCheckout(
            "ผู้ซื้อ",
            "0800000000",
            "",
            Start.AddMinutes(5),
            _transitions);

        transaction.ConfirmPayment(
            "digital-payment-1",
            Start.AddMinutes(10),
            _transitions);

        Assert.Equal(
            TransactionState.PaidAwaitingDigitalDelivery,
            transaction.State);
        Assert.Null(transaction.DeliveryAddress);
        Assert.Equal(0, transaction.ShippingFeeSatang);
    }

    [Fact]
    public void Digital_handoff_never_auto_releases_from_seller_claim_or_time()
    {
        var transaction = PaidDigital();

        transaction.SubmitDigitalDelivery(
            transaction.SellerAccessToken,
            "ส่งข้อมูลผ่าน LINE ที่ตกลงกันแล้ว",
            Start.AddHours(1),
            _transitions);
        transaction.EvaluateDeadline(Start.AddYears(10), _transitions);

        Assert.Equal(TransactionState.DigitalDeliverySubmitted, transaction.State);
        Assert.Null(transaction.DisputeWindowEndsAt);
        Assert.Null(transaction.PayoutReference);
    }

    [Theory]
    [InlineData("password: example")]
    [InlineData("ส่ง recovery code แล้ว")]
    [InlineData("private key อยู่ในแชต")]
    [InlineData("รหัสผ่านคือ 123456")]
    public void Digital_handoff_rejects_secret_bearing_statements(string statement)
    {
        var transaction = PaidDigital();

        Assert.Throws<DomainException>(() =>
            transaction.SubmitDigitalDelivery(
                transaction.SellerAccessToken,
                statement,
                Start.AddHours(1),
                _transitions));

        Assert.Equal(
            TransactionState.PaidAwaitingDigitalDelivery,
            transaction.State);
    }

    [Fact]
    public void Digital_buyer_confirmation_creates_payout_eligibility()
    {
        var transaction = DigitalDelivered();

        transaction.ConfirmReceipt(
            transaction.BuyerAccessToken!,
            Start.AddHours(2),
            _transitions);

        Assert.Equal(TransactionState.PayoutEligible, transaction.State);
        Assert.Null(transaction.PayoutConfirmedAt);
    }

    [Fact]
    public void Digital_dispute_blocks_confirmation_and_payout()
    {
        var transaction = DigitalDelivered();
        var buyerToken = transaction.BuyerAccessToken!;

        transaction.OpenDispute(
            buyerToken,
            DisputeReason.NotReceived,
            "ยังไม่ได้รับสิทธิ์ตามที่ตกลงกัน",
            Start.AddHours(2),
            _transitions);

        Assert.Equal(TransactionState.Disputed, transaction.State);
        Assert.Throws<DomainException>(() =>
            transaction.ConfirmReceipt(
                buyerToken,
                Start.AddHours(3),
                _transitions));
        Assert.Throws<DomainException>(() =>
            transaction.AuthorizeDigitalRelease(
                "REVIEW-BLOCKED",
                Start.AddHours(3),
                _transitions));
    }

    [Fact]
    public void Authorized_manual_review_can_release_digital_but_not_mark_payout_complete()
    {
        var transaction = DigitalDelivered();

        transaction.AuthorizeDigitalRelease(
            "REVIEW-001",
            Start.AddHours(3),
            _transitions);

        Assert.Equal(TransactionState.PayoutEligible, transaction.State);
        Assert.Null(transaction.PayoutConfirmedAt);
        Assert.Contains(
            transaction.AuditEvents,
            x => x.Name == "payout.eligible_digital_manual_review");
    }

    [Fact]
    public void Digital_agreement_cannot_submit_tracking()
    {
        var transaction = PaidDigital();

        Assert.Throws<DomainException>(() =>
            transaction.SubmitTracking(
                transaction.SellerAccessToken,
                "FLASH",
                "TH12345678",
                Start.AddHours(1),
                _transitions));
    }

    [Fact]
    public void Digital_handoff_requires_the_seller_token()
    {
        var transaction = PaidDigital();

        Assert.Throws<DomainException>(() =>
            transaction.SubmitDigitalDelivery(
                "buyer-or-random-token",
                "ส่งข้อมูลผ่าน LINE ที่ตกลงกันแล้ว",
                Start.AddHours(1),
                _transitions));
    }

    [Fact]
    public void Digital_agreement_rejects_shipping_fees()
    {
        Assert.Throws<DomainException>(() =>
            SaleTransaction.CreateAndActivate(
                Guid.NewGuid(),
                "ผู้ขาย",
                "line:seller",
                "KBANK",
                "ผู้ขาย ทดสอบ",
                "1234567890",
                FulfillmentType.DigitalHandoff,
                "ไอดีเกมตัวอย่าง",
                "สินค้าดิจิทัลที่โอนได้",
                ConditionCode.AsDescribed,
                "บัญชีเกมที่ผู้ขายยืนยันว่ามีสิทธิ์โอน",
                "",
                "https://example.com/digital.jpg",
                250_000,
                5_000,
                48,
                "mvp-th-2026-07",
                Start,
                _transitions));
    }

    private SaleTransaction CreateSale() =>
        SaleTransaction.CreateAndActivate(
            Guid.NewGuid(),
            "ผู้ขาย", "line:seller", "KBANK", "ผู้ขาย ทดสอบ", "1234567890",
            FulfillmentType.PhysicalShipment,
            "กล้องฟิล์ม", "กล้องและอุปกรณ์", ConditionCode.UsedGood,
            "ใช้งานได้ปกติพร้อมสายคล้อง", "มีรอยเล็กน้อย", "https://example.com/photo.jpg",
            450_000, 6_000, 48, "mvp-th-2026-07", Start, _transitions);

    private SaleTransaction Checkout()
    {
        var transaction = CreateSale();
        transaction.BeginCheckout("ผู้ซื้อ", "0800000000", "กรุงเทพฯ ประเทศไทย", Start.AddMinutes(5), _transitions);
        return transaction;
    }

    private SaleTransaction Paid()
    {
        var transaction = Checkout();
        transaction.ConfirmPayment("bank-event-1", Start.AddMinutes(10), _transitions);
        return transaction;
    }

    private SaleTransaction Shipped()
    {
        var transaction = Paid();
        transaction.SubmitTracking(transaction.SellerAccessToken, "FLASH", "TH12345678", Start.AddHours(1), _transitions);
        return transaction;
    }

    private SaleTransaction Delivered()
    {
        var transaction = Shipped();
        transaction.RecordCarrierEvent("carrier-delivered", "delivered", Start.AddHours(3), Start.AddHours(3), _transitions);
        return transaction;
    }

    private SaleTransaction CreateDigitalSale() =>
        SaleTransaction.CreateAndActivate(
            Guid.NewGuid(),
            "ผู้ขาย",
            "line:seller",
            "KBANK",
            "ผู้ขาย ทดสอบ",
            "1234567890",
            FulfillmentType.DigitalHandoff,
            "ไอดีเกมตัวอย่าง",
            "สินค้าดิจิทัลที่โอนได้",
            ConditionCode.AsDescribed,
            "บัญชีเกมที่ผู้ขายยืนยันว่ามีสิทธิ์โอน ส่งมอบผ่าน LINE",
            "",
            "https://example.com/digital.jpg",
            250_000,
            0,
            48,
            "mvp-th-2026-07",
            Start,
            _transitions);

    private SaleTransaction PaidDigital()
    {
        var transaction = CreateDigitalSale();
        transaction.BeginCheckout(
            "ผู้ซื้อ",
            "0800000000",
            "",
            Start.AddMinutes(5),
            _transitions);
        transaction.ConfirmPayment(
            "digital-payment-1",
            Start.AddMinutes(10),
            _transitions);
        return transaction;
    }

    private SaleTransaction DigitalDelivered()
    {
        var transaction = PaidDigital();
        transaction.SubmitDigitalDelivery(
            transaction.SellerAccessToken,
            "ส่งข้อมูลผ่าน LINE ที่ตกลงกันแล้ว",
            Start.AddHours(1),
            _transitions);
        return transaction;
    }
}
