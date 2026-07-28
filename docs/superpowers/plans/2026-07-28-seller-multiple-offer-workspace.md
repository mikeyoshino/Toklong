# Seller Multiple-Offer Workspace Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give a seller with multiple offers and active sales exact counts, risk-ordered work, one-tap filters, a conditional problem alert, and home-page badges without changing transaction truth or authorization.

**Architecture:** Add a pure mobile-core `SellerWorkSummary` classifier and a small `SellerWorkspaceState` owner so XAML and view models never interpret raw transaction state independently. Both the authenticated home and transaction list continue reading the existing authenticated `/transactions` response, then project it through the same summary rules. UI analytics flow through a provider-neutral, PII-free logging boundary; no API, database, payment, shipment, dispute, or payout state changes are introduced.

**Tech Stack:** .NET 10, C# 14, .NET MAUI XAML, xUnit 2.9, `System.Text.Json`, `Microsoft.Extensions.Logging`

## Global Constraints

- Work directly on `main`; the user explicitly rejected a worktree for this repository.
- Preserve unrelated uncommitted payment-retry and simulator-session changes already present in the worktree.
- New transaction initiation remains buyer-first; do not add seller-created links, marketplace discovery, bidding, storefronts, chat, bulk actions, wallets, crypto, or stored value.
- `ข้อเสนอใหม่` means only a buyer-created offer whose intended seller has not responded; after acceptance call it `รายการขาย`.
- Seller amounts show item price or seller proceeds only; never expose buyer protection fee or buyer total.
- Seller fulfillment remains unavailable until provider-confirmed payment.
- Disputes remain payout-blocking and AI makes no binding financial decision.
- Money remains integer satang with ISO currency; this feature performs no money arithmetic.
- The seller spotlight priority is overdue/correction, paid fulfillment deadline, offer response deadline, then other offers.
- Exact ship-by and response deadlines remain visible as date and time.
- Initial load failure must not display false zero counts; refresh failure must preserve the last successful data.
- No new API endpoint, database field, transaction state, webhook behavior, audit event, or financial calculation.
- Analytics properties must not include phone numbers, names, product text, transaction tokens, payment references, addresses, or provider credentials.
- Use one primary action per record and retain mobile-first accessibility at supported text sizes.

---

## File Map

### New files

- `src/Toklong.Mobile/Core/SellerWorkSummary.cs` — state classification, exact counts, category filtering, and deterministic spotlight priority.
- `src/Toklong.Mobile/Core/SellerWorkspaceState.cs` — selected seller category and last successful transaction snapshot.
- `src/Toklong.Mobile/Core/IMobileAnalytics.cs` — PII-free presentation-event contract and seller event factories.
- `src/Toklong.Mobile/Services/LoggingMobileAnalytics.cs` — structured local logging sink for presentation analytics.
- `src/Toklong.Mobile/ViewModels/AuthenticatedHomeViewModel.cs` — home seller counts, retained successful data, and retry/error state.
- `tests/Toklong.Mobile.Core.Tests/SellerWorkSummaryTests.cs` — classification, counts, filtering, and priority.
- `tests/Toklong.Mobile.Core.Tests/SellerWorkspaceStateTests.cs` — selected-category and retained-data behavior.
- `tests/Toklong.Mobile.Core.Tests/MobileAnalyticsEventTests.cs` — exact safe analytics payloads.

### Modified files

- `src/Toklong.Mobile/ViewModels/TransactionsViewModel.cs` — delegate seller filtering and spotlight to the core state, preserve stale data on refresh errors, expose commands/counts/banner.
- `src/Toklong.Mobile/Pages/TransactionsPage.xaml` — seller summary tiles, selected treatment, problem banner, purple spotlight, and compact remaining records.
- `src/Toklong.Mobile/Pages/AuthenticatedHomePage.xaml` — new-offer pill, actionable-count line, loading/error copy.
- `src/Toklong.Mobile/Pages/AuthenticatedHomePage.xaml.cs` — injected view model and appearance refresh.
- `src/Toklong.Mobile/MauiProgram.cs` — register analytics and authenticated-home view model.
- `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs` — layout, copy, visibility, command, and accessibility contracts.
- `tests/Toklong.Mobile.Core.Tests/TransactionFilterTests.cs` — keep buyer filtering coverage while seller behavior moves to `SellerWorkSummary`.

---

### Task 1: Seller Work Classification and Counts

**Files:**
- Create: `src/Toklong.Mobile/Core/SellerWorkSummary.cs`
- Create: `tests/Toklong.Mobile.Core.Tests/SellerWorkSummaryTests.cs`

**Interfaces:**
- Consumes: `AppTransaction`, `AppTransactionRole`, `TransactionAction`, `TransactionBucket`.
- Produces:
  - `SellerWorkCategory { All, NewOffers, FulfillmentRequired, InProgress, Problems }`
  - `SellerWorkSnapshot`
  - `SellerWorkSummary.Create(IEnumerable<AppTransaction>, SellerWorkCategory selectedCategory = SellerWorkCategory.All)`

- [ ] **Step 1: Write failing classification and count tests**

Create `SellerWorkSummaryTests.cs` with a fixed fixture. Use literal GUIDs and
timestamps so tie-breaking is independently verifiable:

```csharp
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
```

- [ ] **Step 2: Run the focused tests and confirm RED**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter FullyQualifiedName~SellerWorkSummaryTests --no-restore
```

Expected: compilation fails because `SellerWorkSummary`,
`SellerWorkCategory`, and `SellerWorkSnapshot` do not exist.

- [ ] **Step 3: Implement category precedence and count projection**

Create `SellerWorkSummary.cs`:

```csharp
namespace Toklong.Mobile.Core;

public enum SellerWorkCategory
{
    All,
    NewOffers,
    FulfillmentRequired,
    InProgress,
    Problems
}

