using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class AppTransactionManagedShipmentPresentationTests
{
    [Theory]
    [InlineData(
        AppTransactionRole.Seller,
        true,
        TransactionBucket.InProgress,
        TransactionAction.ViewStatus,
        "ดูสถานะ")]
    [InlineData(
        AppTransactionRole.Buyer,
        true,
        TransactionBucket.ActionRequired,
        TransactionAction.ViewStatus,
        "ดูสถานะ")]
    [InlineData(
        AppTransactionRole.Seller,
        false,
        TransactionBucket.ActionRequired,
        TransactionAction.AddTracking,
        "เพิ่มเลขพัสดุ")]
    public void Shipment_overdue_override_applies_only_to_managed_seller(
        AppTransactionRole role,
        bool shippingManagedByProvider,
        TransactionBucket expectedBucket,
        TransactionAction expectedAction,
        string expectedActionLabel)
    {
        var transaction = new AppTransaction(
            Guid.Parse("00000000-0000-0000-0000-000000000801"),
            "กล้องสะสมพร้อมอุปกรณ์ครบชุด",
            3_000_000,
            "THB",
            role,
            AppFulfillmentType.Physical,
            "ShipmentOverdue",
            DateTimeOffset.Parse("2026-07-28T15:00:00+07:00"),
            DateTimeOffset.Parse("2026-07-28T16:30:00+07:00"),
            role == AppTransactionRole.Seller
                ? "ผู้ซื้อบัญชีทดสอบ"
                : "ผู้ขายบัญชีทดสอบ",
            ItemPriceSatang: 3_000_000,
            ShippingManagedByProvider: shippingManagedByProvider);

        Assert.Equal(expectedBucket, transaction.Presentation.Bucket);
        Assert.Equal(expectedAction, transaction.Presentation.PrimaryAction);
        Assert.Equal(
            expectedActionLabel,
            transaction.Presentation.PrimaryActionLabel);
    }
}
