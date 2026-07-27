using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class TransactionCollectionSynchronizerTests
{
    [Fact]
    public void UpdatesExistingItemsWithoutResettingTheCollection()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var target = new ObservableCollection<AppTransaction>(
        [
            Create(firstId, "เดิม 1"),
            Create(secondId, "เดิม 2")
        ]);
        var actions = new List<NotifyCollectionChangedAction>();
        target.CollectionChanged += (_, args) => actions.Add(args.Action);

        TransactionCollectionSynchronizer.Synchronize(
            target,
            [
                Create(firstId, "ใหม่ 1"),
                Create(secondId, "ใหม่ 2")
            ]);

        Assert.Equal(["ใหม่ 1", "ใหม่ 2"], target.Select(item => item.ProductName));
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
    }

    [Fact]
    public void ReordersAddsAndRemovesWithoutResettingTheCollection()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();
        var target = new ObservableCollection<AppTransaction>(
        [
            Create(firstId, "หนึ่ง"),
            Create(secondId, "สอง")
        ]);
        var actions = new List<NotifyCollectionChangedAction>();
        target.CollectionChanged += (_, args) => actions.Add(args.Action);

        TransactionCollectionSynchronizer.Synchronize(
            target,
            [
                Create(secondId, "สองใหม่"),
                Create(thirdId, "สาม")
            ]);

        Assert.Equal([secondId, thirdId], target.Select(item => item.Id));
        Assert.Equal("สองใหม่", target[0].ProductName);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
    }

    private static AppTransaction Create(Guid id, string productName) =>
        new(
            id,
            productName,
            100,
            "THB",
            AppTransactionRole.Buyer,
            AppFulfillmentType.Physical,
            "AwaitingSellerAcceptance",
            DateTimeOffset.UtcNow,
            null,
            "ผู้ขาย");
}