public sealed record SellerWorkSnapshot(
    int TotalCount,
    int NewOfferCount,
    int FulfillmentRequiredCount,
    int InProgressCount,
    int ProblemCount,
    int ActionableCount,
    SellerWorkCategory SelectedCategory,
    AppTransaction? Spotlight,
    IReadOnlyList<AppTransaction> AllSellerTransactions,
    IReadOnlyList<AppTransaction> VisibleTransactions,
    IReadOnlyList<AppTransaction> RemainingTransactions);

public static class SellerWorkSummary
{
    public static SellerWorkCategory? CategoryOf(AppTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.Role != AppTransactionRole.Seller)
            return null;
        if (transaction.State is "Disputed" or "ResolutionPending")
            return SellerWorkCategory.Problems;

        return transaction.Presentation.PrimaryAction switch
        {
            TransactionAction.ReviewSellerOffer =>
                SellerWorkCategory.NewOffers,
            TransactionAction.AddTracking or
                TransactionAction.ConfirmDigitalHandoff =>
                SellerWorkCategory.FulfillmentRequired,
            _ when transaction.Presentation.Bucket ==
                   TransactionBucket.Completed =>
                null,
            _ => SellerWorkCategory.InProgress
        };
    }

    public static SellerWorkSnapshot Create(
        IEnumerable<AppTransaction> source,
        SellerWorkCategory selectedCategory =
            SellerWorkCategory.All)
    {
        ArgumentNullException.ThrowIfNull(source);
        var seller = source
            .Where(item => item.Role == AppTransactionRole.Seller)
            .ToArray();
        var categorized = seller
            .Select(item => (Item: item, Category: CategoryOf(item)))
            .ToArray();
        var newOfferCount = categorized.Count(
            value => value.Category == SellerWorkCategory.NewOffers);
        var fulfillmentCount = categorized.Count(
            value => value.Category ==
                     SellerWorkCategory.FulfillmentRequired);
        var inProgressCount = categorized.Count(
            value => value.Category == SellerWorkCategory.InProgress);
        var problemCount = categorized.Count(
            value => value.Category == SellerWorkCategory.Problems);
        var visible = selectedCategory == SellerWorkCategory.All
            ? seller
            : categorized
                .Where(value => value.Category == selectedCategory)
                .Select(value => value.Item)
                .ToArray();

        return new SellerWorkSnapshot(
            seller.Length,
            newOfferCount,
            fulfillmentCount,
            inProgressCount,
            problemCount,
            newOfferCount + fulfillmentCount,
            selectedCategory,
            null,
            seller,
            visible,
            visible);
    }
}
```

Keep history in `AllSellerTransactions`; history contributes to `TotalCount`
but has no active category.

- [ ] **Step 4: Run focused tests and confirm GREEN**

Run the Step 2 command.

Expected: all `SellerWorkSummaryTests` pass.

- [ ] **Step 5: Commit the classification slice**

```bash
git add \
  src/Toklong.Mobile/Core/SellerWorkSummary.cs \
  tests/Toklong.Mobile.Core.Tests/SellerWorkSummaryTests.cs
