using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class SellerWorkSummaryTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-07-28T15:00:00+07:00");

    [Fact]
    public void Create_classifies_each_seller_record_once_and_excludes_buyers()
    {
        var source = new[]
        {
            Item("00000000-0000-0000-0000-000000000001",
                AppTransactionRole.Seller, "AwaitingSellerAcceptance", Now.AddHours(3)),
            Item("00000000-0000-0000-0000-000000000002",
                AppTransactionRole.Seller, "PaidAwaitingShipment", Now.AddHours(20)),
            Item("00000000-0000-0000-0000-000000000003",
                AppTransactionRole.Seller, "SellerAcceptedAwaitingPayment", Now.AddHours(1)),
            Item("00000000-0000-0000-0000-000000000004",
                AppTransactionRole.Seller, "Disputed", null),
            Item("00000000-0000-0000-0000-000000000005",
                AppTransactionRole.Seller, "PaidOut", null),
            Item("00000000-0000-0000-0000-000000000006",
                AppTransactionRole.Buyer, "AwaitingSellerAcceptance", Now.AddHours(2))
        };

        var result = SellerWorkSummary.Create(source);

        Assert.Equal(5, result.TotalCount);
        Assert.Equal(1, result.NewOfferCount);
        Assert.Equal(1, result.FulfillmentRequiredCount);
        Assert.Equal(1, result.InProgressCount);
        Assert.Equal(1, result.ProblemCount);
        Assert.Equal(2, result.ActionableCount);
        Assert.Equal(5, result.AllSellerTransactions.Count);
    }

    [Theory]
    [InlineData("Disputed", SellerWorkCategory.Problems)]
    [InlineData("ResolutionPending", SellerWorkCategory.Problems)]
    [InlineData("AwaitingSellerAcceptance", SellerWorkCategory.NewOffers)]
    [InlineData("PaidAwaitingShipment", SellerWorkCategory.FulfillmentRequired)]
    [InlineData("PaidAwaitingDigitalDelivery", SellerWorkCategory.FulfillmentRequired)]
    [InlineData("TrackingUnverified", SellerWorkCategory.FulfillmentRequired)]
    [InlineData("ShipmentOverdue", SellerWorkCategory.FulfillmentRequired)]
    [InlineData("SellerAcceptedAwaitingPayment", SellerWorkCategory.InProgress)]
    [InlineData("CheckoutStarted", SellerWorkCategory.InProgress)]
    [InlineData("PaymentPending", SellerWorkCategory.InProgress)]
    [InlineData("TrackingSubmitted", SellerWorkCategory.InProgress)]
    [InlineData("InTransit", SellerWorkCategory.InProgress)]
    [InlineData("DigitalDeliverySubmitted", SellerWorkCategory.InProgress)]
    [InlineData("DeliveredDisputeWindow", SellerWorkCategory.InProgress)]
    [InlineData("PayoutEligible", SellerWorkCategory.InProgress)]
    [InlineData("PayoutPending", SellerWorkCategory.InProgress)]
    [InlineData("BuyerConfirmedReceipt", SellerWorkCategory.InProgress)]
    [InlineData("RefundPending", SellerWorkCategory.InProgress)]
    public void Category_follows_approved_precedence(
        string state,
        SellerWorkCategory expected)
    {
        var item = Item(
            "00000000-0000-0000-0000-000000000010",
            AppTransactionRole.Seller,
            state,
            Now.AddHours(1));

        Assert.Equal(expected, SellerWorkSummary.CategoryOf(item));
    }

    [Theory]
    [InlineData("PaidAwaitingShipment")]
    [InlineData("TrackingUnverified")]
    public void Provider_managed_shipping_work_stays_in_progress(string state)
    {
        var item = Item(
            "00000000-0000-0000-0000-000000000011",
            AppTransactionRole.Seller,
            state,
            Now.AddHours(1),
            shippingManagedByProvider: true);

        Assert.Equal(
            SellerWorkCategory.InProgress,
            SellerWorkSummary.CategoryOf(item));
    }

    [Theory]
    [InlineData("PaidOut")]
    [InlineData("Refunded")]
    [InlineData("Expired")]
    [InlineData("Cancelled")]
    public void Completed_records_affect_only_total_and_history(string state)
    {
        var result = SellerWorkSummary.Create([
            Item(
                "00000000-0000-0000-0000-000000000012",
                AppTransactionRole.Seller,
                state,
                null)
        ]);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(0, result.ActionableCount);
        Assert.Null(SellerWorkSummary.CategoryOf(
            result.AllSellerTransactions.Single()));
    }

    private static AppTransaction Item(
        string id,
        AppTransactionRole role,
        string state,
        DateTimeOffset? deadline,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null,
        AppFulfillmentType fulfillment = AppFulfillmentType.Physical,
        bool shippingManagedByProvider = false) =>
        new(
            Guid.Parse(id),
            "สินค้าทดสอบ",
            1_000_00,
            "THB",
            role,
            fulfillment,
            state,
            updatedAt ?? Now,
            deadline,
            "คู่รายการ",
            ItemPriceSatang: 1_000_00,
            ShippingManagedByProvider: shippingManagedByProvider,
            CreatedAt: createdAt ?? Now);
}
