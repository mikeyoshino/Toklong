using Toklong.Domain.Common;
using Toklong.Domain.Transactions;

namespace Toklong.Domain.Tests.Transactions;

public sealed class SaleTransactionTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 20, 7, 0, 0, TimeSpan.Zero);
    private readonly TransactionTransitionService _transitions = new();

    [Theory]
    [InlineData(99_999)]
    [InlineData(99_999_901)]
    public void Buyer_offer_rejects_price_outside_technical_range(
        long priceSatang)
    {
        var exception = Assert.Throws<DomainException>(() =>
            TestTransactionFactory.CreateBuyerOffer(
                Guid.NewGuid(),
                "ผู้ซื้อ",
                "+66811111111",
                FulfillmentType.PhysicalShipment,
                "กล้องฟิล์ม",
                "กล้องพร้อมเลนส์ตามที่คุยกัน",
                ConditionCode.UsedGood,
                "",
                null,
                priceSatang,
                "mvp-th-2026-07",
                Start,
                _transitions));

        Assert.Contains(
            "1,000–999,999",
            exception.Message);
    }

    [Fact]
    public void Buyer_offer_accepts_absolute_technical_maximum()
    {
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ",
            "+66811111111",
            FulfillmentType.PhysicalShipment,
            "กล้องฟิล์ม",
            "กล้องพร้อมเลนส์ตามที่คุยกัน",
            ConditionCode.UsedGood,
            "",
            null,
            SaleTransaction.MaximumProtectedItemPriceSatang,
            "mvp-th-2026-07",
            Start,
            _transitions);

        Assert.Equal(
            SaleTransaction.MaximumProtectedItemPriceSatang,
            transaction.PriceSatang);
    }

    [Fact]
    public void Buyer_offer_creates_two_distinct_unguessable_links_without_payment()
    {
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ", "buyer@example.com",
            FulfillmentType.PhysicalShipment,
            "กล้องฟิล์ม", "กล้องพร้อมเลนส์ตามที่คุยกัน",
            ConditionCode.UsedGood, "", "https://example.com/photo.jpg",
            450_000, "mvp-th-2026-07", Start, _transitions);

        Assert.Equal(TransactionState.AwaitingSellerAcceptance, transaction.State);
        Assert.Equal(InitiatorRole.Buyer, transaction.InitiatorRole);
        Assert.Equal(48, transaction.PublicToken.Length);
        Assert.Equal(48, transaction.BuyerAccessToken!.Length);
        Assert.NotEqual(transaction.PublicToken, transaction.BuyerAccessToken);
        Assert.Null(transaction.PaymentReference);
        Assert.Equal(
            Start.AddHours(
                SaleTransaction.SellerAcceptanceWindowHours),
            transaction.SellerAcceptanceDeadlineAt);
        Assert.Null(transaction.BuyerPaymentDeadlineAt);
        Assert.Equal(
            SaleTransaction.PhysicalInspectionWindowHours,
            transaction.InspectionWindowDurationHours);
        Assert.Contains(transaction.AuditEvents, x => x.Name == "buyer_offer.created");
    }

    [Fact]
    public void Physical_buyer_offer_requires_full_delivery_address_at_creation()
    {
        var exception = Assert.Throws<DomainException>(() =>
            SaleTransaction.CreateBuyerOffer(
                Guid.NewGuid(),
                "ผู้ซื้อ",
                "+66811111111",
                FulfillmentType.PhysicalShipment,
                "กล้องฟิล์ม",
                "กล้องพร้อมเลนส์ตามที่คุยกัน",
                ConditionCode.UsedGood,
                "",
                "https://example.com/photo.jpg",
                450_000,
                null,
                "กรุงเทพมหานคร",
                "10110",
                "mvp-th-2026-07",
                Start,
                _transitions));

        Assert.Contains("ที่อยู่จัดส่ง", exception.Message);
    }

    [Fact]
    public void Targeted_buyer_offer_notifies_only_intended_seller_phone()
    {
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ ทดสอบ",
            "+66811111111",
            "+66822222222",
            FulfillmentType.PhysicalShipment,
            "กล้อง Fujifilm X-T30 II",
            "ใช้งานปกติ พร้อมเลนส์และแบตเตอรี่",
            ConditionCode.UsedGood,
            "ไม่มี",
            "https://example.com/photo.jpg",
            450_000,
            "mvp-th-2026-07",
            Start,
            _transitions);

        Assert.True(
            transaction.IsIntendedSeller("+66822222222"));
        Assert.False(
            transaction.IsIntendedSeller("+66833333333"));
        var notification = Assert.Single(
            transaction.Notifications,
            item => item.Template == "buyer_offer_received");
        Assert.Equal("seller", notification.Audience);
        Assert.Equal(
            "+66822222222",
            notification.Recipient);
    }

    [Fact]
    public void Seller_response_deadline_expires_offer_once()
    {
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ",
            "+66811111111",
            FulfillmentType.PhysicalShipment,
            "กล้องฟิล์ม",
            "กล้องพร้อมเลนส์ตามที่คุยกัน",
            ConditionCode.UsedGood,
            "",
            "https://example.com/photo.jpg",
            450_000,
            "mvp-th-2026-07",
            Start,
            _transitions);

        Assert.False(transaction.ExpireIfDue(
            transaction.SellerAcceptanceDeadlineAt.AddTicks(-1),
            _transitions));
        Assert.True(transaction.ExpireIfDue(
            transaction.SellerAcceptanceDeadlineAt,
            _transitions));
        Assert.False(transaction.ExpireIfDue(
            transaction.SellerAcceptanceDeadlineAt.AddMinutes(1),
            _transitions));
        Assert.Equal(TransactionState.Expired, transaction.State);
        Assert.Equal(
            TransactionExpirationReason.SellerDidNotRespond,
            transaction.ExpirationReason);
        Assert.Single(
            transaction.AuditEvents,
            audit =>
                audit.Name ==
                "buyer_offer.seller_response_expired");
    }

    [Fact]
    public void Seller_cannot_accept_at_response_deadline_before_job_runs()
    {
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ",
            "+66811111111",
            FulfillmentType.PhysicalShipment,
            "กล้องฟิล์ม",
            "กล้องพร้อมเลนส์ตามที่คุยกัน",
            ConditionCode.UsedGood,
            "",
            "https://example.com/photo.jpg",
            450_000,
            "mvp-th-2026-07",
            Start,
            _transitions);

        Assert.Throws<DomainException>(() =>
            transaction.AcceptBuyerOffer(
                Guid.NewGuid(),
                "ผู้ขาย",
                "+66822222222",
                "KBANK",
                "ผู้ขาย ทดสอบ",
                "1234567890",
                true,
                transaction.SellerAcceptanceDeadlineAt,
                _transitions));
        Assert.Equal(
            TransactionState.AwaitingSellerAcceptance,
            transaction.State);
        Assert.Null(transaction.SellerAcceptedAt);
    }

    [Fact]
    public void Seller_acceptance_opens_exact_one_hour_payment_window()
    {
        var transaction = CreateAcceptedOffer();

        Assert.Equal(
            transaction.SellerAcceptedAt!.Value.AddHours(
                SaleTransaction.BuyerPaymentWindowHours),
            transaction.BuyerPaymentDeadlineAt);
        Assert.Throws<DomainException>(() =>
            transaction.BeginCheckout(
                "ผู้ซื้อ",
                "+66811111111",
                "กรุงเทพฯ ประเทศไทย",
                transaction.BuyerPaymentDeadlineAt!.Value,
                _transitions));
        Assert.Equal(
            TransactionState.SellerAcceptedAwaitingPayment,
            transaction.State);
    }

    [Fact]
    public void Unpaid_buyer_window_expires_without_marking_payment()
    {
        var transaction = CreateAcceptedOffer();

        Assert.True(transaction.ExpireIfDue(
            transaction.BuyerPaymentDeadlineAt!.Value,
            _transitions));

        Assert.Equal(TransactionState.Expired, transaction.State);
        Assert.Equal(
            TransactionExpirationReason.BuyerDidNotPay,
            transaction.ExpirationReason);
        Assert.Null(transaction.PaymentConfirmedAt);
        Assert.Null(transaction.ShipByAt);
    }

    [Fact]
    public void Provider_payment_confirmed_before_deadline_survives_delayed_webhook()
    {
        var transaction = Checkout();
        var deadline = transaction.BuyerPaymentDeadlineAt!.Value;
        transaction.ExpireIfDue(deadline.AddMinutes(1), _transitions);

        transaction.ConfirmPayment(
            "bank-before-deadline",
            deadline.AddSeconds(-1),
            _transitions);

        Assert.Equal(
            TransactionState.PaidAwaitingShipment,
            transaction.State);
        Assert.Equal(
            deadline.AddSeconds(-1),
            transaction.PaymentConfirmedAt);
        Assert.Null(transaction.ExpirationReason);
    }

    [Fact]
    public void Provider_payment_confirmed_after_deadline_requires_refund()
    {
        var transaction = Checkout();
        var deadline = transaction.BuyerPaymentDeadlineAt!.Value;

        transaction.ConfirmPayment(
            "bank-after-deadline",
            deadline.AddSeconds(1),
            _transitions);

        Assert.Equal(TransactionState.RefundPending, transaction.State);
        Assert.Null(transaction.ShipByAt);
        Assert.Contains(
            transaction.AuditEvents,
            audit =>
                audit.Name ==
                "payment.confirmed_after_deadline_refund_required");
    }

    [Fact]
    public void Refund_is_complete_only_after_matching_provider_confirmation()
    {
        var transaction = CreateAcceptedOffer();
        transaction.BeginCheckout(
            "ผู้ซื้อ",
            "buyer@example.com",
            "กรุงเทพฯ ประเทศไทย",
            Start.AddMinutes(5),
            _transitions,
            "stripe",
            "pi_refund_001");
        transaction.ConfirmStripePayment(
            "evt_late_payment",
            "pi_refund_001",
            455_000,
            "THB",
            transaction.BuyerPaymentDeadlineAt!.Value.AddSeconds(1),
            transaction.BuyerPaymentDeadlineAt.Value.AddSeconds(2),
            _transitions);
        transaction.RecordRefundInstruction(
            "stripe",
            "re_001",
            Start.AddHours(2));

        Assert.Equal(
            TransactionState.RefundPending,
            transaction.State);
        Assert.Null(transaction.RefundConfirmedAt);
        Assert.Throws<DomainException>(() =>
            transaction.ConfirmRefund(
                "stripe",
                "evt_refund_wrong",
                "re_001",
                "pi_refund_001",
                454_999,
                "THB",
                Start.AddHours(3),
                Start.AddHours(3),
                _transitions));

        transaction.ConfirmRefund(
            "stripe",
            "evt_refund_succeeded",
            "re_001",
            "pi_refund_001",
            455_000,
            "THB",
            Start.AddHours(3),
            Start.AddHours(3),
            _transitions);

        Assert.Equal(TransactionState.Refunded, transaction.State);
        Assert.Equal(Start.AddHours(3), transaction.RefundConfirmedAt);
    }

    [Fact]
    public void Full_refund_includes_buyer_protection_fee()
    {
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ",
            "buyer@example.com",
            FulfillmentType.DigitalHandoff,
            "สิทธิ์ดิจิทัล",
            "สิทธิ์ที่ผู้ขายมีสิทธิ์โอน",
            ConditionCode.UsedGood,
            "ไม่มี",
            null,
            450_000,
            "mvp-th-2026-07",
            Start,
            _transitions);
        transaction.AcceptBuyerOffer(
            Guid.NewGuid(),
            "ผู้ขาย",
            "+66822222222",
            "KBANK",
            "ผู้ขาย ทดสอบ",
            "1234567890",
            true,
            Start.AddMinutes(1),
            _transitions,
            buyerProtectionFeeSatang: 20_650,
            platformFeeSatang: 0,
            sellerExpectedNetSatang: 450_000,
            feePolicyVersion:
                "buyer-protection-test-v1");
        transaction.BeginCheckout(
            "ผู้ซื้อ",
            "buyer@example.com",
            "ไม่ใช้ที่อยู่",
            Start.AddMinutes(5),
            _transitions,
            "stripe",
            "pi_refund_with_protection",
            0,
            450_000,
            "buyer-protection-test-v1",
            buyerProtectionFeeSatang: 20_650);
        var confirmedAt =
            transaction.BuyerPaymentDeadlineAt!.Value
                .AddSeconds(1);
        transaction.ConfirmStripePayment(
            "evt_late_with_protection",
            "pi_refund_with_protection",
            470_650,
            "THB",
            confirmedAt,
            confirmedAt,
            _transitions);
        transaction.RecordRefundInstruction(
            "stripe",
            "re_with_protection",
            confirmedAt.AddMinutes(1));
        transaction.ConfirmRefund(
            "stripe",
            "evt_refund_with_protection",
            "re_with_protection",
            "pi_refund_with_protection",
            470_650,
            "THB",
            confirmedAt.AddMinutes(2),
            confirmedAt.AddMinutes(2),
            _transitions);

        Assert.Equal(470_650, transaction.BuyerTotalSatang);
        Assert.Equal(
            20_650,
            transaction.BuyerProtectionFeeSatang);
        Assert.Equal(TransactionState.Refunded, transaction.State);
    }

    [Fact]
    public void Stripe_webhook_after_authorized_reconciliation_is_safe()
    {
        var transaction = CreateAcceptedOffer();
        transaction.BeginCheckout(
            "ผู้ซื้อ",
            "buyer@example.com",
            "กรุงเทพฯ ประเทศไทย",
            Start.AddMinutes(5),
            _transitions,
            "stripe",
            "pi_reconciled_001");
        transaction.ConfirmStripePayment(
            "stripe-reconcile:pi_reconciled_001:ch_001",
            "pi_reconciled_001",
            455_000,
            "THB",
            Start.AddMinutes(10),
            Start.AddMinutes(11),
            _transitions);
        transaction.ConfirmStripePayment(
            "evt_webhook_after_reconcile",
            "pi_reconciled_001",
            455_000,
            "THB",
            Start.AddMinutes(10),
            Start.AddMinutes(12),
            _transitions);

        Assert.Equal(
            TransactionState.PaidAwaitingShipment,
            transaction.State);
        Assert.Equal(
            2,
            transaction.ExternalEvents.Count(
                item => item.Provider == "stripe"));
        Assert.Single(
            transaction.AuditEvents,
            audit => audit.Name == "payment.confirmed");
    }

    [Fact]
    public void Missed_fulfillment_deadline_blocks_fulfillment_and_requires_refund()
    {
        var transaction = Paid();

        Assert.True(transaction.MarkShipmentOverdue(
            transaction.ShipByAt!.Value,
            _transitions));

        Assert.Equal(TransactionState.RefundPending, transaction.State);
        Assert.Contains(
            transaction.AuditEvents,
            audit => audit.Name == "fulfillment.deadline_missed");
        Assert.Contains(
            transaction.AuditEvents,
            audit =>
                audit.Name ==
                "refund.required_fulfillment_overdue");
    }

    [Fact]
    public void Delivery_queues_immediate_notice_and_exact_24_hour_reminder()
    {
        var transaction = Delivered();
        var deliveryNotice = Assert.Single(
            transaction.Notifications,
            message => message.Template == "delivered");
        var reminder = Assert.Single(
            transaction.Notifications,
            message =>
                message.Template == "payout_reminder_24h");

        Assert.Equal("buyer", deliveryNotice.Audience);
        Assert.Equal(
            transaction.DisputeWindowEndsAt!.Value.AddHours(-24),
            reminder.AvailableAt);
        Assert.Null(reminder.SentAt);
    }

    [Fact]
    public void Carrier_event_must_match_submitted_carrier_and_tracking()
    {
        var transaction = Shipped();

        Assert.Throws<DomainException>(() =>
            transaction.RecordCarrierEvent(
                "carrier-mismatch-001",
                "delivered",
                Start.AddHours(3),
                Start.AddHours(3),
                _transitions,
                "KERRY",
                "OTHER12345"));
        Assert.Equal(
            TransactionState.TrackingSubmitted,
            transaction.State);
        Assert.DoesNotContain(
            transaction.ExternalEvents,
            item => item.EventId == "carrier-mismatch-001");
    }

    [Fact]
    public void Authorized_carrier_resolution_closes_managed_shipment_block()
    {
        var transaction = Shipped();
        var shipment = ManagedShipment.CreateOutbound(
            transaction.Id,
            ManagedShipmentDraft(),
            Start.AddHours(1));
        transaction.QueueManagedShipment(
            shipment,
            ShippingOperation.Queue(
                transaction.Id,
                shipment.Id,
                ShippingOperationType.BookOutbound,
                $"book-outbound:{transaction.Id:N}:test",
                new string('a', 64),
                Start.AddHours(1)),
            ActorRole.System,
            "shipping-orchestrator",
            Start.AddHours(1));
        shipment.RecordReservation(
            "purchase-001",
            "provider-track-001",
            "TH12345678",
            Start.AddHours(1));
        shipment.RecordConfirmation(
            "TH12345678",
            "booking",
            Start.AddHours(1));
        transaction.RecordManagedOutboundCarrierException(
            shipment.Id,
            "problem-001",
            "problem",
            "shippop",
            Start.AddHours(2),
            _transitions);

        transaction.ResolveCarrierException(
            TransactionState.RefundPending,
            "crm-user",
            "ตรวจหลักฐานแล้วให้คืนเงิน",
            "CASE-SHIP-001",
            "crm:shipping:resolve:001",
            Start.AddHours(3),
            _transitions);

        Assert.Equal(
            TransactionState.RefundPending,
            transaction.State);
        Assert.False(shipment.HasOpenException);
        Assert.False(transaction.HasOpenShippingException);
    }

    [Fact]
    public void Manual_return_resolution_closes_return_tracking_block()
    {
        var transaction = Delivered();
        transaction.OpenDispute(
            transaction.BuyerAccessToken!,
            DisputeReason.NotAsDescribed,
            "สินค้ามีความเสียหาย",
            Start.AddHours(4),
            _transitions);
        transaction.BeginDisputeResolution(
            "CASE-RETURN-001",
            "crm-user",
            "{}",
            "crm:return:review:001",
            Start.AddHours(5),
            _transitions);
        var shipment = ManagedShipment.CreateReturn(
            transaction.Id,
            ManagedShipmentDraft() with
            {
                OriginPrivateSnapshotReference =
                    "buyer-return-origin",
                DestinationPrivateSnapshotReference =
                    "seller-return-destination"
            },
            Start.AddHours(5));
        transaction.AuthorizeManagedReturn(
            shipment,
            ShippingOperation.Queue(
                transaction.Id,
                shipment.Id,
                ShippingOperationType.BookReturn,
                $"book-return:{transaction.Id:N}:test",
                new string('b', 64),
                Start.AddHours(5)),
            "crm-user",
            "CASE-RETURN-001",
            "อนุมัติให้ส่งคืน",
            "crm:return:authorize:001",
            Start.AddHours(5));
        shipment.RecordReservation(
            "return-purchase-001",
            "return-provider-track-001",
            null,
            Start.AddHours(5));
        shipment.RecordConfirmation(
            "RETURN123456TH",
            "booking",
            Start.AddHours(5));
        var buyerTotalBeforeReturn =
            transaction.BuyerTotalSatang;
        transaction.RecordManagedReturnCost(
            shipment.Id,
            "shippop",
            "return-purchase-001",
            6_100,
            Start.AddHours(5),
            Start.AddHours(5));
        transaction.RecordManagedReturnTrackingEvent(
            shipment.Id,
            "return-problem-001",
            "carrier_exception",
            "return_problem",
            null,
            "shippop",
            Start.AddHours(6));

        transaction.AuthorizeManualReturnResolution(
            "RETURN-REVIEW-001",
            "crm-user",
            "ตรวจหลักฐานการส่งคืนจากผู้ให้บริการแล้ว",
            "crm:return:manual:001",
            Start.AddHours(7));

        Assert.False(shipment.HasOpenException);
        Assert.False(transaction.HasOpenShippingException);
        Assert.Equal(
            "RETURN-REVIEW-001",
            transaction.ManualReturnResolutionReference);
        Assert.Equal(
            buyerTotalBeforeReturn,
            transaction.BuyerTotalSatang);
        var returnCost = Assert.Single(
            transaction.ProviderShippingAdjustments);
        Assert.Equal(
            "authorized-return-cost",
            returnCost.ReasonCode);
        Assert.False(returnCost.IsOpen);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Authorized_dispute_resolution_has_audited_full_outcomes(
        bool sellerWins)
    {
        var transaction = Delivered();
        transaction.OpenDispute(
            transaction.BuyerAccessToken!,
            DisputeReason.NotAsDescribed,
            "สินค้าไม่ตรงตามรายละเอียด",
            Start.AddHours(4),
            _transitions);
        transaction.BeginDisputeResolution(
            "CASE-001",
            Guid.NewGuid().ToString("N"),
            "{}",
            "crm:test:review",
            Start.AddHours(5),
            _transitions);

        if (sellerWins)
        {
            transaction.ResolveDisputeForPayout(
                "CASE-001",
                Guid.NewGuid().ToString("N"),
                "{}",
                "crm:test:payout",
                Start.AddHours(6),
                _transitions);
            transaction.StartPayout(
                "PAYOUT-001",
                Start.AddHours(7),
                _transitions);
            Assert.Equal(TransactionState.PayoutPending, transaction.State);
        }
        else
        {
            transaction.ResolveDisputeForRefund(
                "CASE-001",
                Guid.NewGuid().ToString("N"),
                "{}",
                "crm:test:refund",
                Start.AddHours(6),
                _transitions);
            Assert.Equal(TransactionState.RefundPending, transaction.State);
        }

        Assert.Equal(Start.AddHours(6), transaction.DisputeResolvedAt);
        Assert.Equal("CASE-001", transaction.DisputeResolutionReference);
    }

    [Fact]
    public void Buyer_offer_without_a_product_photo_can_be_accepted_and_snapshotted()
    {
        var buyerId = Guid.NewGuid();
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            buyerId,
            "ผู้ซื้อ",
            "+66811111111",
            "+66822222222",
            FulfillmentType.PhysicalShipment,
            "กล้องฟิล์ม",
            "กล้องพร้อมเลนส์ตามที่คุยกัน",
            ConditionCode.UsedGood,
            "",
            "",
            450_000,
            "mvp-th-2026-07",
            Start,
            _transitions);

        Assert.Null(transaction.PhotoUrl);

        transaction.AcceptBuyerOffer(
            Guid.NewGuid(),
            "ผู้ขาย",
            "+66822222222",
            "KBANK",
            "ผู้ขาย ทดสอบ",
            "1234567890",
            true,
            Start.AddMinutes(1),
            _transitions,
            shipping: TestTransactionFactory.ShippingQuote(
                Start.AddMinutes(1)));
        TestTransactionFactory.PreparePhysicalCheckoutBooking(
            transaction,
            Start.AddMinutes(5));
        transaction.BeginCheckout(
            "ผู้ซื้อ",
            "+66811111111",
            Start.AddMinutes(5),
            _transitions);

        using var snapshot =
            System.Text.Json.JsonDocument.Parse(
                transaction.ProductSnapshotJson!);
        Assert.Equal(
            System.Text.Json.JsonValueKind.Null,
            snapshot.RootElement
                .GetProperty("PhotoUrl")
                .ValueKind);
        Assert.True(transaction.HasValidAgreementSnapshot());
    }

    [Fact]
    public void Buyer_offer_requires_an_explicit_condition()
    {
        Assert.Throws<DomainException>(() =>
            TestTransactionFactory.CreateBuyerOffer(
                Guid.NewGuid(),
                "ผู้ซื้อ", "+66811111111",
                FulfillmentType.PhysicalShipment,
                "รายละเอียดสินค้า", "กล้องพร้อมเลนส์ตามที่คุยกัน",
                ConditionCode.AsDescribed, "",
                "https://example.com/photo.jpg",
                450_000, "mvp-th-2026-07", Start, _transitions));
    }

    [Fact]
    public void Buyer_cannot_checkout_before_seller_acceptance()
    {
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ", "buyer@example.com",
            FulfillmentType.PhysicalShipment,
            "กล้องฟิล์ม", "กล้องพร้อมเลนส์ตามที่คุยกัน",
            ConditionCode.UsedGood, "", "https://example.com/photo.jpg",
            450_000, "mvp-th-2026-07", Start, _transitions);

        Assert.Throws<DomainException>(() =>
            transaction.BeginCheckout(
                "ผู้ซื้อ", "buyer@example.com", "กรุงเทพฯ ประเทศไทย",
                Start.AddMinutes(1), _transitions));
        Assert.Equal(TransactionState.AwaitingSellerAcceptance, transaction.State);
        Assert.Null(transaction.PaymentReference);
    }

    [Fact]
    public void Buyer_role_cannot_force_seller_acceptance_transition()
    {
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ", "buyer@example.com",
            FulfillmentType.PhysicalShipment,
            "กล้องฟิล์ม", "กล้องพร้อมเลนส์ตามที่คุยกัน",
            ConditionCode.UsedGood, "", "https://example.com/photo.jpg",
            450_000, "mvp-th-2026-07", Start, _transitions);

        Assert.Throws<DomainException>(() =>
            _transitions.Transition(
                transaction,
                TransactionState.SellerAcceptedAwaitingPayment,
                ActorRole.Buyer,
                transaction.BuyerAccessToken!,
                "buyer_offer.acceptance_forced",
                Start.AddMinutes(1),
                transaction.Id.ToString("N"),
                $"forced-accept:{transaction.Id:N}"));

        Assert.Equal(
            TransactionState.AwaitingSellerAcceptance,
            transaction.State);
        Assert.DoesNotContain(
            transaction.AuditEvents,
            x => x.Name == "buyer_offer.acceptance_forced");
    }

    [Fact]
    public void Seller_acceptance_is_audited_and_enables_checkout()
    {
        var transaction = CreateAcceptedOffer();

        Assert.Equal(
            TransactionState.SellerAcceptedAwaitingPayment,
            transaction.State);
        Assert.NotNull(transaction.SellerAcceptedAt);
        Assert.NotNull(transaction.AgreementCoreSnapshotJson);
        Assert.NotNull(transaction.AgreementCoreSnapshotHash);
        Assert.NotNull(transaction.TermsSnapshotJson);
        Assert.True(transaction.HasValidAgreementCoreSnapshot());
        var sellerAcceptance = Assert.Single(
            transaction.AgreementAcceptances);
        Assert.Equal(
            AgreementAcceptanceRole.Seller,
            sellerAcceptance.Role);
        Assert.Equal(
            transaction.SellerId,
            sellerAcceptance.ActorUserId);
        Assert.Equal(
            transaction.AgreementCoreSnapshotHash,
            sellerAcceptance.AgreementCoreSnapshotHash);
        Assert.Contains(
            transaction.AuditEvents,
            x => x.Name == "buyer_offer.seller_accepted" &&
                 x.ActorRole == ActorRole.Seller &&
                 x.MetadataJson.Contains(
                     transaction.AgreementCoreSnapshotHash!,
                     StringComparison.Ordinal));

        transaction.BeginCheckout(
            "ผู้ซื้อ", "buyer@example.com", "กรุงเทพฯ ประเทศไทย",
            Start.AddMinutes(5), _transitions);

        Assert.Equal(TransactionState.PaymentPending, transaction.State);
    }

    [Fact]
    public void Buyer_and_seller_accept_the_same_immutable_core_hash()
    {
        var transaction = CreateAcceptedOffer();
        var coreJson = transaction.AgreementCoreSnapshotJson;
        var coreHash = transaction.AgreementCoreSnapshotHash;

        transaction.BeginCheckout(
            "ผู้ซื้อ",
            "buyer@example.com",
            "กรุงเทพฯ ประเทศไทย",
            Start.AddMinutes(5),
            _transitions);

        Assert.Equal(coreJson, transaction.AgreementCoreSnapshotJson);
        Assert.Equal(coreHash, transaction.AgreementCoreSnapshotHash);
        Assert.True(transaction.HasMatchingPartyAcceptances());
        var acceptances =
            transaction.AgreementAcceptances
                .OrderBy(x => x.Role)
                .ToArray();
        Assert.Equal(2, acceptances.Length);
        Assert.All(
            acceptances,
            acceptance =>
                Assert.Equal(
                    coreHash,
                    acceptance.AgreementCoreSnapshotHash));
        Assert.Equal(
            transaction.BuyerId,
            acceptances.Single(
                item =>
                    item.Role ==
                    AgreementAcceptanceRole.Buyer)
                .ActorUserId);
        Assert.Contains(
            coreHash!,
            transaction.AuditEvents.Single(
                    audit => audit.Name == "checkout.started")
                .MetadataJson,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Material_change_after_seller_acceptance_blocks_buyer_acceptance()
    {
        var transaction = CreateAcceptedOffer();
        typeof(SaleTransaction)
            .GetProperty(nameof(SaleTransaction.Description))!
            .SetValue(
                transaction,
                "รายละเอียดถูกแก้หลังผู้ขายยอมรับ");

        Assert.Throws<DomainException>(() =>
            transaction.BeginCheckout(
                "ผู้ซื้อ",
                "buyer@example.com",
                "กรุงเทพฯ ประเทศไทย",
                Start.AddMinutes(5),
                _transitions));

        Assert.Equal(
            TransactionState.SellerAcceptedAwaitingPayment,
            transaction.State);
        Assert.Single(transaction.AgreementAcceptances);
        Assert.Null(transaction.BuyerAcceptedAt);
        Assert.Null(transaction.ProductSnapshotHash);
    }

    [Fact]
    public void Buyer_identity_change_after_seller_acceptance_requires_new_offer()
    {
        var transaction = CreateAcceptedOffer();

        Assert.Throws<DomainException>(() =>
            transaction.BeginCheckout(
                "ผู้ซื้อคนอื่น",
                "other@example.com",
                "กรุงเทพฯ ประเทศไทย",
                Start.AddMinutes(5),
                _transitions));

        Assert.Single(transaction.AgreementAcceptances);
        Assert.Null(transaction.BuyerAcceptedAt);
        Assert.Null(transaction.ProductSnapshotHash);
    }

    [Fact]
    public void Seller_acceptance_preserves_buyer_specified_details_in_snapshot()
    {
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ", "buyer@example.com",
            FulfillmentType.PhysicalShipment,
            "กล้องฟิล์ม", "ผู้ซื้อระบุว่ามีรอยด้านข้าง ใช้งานปกติ",
            ConditionCode.UsedDefects, "มีรอยด้านข้าง",
            "https://example.com/buyer-photo.jpg",
            450_000, "mvp-th-2026-07", Start, _transitions);
        transaction.AcceptBuyerOffer(
            Guid.NewGuid(),
            "ผู้ขาย", "line:seller", "KBANK", "ผู้ขาย ทดสอบ", "1234567890",
            true, Start.AddMinutes(1), _transitions,
            shipping: TestTransactionFactory.ShippingQuote(
                Start.AddMinutes(1)));

        transaction.BeginCheckout(
            "ผู้ซื้อ", "buyer@example.com", "กรุงเทพฯ ประเทศไทย",
            Start.AddMinutes(5), _transitions);

        using var snapshot = System.Text.Json.JsonDocument.Parse(
            transaction.ProductSnapshotJson!);
        Assert.Contains(
            "ผู้ซื้อระบุว่ามีรอยด้านข้าง",
            snapshot.RootElement.GetProperty("Description").GetString());
        Assert.Equal(
            "https://example.com/buyer-photo.jpg",
            snapshot.RootElement.GetProperty("PhotoUrl").GetString());
        Assert.Equal(
            450_000,
            snapshot.RootElement.GetProperty("PriceSatang").GetInt64());
        Assert.Equal(
            5_000,
            snapshot.RootElement
                .GetProperty("ShippingFeeSatang")
                .GetInt64());
        Assert.Equal(
            455_000,
            snapshot.RootElement
                .GetProperty("BuyerTotalSatang")
                .GetInt64());
        var shipping = snapshot.RootElement.GetProperty("Shipping");
        Assert.Equal(
            TestTransactionFactory.ShippingOriginAddress,
            shipping.GetProperty("ShippingOriginAddress").GetString());
        Assert.Equal(
            1_200,
            shipping.GetProperty("PackageWeightGrams").GetInt32());
        Assert.Equal(
            "กรุงเทพฯ ประเทศไทย",
            snapshot.RootElement.GetProperty("DeliveryAddress").GetString());
        Assert.Equal(
            SaleTransaction.AgreementSnapshotSchemaVersion,
            transaction.SnapshotSchemaVersion);
        Assert.Equal(
            Start.AddMinutes(5),
            transaction.AgreementSnapshotCreatedAt);
        Assert.Null(transaction.AgreementSnapshotSealedAt);
        Assert.NotNull(transaction.TermsSnapshotJson);
        Assert.NotNull(transaction.TermsSnapshotHash);
        Assert.True(transaction.HasValidAgreementSnapshot());
    }

    [Fact]
    public void Physical_offer_locks_delivery_region_into_shared_snapshot()
    {
        var transaction = CreateAcceptedOffer();

        Assert.Equal(
            TestTransactionFactory.DeliveryProvinceName,
            transaction.DeliveryProvinceName);
        Assert.Equal(
            TestTransactionFactory.DeliveryPostalCode,
            transaction.DeliveryPostalCode);
        Assert.Contains(
            "\"DeliveryRegion\"",
            transaction.AgreementCoreSnapshotJson);
        Assert.Contains(
            TestTransactionFactory.DeliveryPostalCode,
            transaction.AgreementCoreSnapshotJson);
        Assert.DoesNotContain(
            TestTransactionFactory.DeliveryAddressLine,
            transaction.AgreementCoreSnapshotJson);
        Assert.DoesNotContain(
            TestTransactionFactory.ShippingOriginAddress,
            transaction.AgreementCoreSnapshotJson);
        Assert.False(
            string.IsNullOrWhiteSpace(
                transaction.AgreementCoreSnapshotHash));
    }

    [Fact]
    public void Checkout_uses_address_locked_when_offer_was_created()
    {
        var transaction = CreateAcceptedOffer();

        transaction.BeginCheckout(
            "ผู้ซื้อ",
            "buyer@example.com",
            Start.AddMinutes(5),
            _transitions);

        Assert.Equal(
            TestTransactionFactory.DeliveryAddress,
            transaction.DeliveryAddress);
        using var snapshot =
            System.Text.Json.JsonDocument.Parse(
                transaction.ProductSnapshotJson!);
        Assert.Equal(
            TestTransactionFactory.DeliveryAddress,
            snapshot.RootElement
                .GetProperty("DeliveryAddress")
                .GetString());
    }

    [Fact]
    public void Seller_can_decline_without_creating_payment_or_refund()
    {
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ", "buyer@example.com",
            FulfillmentType.PhysicalShipment,
            "กล้องฟิล์ม", "กล้องพร้อมเลนส์ตามที่คุยกัน",
            ConditionCode.UsedGood, "", "https://example.com/photo.jpg",
            450_000, "mvp-th-2026-07", Start, _transitions);

        transaction.DeclineBuyerOffer(
            Guid.NewGuid(), Start.AddMinutes(1), _transitions);

        Assert.Equal(TransactionState.Cancelled, transaction.State);
        Assert.Null(transaction.PaymentReference);
        Assert.Null(transaction.PaymentConfirmedAt);
        Assert.Contains(
            transaction.AuditEvents,
            x => x.Name == "buyer_offer.seller_declined");
    }

    [Fact]
    public void Checkout_does_not_mark_transaction_paid()
    {
        var transaction = CreateAcceptedOffer();

        transaction.BeginCheckout("ผู้ซื้อ", "buyer@example.com", "กรุงเทพฯ ประเทศไทย", Start.AddMinutes(5), _transitions);

        Assert.Equal(TransactionState.PaymentPending, transaction.State);
        Assert.Null(transaction.PaymentConfirmedAt);
        Assert.Null(transaction.ShipByAt);
        Assert.NotNull(transaction.ProductSnapshotHash);
        Assert.True(transaction.HasValidAgreementSnapshot());
        Assert.Null(transaction.AgreementSnapshotSealedAt);
        var checkoutAudit = Assert.Single(
            transaction.AuditEvents,
            audit => audit.Name == "checkout.started");
        Assert.Contains(
            transaction.ProductSnapshotHash!,
            checkoutAudit.MetadataJson,
            StringComparison.Ordinal);
        Assert.Contains(
            transaction.TermsSnapshotHash!,
            checkoutAudit.MetadataJson,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Tampered_agreement_snapshot_blocks_payment_confirmation()
    {
        var transaction = Checkout();
        var snapshotProperty = typeof(SaleTransaction)
            .GetProperty(nameof(SaleTransaction.ProductSnapshotJson))!;
        snapshotProperty.SetValue(transaction, "{\"tampered\":true}");

        Assert.Throws<DomainException>(() =>
            transaction.ConfirmPayment(
                "bank-event-tampered",
                Start.AddMinutes(10),
                _transitions));
        Assert.Equal(TransactionState.PaymentPending, transaction.State);
        Assert.Null(transaction.PaymentConfirmedAt);
    }

    [Fact]
    public void Agreement_snapshot_remains_unchanged_through_fulfillment_and_release()
    {
        var transaction = Paid();
        var productJson = transaction.ProductSnapshotJson;
        var productHash = transaction.ProductSnapshotHash;
        var termsJson = transaction.TermsSnapshotJson;
        var termsHash = transaction.TermsSnapshotHash;
        var coreJson = transaction.AgreementCoreSnapshotJson;
        var coreHash = transaction.AgreementCoreSnapshotHash;
        var acceptanceEvidence =
            transaction.AgreementAcceptances
                .Select(x => new
                {
                    x.Role,
                    x.ActorUserId,
                    x.AcceptedAt,
                    x.AgreementCoreSnapshotHash
                })
                .OrderBy(x => x.Role)
                .ToArray();

        transaction.SubmitTracking(
            transaction.SellerAccessToken,
            "FLASH",
            "TH12345678",
            Start.AddHours(1),
            _transitions);
        transaction.RecordCarrierEvent(
            "carrier-delivered-snapshot",
            "delivered",
            Start.AddHours(3),
            Start.AddHours(3).AddMinutes(1),
            _transitions);
        transaction.ConfirmReceipt(
            transaction.BuyerAccessToken!,
            Start.AddHours(4),
            _transitions);

        Assert.Equal(productJson, transaction.ProductSnapshotJson);
        Assert.Equal(productHash, transaction.ProductSnapshotHash);
        Assert.Equal(termsJson, transaction.TermsSnapshotJson);
        Assert.Equal(termsHash, transaction.TermsSnapshotHash);
        Assert.Equal(coreJson, transaction.AgreementCoreSnapshotJson);
        Assert.Equal(coreHash, transaction.AgreementCoreSnapshotHash);
        Assert.Equal(
            acceptanceEvidence,
            transaction.AgreementAcceptances
                .Select(x => new
                {
                    x.Role,
                    x.ActorUserId,
                    x.AcceptedAt,
                    x.AgreementCoreSnapshotHash
                })
                .OrderBy(x => x.Role)
                .ToArray());
        Assert.True(transaction.HasValidAgreementSnapshot());
    }

    [Fact]
    public void Unsupported_or_prohibited_item_is_blocked_before_activation()
    {
        Assert.Throws<DomainException>(() =>
            TestTransactionFactory.CreateBuyerOffer(
                Guid.NewGuid(),
                "ผู้ซื้อ", "buyer@example.com",
                FulfillmentType.PhysicalShipment,
                "บริการรับถ่ายภาพ", "รับถ่ายภาพนอกสถานที่",
                ConditionCode.New, "", "https://example.com/photo.jpg",
                100_000, "mvp-th-2026-07", Start, _transitions));
    }

    [Fact]
    public void Seller_payout_account_is_required_before_activation()
    {
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ", "buyer@example.com",
            FulfillmentType.PhysicalShipment,
            "กล้องฟิล์ม", "ใช้งานได้ปกติพร้อมสายคล้อง",
            ConditionCode.UsedGood, "ไม่มี", "https://example.com/photo.jpg",
            450_000, "mvp-th-2026-07", Start, _transitions);

        Assert.Throws<DomainException>(() =>
            transaction.AcceptBuyerOffer(
                Guid.NewGuid(),
                "ผู้ขาย", "line:seller", "KBANK", "ผู้ขาย ทดสอบ", "",
                true, Start, _transitions));
    }

    [Fact]
    public void Verified_payment_is_the_only_path_that_enables_shipping()
    {
        var transaction = Checkout();

        transaction.ConfirmPayment("bank-event-1", Start.AddMinutes(10), _transitions);

        Assert.Equal(TransactionState.PaidAwaitingShipment, transaction.State);
        Assert.Equal(Start.AddHours(72).AddMinutes(10), transaction.ShipByAt);
        Assert.Equal(
            Start.AddMinutes(10),
            transaction.AgreementSnapshotSealedAt);
        Assert.True(transaction.HasValidAgreementSnapshot());
        var paymentAudit = Assert.Single(
            transaction.AuditEvents,
            x => x.Name == "payment.confirmed");
        Assert.Contains(
            transaction.ProductSnapshotHash!,
            paymentAudit.MetadataJson,
            StringComparison.Ordinal);
        Assert.Contains(
            transaction.TermsSnapshotHash!,
            paymentAudit.MetadataJson,
            StringComparison.Ordinal);
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
    public void Tracking_carrier_must_match_the_paid_shipping_quote()
    {
        var transaction = Paid();

        Assert.Throws<DomainException>(() =>
            transaction.SubmitTracking(
                transaction.SellerAccessToken,
                "THAIPOST",
                "EF123456789TH",
                Start.AddHours(1),
                _transitions));

        Assert.Equal(
            TransactionState.PaidAwaitingShipment,
            transaction.State);
        Assert.Null(transaction.TrackingNumber);
    }

    [Fact]
    public void Provider_managed_shipment_blocks_manual_tracking_and_uses_reconciliation_transition()
    {
        var transaction = ManagedPaid();

        Assert.Throws<DomainException>(() =>
            transaction.SubmitTracking(
                transaction.SellerAccessToken,
                "FLASH",
                "TH123456789012",
                Start.AddHours(1),
                _transitions));

        transaction.ConfirmProviderManagedShipment(
            "development-shipping",
            "DEVSP12345678",
            "TH123456789012",
            "FLASH",
            "booking",
            Start.AddMinutes(11),
            _transitions);

        Assert.Equal(
            TransactionState.TrackingSubmitted,
            transaction.State);
        Assert.Equal(
            "TH123456789012",
            transaction.TrackingNumber);
        Assert.Equal(
            Start.AddMinutes(11),
            transaction.ShippingConfirmedAt);
        Assert.Contains(
            transaction.AuditEvents,
            item =>
                item.Name ==
                "shipment.provider_confirmed" &&
                item.ActorRole ==
                ActorRole.Reconciliation);
    }

    [Fact]
    public void Provider_tracking_allocation_does_not_satisfy_ship_by_without_carrier_scan()
    {
        var transaction = ManagedPaid();
        transaction.ConfirmProviderManagedShipment(
            "development-shipping",
            "DEVSP12345678",
            "TH123456789012",
            "FLASH",
            "booking",
            Start.AddMinutes(11),
            _transitions);

        Assert.True(
            transaction.MarkShipmentOverdue(
                transaction.ShipByAt!.Value.AddMinutes(1),
                _transitions));
        Assert.Equal(
            TransactionState.RefundPending,
            transaction.State);
        Assert.True(
            transaction.RequiresShippingCancellationBeforeRefund);
    }

    [Fact]
    public void Provider_carrier_scan_satisfies_ship_by_deadline()
    {
        var transaction = ManagedPaid();
        transaction.ConfirmProviderManagedShipment(
            "development-shipping",
            "DEVSP12345678",
            "TH123456789012",
            "FLASH",
            "booking",
            Start.AddMinutes(11),
            _transitions);
        transaction.RecordCarrierEvent(
            "provider-shipping-1",
            "in_transit",
            Start.AddHours(1),
            Start.AddHours(1),
            _transitions,
            "FLASH",
            "TH123456789012");

        Assert.False(
            transaction.MarkShipmentOverdue(
                transaction.ShipByAt!.Value.AddMinutes(1),
                _transitions));
        Assert.Equal(
            TransactionState.InTransit,
            transaction.State);
    }

    [Fact]
    public void Refund_waits_for_provider_shipping_cancellation_before_refund_creation()
    {
        var transaction = ManagedPaid();
        var afterDeadline =
            transaction.ShipByAt!.Value.AddMinutes(1);

        Assert.True(
            transaction.MarkShipmentOverdue(
                afterDeadline,
                _transitions));
        Assert.True(
            transaction.RequiresShippingCancellationBeforeRefund);

        transaction.RecordShippingCancellation(
            "development-shipping",
            afterDeadline.AddMinutes(1));

        Assert.False(
            transaction.RequiresShippingCancellationBeforeRefund);
        Assert.Equal(
            afterDeadline.AddMinutes(1),
            transaction.ShippingCancelledAt);
    }

    [Fact]
    public void Timely_carrier_scan_found_during_cancellation_stops_automatic_refund()
    {
        var transaction = ManagedPaid();
        var afterDeadline =
            transaction.ShipByAt!.Value.AddMinutes(1);
        var timelyScan =
            transaction.ShipByAt.Value.AddMinutes(-1);

        Assert.True(
            transaction.MarkShipmentOverdue(
                afterDeadline,
                _transitions));

        transaction.RecordShipmentScanDuringRefund(
            "development-shipping",
            "shipping",
            timelyScan,
            afterDeadline.AddMinutes(1),
            _transitions);

        Assert.Equal(
            TransactionState.TrackingUnverified,
            transaction.State);
        Assert.True(
            transaction.HasTimelyTrustedCarrierAcceptance);
        Assert.False(
            transaction.RequiresShippingCancellationBeforeRefund);
        Assert.Contains(
            transaction.AuditEvents,
            item =>
                item.Name ==
                "shipment.timely_acceptance_recovered" &&
                item.FromState ==
                TransactionState.RefundPending &&
                item.ToState ==
                TransactionState.TrackingUnverified);
    }

    [Fact]
    public void Late_carrier_scan_is_evidence_but_not_timely_seller_protection()
    {
        var transaction = ManagedPaid();
        var afterDeadline =
            transaction.ShipByAt!.Value.AddMinutes(1);

        Assert.True(
            transaction.MarkShipmentOverdue(
                afterDeadline,
                _transitions));

        transaction.RecordShipmentScanDuringRefund(
            "development-shipping",
            "shipping",
            afterDeadline,
            afterDeadline.AddMinutes(1),
            _transitions);

        Assert.Equal(
            TransactionState.RefundPending,
            transaction.State);
        Assert.False(
            transaction.HasTimelyTrustedCarrierAcceptance);
        Assert.Contains(
            transaction.AuditEvents,
            item =>
                item.Name ==
                "shipment.cancellation_skipped_after_carrier_scan");
    }

    [Fact]
    public void Unverified_tracking_never_starts_dispute_clock()
    {
        var transaction = Shipped();

        transaction.RecordCarrierEvent("carrier-1", "unverified", Start.AddHours(2), Start.AddHours(2), _transitions);

        Assert.Equal(TransactionState.TrackingUnverified, transaction.State);
        Assert.Equal(
            TrackingVerificationStatus.Unverified,
            transaction.TrackingVerificationStatus);
        Assert.Null(transaction.DeliveredAt);
        Assert.Null(transaction.DisputeWindowStartsAt);
        Assert.Null(transaction.DisputeWindowEndsAt);
    }

    [Fact]
    public void Trusted_delivery_starts_exact_72_hour_window()
    {
        var transaction = Shipped();
        transaction.RecordCarrierEvent("carrier-move", "in_transit", Start.AddHours(2), Start.AddHours(2), _transitions);
        var delivered = new DateTimeOffset(2026, 7, 20, 7, 18, 0, TimeSpan.Zero);

        transaction.RecordCarrierEvent("carrier-delivered", "delivered", delivered, delivered.AddMinutes(1), _transitions);

        Assert.Equal(TransactionState.DeliveredDisputeWindow, transaction.State);
        Assert.Equal(delivered, transaction.DeliveredAt);
        Assert.Equal(delivered, transaction.DisputeWindowStartsAt);
        Assert.Equal(
            delivered.AddHours(
                transaction.InspectionWindowDurationHours),
            transaction.DisputeWindowEndsAt);
        Assert.Equal(
            TrackingVerificationStatus.Delivered,
            transaction.TrackingVerificationStatus);
        Assert.Equal(
            delivered.AddMinutes(1),
            transaction.DeliveryEventReceivedAt);
    }

    [Fact]
    public void Legacy_transaction_keeps_its_stored_168_hour_window()
    {
        var transaction = Shipped();
        typeof(SaleTransaction)
            .GetProperty(
                nameof(
                    SaleTransaction
                        .InspectionWindowDurationHours))!
            .SetValue(transaction, 168);
        typeof(SaleTransaction)
            .GetProperty(
                nameof(
                    SaleTransaction
                        .SnapshotSchemaVersion))!
            .SetValue(transaction, null);
        var delivered =
            new DateTimeOffset(
                2026,
                7,
                20,
                7,
                18,
                0,
                TimeSpan.Zero);

        transaction.RecordCarrierEvent(
            "legacy-carrier-delivered",
            "delivered",
            delivered,
            delivered.AddMinutes(1),
            _transitions);

        Assert.Equal(
            delivered.AddHours(168),
            transaction.DisputeWindowEndsAt);
    }

    [Fact]
    public void Changed_window_value_cannot_override_a_versioned_snapshot()
    {
        var transaction = Shipped();
        typeof(SaleTransaction)
            .GetProperty(
                nameof(
                    SaleTransaction
                        .InspectionWindowDurationHours))!
            .SetValue(transaction, 24);

        Assert.Throws<DomainException>(() =>
            transaction.RecordCarrierEvent(
                "tampered-window-delivery",
                "delivered",
                Start.AddHours(3),
                Start.AddHours(3),
                _transitions));
        Assert.Equal(
            TransactionState.TrackingSubmitted,
            transaction.State);
        Assert.DoesNotContain(
            transaction.ExternalEvents,
            item =>
                item.EventId ==
                "tampered-window-delivery");
        Assert.Null(transaction.DisputeWindowStartsAt);
        Assert.Null(transaction.DisputeWindowEndsAt);
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
        Assert.Contains(
            transaction.Notifications,
            item => item.Template == "dispute_opened" &&
                    item.Audience == "buyer");
        Assert.Contains(
            transaction.Notifications,
            item => item.Template == "dispute_opened" &&
                    item.Audience == "seller");
    }

    [Fact]
    public void Buyer_confirmation_creates_eligibility_but_not_payout_success()
    {
        var transaction = Delivered();

        transaction.ConfirmReceipt(transaction.BuyerAccessToken!, Start.AddHours(4), _transitions);
        transaction.StartPayout("PAYOUT-1", Start.AddHours(4), _transitions);

        Assert.Equal(TransactionState.PayoutPending, transaction.State);
        Assert.Equal(
            PayoutReleaseReason.BuyerConfirmedAfterInspection,
            transaction.PayoutReleaseReason);
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
        Assert.Equal(
            PayoutReleaseReason.PhysicalInspectionWindowElapsed,
            transaction.PayoutReleaseReason);
        Assert.Null(transaction.PayoutConfirmedAt);
    }

    [Fact]
    public void Unknown_shipping_operation_blocks_an_otherwise_eligible_payout()
    {
        var transaction = Delivered();
        var deadline = transaction.DisputeWindowEndsAt!.Value;
        transaction.EvaluateDeadline(deadline, _transitions);
        var shipment = ManagedShipment.CreateOutbound(
            transaction.Id,
            new ManagedShipmentDraft(
                "shippop",
                "seller-origin-snapshot",
                "buyer-destination-snapshot",
                "กล้องพร้อมเลนส์",
                1_200,
                20,
                30,
                15,
                "THAIPOST",
                "EMST",
                "ไปรษณีย์ไทย EMS",
                5_200,
                1_100,
                450_000,
                "FULL_VALUE",
                "quote-reference",
                deadline.AddHours(2)),
            deadline);
        var operation = ShippingOperation.Queue(
            transaction.Id,
            shipment.Id,
            ShippingOperationType.BookOutbound,
            $"book-outbound:{transaction.Id:N}:payout-guard",
            new string('a', 64),
            deadline);
        transaction.QueueManagedShipment(
            shipment,
            operation,
            ActorRole.Reconciliation,
            "shipping-reconciliation",
            deadline);
        operation.Claim(
            "worker-a",
            deadline,
            TimeSpan.FromMinutes(5));
        operation.MarkOutcomeUnknown(
            "worker-a",
            "provider-timeout",
            deadline.AddSeconds(20));

        Assert.True(transaction.HasOpenShippingException);
        Assert.False(transaction.IsPayoutEligible);
        Assert.Throws<DomainException>(() =>
            transaction.StartPayout(
                "PAYOUT-BLOCKED",
                deadline.AddMinutes(1),
                _transitions));
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
            "buyer@example.com",
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

    private SaleTransaction CreateAcceptedOffer()
    {
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ", "buyer@example.com",
            FulfillmentType.PhysicalShipment,
            "กล้องฟิล์ม", "ใช้งานได้ปกติพร้อมสายคล้อง",
            ConditionCode.UsedGood, "มีรอยเล็กน้อย",
            "https://example.com/photo.jpg",
            450_000, "mvp-th-2026-07", Start, _transitions);
        transaction.AcceptBuyerOffer(
            Guid.NewGuid(),
            "ผู้ขาย", "line:seller", "KBANK", "ผู้ขาย ทดสอบ", "1234567890",
            true, Start.AddMinutes(1), _transitions,
            shipping: TestTransactionFactory.ShippingQuote(
                Start.AddMinutes(1)) with
            {
                ReservedAt = Start.AddMinutes(1)
            });
        transaction.RecordParcelProtectionElection(
            transaction.BuyerId!.Value,
            new ParcelProtectionSelection(
                ParcelProtectionElectionStatus.Declined,
                0,
                0,
                0,
                0,
                0,
                "parcel-protection-included-v1",
                null,
                Start.AddMinutes(1),
                Start.AddMinutes(30)),
            Start.AddMinutes(2));
        return transaction;
    }

    private static ManagedShipmentDraft ManagedShipmentDraft() =>
        new(
            "shippop",
            "seller-origin-snapshot",
            "buyer-destination-snapshot",
            "กล้องฟิล์ม",
            1_200,
            20,
            30,
            15,
            "FLASH",
            "FLE",
            "Flash Express",
            5_000,
            1_100,
            450_000,
            "FULL_VALUE",
            "quote-reference",
            Start.AddHours(8));

    private SaleTransaction Checkout()
    {
        var transaction = CreateAcceptedOffer();
        transaction.BeginCheckout("ผู้ซื้อ", "buyer@example.com", "กรุงเทพฯ ประเทศไทย", Start.AddMinutes(5), _transitions);
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

    private SaleTransaction ManagedPaid()
    {
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ",
            "buyer@example.com",
            FulfillmentType.PhysicalShipment,
            "กล้องฟิล์ม",
            "ใช้งานได้ปกติพร้อมสายคล้อง",
            ConditionCode.UsedGood,
            "มีรอยเล็กน้อย",
            "https://example.com/photo.jpg",
            450_000,
            "mvp-th-2026-07",
            Start,
            _transitions);
        transaction.AcceptBuyerOffer(
            Guid.NewGuid(),
            "ผู้ขาย",
            "line:seller",
            "KBANK",
            "ผู้ขาย ทดสอบ",
            "1234567890",
            true,
            Start.AddMinutes(1),
            _transitions,
            shipping:
                TestTransactionFactory.ShippingQuote(
                    Start.AddMinutes(1)) with
                {
                    Provider =
                        "development-shipping",
                    PurchaseReference =
                        "dev-purchase-123",
                    ProviderTrackingCode =
                        "DEVSP12345678",
                    CourierTrackingCode =
                        "TH123456789012",
                    ReservedAt =
                        Start.AddMinutes(1)
                });
        transaction.BeginCheckout(
            "ผู้ซื้อ",
            "buyer@example.com",
            "กรุงเทพฯ ประเทศไทย",
            Start.AddMinutes(5),
            _transitions);
        transaction.ConfirmPayment(
            "bank-event-managed",
            Start.AddMinutes(10),
            _transitions);
        return transaction;
    }

    private SaleTransaction Delivered()
    {
        var transaction = Shipped();
        transaction.RecordCarrierEvent("carrier-delivered", "delivered", Start.AddHours(3), Start.AddHours(3), _transitions);
        return transaction;
    }

    private SaleTransaction CreateDigitalSale()
    {
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ",
            "buyer@example.com",
            FulfillmentType.DigitalHandoff,
            "ไอดีเกมตัวอย่าง",
            "บัญชีเกมที่ผู้ขายยืนยันว่ามีสิทธิ์โอน ส่งมอบผ่าน LINE",
            ConditionCode.New,
            "",
            "https://example.com/digital.jpg",
            250_000,
            "mvp-th-2026-07",
            Start,
            _transitions);
        transaction.AcceptBuyerOffer(
            Guid.NewGuid(),
            "ผู้ขาย",
            "line:seller",
            "KBANK",
            "ผู้ขาย ทดสอบ",
            "1234567890",
            true,
            Start.AddMinutes(1),
            _transitions);
        return transaction;
    }

    private SaleTransaction PaidDigital()
    {
        var transaction = CreateDigitalSale();
        transaction.BeginCheckout(
            "ผู้ซื้อ",
            "buyer@example.com",
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