git commit -m "feat: classify seller work summary"
```

---

### Task 2: Risk Priority, Filters, and Retained Seller State

**Files:**
- Modify: `src/Toklong.Mobile/Core/SellerWorkSummary.cs`
- Create: `src/Toklong.Mobile/Core/SellerWorkspaceState.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/SellerWorkSummaryTests.cs`
- Create: `tests/Toklong.Mobile.Core.Tests/SellerWorkspaceStateTests.cs`

**Interfaces:**
- Consumes: Task 1 `SellerWorkSummary.Create` and `SellerWorkCategory`.
- Produces:
  - risk-ordered `SellerWorkSnapshot.Spotlight`;
  - risk-ordered `VisibleTransactions`;
  - `SellerWorkspaceState.ReplaceSuccessful(...)`;
  - `SellerWorkspaceState.MarkLoadFailed()`;
  - `SellerWorkspaceState.Select(...)`;
  - first-load and stale-refresh error state without replacing successful data.

- [ ] **Step 1: Add failing priority and filter tests**

Append:

```csharp
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
```

Create `SellerWorkspaceStateTests.cs`:

```csharp
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class SellerWorkspaceStateTests
{
    [Fact]
    public void Empty_selected_category_returns_to_all_after_successful_refresh()
    {
        var state = new SellerWorkspaceState();
        state.ReplaceSuccessful([
            Item("AwaitingSellerAcceptance")
        ]);
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
```

- [ ] **Step 2: Run focused tests and confirm RED**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~SellerWorkSummaryTests|FullyQualifiedName~SellerWorkspaceStateTests" \
  --no-restore
```

Expected: priority assertions fail because `Spotlight` is null, and compilation
fails because `SellerWorkspaceState` does not exist.

- [ ] **Step 3: Implement deterministic ordering**

In `SellerWorkSummary.Create`, order `visible` with:

```csharp
static int Priority(AppTransaction item) =>
    item.State is "ShipmentOverdue" ||
    (item.State == "TrackingUnverified" &&
     item.Presentation.PrimaryAction == TransactionAction.AddTracking)
        ? 0
        : item.Presentation.PrimaryAction is
            TransactionAction.AddTracking or
            TransactionAction.ConfirmDigitalHandoff
            ? 1
            : item.Presentation.PrimaryAction ==
              TransactionAction.ReviewSellerOffer
                ? 2
                : 3;

var ordered = visible
    .OrderBy(Priority)
    .ThenBy(item =>
        Priority(item) < 3
            ? item.ActionDeadline ?? DateTimeOffset.MaxValue
            : DateTimeOffset.MaxValue)
    .ThenByDescending(item =>
        Priority(item) == 3
            ? item.UpdatedAt
            : item.CreatedAt)
    .ThenBy(item => item.Id)
    .ToArray();
var spotlight = ordered.FirstOrDefault(item =>
    item.Presentation.PrimaryAction is
        TransactionAction.ReviewSellerOffer or
        TransactionAction.AddTracking or
        TransactionAction.ConfirmDigitalHandoff);
var remaining = spotlight is null
    ? ordered
    : ordered.Where(item => item.Id != spotlight.Id).ToArray();
```

Replace the final Task 1 constructor arguments
`null, seller, visible, visible` with
`spotlight, seller, ordered, remaining`. Do not elevate
`Disputed`, `ResolutionPending`, waiting, or completed records.
This makes new offers and fulfillment deadline-first, while in-progress and
problem-only views use their most recent verified/activity timestamp.

- [ ] **Step 4: Implement selected-category state**

Create:

```csharp
namespace Toklong.Mobile.Core;

public sealed class SellerWorkspaceState
{
    private IReadOnlyList<AppTransaction> transactions = [];

    public bool HasSuccessfulLoad { get; private set; }
    public bool HasVisibleSummary =>
        HasSuccessfulLoad && Snapshot.TotalCount > 0;
    public string LoadErrorText { get; private set; } = "";
    public bool HasLoadError =>
        !string.IsNullOrWhiteSpace(LoadErrorText);
    public SellerWorkCategory SelectedCategory { get; private set; }
        = SellerWorkCategory.All;
    public SellerWorkSnapshot Snapshot { get; private set; }
        = SellerWorkSummary.Create([]);
    public IReadOnlyList<AppTransaction> Transactions => transactions;

    public void Select(SellerWorkCategory category)
    {
        SelectedCategory = category;
        Rebuild();
    }

    public void ReplaceSuccessful(
        IReadOnlyList<AppTransaction> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        transactions = value;
        HasSuccessfulLoad = true;
        LoadErrorText = "";
        Rebuild();
        if (SelectedCategory != SellerWorkCategory.All &&
            Snapshot.VisibleTransactions.Count == 0)
        {
            SelectedCategory = SellerWorkCategory.All;
            Rebuild();
        }
    }

    public void MarkLoadFailed() =>
        LoadErrorText = HasSuccessfulLoad
            ? "อัปเดตล่าสุดไม่สำเร็จ"
            : "โหลดรายการไม่สำเร็จ · ลองอีกครั้ง";

    private void Rebuild() =>
        Snapshot = SellerWorkSummary.Create(
            transactions,
            SelectedCategory);
}
```

- [ ] **Step 5: Run focused and existing filter tests**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~SellerWorkSummaryTests|FullyQualifiedName~SellerWorkspaceStateTests|FullyQualifiedName~TransactionFilterTests" \
  --no-restore
```

Expected: all focused tests pass. Existing buyer `TransactionFilter` behavior
remains green.

- [ ] **Step 6: Commit the priority/state slice**

```bash
git add \
  src/Toklong.Mobile/Core/SellerWorkSummary.cs \
  src/Toklong.Mobile/Core/SellerWorkspaceState.cs \
  tests/Toklong.Mobile.Core.Tests/SellerWorkSummaryTests.cs \
  tests/Toklong.Mobile.Core.Tests/SellerWorkspaceStateTests.cs
git commit -m "feat: prioritize seller workspace"
```

---

### Task 3: PII-Free Seller Presentation Analytics

**Files:**
- Create: `src/Toklong.Mobile/Core/IMobileAnalytics.cs`
- Create: `src/Toklong.Mobile/Services/LoggingMobileAnalytics.cs`
- Create: `tests/Toklong.Mobile.Core.Tests/MobileAnalyticsEventTests.cs`
- Modify: `src/Toklong.Mobile/MauiProgram.cs`

**Interfaces:**
- Produces:
  - `MobileAnalyticsEvent(string Name, IReadOnlyDictionary<string, string> Properties)`
  - `IMobileAnalytics.Track(MobileAnalyticsEvent value)`
  - `SellerWorkspaceAnalytics.FilterSelected(...)`
  - `SellerWorkspaceAnalytics.SpotlightOpened(...)`
  - `SellerWorkspaceAnalytics.ProblemBannerOpened(...)`
  - `SellerWorkspaceAnalytics.HomeOpened(...)`

- [ ] **Step 1: Write failing exact-payload tests**

```csharp
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class MobileAnalyticsEventTests
{
    [Fact]
    public void Seller_events_contain_only_approved_aggregate_properties()
    {
        var filter = SellerWorkspaceAnalytics.FilterSelected(
            SellerWorkCategory.NewOffers,
            3);
        var spotlight = SellerWorkspaceAnalytics.SpotlightOpened(
            TransactionAction.ReviewSellerOffer,
            "AwaitingSellerAcceptance");
        var problem = SellerWorkspaceAnalytics.ProblemBannerOpened(2);
        var home = SellerWorkspaceAnalytics.HomeOpened(3, 4);
        var unknownState = SellerWorkspaceAnalytics.SpotlightOpened(
            TransactionAction.AddTracking,
            "unexpected product text");

        Assert.Equal("seller_summary_filter_selected", filter.Name);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["category"] = "NewOffers",
                ["visible_count"] = "3"
            },
            filter.Properties);
        Assert.Equal("seller_spotlight_opened", spotlight.Name);
        Assert.Equal("ReviewSellerOffer", spotlight.Properties["action"]);
        Assert.Equal(
            "AwaitingSellerAcceptance",
            spotlight.Properties["state"]);
        Assert.Equal("2", problem.Properties["visible_count"]);
        Assert.Equal("3", home.Properties["new_offer_count"]);
        Assert.Equal("4", home.Properties["actionable_count"]);
        Assert.Equal("Unknown", unknownState.Properties["state"]);
    }
}
```

- [ ] **Step 2: Run the analytics test and confirm RED**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter FullyQualifiedName~MobileAnalyticsEventTests --no-restore
```

Expected: compilation fails because the analytics types do not exist.

- [ ] **Step 3: Implement exact event factories**

Create `IMobileAnalytics.cs`:

