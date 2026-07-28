using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class SellerWorkspaceStateTests
{
    [Fact]
    public void Empty_selected_category_returns_to_all_after_successful_refresh()
    {
        var state = new SellerWorkspaceState();
        state.ReplaceSuccessful([Item("AwaitingSellerAcceptance")]);
        state.Select(SellerWorkCategory.NewOffers);

        state.ReplaceSuccessful([Item("InTransit")]);

        Assert.Equal(SellerWorkCategory.All, state.SelectedCategory);
        Assert.Equal(1, state.Snapshot.InProgressCount);
    }

    [Fact]
    public void State_changes_only_on_successful_replacement()
    {
        var state = new SellerWorkspaceState();
        state.ReplaceSuccessful([Item("AwaitingSellerAcceptance")]);
        var before = state.Snapshot;

        state.MarkLoadFailed();

        Assert.Same(before, state.Snapshot);
        Assert.True(state.HasSuccessfulLoad);
        Assert.Equal("อัปเดตล่าสุดไม่สำเร็จ", state.LoadErrorText);
    }

    [Fact]
    public void Initial_failure_exposes_no_false_zero_summary()
    {
        var state = new SellerWorkspaceState();

        state.MarkLoadFailed();

        Assert.False(state.HasSuccessfulLoad);
        Assert.False(state.HasVisibleSummary);
        Assert.Equal(
            "โหลดรายการไม่สำเร็จ · ลองอีกครั้ง",
            state.LoadErrorText);
    }

    [Fact]
    public void Successful_empty_refresh_clears_prior_records_and_error()
    {
        var state = new SellerWorkspaceState();
        state.ReplaceSuccessful([Item("AwaitingSellerAcceptance")]);
        state.MarkLoadFailed();

        state.ReplaceSuccessful([]);

        Assert.True(state.HasSuccessfulLoad);
        Assert.False(state.HasVisibleSummary);
        Assert.Empty(state.Transactions);
        Assert.Equal("", state.LoadErrorText);
    }

    [Fact]
    public void Home_and_transaction_consumers_get_identical_counts()
    {
        var source = new[]
        {
            Item("AwaitingSellerAcceptance"),
            Item("PaidAwaitingShipment"),
            Item("InTransit"),
            Item("Disputed"),
            Item("PaidOut")
        };
        var home = new SellerWorkspaceState();
        var transactions = new SellerWorkspaceState();

        home.ReplaceSuccessful(source);
        transactions.ReplaceSuccessful(source);
        transactions.Select(SellerWorkCategory.NewOffers);

        Assert.Equal(
            (
                home.Snapshot.TotalCount,
                home.Snapshot.NewOfferCount,
                home.Snapshot.FulfillmentRequiredCount,
                home.Snapshot.InProgressCount,
                home.Snapshot.ProblemCount,
                home.Snapshot.ActionableCount
            ),
            (
                transactions.Snapshot.TotalCount,
                transactions.Snapshot.NewOfferCount,
                transactions.Snapshot.FulfillmentRequiredCount,
                transactions.Snapshot.InProgressCount,
                transactions.Snapshot.ProblemCount,
                transactions.Snapshot.ActionableCount
            ));
    }

    private static AppTransaction Item(string state) =>
        new(
            Guid.NewGuid(),
            "สินค้า",
            100_00,
            "THB",
            AppTransactionRole.Seller,
            AppFulfillmentType.Physical,
            state,
            DateTimeOffset.Parse("2026-07-28T15:00:00+07:00"),
            DateTimeOffset.Parse("2026-07-29T15:00:00+07:00"),
            "ผู้ซื้อ",
            ItemPriceSatang: 100_00,
            CreatedAt:
                DateTimeOffset.Parse("2026-07-28T14:00:00+07:00"));
}
