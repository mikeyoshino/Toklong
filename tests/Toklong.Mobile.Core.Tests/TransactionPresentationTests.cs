using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class TransactionPresentationTests
{
    [Fact]
    public void SellerAcceptedOfferRequiresBuyerPaymentAction()
    {
        var presentation = TransactionStatePresenter.Present(
            "SellerAcceptedAwaitingPayment",
            AppTransactionRole.Buyer,
            AppFulfillmentType.Physical);

        Assert.Equal(TransactionBucket.ActionRequired, presentation.Bucket);
        Assert.Equal(TransactionAction.ReviewAndPay, presentation.PrimaryAction);
        Assert.Equal("จ่ายเงินได้", presentation.StatusLabel);
        Assert.Equal("ดูรายละเอียดแล้วจ่าย", presentation.PrimaryActionLabel);
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
        Assert.Equal("รอผู้ขายตอบ", presentation.StatusLabel);
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
            "ตรวจข้อเสนอ",
            presentation.PrimaryActionLabel);
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

        Assert.Equal("progress_agreement_completed.png", item.ProgressOne.Icon);
        Assert.Equal("progress_payment_disabled.png", item.ProgressTwo.Icon);
        Assert.Equal("progress_parcel_received_disabled.png", item.ProgressThree.Icon);
        Assert.Equal("สร้างข้อตกลง เสร็จแล้ว", item.ProgressOne.SemanticDescription);
        Assert.Equal("จ่ายเงิน ยังไม่เสร็จ", item.ProgressTwo.SemanticDescription);
        Assert.Equal("#087C68", item.ProgressOne.BackgroundColor);
        Assert.Equal("#FFFFFF", item.ProgressTwo.BackgroundColor);
        Assert.Equal("#E4EAF1", item.ProgressTwo.StrokeColor);
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
        "progress_parcel_handoff_disabled.png",
        "progress_payout_disabled.png")]
    [InlineData(
        AppTransactionRole.Seller,
        AppFulfillmentType.Digital,
        "progress_digital_handoff_disabled.png",
        "progress_payout_disabled.png")]
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
        Assert.Equal("#087C68", secondComplete.ProgressConnectorOneColor);
        Assert.Equal("#E4EAF1", secondComplete.ProgressConnectorTwoColor);
        Assert.Equal("#087C68", thirdComplete.ProgressConnectorOneColor);
        Assert.Equal("#087C68", thirdComplete.ProgressConnectorTwoColor);
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
        Assert.Equal("progress_parcel_received_disabled.png", item.ProgressThree.Icon);
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
        Assert.Equal("ui_truck_disabled.png", item.ProgressTwoIcon);
        Assert.Equal("#98A2B3", item.ProgressTwoLabelColor);
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
            "ภายใน ",
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