```csharp
namespace Toklong.Mobile.Core;

public sealed record MobileAnalyticsEvent(
    string Name,
    IReadOnlyDictionary<string, string> Properties);

public interface IMobileAnalytics
{
    void Track(MobileAnalyticsEvent value);
}

public static class SellerWorkspaceAnalytics
{
    public static MobileAnalyticsEvent FilterSelected(
        SellerWorkCategory category,
        int visibleCount) =>
        Event(
            "seller_summary_filter_selected",
            ("category", category.ToString()),
            ("visible_count", visibleCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture)));

    public static MobileAnalyticsEvent SpotlightOpened(
        TransactionAction action,
        string state) =>
        Event(
            "seller_spotlight_opened",
            ("action", action.ToString()),
            ("state", SafeSpotlightState(state)));

    public static MobileAnalyticsEvent ProblemBannerOpened(int count) =>
        Event(
            "seller_problem_banner_opened",
            ("visible_count", count.ToString(
                System.Globalization.CultureInfo.InvariantCulture)));

    public static MobileAnalyticsEvent HomeOpened(
        int newOfferCount,
        int actionableCount) =>
        Event(
            "seller_home_opened",
            ("new_offer_count", newOfferCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture)),
            ("actionable_count", actionableCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture)));

    private static MobileAnalyticsEvent Event(
        string name,
        params (string Key, string Value)[] properties) =>
        new(
            name,
            properties.ToDictionary(
                property => property.Key,
                property => property.Value,
                StringComparer.Ordinal));

    private static string SafeSpotlightState(string state) =>
        state switch
        {
            "AwaitingSellerAcceptance" or
            "PaidAwaitingShipment" or
            "PaidAwaitingDigitalDelivery" or
            "TrackingUnverified" or
            "ShipmentOverdue" => state,
            _ => "Unknown"
        };
}
```

- [ ] **Step 4: Add the logging sink and DI registration**

Create:

```csharp
using Microsoft.Extensions.Logging;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Services;

public sealed class LoggingMobileAnalytics(
    ILogger<LoggingMobileAnalytics> logger) : IMobileAnalytics
{
    public void Track(MobileAnalyticsEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        logger.LogInformation(
            "Mobile analytics {EventName} {@Properties}",
            value.Name,
            value.Properties);
    }
}
```

Register in `MauiProgram.cs`:

```csharp
builder.Services.AddSingleton<
    IMobileAnalytics,
    LoggingMobileAnalytics>();
```

Do not log an `AppTransaction`, request object, or user profile.
The repository currently has no remote analytics provider abstraction, so this
structured logging sink is the first provider-neutral recording boundary; do
not claim durable remote delivery. Connecting an approved analytics provider
remains a separate product/privacy decision.

- [ ] **Step 5: Run focused tests and compile the iOS target**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter FullyQualifiedName~MobileAnalyticsEventTests --no-restore
dotnet msbuild src/Toklong.Mobile/Toklong.Mobile.csproj \
  -t:Compile \
  -p:TargetFrameworks=net10.0-ios \
  -p:TargetFramework=net10.0-ios \
  -p:RuntimeIdentifier=iossimulator-arm64 \
  -p:NuGetAudit=false
```

Expected: test and compile pass. No analytics property contains personal or
transaction-identifying data.

- [ ] **Step 6: Commit the analytics slice**

```bash
git add \
  src/Toklong.Mobile/Core/IMobileAnalytics.cs \
  src/Toklong.Mobile/Services/LoggingMobileAnalytics.cs \
  tests/Toklong.Mobile.Core.Tests/MobileAnalyticsEventTests.cs
git add -p src/Toklong.Mobile/MauiProgram.cs
git diff --cached -- src/Toklong.Mobile/MauiProgram.cs
git commit -m "feat: add seller workspace analytics"
```

For the patch prompt, stage only the `IMobileAnalytics` registration hunk.
Reject the pre-existing simulator-session registration hunk.

---

### Task 4: Seller Transaction Workspace View Model and UI

**Files:**
- Modify: `src/Toklong.Mobile/ViewModels/TransactionsViewModel.cs`
- Modify: `src/Toklong.Mobile/Pages/TransactionsPage.xaml`
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/TransactionFilterTests.cs`

**Interfaces:**
- Consumes: `SellerWorkspaceState`, `SellerWorkCategory`,
  `SellerWorkspaceAnalytics`, existing `ITransactionService`.
- Produces XAML bindings:
  - `HasSellerSummary`, `SellerTotalText`;
  - `NewOfferCountText`, `FulfillmentCountText`, `InProgressCountText`;
  - `NewOfferSemanticText`, `FulfillmentSemanticText`,
    `InProgressSemanticText`;
  - `HasSellerProblems`, `SellerProblemText`;
  - `SpotlightAmountText`, `SellerPriorityExplanation`;
  - `IsSellerNewOffersSelected`, `IsSellerFulfillmentSelected`,
    `IsSellerInProgressSelected`;
  - `SelectSellerNewOffersCommand`, `SelectSellerFulfillmentCommand`,
    `SelectSellerInProgressCommand`, `SelectSellerProblemsCommand`,
    `SelectAllSellerWorkCommand`.

- [ ] **Step 1: Write failing seller-workspace XAML contracts**

Append a `SellerWorkspace_ShowsSummaryProblemAndPriorityContracts` test to
`UiLayoutConsistencyTests.cs`:

```csharp
[Fact]
public void SellerWorkspace_ShowsSummaryProblemAndPriorityContracts()
{
    var page = Load("Ui", "Pages", "TransactionsPage.xaml");
    var summary = page.Descendants(Maui + "Grid")
        .Single(element =>
            AttributeValue(element, "AutomationId") ==
            "SellerWorkSummary");
    var summaryButtons = summary.Descendants(Maui + "Button").ToArray();
    var problem = page.Descendants(Maui + "Border")
        .Single(element =>
            AttributeValue(element, "AutomationId") ==
            "SellerProblemBanner");
    var spotlight = page.Descendants(Maui + "Border")
        .Single(element =>
            AttributeValue(element, "AutomationId") ==
            "ActionSpotlightCard");

    Assert.Equal("{Binding IsSelling}",
        AttributeValue(summary, "IsVisible"));
    Assert.Contains(summaryButtons, button =>
        AttributeValue(button, "Command") ==
            "{Binding SelectSellerNewOffersCommand}" &&
        AttributeValue(button, "SemanticProperties.Description") ==
            "{Binding NewOfferSemanticText}");
    Assert.Contains(summaryButtons, button =>
        AttributeValue(button, "Command") ==
            "{Binding SelectSellerFulfillmentCommand}");
    Assert.Contains(summaryButtons, button =>
        AttributeValue(button, "Command") ==
            "{Binding SelectSellerInProgressCommand}");
    Assert.Equal("{Binding HasSellerProblems}",
        AttributeValue(problem, "IsVisible"));
    Assert.Contains(problem.Descendants(Maui + "Button"), button =>
        AttributeValue(button, "Command") ==
            "{Binding SelectSellerProblemsCommand}");
    Assert.Contains(
        spotlight.Descendants(Maui + "GradientStop"),
        stop => AttributeValue(stop, "Color") ==
            "{Binding SpotlightTransaction.RoleHeaderStart}");
}
```

