using System.Text.Json;
using Toklong.Domain.Common;
using Toklong.Domain.Transactions;

namespace Toklong.Domain.Tests.Transactions;

public sealed class ShippingMoneyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Insured_physical_offer_freezes_separate_buyer_costs_without_changing_seller_net()
    {
        var transitions = new TransactionTransitionService();
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ ทดสอบ",
            "+66899999999",
            FulfillmentType.PhysicalShipment,
            "กล้องทดสอบ",
            "กล้องพร้อมเลนส์และอุปกรณ์",
            ConditionCode.UsedGood,
            "ไม่มี",
            null,
            120_000,
            "terms-v1",
            Now,
            transitions);
        transaction.AcceptBuyerOffer(
            Guid.NewGuid(),
            "ผู้ขาย ทดสอบ",
            "+66811111111",
            "KBANK",
            "ผู้ขาย ทดสอบ",
            "1234567890",
            true,
            Now.AddMinutes(1),
            transitions,
            buyerProtectionFeeSatang: 5_900,
            sellerExpectedNetSatang: 120_000,
            feePolicyVersion: "buyer-protection-test-v1",
            shipping: new AcceptedShippingQuote(
                TestTransactionFactory.ShippingOriginAddress,
                TestTransactionFactory.DeliveryProvinceName,
                TestTransactionFactory.DeliveryPostalCode,
                1_200,
                20,
                30,
                15,
                "test-shipping",
                "insured-quote",
                "FLASH",
                "STANDARD",
                "Flash Express Standard",
                5_200,
                1_100,
                120_000,
                "FULL_VALUE",
                Now.AddHours(2)));

        transaction.BeginCheckout(
            "ผู้ซื้อ ทดสอบ",
            "+66899999999",
            Now.AddMinutes(2),
            transitions,
            buyerProtectionFeeSatang: 5_900,
            sellerExpectedNetSatang: 120_000,
            feePolicyVersion: "buyer-protection-test-v1");

        Assert.Equal(5_200, transaction.ShippingFeeSatang);
        Assert.Equal(
            1_100,
            transaction.ParcelInsuranceFeeSatang);
        Assert.Equal(
            120_000,
            transaction.ShippingDeclaredValueSatang);
        Assert.Equal(
            "FULL_VALUE",
            transaction.ShippingInsuranceCode);
        Assert.Equal(132_200, transaction.BuyerTotalSatang);
        Assert.Equal(
            120_000,
            transaction.SellerExpectedNetSatang);
        Assert.Equal(9, transaction.SnapshotSchemaVersion);

        using var snapshot = JsonDocument.Parse(
            transaction.ProductSnapshotJson!);
        Assert.Equal(
            1_100,
            snapshot.RootElement
                .GetProperty("ParcelInsuranceFeeSatang")
                .GetInt64());
        Assert.Equal(
            120_000,
            snapshot.RootElement
                .GetProperty("ShippingDeclaredValueSatang")
                .GetInt64());
    }

    [Fact]
    public void Insurance_below_full_item_value_is_rejected()
    {
        var transitions = new TransactionTransitionService();
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ ทดสอบ",
            "+66899999999",
            FulfillmentType.PhysicalShipment,
            "กล้องทดสอบ",
            "กล้องพร้อมเลนส์และอุปกรณ์",
            ConditionCode.UsedGood,
            "ไม่มี",
            null,
            120_000,
            "terms-v1",
            Now,
            transitions);

        Assert.Throws<DomainException>(() =>
            transaction.AcceptBuyerOffer(
                Guid.NewGuid(),
                "ผู้ขาย ทดสอบ",
                "+66811111111",
                "KBANK",
                "ผู้ขาย ทดสอบ",
                "1234567890",
                true,
                Now.AddMinutes(1),
                transitions,
                shipping: new AcceptedShippingQuote(
                    TestTransactionFactory.ShippingOriginAddress,
                    TestTransactionFactory.DeliveryProvinceName,
                    TestTransactionFactory.DeliveryPostalCode,
                    1_200,
                    20,
                    30,
                    15,
                    "test-shipping",
                    "underinsured-quote",
                    "FLASH",
                    "STANDARD",
                    "Flash Express Standard",
                    5_200,
                    1_100,
                    119_999,
                    "FULL_VALUE",
                    Now.AddHours(2))));
    }
}
