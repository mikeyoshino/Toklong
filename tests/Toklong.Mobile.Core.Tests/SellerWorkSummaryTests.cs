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

    [Fact]
    public void Spotlight_prioritizes_overdue_then_ship_by_then_offer_deadline()
    {
        var offer = Item(
            "00000000-0000-0000-0000-000000000021",
            AppTransactionRole.Seller,
            "AwaitingSellerAcceptance",
            Now.AddMinutes(10));
        var paid = Item(
            "00000000-0000-0000-0000-000000000022",
            AppTransactionRole.Seller,
            "PaidAwaitingShipment",
            Now.AddHours(1));
        var overdue = Item(
            "00000000-0000-0000-0000-000000000023",
            AppTransactionRole.Seller,
            "ShipmentOverdue",
            Now.AddHours(-1));

        Assert.Equal(
            overdue.Id,
            SellerWorkSummary.Create([offer, paid, overdue]).Spotlight?.Id);
        Assert.Equal(
            paid.Id,
            SellerWorkSummary.Create([offer, paid]).Spotlight?.Id);
        Assert.Equal(
            offer.Id,
            SellerWorkSummary.Create([offer]).Spotlight?.Id);
    }

    [Fact]
    public void Selected_category_recalculates_spotlight_and_visible_records()
    {
        var first = Item(
            "00000000-0000-0000-0000-000000000031",
            AppTransactionRole.Seller,
            "AwaitingSellerAcceptance",
            Now.AddHours(8));
        var urgent = Item(
            "00000000-0000-0000-0000-000000000032",
            AppTransactionRole.Seller,
            "AwaitingSellerAcceptance",
            Now.AddHours(1));
        var shipping = Item(
            "00000000-0000-0000-0000-000000000033",
            AppTransactionRole.Seller,
            "PaidAwaitingShipment",
            Now.AddHours(12));

        var result = SellerWorkSummary.Create(
            [first, urgent, shipping],
            SellerWorkCategory.NewOffers);

        Assert.Equal(urgent.Id, result.Spotlight?.Id);
        Assert.Equal(
            [urgent.Id, first.Id],
            result.VisibleTransactions.Select(item => item.Id));
        Assert.Equal(
            [first.Id],
            result.RemainingTransactions.Select(item => item.Id));
    }

    [Fact]
    public void Equal_deadlines_use_newest_creation_then_id()
    {
        var older = Item(
            "00000000-0000-0000-0000-000000000041",
            AppTransactionRole.Seller,
            "AwaitingSellerAcceptance",
            Now.AddHours(1),
            Now.AddMinutes(-10));
        var newerHigherId = Item(
            "00000000-0000-0000-0000-000000000043",
            AppTransactionRole.Seller,
            "AwaitingSellerAcceptance",
            Now.AddHours(1),
            Now);
        var newerLowerId = Item(
            "00000000-0000-0000-0000-000000000042",
            AppTransactionRole.Seller,
            "AwaitingSellerAcceptance",
            Now.AddHours(1),
            Now);

        var result = SellerWorkSummary.Create(
            [older, newerHigherId, newerLowerId],
            SellerWorkCategory.NewOffers);

        Assert.Equal(
            [newerLowerId.Id, newerHigherId.Id, older.Id],
            result.VisibleTransactions.Select(item => item.Id));
    }

    [Fact]
    public void In_progress_and_problem_filters_sort_by_latest_update()
    {
        var olderProgress = Item(
            "00000000-0000-0000-0000-000000000053",
            AppTransactionRole.Seller,
            "InTransit",
            null,
            updatedAt: Now.AddHours(-4));
        var newerProgress = Item(
            "00000000-0000-0000-0000-000000000054",
            AppTransactionRole.Seller,
            "PayoutPending",
            null,
            updatedAt: Now.AddHours(-3));
        var olderProblem = Item(
            "00000000-0000-0000-0000-000000000051",
            AppTransactionRole.Seller,
            "Disputed",
            null,
            updatedAt: Now.AddHours(-2));
        var newerProblem = Item(
            "00000000-0000-0000-0000-000000000052",
            AppTransactionRole.Seller,
            "ResolutionPending",
            null,
            updatedAt: Now.AddHours(-1));

        var progress = SellerWorkSummary.Create(
            [olderProgress, newerProgress],
            SellerWorkCategory.InProgress);
        var problems = SellerWorkSummary.Create(
            [olderProblem, newerProblem],
            SellerWorkCategory.Problems);

        Assert.Equal(
            [newerProgress.Id, olderProgress.Id],
            progress.VisibleTransactions.Select(item => item.Id));
        Assert.Null(progress.Spotlight);
        Assert.Equal(
            [newerProblem.Id, olderProblem.Id],
            problems.VisibleTransactions.Select(item => item.Id));
        Assert.Null(problems.Spotlight);
    }

    [Fact]
    public void Provider_managed_overdue_shipment_stays_in_progress_without_spotlight()
    {
        var managedOverdue = Item(
            "00000000-0000-0000-0000-000000000061",
            AppTransactionRole.Seller,
            "ShipmentOverdue",
            Now.AddHours(-1),
            shippingManagedByProvider: true);

        var result = SellerWorkSummary.Create([managedOverdue]);

        Assert.Equal(
            TransactionAction.ViewStatus,
            managedOverdue.Presentation.PrimaryAction);
        Assert.Equal(
            SellerWorkCategory.InProgress,
            SellerWorkSummary.CategoryOf(managedOverdue));
        Assert.Null(result.Spotlight);
        Assert.Equal(
            [managedOverdue.Id],
            result.VisibleTransactions.Select(item => item.Id));
    }

    [Fact]
    public void Manual_tracking_correction_outranks_ordinary_paid_fulfillment()
    {
        var paid = Item(
            "00000000-0000-0000-0000-000000000071",
            AppTransactionRole.Seller,
            "PaidAwaitingShipment",
            Now.AddHours(1));
        var correction = Item(
            "00000000-0000-0000-0000-000000000072",
            AppTransactionRole.Seller,
            "TrackingUnverified",
            Now.AddHours(12));

        var result = SellerWorkSummary.Create([paid, correction]);

        Assert.Equal(correction.Id, result.Spotlight?.Id);
        Assert.Equal(
            [correction.Id, paid.Id],
            result.VisibleTransactions.Select(item => item.Id));
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