Add assertions that no seller summary or spotlight label binds
`BuyerProtectionFeeText` or `FormattedAmount` when `ItemPriceText` is the
approved seller amount binding. Also assert:

- the selected tile has a non-color selected semantic/state binding;
- the spotlight includes visible priority-reason and exact-deadline text;
- the remaining list uses the compact seller card template and excludes the
  spotlight transaction in the view-model testable projection; and
- red, amber, purple, and blue surfaces each retain a visible text label so
  color is never the only indicator.

- [ ] **Step 2: Run the UI contract and confirm RED**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter FullyQualifiedName~SellerWorkspace_ShowsSummaryProblemAndPriorityContracts \
  --no-restore
```

Expected: FAIL because `SellerWorkSummary` and `SellerProblemBanner` are absent
and spotlight colors are hard-coded buyer blue.

- [ ] **Step 3: Delegate seller state in `TransactionsViewModel`**

Add fields:

```csharp
private readonly SellerWorkspaceState sellerState = new();
private readonly IMobileAnalytics analytics;
```

Inject `IMobileAnalytics`. Replace seller filter commands with the exact
commands listed in Interfaces. The selection helper must be:

```csharp
SelectSellerNewOffersCommand = new Command(
    () => SelectSellerWork(SellerWorkCategory.NewOffers));
SelectSellerFulfillmentCommand = new Command(
    () => SelectSellerWork(SellerWorkCategory.FulfillmentRequired));
SelectSellerInProgressCommand = new Command(
    () => SelectSellerWork(SellerWorkCategory.InProgress));
SelectSellerProblemsCommand = new Command(
    () =>
    {
        SelectSellerWork(SellerWorkCategory.Problems);
        analytics.Track(SellerWorkspaceAnalytics.ProblemBannerOpened(
            sellerState.Snapshot.ProblemCount));
    });
SelectAllSellerWorkCommand = new Command(
    () => SelectSellerWork(SellerWorkCategory.All));

private void SelectSellerWork(SellerWorkCategory category)
{
    sellerState.Select(category);
    ApplyFilter();
    var snapshot = sellerState.Snapshot;
    analytics.Track(SellerWorkspaceAnalytics.FilterSelected(
        snapshot.SelectedCategory,
        snapshot.VisibleTransactions.Count));
    RaiseSellerSummaryProperties();
}
```

On successful load:

```csharp
var loaded = await transactionService.GetTransactionsAsync();
allTransactions = loaded;
sellerState.ReplaceSuccessful(loaded);
ApplyFilter();
RaiseSellerSummaryProperties();
```

On failure:

```csharp
sellerState.MarkLoadFailed();
if (!sellerState.HasSuccessfulLoad)
{
    allTransactions = [];
    ApplyFilter();
}
ErrorText = sellerState.LoadErrorText;
```

Do not clear a last successful collection on refresh failure.

In seller mode, `ApplyFilter()` uses `sellerState.Snapshot`; spotlight is its
`Spotlight`, and the collection receives `RemainingTransactions`. Buyer mode
continues using existing `TransactionFilter`.

Expose literal count/semantic properties:

```csharp
public bool HasSellerSummary =>
    IsSelling && sellerState.HasVisibleSummary;
public string SellerTotalText =>
    $"รายการขายทั้งหมด {sellerState.Snapshot.TotalCount} รายการ";
public string NewOfferCountText =>
    sellerState.Snapshot.NewOfferCount.ToString();
public string FulfillmentCountText =>
    sellerState.Snapshot.FulfillmentRequiredCount.ToString();
public string InProgressCountText =>
    sellerState.Snapshot.InProgressCount.ToString();
public bool IsSellerNewOffersSelected =>
    sellerState.SelectedCategory == SellerWorkCategory.NewOffers;
public bool IsSellerFulfillmentSelected =>
    sellerState.SelectedCategory ==
    SellerWorkCategory.FulfillmentRequired;
public bool IsSellerInProgressSelected =>
    sellerState.SelectedCategory == SellerWorkCategory.InProgress;
public string NewOfferSemanticText =>
    SellerSemanticText(
        "ข้อเสนอใหม่",
        NewOfferCountText,
        IsSellerNewOffersSelected);
public string FulfillmentSemanticText =>
    SellerSemanticText(
        "ต้องส่ง",
        FulfillmentCountText,
        IsSellerFulfillmentSelected);
public string InProgressSemanticText =>
    SellerSemanticText(
        "กำลังไปต่อ",
        InProgressCountText,
        IsSellerInProgressSelected);
public bool HasSellerProblems =>
    IsSelling && sellerState.Snapshot.ProblemCount > 0;
public string SellerProblemText =>
    $"มี {sellerState.Snapshot.ProblemCount} รายการแจ้งปัญหา · " +
    "ยอดรับหยุดไว้ระหว่างตรวจสอบ";
public string SpotlightAmountText =>
    SpotlightTransaction is null
        ? ""
        : IsSelling
            ? SpotlightTransaction.ItemPriceText
            : SpotlightTransaction.FormattedAmount;
