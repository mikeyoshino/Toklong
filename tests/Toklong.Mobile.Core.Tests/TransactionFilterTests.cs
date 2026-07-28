using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class TransactionFilterTests
{
    [Fact]
    public void FilterCombinesRoleAndActionBucket()
    {
        var now = DateTimeOffset.Parse("2026-07-25T10:00:00+07:00");
        var source = new[]
        {
            Item(AppTransactionRole.Buyer, "SellerAcceptedAwaitingPayment", now.AddHours(3)),
            Item(AppTransactionRole.Seller, "PaidAwaitingShipment", now.AddHours(2)),
            Item(AppTransactionRole.Buyer, "InTransit", null)
        };

        var result = TransactionFilter.Apply(
            source,
            RoleFilter.Buying,
            BucketFilter.ActionRequired);

        var item = Assert.Single(result);
        Assert.Equal(AppTransactionRole.Buyer, item.Role);
        Assert.Equal("SellerAcceptedAwaitingPayment", item.State);
    }

    [Theory]
    [InlineData(AppTransactionRole.Buyer)]
    [InlineData(AppTransactionRole.Seller)]
    public void RoleListsAreOrderedFromNewestToOldest(
        AppTransactionRole role)
    {
        var now = DateTimeOffset.Parse("2026-07-25T10:00:00+07:00");
        var oldestAction = Item(
            role,
            "PaidAwaitingShipment",
            now.AddHours(10),
            now.AddHours(-3));
        var middleAction = Item(
            role,
            "SellerAcceptedAwaitingPayment",
            now.AddHours(2),
            now.AddHours(-2));
        var newestProgress = Item(
            role,
            "InTransit",
            null,
            now.AddHours(-1));

        var result = TransactionFilter.Apply(
            [oldestAction, newestProgress, middleAction],
            role == AppTransactionRole.Buyer
                ? RoleFilter.Buying
                : RoleFilter.Selling,
            BucketFilter.All);

        Assert.Equal(
            [newestProgress.Id, middleAction.Id, oldestAction.Id],
            result.Select(item => item.Id));
    }

    [Fact]
    public void SpotlightDoesNotFallBackToCompletedTransaction()
    {
        var completed = Item(
            AppTransactionRole.Buyer,
            "PayoutCompleted",
            null);

        var result = TransactionFilter.FindActionRequired([completed]);

        Assert.Null(result);
    }

    [Fact]
    public void SpotlightReturnsActionRequiredTransaction()
    {
        var progress = Item(
            AppTransactionRole.Buyer,
            "InTransit",
            null);
        var action = Item(
            AppTransactionRole.Seller,
            "PaidAwaitingShipment",
            DateTimeOffset.Parse("2026-07-26T10:00:00+07:00"));

        var result = TransactionFilter.FindActionRequired([progress, action]);

        Assert.Equal(action.Id, result?.Id);
    }

    [Fact]
    public void BuyerModeFiltersActionProgressAndCompletedSeparately()
    {
        var action = Item(
            AppTransactionRole.Buyer,
            "SellerAcceptedAwaitingPayment",
            null);
        var progress = Item(
            AppTransactionRole.Buyer,
            "InTransit",
            null);
        var completed = Item(
            AppTransactionRole.Buyer,
            "PaidOut",
            null);
        var seller = Item(
            AppTransactionRole.Seller,
            "PaidAwaitingShipment",
            null);
        var source = new[]
        {
            action,
            progress,
            completed,
            seller
        };

        Assert.Equal(
            action.Id,
            Assert.Single(TransactionFilter.Apply(
                source,
                RoleFilter.Buying,
                BucketFilter.ActionRequired)).Id);
        Assert.Equal(
            progress.Id,
            Assert.Single(TransactionFilter.Apply(
                source,
                RoleFilter.Buying,
                BucketFilter.InProgress)).Id);
        Assert.Equal(
            completed.Id,
            Assert.Single(TransactionFilter.Apply(
                source,
                RoleFilter.Buying,
                BucketFilter.Completed)).Id);
    }

    private static AppTransaction Item(
        AppTransactionRole role,
        string state,
        DateTimeOffset? deadline,
        DateTimeOffset? createdAt = null)
    {
        var timestamp = createdAt ??
            DateTimeOffset.Parse("2026-07-25T09:00:00+07:00");
        return
        new(
            Guid.NewGuid(),
            "สินค้า",
            10000,
            "THB",
            role,
            AppFulfillmentType.Physical,
            state,
            timestamp,
            deadline,
            "คู่รายการ",
            CreatedAt: timestamp);
    }
}
