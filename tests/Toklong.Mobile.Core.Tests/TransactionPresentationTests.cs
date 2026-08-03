using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class TransactionPresentationTests
{
    [Theory]
    [InlineData("PaymentPending", AppTransactionRole.Buyer)]
    [InlineData("Disputed", AppTransactionRole.Seller)]
    [InlineData("DigitalDeliverySubmitted", AppTransactionRole.Buyer)]
    public void Guidance_contains_no_internal_transaction_vocabulary(
        string state,
        AppTransactionRole role)
    {
        var guidance = (CreateItem(null) with
        {
            State = state,
            Role = role
        }).StatusGuidance;

        Assert.DoesNotContain(
            "webhook",
            guidance,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "state machine",
            guidance,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "hash",
            guidance,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "reconciliation",
            guidance,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SellerAcceptedOfferRequiresBuyerPaymentAction()
    {
        var presentation = TransactionStatePresenter.Present(
            "SellerAcceptedAwaitingPayment",
            AppTransactionRole.Buyer,
            AppFulfillmentType.Physical);

        Assert.Equal(TransactionBucket.ActionRequired, presentation.Bucket);
        Assert.Equal(TransactionAction.ReviewAndPay, presentation.PrimaryAction);
        Assert.Equal("ผู้ขายพร้อมขายแล้ว", presentation.StatusLabel);
        Assert.Equal("ตรวจยอดและชำระ", presentation.PrimaryActionLabel);
    }

    [Fact]
    public void WaitingBuyerDoesNotNeedToShareALink()
    {
        var presentation = TransactionStatePresenter.Present(
            "AwaitingSellerAcceptance",
            AppTransactionRole.Buyer,
            AppFulfillmentType.Physical);

        Assert.Equal(
            TransactionBucket.InProgress,
            presentation.Bucket);
        Assert.Equal(
            TransactionAction.ViewStatus,
            presentation.PrimaryAction);
        Assert.Equal("รอผู้ขายเตรียมขาย", presentation.StatusLabel);
    }

    [Fact]
    public void IntendedSellerSeesPendingOfferAsActionRequired()
    {
        var presentation = TransactionStatePresenter.Present(
            "AwaitingSellerAcceptance",
            AppTransactionRole.Seller,
            AppFulfillmentType.Physical);

        Assert.Equal(
            TransactionBucket.ActionRequired,
            presentation.Bucket);
        Assert.Equal(
            TransactionAction.ReviewSellerOffer,
            presentation.PrimaryAction);
        Assert.Equal(
            "เตรียมขาย",
            presentation.PrimaryActionLabel);
        Assert.Equal("มีรายการรอเตรียมขาย", presentation.StatusLabel);
    }

    [Theory]
    [InlineData(
        "SellerDidNotRespond",
        AppTransactionRole.Buyer,
        "ผู้ขายไม่ได้ตอบ")]
    [InlineData(
        "BuyerDidNotPay",
        AppTransactionRole.Buyer,
        "หมดเวลาชำระ")]
    [InlineData(
        "BuyerDidNotPay",
        AppTransactionRole.Seller,
        "ผู้ซื้อไม่ได้จ่าย")]
    public void ExpiredOfferExplainsWhoMissedTheDeadline(
        string reason,
        AppTransactionRole role,
        string expectedLabel)
    {
        var presentation = TransactionStatePresenter.Present(
            "Expired",
            role,
            AppFulfillmentType.Physical,
            reason);

        Assert.Equal(expectedLabel, presentation.StatusLabel);
        Assert.Equal(TransactionBucket.Completed, presentation.Bucket);
    }

    [Fact]
    public void PaymentPendingUsesPlainThaiCopy()
    {
        var presentation = TransactionStatePresenter.Present(
            "PaymentPending",
            AppTransactionRole.Buyer,
            AppFulfillmentType.Physical);

        Assert.Equal("กำลังเช็กการจ่ายเงิน", presentation.StatusLabel);
        Assert.DoesNotContain("ชำระ", presentation.StatusLabel);
    }

    [Fact]
    public void BuyerCanRetryPaymentAfterClosingPaymentSheet()
    {
        var buyerPresentation = TransactionStatePresenter.Present(
            "PaymentPending",
            AppTransactionRole.Buyer,
            AppFulfillmentType.Physical);
        var sellerPresentation = TransactionStatePresenter.Present(
            "PaymentPending",
            AppTransactionRole.Seller,
            AppFulfillmentType.Physical);

        Assert.Equal(
            TransactionBucket.ActionRequired,
            buyerPresentation.Bucket);
        Assert.Equal(
            TransactionAction.ReviewAndPay,
            buyerPresentation.PrimaryAction);
        Assert.Equal(
            TransactionAction.ViewStatus,
            sellerPresentation.PrimaryAction);
    }

    [Fact]
    public void SellerSeesFulfillmentOnlyAfterConfirmedPhysicalPayment()
    {
        var beforePayment = TransactionStatePresenter.Present(
            "SellerAcceptedAwaitingPayment",
            AppTransactionRole.Seller,
            AppFulfillmentType.Physical);
        var afterPayment = TransactionStatePresenter.Present(
            "PaidAwaitingShipment",
            AppTransactionRole.Seller,
            AppFulfillmentType.Physical);

        Assert.NotEqual(TransactionAction.AddTracking, beforePayment.PrimaryAction);
        Assert.Equal(TransactionAction.AddTracking, afterPayment.PrimaryAction);
    }

    [Fact]
    public void DigitalSellerGetsHandoffInsteadOfTracking()
    {
        var presentation = TransactionStatePresenter.Present(
            "PaidAwaitingDigitalDelivery",
            AppTransactionRole.Seller,
            AppFulfillmentType.Digital);

        Assert.Equal(TransactionAction.ConfirmDigitalHandoff, presentation.PrimaryAction);
    }

    [Fact]
    public void DisputeNeverShowsPayoutAction()
    {
        var presentation = TransactionStatePresenter.Present(
            "Disputed",
            AppTransactionRole.Seller,
            AppFulfillmentType.Physical);

        Assert.Equal(TransactionAction.ViewStatus, presentation.PrimaryAction);
        Assert.Contains("หยุดจ่ายเงิน", presentation.StatusLabel);
    }

    [Fact]
    public void MoneyIsFormattedFromIntegerSatang()
    {
        Assert.Equal("฿4,500.25", MoneyFormatter.Format(450025, "THB"));
        Assert.Equal("฿24,500", MoneyFormatter.Format(2450000, "THB"));
    }

    [Fact]
    public void DeadlineUsesThaiMonthAndBuddhistYear()
    {
        var item = new AppTransaction(
            Guid.NewGuid(),
            "สินค้า",
            10000,
            "THB",
            AppTransactionRole.Buyer,
            AppFulfillmentType.Physical,
            "SellerAcceptedAwaitingPayment",
            DateTimeOffset.Parse("2026-07-25T08:00:00+07:00"),
            DateTimeOffset.Parse("2026-07-25T10:26:00+07:00"),
            "ผู้ขาย");

        Assert.Contains("ก.ค.", item.DeadlineText);
        Assert.Contains("2569", item.DeadlineText);
    }

    [Fact]
    public void Readiness_guidance_keeps_the_exact_deadline_visible()
    {
        var deadline = DateTimeOffset.Parse(
            "2026-08-02T17:00:00+07:00");
        var awaiting = new AppTransaction(
            Guid.NewGuid(), "สินค้า", 10000, "THB",
            AppTransactionRole.Buyer,
            AppFulfillmentType.Physical,
            "AwaitingSellerAcceptance",
            deadline.AddHours(-1), deadline, "ผู้ขาย");
        var ready = awaiting with
        {
            State = "SellerAcceptedAwaitingPayment"
        };

        Assert.Contains(
            "รอผู้ขายตรวจสอบและเตรียมขาย",
            awaiting.StatusGuidance);
        Assert.Contains("2 ส.ค. 2569 · 17:00", awaiting.StatusGuidance);
        Assert.Contains("ผู้ขายพร้อมขายแล้ว", ready.StatusGuidance);
        Assert.Contains("2 ส.ค. 2569 · 17:00", ready.StatusGuidance);
    }

    [Theory]
    [InlineData("AwaitingSellerAcceptance", AppTransactionRole.Buyer, "ผู้ขายตอบได้ถึง ")]
    [InlineData("AwaitingSellerAcceptance", AppTransactionRole.Seller, "ตอบภายใน ")]
    [InlineData("SellerAcceptedAwaitingPayment", AppTransactionRole.Buyer, "จ่ายภายใน ")]
    [InlineData("SellerAcceptedAwaitingPayment", AppTransactionRole.Seller, "รอผู้ซื้อจ่ายถึง ")]
    [InlineData("CheckoutStarted", AppTransactionRole.Buyer, "จ่ายภายใน ")]
    [InlineData("CheckoutStarted", AppTransactionRole.Seller, "รอผู้ซื้อจ่ายถึง ")]
    [InlineData("PaymentPending", AppTransactionRole.Buyer, "จ่ายภายใน ")]
    [InlineData("PaymentPending", AppTransactionRole.Seller, "รอผู้ซื้อจ่ายถึง ")]
    [InlineData("PaidAwaitingShipment", AppTransactionRole.Seller, "ส่งภายใน ")]
    [InlineData("PaidAwaitingShipment", AppTransactionRole.Buyer, "ผู้ขายต้องส่งภายใน ")]
    [InlineData("TrackingSubmitted", AppTransactionRole.Seller, "ส่งภายใน ")]
    [InlineData("TrackingSubmitted", AppTransactionRole.Buyer, "ผู้ขายต้องส่งภายใน ")]
    [InlineData("DeliveredDisputeWindow", AppTransactionRole.Buyer, "แจ้งปัญหาได้ถึง ")]
    [InlineData("DeliveredDisputeWindow", AppTransactionRole.Seller, "คาดว่าจะเริ่มจ่ายหลัง ")]
    public void DeadlineNamesTheActionInsteadOfOnlySayingWithin(
        string state,
        AppTransactionRole role,
        string expectedPrefix)
    {
        var item = new AppTransaction(
            Guid.NewGuid(),
            "สินค้า",
            10000,
            "THB",
            role,
            AppFulfillmentType.Physical,
            state,
            DateTimeOffset.UtcNow,
            DateTimeOffset.Parse("2026-07-25T10:26:00+07:00"),
            "คู่รายการ");

        Assert.StartsWith(expectedPrefix, item.DeadlineText);
    }

    [Fact]
    public void ListSemanticDescriptionCombinesRoleStateMoneyDeadlineAndAction()
    {
        var item = new AppTransaction(
            Guid.NewGuid(),
            "กล้อง",
            100000,
            "THB",
            AppTransactionRole.Buyer,
            AppFulfillmentType.Physical,
            "SellerAcceptedAwaitingPayment",
            DateTimeOffset.UtcNow,
            DateTimeOffset.Parse("2026-07-25T10:26:00+07:00"),
            "สมชาย");

        Assert.Contains("ซื้อ กล้อง", item.ListSemanticDescription);
        Assert.Contains("ผู้ขาย · สมชาย", item.ListSemanticDescription);
        Assert.Contains(item.StatusLabel, item.ListSemanticDescription);
        Assert.Contains(item.RoleAmountText, item.ListSemanticDescription);
        Assert.Contains(item.DeadlineText, item.ListSemanticDescription);
        Assert.Contains(item.PrimaryActionLabel, item.ListSemanticDescription);
    }

    [Theory]
    [InlineData(
        AppFulfillmentType.Physical,
        "สินค้าที่จัดส่ง",
        "#145FC7",
        "#EEF7FF")]
    [InlineData(
        AppFulfillmentType.Digital,
        "ไอดีเกม",
        "#5144BF",
        "#F3F1FF")]
    public void ListCardsExposePlainLanguageProductTypeWithoutSecrets(
        AppFulfillmentType type,
        string label,
        string color,
        string background)
    {
        var item = new AppTransaction(
            Guid.NewGuid(),
            "รายการตัวอย่าง",
            100000,
            "THB",
            AppTransactionRole.Buyer,
            type,
            "AwaitingSellerAcceptance",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1),
            "ผู้ขาย");

        Assert.Equal(label, item.ProductTypeLabel);
        Assert.Equal($"ซื้อ · {label}", item.RoleAndProductTypeLabel);
        Assert.Equal(color, item.ProductTypeColor);
        Assert.Equal(background, item.ProductTypeBackground);
        Assert.Contains($"ประเภท {label}", item.ListSemanticDescription);
    }

    [Fact]
    public void SellerListSemanticDescriptionUsesExpectedNetInsteadOfBuyerTotal()
    {
        var item = new AppTransaction(
            Guid.NewGuid(),
            "กล้อง",
            110000,
            "THB",
            AppTransactionRole.Seller,
            AppFulfillmentType.Physical,
            "PaidAwaitingShipment",
            DateTimeOffset.UtcNow,
            DateTimeOffset.Parse("2026-07-25T10:26:00+07:00"),
            "สมชาย",
            SellerExpectedNetSatang: 90000);

        Assert.Contains("ยอดที่จะได้รับ", item.ListSemanticDescription);
        Assert.Contains(item.SellerNetText, item.ListSemanticDescription);
        Assert.DoesNotContain(item.FormattedAmount, item.ListSemanticDescription);
    }

    [Fact]
    public void PaymentAndDigitalGuidanceStayPlainAndFulfillmentSpecific()
    {
        var pending = CreateItem(null) with { State = "PaymentPending" };
        var digitalSeller = CreateItem(null) with
        {
            Role = AppTransactionRole.Seller,
            FulfillmentType = AppFulfillmentType.Digital,
            State = "PaidAwaitingDigitalDelivery"
        };
        var digitalBuyerReview = CreateItem(null) with
        {
            FulfillmentType = AppFulfillmentType.Digital,
            State = "DigitalDeliverySubmitted"
        };
        var physicalSeller = CreateItem(null) with
        {
            Role = AppTransactionRole.Seller,
            State = "PaidAwaitingShipment"
        };
        var physicalBuyer = CreateItem(null) with
        {
            State = "PaidAwaitingShipment"
        };
        var digitalSellerReview = digitalBuyerReview with
        {
            Role = AppTransactionRole.Seller
        };

        Assert.Contains("กำลังตรวจสอบยอดชำระ", pending.StatusGuidance);
        Assert.DoesNotContain("Stripe", pending.StatusGuidance);
        Assert.Contains("ส่งมอบผ่านช่องทางที่ตกลง", digitalSeller.StatusGuidance);
        Assert.Contains("ไม่มีการจ่ายอัตโนมัติจากเวลา", digitalBuyerReview.StatusGuidance);
        Assert.Contains("ส่งสินค้าและเพิ่มเลขพัสดุ", physicalSeller.StatusGuidance);
        Assert.Contains("รอผู้ขายส่งสินค้า", physicalBuyer.StatusGuidance);
        Assert.Contains("ไม่มีการจ่ายอัตโนมัติจากเวลา", digitalSellerReview.StatusGuidance);
    }

    [Theory]
    [InlineData("AwaitingSellerAcceptance", 1, 0)]
    [InlineData("SellerAcceptedAwaitingPayment", 1, 2)]
    [InlineData("PaidAwaitingShipment", 2, 3)]
    [InlineData("DeliveredDisputeWindow", 2, 3)]
    [InlineData("PaidOut", 3, 0)]
    public void BuyerProgressReflectsVerifiedTransactionState(
        string state,
        int expectedCompletedThrough,
        int expectedActiveStep)
    {
        var item = new AppTransaction(
            Guid.NewGuid(),
            "สินค้า",
            10000,
            "THB",
            AppTransactionRole.Buyer,
            AppFulfillmentType.Physical,
            state,
            DateTimeOffset.UtcNow,
            null,
            "คู่รายการ");

        Assert.Equal(
            expectedCompletedThrough,
            item.ProgressCompletedThrough);
        Assert.Equal(expectedActiveStep, item.ProgressActiveStep);
    }

    [Fact]
    public void ConnectedProgressMapsBuyerPhysicalGlyphsAndSemantics()
    {
        var item = CreateItem(null) with
        {
            Role = AppTransactionRole.Buyer,
            FulfillmentType = AppFulfillmentType.Physical,
            State = "AwaitingSellerAcceptance"
        };

        Assert.Equal("progress_agreement_buyer_completed.png", item.ProgressOne.Icon);
        Assert.Equal("progress_payment_disabled.png", item.ProgressTwo.Icon);
        Assert.Equal("progress_physical_receipt_disabled.png", item.ProgressThree.Icon);
        Assert.Equal("สร้างข้อตกลง เสร็จแล้ว", item.ProgressOne.SemanticDescription);
        Assert.Equal("จ่ายเงิน ยังไม่เสร็จ", item.ProgressTwo.SemanticDescription);
        Assert.Equal("#E9F6FF", item.ProgressOne.BackgroundColor);
        Assert.Equal("#1988D3", item.ProgressOne.StrokeColor);
        Assert.Equal("#1988D3", item.ProgressOne.LabelColor);
        Assert.Equal("#FFFFFF", item.ProgressTwo.BackgroundColor);
        Assert.Equal("#E4EAF1", item.ProgressTwo.StrokeColor);
        Assert.Equal("#98A2B3", item.ProgressTwo.LabelColor);
    }

    [Fact]
    public void ConnectedProgressUsesSellerCompletedVariantAndPalette()
    {
        var item = CreateItem(null) with
        {
            Role = AppTransactionRole.Seller,
            FulfillmentType = AppFulfillmentType.Physical,
            State = "TrackingSubmitted"
        };

        Assert.Equal("progress_seller_agreement_proof_seller_completed.png", item.ProgressOne.Icon);
        Assert.Equal("progress_seller_physical_shipment_proof_seller_completed.png", item.ProgressTwo.Icon);
        Assert.Equal("progress_seller_payout_proof_seller_current.png", item.ProgressThree.Icon);
        Assert.Equal(
            SellerColorPalette.Surface,
            item.ProgressTwo.BackgroundColor);
        Assert.Equal(
            SellerColorPalette.Role,
            item.ProgressTwo.StrokeColor);
        Assert.Equal(
            SellerColorPalette.Role,
            item.ProgressTwo.LabelColor);
    }

    [Fact]
    public void SellerProgressUsesProofGlyphsAndCurrentPalette()
    {
        var item = CreateItem(null) with
        {
            Role = AppTransactionRole.Seller,
            FulfillmentType = AppFulfillmentType.Physical,
            State = "PaidAwaitingShipment"
        };

        Assert.Equal(
            TransactionProgressGlyph.SellerAgreementProof,
            item.ProgressOne.Glyph);
        Assert.Equal(
            TransactionProgressGlyph.SellerPhysicalShipmentProof,
            item.ProgressTwo.Glyph);
        Assert.Equal(
            TransactionProgressGlyph.SellerPayoutProof,
            item.ProgressThree.Glyph);
        Assert.Equal(
            SellerColorPalette.Role,
            item.ProgressOne.StrokeColor);
        Assert.Equal("#087C68", item.ProgressTwo.StrokeColor);
        Assert.Equal("#EAFBF7", item.ProgressTwo.BackgroundColor);
        Assert.Equal("#087C68", item.ProgressTwo.LabelColor);
        Assert.Equal(
            "ส่งของ ขั้นปัจจุบัน",
            item.ProgressTwo.SemanticDescription);
        Assert.Equal("#E4EAF1", item.ProgressThree.StrokeColor);
    }

    [Theory]
    [InlineData(
        AppTransactionRole.Buyer,
        AppFulfillmentType.Digital,
        "progress_payment_disabled.png",
        "progress_digital_handoff_disabled.png")]
    [InlineData(
        AppTransactionRole.Seller,
        AppFulfillmentType.Physical,
        "progress_seller_physical_shipment_proof_disabled.png",
        "progress_seller_payout_proof_disabled.png")]
    [InlineData(
        AppTransactionRole.Seller,
        AppFulfillmentType.Digital,
        "progress_digital_handoff_disabled.png",
        "progress_seller_payout_proof_disabled.png")]
    public void ConnectedProgressUsesRoleAndFulfillmentGlyphs(
        AppTransactionRole role,
        AppFulfillmentType fulfillmentType,
        string expectedSecond,
        string expectedThird)
    {
        var item = CreateItem(null) with
        {
            Role = role,
            FulfillmentType = fulfillmentType,
            State = "AwaitingSellerAcceptance"
        };

        Assert.Equal(expectedSecond, item.ProgressTwo.Icon);
        Assert.Equal(expectedThird, item.ProgressThree.Icon);
        Assert.Equal(
            role == AppTransactionRole.Seller
                ? TransactionProgressGlyph.SellerAgreementProof
                : TransactionProgressGlyph.Agreement,
            item.ProgressOne.Glyph);
        Assert.Equal(
            role == AppTransactionRole.Buyer
                ? TransactionProgressGlyph.Payment
                : fulfillmentType == AppFulfillmentType.Physical
                    ? TransactionProgressGlyph.SellerPhysicalShipmentProof
                    : TransactionProgressGlyph.DigitalHandoff,
            item.ProgressTwo.Glyph);
        Assert.Equal(
            role == AppTransactionRole.Seller
                ? TransactionProgressGlyph.SellerPayoutProof
                : fulfillmentType == AppFulfillmentType.Physical
                    ? TransactionProgressGlyph.PhysicalReceipt
                    : TransactionProgressGlyph.DigitalHandoff,
            item.ProgressThree.Glyph);
    }

    [Fact]
    public void ConnectedProgressColorsOnlyCompletedDestinationSegments()
    {
        var firstComplete = CreateItem(null) with
        {
            Role = AppTransactionRole.Buyer,
            State = "AwaitingSellerAcceptance"
        };
        var secondComplete = firstComplete with { State = "PaidAwaitingShipment" };
        var thirdComplete = firstComplete with { State = "PayoutPending" };

        Assert.Equal("#E4EAF1", firstComplete.ProgressConnectorOneColor);
        Assert.Equal("#E4EAF1", firstComplete.ProgressConnectorTwoColor);
        Assert.Equal("#1988D3", secondComplete.ProgressConnectorOneColor);
        Assert.Equal("#E4EAF1", secondComplete.ProgressConnectorTwoColor);
        Assert.Equal("#1988D3", thirdComplete.ProgressConnectorOneColor);
        Assert.Equal("#1988D3", thirdComplete.ProgressConnectorTwoColor);

        var seller = thirdComplete with
        {
            Role = AppTransactionRole.Seller,
            State = "PaidOut"
        };
        Assert.Equal(
            SellerColorPalette.Role,
            seller.ProgressConnectorOneColor);
        Assert.Equal(
            SellerColorPalette.Role,
            seller.ProgressConnectorTwoColor);
    }

    [Fact]
    public void ActiveButIncompleteConnectedTokenStaysGray()
    {
        var item = CreateItem(null) with
        {
            Role = AppTransactionRole.Buyer,
            State = "DeliveredDisputeWindow"
        };

        Assert.Equal(3, item.ProgressActiveStep);
        Assert.Equal("progress_physical_receipt_disabled.png", item.ProgressThree.Icon);
        Assert.Equal("#FFFFFF", item.ProgressThree.BackgroundColor);
        Assert.Equal("#E4EAF1", item.ProgressThree.StrokeColor);
        Assert.Equal("#98A2B3", item.ProgressThree.LabelColor);
        Assert.Equal("ได้รับของ ยังไม่เสร็จ", item.ProgressThree.SemanticDescription);
    }

    [Fact]
    public void Buyer_and_seller_have_distinct_detail_themes_and_payout_copy()
    {
        var buyer = CreateItem(null) with { State = "PayoutPending" };
        var seller = buyer with { Role = AppTransactionRole.Seller };

        Assert.NotEqual(buyer.RoleHeaderStart, seller.RoleHeaderStart);
        Assert.NotEqual(buyer.RolePageTint, seller.RolePageTint);
        Assert.Equal("ยืนยันรับของแล้ว", buyer.StatusLabel);
        Assert.Equal("กำลังดำเนินการรับเงิน", seller.StatusLabel);
        Assert.Contains("จ่ายเงินให้ผู้ขาย", buyer.StatusGuidance);
        Assert.Contains("เข้าบัญชีของคุณ", seller.StatusGuidance);
        Assert.Equal("ui_check_money.png", buyer.StatusGuidanceIcon);
        Assert.Equal("ui_bank.png", seller.StatusGuidanceIcon);
    }

    [Fact]
    public void BuyerAndSellerSeeOnlyTheirOwnThreeSteps()
    {
        var buyer = CreateItem(null);
        var seller = buyer with { Role = AppTransactionRole.Seller };

        Assert.Equal("สร้างข้อตกลง", buyer.ProgressOneLabel);
        Assert.Equal("จ่ายเงิน", buyer.ProgressTwoLabel);
        Assert.Equal("ได้รับของ", buyer.ProgressThreeLabel);
        Assert.Equal("ยอมรับข้อตกลง", seller.ProgressOneLabel);
        Assert.Equal("ส่งของ", seller.ProgressTwoLabel);
        Assert.Equal("รับเงิน", seller.ProgressThreeLabel);
    }

    [Fact]
    public void SellerCannotSeeShippingAsActiveBeforeConfirmedPayment()
    {
        var item = new AppTransaction(
            Guid.NewGuid(),
            "สินค้า",
            10000,
            "THB",
            AppTransactionRole.Seller,
            AppFulfillmentType.Physical,
            "SellerAcceptedAwaitingPayment",
            DateTimeOffset.UtcNow,
            null,
            "คู่รายการ");

        Assert.Equal(1, item.ProgressCompletedThrough);
        Assert.Equal(0, item.ProgressActiveStep);
        Assert.Equal(
            "progress_seller_physical_shipment_proof_disabled.png",
            item.ProgressTwo.Icon);
        Assert.Equal("#FFFFFF", item.ProgressTwo.BackgroundColor);
        Assert.Equal("#E4EAF1", item.ProgressTwo.StrokeColor);
        Assert.Equal("#98A2B3", item.ProgressTwo.LabelColor);
        Assert.Equal(
            "ส่งของ ยังไม่เสร็จ",
            item.ProgressTwo.SemanticDescription);
    }

    [Fact]
    public void Provider_managed_shipping_never_asks_seller_to_type_tracking()
    {
        var item = new AppTransaction(
            Guid.NewGuid(),
            "สินค้า",
            10000,
            "THB",
            AppTransactionRole.Seller,
            AppFulfillmentType.Physical,
            "PaidAwaitingShipment",
            DateTimeOffset.UtcNow,
            null,
            "ผู้ซื้อ") with
        {
            ShippingManagedByProvider = true
        };

        Assert.Equal(
            TransactionAction.ViewStatus,
            item.Presentation.PrimaryAction);
        Assert.Contains(
            "ออกเลขพัสดุ",
            item.StatusGuidance);
    }

    [Fact]
    public void Provider_tracking_allocation_keeps_exact_ship_by_visible()
    {
        var shipBy = DateTimeOffset.UtcNow.AddHours(72);
        var item = new AppTransaction(
            Guid.NewGuid(),
            "สินค้า",
            10000,
            "THB",
            AppTransactionRole.Seller,
            AppFulfillmentType.Physical,
            "TrackingSubmitted",
            DateTimeOffset.UtcNow,
            shipBy,
            "ผู้ซื้อ") with
        {
            ShippingManagedByProvider = true,
            TrackingNumber = "TH123456789012",
            ShippingLabelAvailable = true
        };

        Assert.Contains("ภายใน", item.StatusGuidance);
        Assert.StartsWith(
            "ส่งภายใน ",
            item.DeadlineText);
        Assert.Equal(
            TransactionAction.ViewStatus,
            item.Presentation.PrimaryAction);
    }

    [Fact]
    public void Timely_provider_scan_shows_seller_protection_review_without_edit_action()
    {
        var shipBy = DateTimeOffset.UtcNow.AddHours(72);
        var item = new AppTransaction(
            Guid.NewGuid(),
            "สินค้า",
            10000,
            "THB",
            AppTransactionRole.Seller,
            AppFulfillmentType.Physical,
            "TrackingUnverified",
            DateTimeOffset.UtcNow,
            null,
            "ผู้ซื้อ") with
        {
            ShippingManagedByProvider = true,
            TrackingNumber = "TH123456789012",
            ShipByAt = shipBy,
            FirstCarrierScanAt = shipBy.AddMinutes(-1)
        };

        Assert.True(
            item.HasTimelyTrustedCarrierAcceptance);
        Assert.Equal(
            TransactionAction.ViewStatus,
            item.Presentation.PrimaryAction);
        Assert.Equal(
            "ส่งพัสดุทันเวลา · กำลังตรวจขนส่ง",
            item.Presentation.StatusLabel);
        Assert.Contains(
            "ขนส่งรับพัสดุของคุณภายในกำหนดแล้ว",
            item.StatusGuidance);
    }

    [Fact]
    public void PromptPay_refund_action_is_clear_and_never_collects_bank_data_in_app()
    {
        var item = new AppTransaction(
            Guid.NewGuid(),
            "สินค้า",
            10000,
            "THB",
            AppTransactionRole.Buyer,
            AppFulfillmentType.Physical,
            "RefundPending",
            DateTimeOffset.UtcNow,
            null,
            "ผู้ขาย") with
        {
            RefundProviderStatus = "requires_action",
            RefundActionExpiresAt =
                new DateTimeOffset(
                    2026, 9, 10, 9, 0, 0,
                    TimeSpan.Zero)
        };

        Assert.Equal(
            TransactionBucket.ActionRequired,
            item.Presentation.Bucket);
        Assert.Equal(
            "ต้องยืนยันข้อมูลรับเงินคืน",
            item.Presentation.StatusLabel);
        Assert.Contains(
            "อีเมล",
            item.StatusGuidance);
        Assert.Contains(
            "ให้ Stripe โดยตรง",
            item.StatusGuidance);
        Assert.Contains(
            "TOKLONG จะไม่ขอเลขบัญชี",
            item.StatusGuidance);

        var processing = item with
        {
            RefundProviderStatus = "pending"
        };
        Assert.Equal(
            TransactionBucket.InProgress,
            processing.Presentation.Bucket);
        Assert.Contains(
            "Stripe ได้รับข้อมูลแล้ว",
            processing.StatusGuidance);
    }

    [Fact]
    public void ProductVisualUsesPhotoAndFallsBackToTypeIcon()
    {
        var withPhoto = CreateItem("https://cdn.example/item.jpg");
        var withoutPhoto = CreateItem(null);

        Assert.Equal("https://cdn.example/item.jpg", withPhoto.ProductVisual);
        Assert.Equal("product_physical.png", withoutPhoto.ProductVisual);
    }

    [Fact]
    public void RoleLabelsStayShort()
    {
        var buyer = CreateItem(null);
        var seller = buyer with { Role = AppTransactionRole.Seller };

        Assert.Equal("ซื้อ", buyer.RoleLabel);
        Assert.Equal("ขาย", seller.RoleLabel);
    }

    [Fact]
    public void RoleVisibilityFlagsAreMutuallyExclusive()
    {
        var buyer = CreateItem(null);
        var seller = buyer with { Role = AppTransactionRole.Seller };

        Assert.True(buyer.IsBuyerRole);
        Assert.False(buyer.IsSellerRole);
        Assert.False(seller.IsBuyerRole);
        Assert.True(seller.IsSellerRole);
    }

    [Fact]
    public void RoleHeaderAmountDoesNotExposeBuyerTotalToSeller()
    {
        var buyer = CreateItem(null) with
        {
            AmountSatang = 594845,
            ItemPriceSatang = 500000,
            SellerExpectedNetSatang = 500000
        };
        var seller = buyer with { Role = AppTransactionRole.Seller };

        Assert.Equal("ยอดรวม", buyer.RoleAmountLabel);
        Assert.Equal("฿5,948.45", buyer.RoleAmountText);
        Assert.Equal("ยอดที่จะได้รับ", seller.RoleAmountLabel);
        Assert.Equal("฿5,000", seller.RoleAmountText);
    }

    [Fact]
    public void AgreementEvidenceShowsSharedHashAndBothAcceptanceStates()
    {
        var acceptedAt = new DateTimeOffset(
            2026,
            7,
            25,
            9,
            0,
            0,
            TimeSpan.FromHours(7));
        var item = CreateItem(null) with
        {
            AgreementCoreSnapshotHash =
                "abc123sharedagreementhash",
            SellerAcceptedAt = acceptedAt,
            BuyerAcceptedAt = acceptedAt.AddMinutes(4)
        };

        Assert.True(item.HasAgreementEvidence);
        Assert.Equal(
            "abc123sharedagreementhash",
            item.AgreementEvidenceHash);
        Assert.Contains(
            "ผู้ขายยอมรับเมื่อ",
            item.SellerAcceptanceText);
        Assert.Contains(
            "ผู้ซื้อยอมรับเมื่อ",
            item.BuyerAcceptanceText);
        Assert.Contains(
            "บัญชีที่ยืนยันด้วยเบอร์โทร",
            item.BuyerAcceptanceText);
    }

    [Fact]
    public void ProductDetailHidesFallbackTextAndShowsRealCondition()
    {
        var usedGood = CreateItem(null) with
        {
            AgreementDetails = "สินค้า",
            Condition = AppCondition.UsedGood,
            KnownDefects =
                QuickDealSnapshotComposer.NoBuyerReportedDefects
        };
        var withDefect = usedGood with
        {
            AgreementDetails = "พร้อมกล่องและสายชาร์จ",
            Condition = AppCondition.UsedDefects,
            KnownDefects = "มีรอยที่มุมซ้าย"
        };

        Assert.False(usedGood.HasAdditionalAgreementDetails);
        Assert.Equal("มือสอง สภาพดี", usedGood.ConditionLabel);
        Assert.False(usedGood.HasKnownDefects);
        Assert.True(withDefect.HasAdditionalAgreementDetails);
        Assert.Equal("มือสอง มีตำหนิ", withDefect.ConditionLabel);
        Assert.True(withDefect.HasKnownDefects);
    }

    [Fact]
    public void Parcel_protection_requires_choice_only_before_a_booking_or_payment()
    {
        var offered = new BuyerParcelProtection(
            RequiresChoice: true,
            AddOnAvailable: true,
            IncludedCoverageLimitSatang: 1_000_00,
            MaximumCoverageLimitSatang: 4_500_00,
            CustomerPriceSatang: 60_00,
            OptionReference: "option",
            TermsVersion: "parcel-v1",
            ExpiresAt: DateTimeOffset.Parse("2026-07-30T10:55:00+07:00"),
            Election: "Pending",
            BookingReady: false,
            ReconfirmationRequired: false);
        var included = offered with
        {
            RequiresChoice = false,
            AddOnAvailable = false,
            MaximumCoverageLimitSatang = null,
            CustomerPriceSatang = null,
            OptionReference = null
        };

        Assert.Equal(
            ParcelProtectionCheckoutStep.Choose,
            ParcelProtectionCheckoutPresentation.Next(offered));
        Assert.Equal(
            ParcelProtectionCheckoutStep.SubmitIncludedCoverage,
            ParcelProtectionCheckoutPresentation.Next(included));
        Assert.Equal(
            ParcelProtectionCheckoutStep.SubmitIncludedCoverage,
            ParcelProtectionCheckoutPresentation.Next(
                included with { Election = "Unavailable" }));
        Assert.Equal(
            ParcelProtectionCheckoutStep.PresentPayment,
            ParcelProtectionCheckoutPresentation.Next(
                included with
                {
                    Election = "Unavailable",
                    ElectionPersisted = true
                }));
        Assert.Equal(
            ParcelProtectionCheckoutStep.PresentPayment,
            ParcelProtectionCheckoutPresentation.Next(
                offered with
                {
                    RequiresChoice = false,
                    Election = "Accepted"
                }));
        Assert.Equal(
            ParcelProtectionCheckoutStep.PresentPayment,
            ParcelProtectionCheckoutPresentation.Next(
                offered with
                {
                    RequiresChoice = false,
                    Election = "Accepted",
                    BookingReady = true
                }));
    }

    [Fact]
    public void Persisted_protection_state_wins_over_an_old_choice_prompt()
    {
        var stalePrompt = new BuyerParcelProtection(
            RequiresChoice: true,
            AddOnAvailable: true,
            IncludedCoverageLimitSatang: 1_000_00,
            MaximumCoverageLimitSatang: 4_500_00,
            CustomerPriceSatang: 60_00,
            OptionReference: "old-option",
            TermsVersion: "parcel-v1",
            ExpiresAt: DateTimeOffset.Parse("2026-07-30T10:55:00+07:00"),
            Election: "Accepted",
            BookingReady: true,
            ReconfirmationRequired: false);

        Assert.Equal(
            ParcelProtectionCheckoutStep.PresentPayment,
            ParcelProtectionCheckoutPresentation.Next(stalePrompt));
        Assert.Equal(
            ParcelProtectionCheckoutStep.PresentPayment,
            ParcelProtectionCheckoutPresentation.Next(stalePrompt with
            {
                BookingReady = false
            }));
    }

    private static AppTransaction CreateItem(string? photoUrl) =>
        new(
            Guid.NewGuid(),
            "สินค้า",
            10000,
            "THB",
            AppTransactionRole.Buyer,
            AppFulfillmentType.Physical,
            "AwaitingSellerAcceptance",
            DateTimeOffset.UtcNow,
            null,
            "คู่รายการ",
            photoUrl);
}