public string SellerPriorityExplanation =>
    sellerState.SelectedCategory switch
    {
        SellerWorkCategory.NewOffers => "ใกล้หมดเวลาตอบก่อน",
        SellerWorkCategory.FulfillmentRequired => "เร่งส่งก่อน",
        SellerWorkCategory.InProgress => "อัปเดตล่าสุดก่อน",
        SellerWorkCategory.Problems => "ปัญหาล่าสุดก่อน",
        _ => "เรียงตามสิ่งที่ต้องทำก่อน"
    };

private static string SellerSemanticText(
    string label,
    string count,
    bool selected) =>
    $"{label} {count} รายการ" +
    (selected ? " เลือกอยู่" : "");
```

Add the concrete notification helper:

```csharp
private void RaiseSellerSummaryProperties()
{
    OnPropertyChanged(nameof(HasSellerSummary));
    OnPropertyChanged(nameof(SellerTotalText));
    OnPropertyChanged(nameof(NewOfferCountText));
    OnPropertyChanged(nameof(FulfillmentCountText));
    OnPropertyChanged(nameof(InProgressCountText));
    OnPropertyChanged(nameof(NewOfferSemanticText));
    OnPropertyChanged(nameof(FulfillmentSemanticText));
    OnPropertyChanged(nameof(InProgressSemanticText));
    OnPropertyChanged(nameof(HasSellerProblems));
    OnPropertyChanged(nameof(SellerProblemText));
    OnPropertyChanged(nameof(IsSellerNewOffersSelected));
    OnPropertyChanged(nameof(IsSellerFulfillmentSelected));
    OnPropertyChanged(nameof(IsSellerInProgressSelected));
    OnPropertyChanged(nameof(SpotlightAmountText));
    OnPropertyChanged(nameof(SellerPriorityExplanation));
}
```

Also raise `SpotlightAmountText` inside the existing
`SpotlightTransaction` setter, and call `RaiseSellerSummaryProperties()` from
`RaiseFilterProperties()` so buy/sell mode changes update visibility and
semantics.

`OpenTransactionAsync` records `seller_spotlight_opened` only when the opened
item equals the current seller spotlight. The problem command records
`seller_problem_banner_opened`.

- [ ] **Step 4: Replace seller chips with the approved summary UI**

In `TransactionsPage.xaml`:

- keep buyer filters unchanged;
- replace `SellerStatusFilters` with a three-column `Grid`
  `AutomationId="SellerWorkSummary"`;
- each tile is one `Button` with a nested visual avoided; use a `Border` +
  transparent `Button` overlay when count and label need separate font sizes;
- bind count, semantic description, command, and selected trigger;
- add `SellerProblemBanner` between summary and spotlight;
- bind spotlight gradient to the role header colors;
- bind seller spotlight amount to `ItemPriceText` through a view-model
  `SpotlightAmountText` property, while buyer mode retains `FormattedAmount`;
- use exact Thai labels `รอตอบ`, `ต้องส่ง`, `กำลังไปต่อ`;
- expose `ดูทั้งหมด` for returning from a selected seller filter; and
- change the seller section sort label from `ใหม่สุดก่อน` to the bound
  priority explanation.

Use `MinimumHeightRequest="{StaticResource CompactControlMinimumHeight}"` on
every tappable tile and banner action.

Update the existing
`UiLayoutConsistencyTests.TransactionsUseTopLevelBuySellModes` assertion that
currently requires `SellerStatusFilters`: it must instead require
`SellerWorkSummary`, while continuing to verify the top-level buy/sell switch.

- [ ] **Step 5: Update obsolete seller filter tests**

Change `TransactionFilterTests.SellerModeFiltersReviewFulfillmentAndPayoutSeparately`
to cover buyer filters only or remove its seller assertions after equivalent
`SellerWorkSummaryTests` exist. Do not keep two classification authorities.

- [ ] **Step 6: Run mobile-core and iOS compile**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --no-restore
dotnet msbuild src/Toklong.Mobile/Toklong.Mobile.csproj \
  -t:Compile \
  -p:TargetFrameworks=net10.0-ios \
  -p:TargetFramework=net10.0-ios \
  -p:RuntimeIdentifier=iossimulator-arm64 \
  -p:NuGetAudit=false
```

Expected: all mobile-core tests and iOS compile pass. Seller UI shows no buyer
protection fee or buyer total.

- [ ] **Step 7: Commit the seller workspace slice**

```bash
git add \
  src/Toklong.Mobile/ViewModels/TransactionsViewModel.cs \
  src/Toklong.Mobile/Pages/TransactionsPage.xaml \
  tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs \
  tests/Toklong.Mobile.Core.Tests/TransactionFilterTests.cs
git commit -m "feat: add seller work dashboard"
```

---

### Task 5: Authenticated Home Seller Counts

**Files:**
- Create: `src/Toklong.Mobile/ViewModels/AuthenticatedHomeViewModel.cs`
- Modify: `src/Toklong.Mobile/Pages/AuthenticatedHomePage.xaml`
- Modify: `src/Toklong.Mobile/Pages/AuthenticatedHomePage.xaml.cs`
- Modify: `src/Toklong.Mobile/MauiProgram.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`

**Interfaces:**
- Consumes: `ITransactionService`, `SellerWorkSummary`,
  `SellerWorkspaceState`, `SellerWorkspaceAnalytics`, `IMobileAnalytics`.
- Produces:
  - `LoadAsync()`;
  - `HasNewOffers`, `NewOfferBadgeText`;
  - `HasActionableSellerWork`, `ActionableSellerWorkText`;
  - `HasSellerSummary`, `SellerCardSemanticText`;
  - `HasLoadError`, `LoadErrorText`, `RetryCommand`.

- [ ] **Step 1: Write failing authenticated-home UI contracts**

Extend `AuthenticatedHome_UsesCenteredBrandAndBuyerFirstActions`:

