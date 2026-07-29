using System.Globalization;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class BuyerReceiptConfirmationPresenterTests
{
    [Fact]
    public void Physical_delivery_uses_exact_trusted_deadline_and_approved_copy()
    {
        var deadline = new DateTimeOffset(
            2026, 8, 2, 23, 58, 0, TimeSpan.FromHours(7));
        var transaction = Eligible(
            AppFulfillmentType.Physical,
            "DeliveredDisputeWindow",
            deadline);

        var result = BuyerReceiptConfirmationPresenter.Present(transaction);

        Assert.NotNull(result);
        Assert.Equal("ตรวจสินค้าให้เรียบร้อย", result.Heading);
        Assert.Equal(
            "เช็กสินค้าและอุปกรณ์ให้ครบก่อนยืนยัน เมื่อยืนยันแล้ว ระบบจะเริ่มจ่ายเงินให้ผู้ขาย",
            result.SupportingText);
        Assert.True(result.HasDeadline);
        var localized = deadline.ToLocalTime().ToString(
            "d MMM yyyy · HH:mm 'น.'",
            CultureInfo.GetCultureInfo("th-TH"));
        Assert.Equal(
            $"แจ้งปัญหาได้ถึง {localized}",
            result.DeadlineText);
        Assert.Equal(
            "ยืนยันว่าได้รับของเรียบร้อย",
            result.PrimaryActionText);
        Assert.Equal(
            "พบปัญหากับรายการนี้",
            result.ProblemActionText);
    }

    [Fact]
    public void Digital_delivery_has_specific_copy_and_no_automatic_deadline()
    {
        var transaction = Eligible(
            AppFulfillmentType.Digital,
            "DigitalDeliverySubmitted",
            null);

        var result = BuyerReceiptConfirmationPresenter.Present(transaction);

        Assert.NotNull(result);
        Assert.Equal("ตรวจรายการที่ได้รับ", result.Heading);
        Assert.Equal(
            "ตรวจรายการและการเข้าถึงให้เรียบร้อยก่อนยืนยัน เมื่อยืนยันแล้ว ระบบจะเริ่มจ่ายเงินให้ผู้ขาย",
            result.SupportingText);
        Assert.False(result.HasDeadline);
        Assert.Equal("", result.DeadlineText);
        Assert.Contains("รายการ", result.ConfirmationMessage);
        Assert.DoesNotContain("หมดเวลา", result.SupportingText);
    }

    [Fact]
    public void Physical_delivery_without_trusted_deadline_does_not_offer_confirmation()
    {
        var transaction = Eligible(
            AppFulfillmentType.Physical,
            "DeliveredDisputeWindow",
            null);

        Assert.Null(
            BuyerReceiptConfirmationPresenter.Present(transaction));
    }

    [Theory]
    [InlineData(
        AppTransactionRole.Seller,
        "DeliveredDisputeWindow")]
    [InlineData(AppTransactionRole.Buyer, "InTransit")]
    [InlineData(AppTransactionRole.Buyer, "Disputed")]
    [InlineData(
        AppTransactionRole.Buyer,
        "BuyerConfirmedReceipt")]
    public void Ineligible_role_or_state_has_no_confirmation_card(
        AppTransactionRole role,
        string state)
    {
        var transaction = Eligible(
            AppFulfillmentType.Physical,
            state,
            DateTimeOffset.UtcNow.AddHours(72)) with
        {
            Role = role
        };

        Assert.Null(
            BuyerReceiptConfirmationPresenter.Present(transaction));
    }

    private static AppTransaction Eligible(
        AppFulfillmentType fulfillmentType,
        string state,
        DateTimeOffset? deadline) =>
        new(
            Guid.NewGuid(),
            "สินค้า",
            450000,
            "THB",
            AppTransactionRole.Buyer,
            fulfillmentType,
            state,
            DateTimeOffset.UtcNow,
            deadline,
            "ผู้ขาย ทดสอบ");
}