```csharp
var offerBadge = home.Descendants(Maui + "Border")
    .Single(element =>
        AttributeValue(element, "AutomationId") ==
        "SellerNewOfferBadge");
var actionableLine = home.Descendants(Maui + "HorizontalStackLayout")
    .Single(element =>
        AttributeValue(element, "AutomationId") ==
        "SellerActionableLine");
var sellerButton = buttons.Single(button =>
    AttributeValue(button, "AutomationId") ==
    "OpenSellingHomeButton");

Assert.Equal("{Binding HasNewOffers}",
    AttributeValue(offerBadge, "IsVisible"));
Assert.Contains(offerBadge.Descendants(Maui + "Label"), label =>
    AttributeValue(label, "Text") ==
        "{Binding NewOfferBadgeText}");
Assert.Equal("{Binding HasActionableSellerWork}",
    AttributeValue(actionableLine, "IsVisible"));
Assert.Equal("{Binding SellerCardSemanticText}",
    AttributeValue(
        sellerButton,
        "SemanticProperties.Description"));
```

Add an assertion for an error label bound to `LoadErrorText` and a retry button
bound to `RetryCommand`.

- [ ] **Step 2: Run the focused UI test and confirm RED**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter FullyQualifiedName~AuthenticatedHome_UsesCenteredBrandAndBuyerFirstActions \
  --no-restore
```

Expected: FAIL because the badge, actionable line, retry UI, and dynamic
semantic binding do not exist.

- [ ] **Step 3: Implement `AuthenticatedHomeViewModel`**

Create a focused view model:

```csharp
public sealed class AuthenticatedHomeViewModel : ObservableViewModel
{
    private readonly ITransactionService transactions;
    private readonly IMobileAnalytics analytics;
    private readonly SellerWorkspaceState sellerState = new();
    private readonly AsyncCommand retryCommand;
    private bool isBusy;

    public AuthenticatedHomeViewModel(
        ITransactionService transactions,
        IMobileAnalytics analytics)
    {
        this.transactions = transactions;
        this.analytics = analytics;
        retryCommand = new AsyncCommand(LoadAsync);
    }

    public bool HasSellerSummary => sellerState.HasVisibleSummary;
    public bool HasNewOffers =>
        HasSellerSummary && sellerState.Snapshot.NewOfferCount > 0;
    public bool HasActionableSellerWork =>
        HasSellerSummary && sellerState.Snapshot.ActionableCount > 0;
    public string NewOfferBadgeText =>
        $"{sellerState.Snapshot.NewOfferCount} ข้อเสนอใหม่";
    public string ActionableSellerWorkText =>
        $"มี {sellerState.Snapshot.ActionableCount} รายการที่ต้องจัดการ";
    public string SellerCardSemanticText =>
        !HasSellerSummary
            ? "ขาย ตรวจข้อเสนอ ส่งสินค้า และติดตามยอดรับ"
            : $"ขาย {NewOfferBadgeText} {ActionableSellerWorkText}";
    public bool HasLoadError => sellerState.HasLoadError;
    public string LoadErrorText => sellerState.LoadErrorText;
    public ICommand RetryCommand => retryCommand;

    public async Task LoadAsync()
    {
        if (isBusy)
            return;
        isBusy = true;
        try
        {
            var loaded = await transactions.GetTransactionsAsync();
            sellerState.ReplaceSuccessful(loaded);
            RaiseSummaryProperties();
            analytics.Track(SellerWorkspaceAnalytics.HomeOpened(
                sellerState.Snapshot.NewOfferCount,
                sellerState.Snapshot.ActionableCount));
        }
        catch
        {
            sellerState.MarkLoadFailed();
            RaiseSummaryProperties();
        }
        finally
        {
            isBusy = false;
        }
    }

    private void RaiseSummaryProperties()
    {
        OnPropertyChanged(nameof(HasSellerSummary));
        OnPropertyChanged(nameof(HasNewOffers));
        OnPropertyChanged(nameof(HasActionableSellerWork));
        OnPropertyChanged(nameof(NewOfferBadgeText));
        OnPropertyChanged(nameof(ActionableSellerWorkText));
        OnPropertyChanged(nameof(SellerCardSemanticText));
        OnPropertyChanged(nameof(HasLoadError));
        OnPropertyChanged(nameof(LoadErrorText));
    }
}
```

Keep the prior successful seller snapshot on refresh failure; never replace it
with zero.

- [ ] **Step 4: Bind the existing home page**

Change the page constructor:

```csharp
private readonly AuthenticatedHomeViewModel viewModel;

public AuthenticatedHomePage(AuthenticatedHomeViewModel viewModel)
{
    InitializeComponent();
    BindingContext = this.viewModel = viewModel;
}

protected override async void OnAppearing()
{
    base.OnAppearing();
    await viewModel.LoadAsync();
}
```

In the existing purple seller card:

- add `SellerNewOfferBadge` at top-right with white background and purple text;
- add `SellerActionableLine` with a mint dot below the description;
- preserve the original layout when both are hidden;
- bind the transparent seller button semantic description dynamically; and
- add a compact error/retry row below the buy/sell cards.

Do not add a timer to the home page.

- [ ] **Step 5: Register the view model**

In `MauiProgram.cs`:

```csharp
builder.Services.AddSingleton<AuthenticatedHomeViewModel>();
```

Keep `AuthenticatedHomePage` singleton registration.

- [ ] **Step 6: Run focused/full mobile tests and iOS compile**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --no-restore
dotnet msbuild src/Toklong.Mobile/Toklong.Mobile.csproj \
  -t:Compile \
  -p:TargetFrameworks=net10.0-ios \
  -p:TargetFramework=net10.0-ios \
  -p:RuntimeIdentifier=iossimulator-arm64 \
  -p:NuGetAudit=false
```

Expected: all tests and compile pass. A successful empty result hides both
seller-card count lines; a failed first load shows no zero.

- [ ] **Step 7: Commit the authenticated-home slice**

```bash
git add \
  src/Toklong.Mobile/ViewModels/AuthenticatedHomeViewModel.cs \
  src/Toklong.Mobile/Pages/AuthenticatedHomePage.xaml \
  src/Toklong.Mobile/Pages/AuthenticatedHomePage.xaml.cs \
  tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs
git add -p src/Toklong.Mobile/MauiProgram.cs
git diff --cached -- src/Toklong.Mobile/MauiProgram.cs
git commit -m "feat: show seller work counts on home"
```

For the patch prompt, stage only the
`AuthenticatedHomeViewModel` registration hunk. Reject any remaining
pre-existing simulator-session hunk.

---

### Task 6: Full Verification and Simulator Ceremony

**Files:**
- Modify only if a verification failure identifies a defect in files already
  listed above.

**Interfaces:**
- Verifies the complete approved design; produces no new behavior.

- [ ] **Step 1: Review the diff for scope and secrets**

```bash
git diff --check
git status --short
git diff -- \
  src/Toklong.Mobile/Core/SellerWorkSummary.cs \
  src/Toklong.Mobile/Core/SellerWorkspaceState.cs \
  src/Toklong.Mobile/Core/IMobileAnalytics.cs \
  src/Toklong.Mobile/Services/LoggingMobileAnalytics.cs \
  src/Toklong.Mobile/ViewModels/TransactionsViewModel.cs \
  src/Toklong.Mobile/ViewModels/AuthenticatedHomeViewModel.cs \
  src/Toklong.Mobile/Pages/TransactionsPage.xaml \
  src/Toklong.Mobile/Pages/AuthenticatedHomePage.xaml \
  src/Toklong.Mobile/Pages/AuthenticatedHomePage.xaml.cs \
  src/Toklong.Mobile/MauiProgram.cs \
  tests/Toklong.Mobile.Core.Tests
```

Expected: only approved seller-workspace changes plus pre-existing unrelated
working-tree changes; no token, phone, address, payment reference, or key.

- [ ] **Step 2: Run every automated test project**

```bash
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj --no-restore
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj --no-restore
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj --no-restore
dotnet test tests/Toklong.Crm.Tests/Toklong.Crm.Tests.csproj --no-restore
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore
```

Expected: all five projects pass with zero failed tests. The existing intended
seller-phone API tests remain green.

- [ ] **Step 3: Build the iOS simulator app**

```bash
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj \
  -p:TargetFrameworks=net10.0-ios \
  -f net10.0-ios \
  -r iossimulator-arm64 \
  --no-restore \
  -p:NuGetAudit=false
```

Expected: build succeeds. Existing NU1900 or obsolete MediaPicker warnings may
remain; no new seller-workspace warning is accepted.

- [ ] **Step 4: Prepare deterministic multi-offer seller data**

Use the existing Development API and authenticated test helpers to create, for
one intended seller phone:

- three `AwaitingSellerAcceptance` physical offers with distinct deadlines;
- one provider-confirmed paid physical transaction that needs fulfillment;
- two active in-progress seller records;
- one disputed record; and
- one completed record.

Do not mutate database rows directly. Create every state through existing
commands, verified provider events, carrier events, or the approved Development
simulation worker.

- [ ] **Step 5: Verify the authenticated home**

On the seller simulator, confirm:

- purple `ขาย` card shows `3 ข้อเสนอใหม่`;
- actionable line shows `มี 4 รายการที่ต้องจัดการ`;
- accessibility announces both counts;
- no buyer protection fee or buyer total is visible; and
- terminating and relaunching retains the debug simulator session using the
  separately implemented simulator-session fix.

- [ ] **Step 6: Verify seller workspace interactions**

Confirm:

- total count includes active and completed seller records;
- summary counts show `3`, `1`, and `2`;
- problem banner shows `1`;
- spotlight follows the approved risk order;
- tapping each summary filters the list and recalculates spotlight;
- tapping `ดูทั้งหมด` restores all seller records;
- a problem filter never exposes a payout action;
- seller fulfillment is absent from unpaid records; and
- all exact deadlines fit at default and one accessibility text size.

- [ ] **Step 7: Verify loading failures**

Stop the Development API before a first launch and confirm no `0` badge or
summary is shown. Restart, load successfully, stop the API again, pull to
refresh, and confirm the last successful records remain with
`อัปเดตล่าสุดไม่สำเร็จ`.

- [ ] **Step 8: Commit any verification-only correction**

If no correction was required, skip this step. Otherwise:

```bash
git add \
  src/Toklong.Mobile/Core/SellerWorkSummary.cs \
  src/Toklong.Mobile/Core/SellerWorkspaceState.cs \
  src/Toklong.Mobile/Core/IMobileAnalytics.cs \
  src/Toklong.Mobile/Services/LoggingMobileAnalytics.cs \
  src/Toklong.Mobile/ViewModels/TransactionsViewModel.cs \
  src/Toklong.Mobile/ViewModels/AuthenticatedHomeViewModel.cs \
  src/Toklong.Mobile/Pages/TransactionsPage.xaml \
  src/Toklong.Mobile/Pages/AuthenticatedHomePage.xaml \
  src/Toklong.Mobile/Pages/AuthenticatedHomePage.xaml.cs \
  tests/Toklong.Mobile.Core.Tests/SellerWorkSummaryTests.cs \
  tests/Toklong.Mobile.Core.Tests/SellerWorkspaceStateTests.cs \
  tests/Toklong.Mobile.Core.Tests/MobileAnalyticsEventTests.cs \
  tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs \
  tests/Toklong.Mobile.Core.Tests/TransactionFilterTests.cs
git add -p src/Toklong.Mobile/MauiProgram.cs
git diff --cached -- src/Toklong.Mobile/MauiProgram.cs
git commit -m "fix: refine seller workspace verification"
```

At the patch prompt, stage only a seller-workspace correction. Never stage the
pre-existing simulator-session hunk as part of this feature.

- [ ] **Step 9: Final status**

```bash
git status --short
git log --oneline -8
```

Report:

1. what changed;
2. which presentation requirements were implemented and that no domain
   transition changed;
3. tests added/updated and exact passing counts;
4. assumptions;
5. open provider decisions or blockers; and
6. the next smallest vertical slice.
