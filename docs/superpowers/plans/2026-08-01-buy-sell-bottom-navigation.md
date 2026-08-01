# Buy/Sell Bottom Navigation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the authenticated role chooser and in-page role switch with native Buy, Sell, and Account bottom tabs plus a global top-right Activity entry point, while preserving all transaction behavior.

**Architecture:** `AppShell` owns three authenticated roots. Buy and Sell are thin page types over one shared `TransactionsPage` XAML surface, but each receives a separately constructed `TransactionsViewModel` whose role is immutable. A small preference service resolves and stores the last explicitly selected Buy/Sell root, and a shared root-header control pushes the existing Activity page.

**Tech Stack:** .NET 10, .NET MAUI Shell, XAML SourceGen, C# 14, MAUI Preferences, xUnit, static XAML structure tests.

## Global Constraints

- Preserve the existing uncommitted mobile UI clarity changes in `AppTransaction.cs`, `CreateOfferPage.xaml`, `SellerOfferPage.xaml`, `TransactionDetailPage.xaml`, `TransactionsPage.xaml`, their ViewModels, and mobile tests.
- Do not create marketplace discovery, listings, storefronts, bidding, chat, wallets, crypto, seller-created deal links, or a new dashboard tab.
- Navigation must never create or mutate a transaction.
- Keep existing Buyer Blue, Seller Graphite/Navy, all theme resources, and Noto Sans Thai font registrations.
- Keep buyer-first offer creation and every payment, fulfillment, dispute, refund, and payout rule unchanged.
- Activity has no unread badge until authoritative unread state exists.
- Buy and Sell must use independent page and ViewModel instances; no busy, filter, collection, or refresh state may leak between roots.
- First authenticated use opens Buy; ordinary launches restore the last Buy/Sell root; explicit logout clears that preference; Account never becomes the preferred role.
- Deep links take precedence over the preference and do not overwrite it.
- Before each commit, stage only the files listed in that task; the working tree already contains unrelated-to-this-plan uncommitted UI changes that must remain intact.

---

## File Map

### New files

- `src/Toklong.Mobile/Core/WorkspaceRolePreference.cs` — validated preferred-role persistence and route-independent policy.
- `src/Toklong.Mobile/ViewModels/TransactionWorkspaceViewModelFactory.cs` — creates fixed-role transaction ViewModels with shared services.
- `src/Toklong.Mobile/Pages/BuyingTransactionsPage.cs` — thin buyer root over shared transaction XAML.
- `src/Toklong.Mobile/Pages/SellingTransactionsPage.cs` — thin seller root over shared transaction XAML.
- `src/Toklong.Mobile/Controls/RootPageHeaderView.xaml` — shared page identity and Activity bell.
- `src/Toklong.Mobile/Controls/RootPageHeaderView.xaml.cs` — bindable header properties and Activity navigation.
- `src/Toklong.Mobile/Resources/Images/nav_buy.svg` — buyer bottom-tab icon.
- `src/Toklong.Mobile/Resources/Images/nav_sell.svg` — seller bottom-tab icon.
- `tests/Toklong.Mobile.Core.Tests/WorkspaceRolePreferenceTests.cs` — preference fallback/save/clear tests.

### Modified files

- `src/Toklong.Mobile/Core/AuthenticatedHomeRoutes.cs` — direct Buy/Sell root routes and route parsing.
- `src/Toklong.Mobile/Core/StartupCoordinator.cs` — route valid sessions directly to the preferred role.
- `src/Toklong.Mobile/App.xaml.cs` — initialize authenticated services for either role root.
- `src/Toklong.Mobile/AppShell.xaml` — three native roots, no Activity or authenticated-home root.
- `src/Toklong.Mobile/AppShell.xaml.cs` — Activity route registration and root-selection preference updates.
- `src/Toklong.Mobile/MauiProgram.cs` — preference/factory/root registrations and obsolete-home removal.
- `src/Toklong.Mobile/ViewModels/TransactionsViewModel.cs` — immutable constructor role and no role-switch commands/preferences.
- `src/Toklong.Mobile/ViewModels/VerifyCodeViewModel.cs` — post-login preferred-root navigation.
- `src/Toklong.Mobile/ViewModels/CompleteRegistrationViewModel.cs` — post-registration preferred-root navigation.
- `src/Toklong.Mobile/Pages/TransactionsPage.xaml` — action-first fixed-role UI, shared header, no role switch.
- `src/Toklong.Mobile/Pages/TransactionsPage.xaml.cs` — no query-role mutation; separate root instances keep refresh state.
- `src/Toklong.Mobile/Pages/AccountPage.xaml` — shared header.
- `src/Toklong.Mobile/Pages/ActivityPage.xaml` — pushed-page chrome and hidden bottom bar.
- `tests/Toklong.Mobile.Core.Tests/MauiViewModelTestDoubles.cs` — preference `Remove` support.
- `tests/Toklong.Mobile.Core.Tests/StartupCoordinatorTests.cs` — preferred-role startup coverage.
- `tests/Toklong.Mobile.Core.Tests/AuthenticatedHomeRoutesTests.cs` — new root routes and parser coverage.
- `tests/Toklong.Mobile.Core.Tests/ViewModelSessionBoundaryTests.cs` — independent buyer/seller instances and stale-load coverage.
- `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs` — new Shell/header/workspace/Activity contract.
- `tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj` — shared-header source links; remove obsolete home ViewModel link.
- `docs/02_UI_UX_AND_CONTENT_SPEC.md` — replace role-home and mode-switch requirements.
- `docs/05_ACCEPTANCE_TESTS.md` — replace role-home/mode-switch scenarios with native-root scenarios.

### Deleted files after all callers migrate

- `src/Toklong.Mobile/Pages/AuthenticatedHomePage.xaml`
- `src/Toklong.Mobile/Pages/AuthenticatedHomePage.xaml.cs`
- `src/Toklong.Mobile/ViewModels/AuthenticatedHomeViewModel.cs`
- `tests/Toklong.Mobile.Core.Tests/AuthenticatedHomeViewModelTests.cs`

---

### Task 0: Checkpoint the Existing Approved UI Clarity Pass

**Files:**
- Existing modified files only: `src/Toklong.Mobile/Core/AppTransaction.cs`, `src/Toklong.Mobile/Pages/CreateOfferPage.xaml`, `src/Toklong.Mobile/Pages/SellerOfferPage.xaml`, `src/Toklong.Mobile/Pages/TransactionDetailPage.xaml`, `src/Toklong.Mobile/Pages/TransactionsPage.xaml`, `src/Toklong.Mobile/ViewModels/TransactionDetailViewModel.cs`, `src/Toklong.Mobile/ViewModels/TransactionsViewModel.cs`, `tests/Toklong.Mobile.Core.Tests/TransactionDetailParcelProtectionViewModelTests.cs`, `tests/Toklong.Mobile.Core.Tests/TransactionPresentationTests.cs`, `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`.

**Interfaces:**
- Produces: a clean, tested baseline commit containing the already completed buyer/seller UI clarity work.
- Does not produce: any bottom-navigation behavior.

- [ ] **Step 1: Confirm the existing diff contains only the completed UI pass**

```bash
git status --short
git diff --check
git diff --stat
```

Expected: exactly the ten files listed above are modified and no unknown application files are present.

- [ ] **Step 2: Re-run the existing UI verification**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore --logger "console;verbosity=minimal"
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj -p:TargetFrameworks=net10.0-ios -f net10.0-ios -c Debug -p:RuntimeIdentifier=iossimulator-arm64 --no-restore -t:Compile
```

Expected: 529 or more mobile tests pass with zero failures; iOS compile succeeds, with only the existing media-picker deprecation warning allowed.

- [ ] **Step 3: Commit only the existing UI pass**

```bash
git add src/Toklong.Mobile/Core/AppTransaction.cs src/Toklong.Mobile/Pages/CreateOfferPage.xaml src/Toklong.Mobile/Pages/SellerOfferPage.xaml src/Toklong.Mobile/Pages/TransactionDetailPage.xaml src/Toklong.Mobile/Pages/TransactionsPage.xaml src/Toklong.Mobile/ViewModels/TransactionDetailViewModel.cs src/Toklong.Mobile/ViewModels/TransactionsViewModel.cs tests/Toklong.Mobile.Core.Tests/TransactionDetailParcelProtectionViewModelTests.cs tests/Toklong.Mobile.Core.Tests/TransactionPresentationTests.cs tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs
git commit -m "feat: clarify buyer and seller mobile UI"
```

Expected: the working tree is clean. This checkpoint allows a safe implementation worktree without losing the approved UI changes.

---

### Task 1: Preferred Role Policy and Root Routes

**Files:**
- Create: `src/Toklong.Mobile/Core/WorkspaceRolePreference.cs`
- Create: `tests/Toklong.Mobile.Core.Tests/WorkspaceRolePreferenceTests.cs`
- Modify: `src/Toklong.Mobile/Core/AuthenticatedHomeRoutes.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/AuthenticatedHomeRoutesTests.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/MauiViewModelTestDoubles.cs`

**Interfaces:**
- Produces: `IWorkspaceRolePreference.GetPreferredRole()`, `SavePreferredRole(TransactionRoleRoute)`, and `Clear()`.
- Produces: `AuthenticatedHomeRoutes.Root(TransactionRoleRoute)`, `TryParseRoot(string?, out TransactionRoleRoute)`, and `IsAuthenticatedRoot(string?)`.
- Consumes: existing `TransactionRoleRoute.Buying` and `TransactionRoleRoute.Selling`.

- [ ] **Step 1: Write failing preference tests**

```csharp
public sealed class WorkspaceRolePreferenceTests
{
    private readonly AuthenticatedSessionBoundary session = new();

    public WorkspaceRolePreferenceTests() => Preferences.Default.Clear();

    [Fact]
    public void Missing_or_invalid_value_falls_back_to_buying()
    {
        var preference = new WorkspaceRolePreference(session);
        Assert.Equal(TransactionRoleRoute.Buying, preference.GetPreferredRole());

        Preferences.Default.Set("workspace.preferred-role", "invalid");
        Assert.Equal(TransactionRoleRoute.Buying, preference.GetPreferredRole());
    }

    [Fact]
    public void Save_and_clear_round_trip_only_supported_roles()
    {
        var preference = new WorkspaceRolePreference(session);
        preference.SavePreferredRole(TransactionRoleRoute.Selling);
        Assert.Equal(TransactionRoleRoute.Selling, preference.GetPreferredRole());

        session.Reset();
        Assert.Equal(TransactionRoleRoute.Buying, preference.GetPreferredRole());
    }
}
```

Add `Remove(string key) => values.Remove(key);` to the test `PreferenceStore`.

- [ ] **Step 2: Extend route tests and verify failure**

```csharp
[Theory]
[InlineData(TransactionRoleRoute.Buying, "//main/buying")]
[InlineData(TransactionRoleRoute.Selling, "//main/selling")]
public void Root_returns_native_tab_route(TransactionRoleRoute role, string expected) =>
    Assert.Equal(expected, AuthenticatedHomeRoutes.Root(role));

[Theory]
[InlineData("//main/buying", TransactionRoleRoute.Buying)]
[InlineData("//main/selling", TransactionRoleRoute.Selling)]
[InlineData("main/selling/TransactionDetailPage", TransactionRoleRoute.Selling)]
public void TryParseRoot_recognizes_role_root(string route, TransactionRoleRoute expected)
{
    Assert.True(AuthenticatedHomeRoutes.TryParseRoot(route, out var actual));
    Assert.Equal(expected, actual);
}

[Theory]
[InlineData("//main/account")]
[InlineData("ActivityPage")]
[InlineData(null)]
public void TryParseRoot_ignores_non_role_destinations(string? route) =>
    Assert.False(AuthenticatedHomeRoutes.TryParseRoot(route, out _));
```

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~WorkspaceRolePreferenceTests|FullyQualifiedName~AuthenticatedHomeRoutesTests"
```

Expected: FAIL because the preference type and root helpers do not exist.

- [ ] **Step 3: Implement the preference service**

```csharp
namespace Toklong.Mobile.Core;

public interface IWorkspaceRolePreference
{
    TransactionRoleRoute GetPreferredRole();
    void SavePreferredRole(TransactionRoleRoute role);
    void Clear();
}

public sealed class WorkspaceRolePreference : IWorkspaceRolePreference
{
    private const string Key = "workspace.preferred-role";

    public WorkspaceRolePreference(AuthenticatedSessionBoundary session) =>
        session.ResetRequested += (_, _) => Clear();

    public TransactionRoleRoute GetPreferredRole() =>
        Preferences.Default.Get(Key, "buying") == "selling"
            ? TransactionRoleRoute.Selling
            : TransactionRoleRoute.Buying;

    public void SavePreferredRole(TransactionRoleRoute role) =>
        Preferences.Default.Set(Key, role switch
        {
            TransactionRoleRoute.Buying => "buying",
            TransactionRoleRoute.Selling => "selling",
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        });

    public void Clear() => Preferences.Default.Remove(Key);
}
```

- [ ] **Step 4: Implement direct root routes**

Replace the query-based transaction route with:

```csharp
public const string Buying = "//main/buying";
public const string Selling = "//main/selling";

public static string Root(TransactionRoleRoute role) => role switch
{
    TransactionRoleRoute.Buying => Buying,
    TransactionRoleRoute.Selling => Selling,
    _ => throw new ArgumentOutOfRangeException(nameof(role))
};

public static bool TryParseRoot(string? route, out TransactionRoleRoute role)
{
    var value = route?.TrimEnd('/');
    if (value?.Contains("/selling", StringComparison.Ordinal) == true)
    {
        role = TransactionRoleRoute.Selling;
        return true;
    }
    if (value?.Contains("/buying", StringComparison.Ordinal) == true)
    {
        role = TransactionRoleRoute.Buying;
        return true;
    }
    role = default;
    return false;
}

public static bool IsAuthenticatedRoot(string? route) =>
    TryParseRoot(route, out _);
```

Keep the existing `Transactions`, `TryParseRole`, and `ToRoleFilter` methods unchanged through Tasks 1–5 because the obsolete authenticated-home page still compiles against them. Delete those methods with the page in Task 6.

- [ ] **Step 5: Run focused tests**

Run the filtered command from Step 2.

Expected: PASS.

- [ ] **Step 6: Commit Task 1**

```bash
git add src/Toklong.Mobile/Core/WorkspaceRolePreference.cs src/Toklong.Mobile/Core/AuthenticatedHomeRoutes.cs tests/Toklong.Mobile.Core.Tests/WorkspaceRolePreferenceTests.cs tests/Toklong.Mobile.Core.Tests/AuthenticatedHomeRoutesTests.cs tests/Toklong.Mobile.Core.Tests/MauiViewModelTestDoubles.cs
git commit -m "feat: add preferred transaction workspace routes"
```

---

### Task 2: Independent Fixed-Role Transaction Workspaces

**Files:**
- Create: `src/Toklong.Mobile/ViewModels/TransactionWorkspaceViewModelFactory.cs`
- Create: `src/Toklong.Mobile/Pages/BuyingTransactionsPage.cs`
- Create: `src/Toklong.Mobile/Pages/SellingTransactionsPage.cs`
- Modify: `src/Toklong.Mobile/ViewModels/TransactionsViewModel.cs`
- Modify: `src/Toklong.Mobile/Pages/TransactionsPage.xaml.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/ViewModelSessionBoundaryTests.cs`

**Interfaces:**
- Consumes: `RoleFilter.Buying` or `RoleFilter.Selling` as an immutable constructor value.
- Produces: `TransactionsViewModel.Role`, `IsBuying`, and `IsSelling` with no role-selection commands.
- Produces: `TransactionsViewModel.WorkspaceAccentColor` for shared-header presentation only.
- Produces: `TransactionWorkspaceViewModelFactory.Create(RoleFilter role)`.
- Produces: distinct `BuyingTransactionsPage` and `SellingTransactionsPage` root types.

- [ ] **Step 1: Write the buyer/seller isolation test**

In `ViewModelSessionBoundaryTests`, use its existing `SequencedTransactionService`, `NoOpDeepLinks`, and `RecordingAnalytics`:

```csharp
[Fact]
public async Task Fixed_role_workspaces_never_share_records_or_filters()
{
    Preferences.Default.Clear();
    var session = new AuthenticatedSessionBoundary();
    var service = new SequencedTransactionService();
    var buyerItem = BuyerItem("00000000-0000-0000-0000-000000000901");
    var sellerItem = Item(
        "00000000-0000-0000-0000-000000000902",
        "กล้องของผู้ขาย", "ผู้ซื้อ", "AwaitingSellerAcceptance");
    service.EnqueueResult([buyerItem, sellerItem]);
    service.EnqueueResult([buyerItem, sellerItem]);

    var buyer = new TransactionsViewModel(
        service, new NoOpDeepLinks(), new RecordingAnalytics(), session,
        RoleFilter.Buying);
    var seller = new TransactionsViewModel(
        service, new NoOpDeepLinks(), new RecordingAnalytics(), session,
        RoleFilter.Selling);

    await buyer.LoadAsync();
    await seller.LoadAsync();

    Assert.True(buyer.IsBuying);
    Assert.All(buyer.Transactions.Append(buyer.SpotlightTransaction!),
        item => Assert.Equal(AppTransactionRole.Buyer, item.Role));
    Assert.True(seller.IsSelling);
    Assert.All(seller.Transactions.Append(seller.SpotlightTransaction!),
        item => Assert.Equal(AppTransactionRole.Seller, item.Role));
}
```

Add this local buyer helper:

```csharp
private static AppTransaction BuyerItem(string id) => new(
    Guid.Parse(id),
    "รายการซื้อ",
    2_500_00,
    "THB",
    AppTransactionRole.Buyer,
    AppFulfillmentType.Physical,
    "SellerAcceptedAwaitingPayment",
    DateTimeOffset.Parse("2026-08-01T10:00:00+07:00"),
    DateTimeOffset.Parse("2026-08-02T10:00:00+07:00"),
    "ผู้ขาย");
```

Add a pending-response isolation test using the same helpers:

```csharp
[Fact]
public async Task Late_buyer_response_never_replaces_seller_workspace()
{
    var session = new AuthenticatedSessionBoundary();
    var service = new SequencedTransactionService();
    var buyerPending = service.EnqueuePending();
    var sellerPending = service.EnqueuePending();
    var buyer = new TransactionsViewModel(
        service, new NoOpDeepLinks(), new RecordingAnalytics(), session,
        RoleFilter.Buying);
    var seller = new TransactionsViewModel(
        service, new NoOpDeepLinks(), new RecordingAnalytics(), session,
        RoleFilter.Selling);

    var buyerLoad = buyer.LoadAsync();
    var sellerLoad = seller.LoadAsync();
    var sellerItem = Item(
        "00000000-0000-0000-0000-000000000903",
        "รายการขาย", "ผู้ซื้อ", "AwaitingSellerAcceptance");
    sellerPending.SetResult([sellerItem]);
    await sellerLoad;
    buyerPending.SetResult([BuyerItem(
        "00000000-0000-0000-0000-000000000904")]);
    await buyerLoad;

    Assert.Equal("รายการขาย", seller.SpotlightTransaction?.ProductName);
    Assert.All(seller.Transactions.Append(seller.SpotlightTransaction!),
        item => Assert.Equal(AppTransactionRole.Seller, item.Role));
}
```

- [ ] **Step 2: Run the isolation test and verify failure**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~Fixed_role_workspaces_never_share_records_or_filters"
```

Expected: FAIL because the ViewModel does not accept a fixed role.

- [ ] **Step 3: Make role immutable in `TransactionsViewModel`**

Change the constructor to:

```csharp
public TransactionsViewModel(
    ITransactionService transactionService,
    IDeepLinkCoordinator deepLinks,
    IMobileAnalytics analytics,
    AuthenticatedSessionBoundary session,
    RoleFilter role)
{
    if (role is not (RoleFilter.Buying or RoleFilter.Selling))
        throw new ArgumentOutOfRangeException(nameof(role));
    this.transactionService = transactionService;
    this.deepLinks = deepLinks;
    this.analytics = analytics;
    this.session = session;
    session.ResetRequested += (_, _) => ResetForSessionBoundary();
    this.roleFilter = role;
    spotlightEmptyState = new(roleFilter, hasSpotlight: false);
    spotlightEmptyState.PropertyChanged +=
        (_, eventArgs) => OnPropertyChanged(eventArgs.PropertyName);

    SelectAllBucketsCommand = new Command(() => SelectBucket(BucketFilter.All));
    SelectActionCommand = new Command(() => SelectBucket(BucketFilter.ActionRequired));
    SelectProgressCommand = new Command(() => SelectBucket(BucketFilter.InProgress));
    SelectCompletedCommand = new Command(() => SelectBucket(BucketFilter.Completed));
    SelectSellerNewOffersCommand = new Command(
        () => SelectSellerWork(SellerWorkCategory.NewOffers));
    SelectSellerFulfillmentCommand = new Command(
        () => SelectSellerWork(SellerWorkCategory.FulfillmentRequired));
    SelectSellerInProgressCommand = new Command(
        () => SelectSellerWork(SellerWorkCategory.InProgress));
    SelectSellerProblemsCommand = new Command(() =>
    {
        SelectSellerWork(SellerWorkCategory.Problems);
        analytics.Track(SellerWorkspaceAnalytics.ProblemBannerOpened(
            sellerState.Snapshot.ProblemCount));
    });
    SelectAllSellerWorkCommand = new Command(
        () => SelectSellerWork(SellerWorkCategory.All));
    OpenTransactionCommand = new Command<AppTransaction>(
        async item => await OpenTransactionAsync(item));
    CreateOfferCommand = new Command(
        async () => await Shell.Current.GoToAsync(nameof(CreateOfferPage)));
    RefreshCommand = new AsyncCommand(RefreshAsync);
}

public RoleFilter Role => roleFilter;
public string WorkspaceAccentColor => IsBuying
    ? "#2B7FFF"
    : SellerColorPalette.Role;
```

Remove `SelectBuyingCommand`, `SelectSellingCommand`, `ApplyRoleNavigation`, `SelectRole`, and all direct `Preferences` access. Keep buyer bucket filters and seller summary filters unchanged. `ResetForSessionBoundary` must clear data without changing `roleFilter`.

- [ ] **Step 4: Add the factory and root page types**

```csharp
using Toklong.Mobile.Core;

namespace Toklong.Mobile.ViewModels;

public sealed class TransactionWorkspaceViewModelFactory(
    ITransactionService transactions,
    IDeepLinkCoordinator deepLinks,
    IMobileAnalytics analytics,
    AuthenticatedSessionBoundary session)
{
    public TransactionsViewModel Create(RoleFilter role) =>
        new(transactions, deepLinks, analytics, session, role);
}
```

```csharp
using Toklong.Mobile.Core;
using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Pages;

public sealed class BuyingTransactionsPage : TransactionsPage
{
    public BuyingTransactionsPage(TransactionWorkspaceViewModelFactory factory)
        : base(factory.Create(RoleFilter.Buying)) { }
}

public sealed class SellingTransactionsPage : TransactionsPage
{
    public SellingTransactionsPage(TransactionWorkspaceViewModelFactory factory)
        : base(factory.Create(RoleFilter.Selling)) { }
}
```

Remove `IQueryAttributable` and `ApplyQueryAttributes` from `TransactionsPage`; retain its appearance refresh loop and native root-chrome restoration.

- [ ] **Step 5: Update existing ViewModel constructor calls**

In `ViewModelSessionBoundaryTests`, pass `RoleFilter.Selling` to seller-specific existing tests and `RoleFilter.Buying` to buyer/default tests. Do not restore constructor-time preference lookup.

- [ ] **Step 6: Run the mobile test suite**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore
```

Expected: PASS.

- [ ] **Step 7: Commit Task 2**

```bash
git add src/Toklong.Mobile/ViewModels/TransactionsViewModel.cs src/Toklong.Mobile/ViewModels/TransactionWorkspaceViewModelFactory.cs src/Toklong.Mobile/Pages/TransactionsPage.xaml.cs src/Toklong.Mobile/Pages/BuyingTransactionsPage.cs src/Toklong.Mobile/Pages/SellingTransactionsPage.cs tests/Toklong.Mobile.Core.Tests/ViewModelSessionBoundaryTests.cs
git commit -m "refactor: isolate buyer and seller workspaces"
```

---

### Task 3: Native Shell Roots and Authenticated Routing

**Files:**
- Modify: `src/Toklong.Mobile/AppShell.xaml`
- Modify: `src/Toklong.Mobile/AppShell.xaml.cs`
- Modify: `src/Toklong.Mobile/App.xaml.cs`
- Modify: `src/Toklong.Mobile/Core/StartupCoordinator.cs`
- Modify: `src/Toklong.Mobile/ViewModels/VerifyCodeViewModel.cs`
- Modify: `src/Toklong.Mobile/ViewModels/CompleteRegistrationViewModel.cs`
- Modify: `src/Toklong.Mobile/MauiProgram.cs`
- Create: `src/Toklong.Mobile/Resources/Images/nav_buy.svg`
- Create: `src/Toklong.Mobile/Resources/Images/nav_sell.svg`
- Modify: `tests/Toklong.Mobile.Core.Tests/StartupCoordinatorTests.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/ViewModelSessionBoundaryTests.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`

**Interfaces:**
- Consumes: Task 1 preference and route helpers.
- Consumes: Task 2 buyer/seller root page types and factory.
- Produces: native `buying`, `selling`, and `account` Shell roots.
- Produces: pushed `ActivityPage` route.

- [ ] **Step 1: Replace the old Shell structure test with a failing native-root test**

```csharp
[Fact]
public void Shell_exposes_buy_sell_account_and_pushes_activity()
{
    var shell = Load("Ui", "AppShell.xaml");
    var tabBar = shell.Descendants()
        .Single(element => element.Name.LocalName == "TabBar");
    var roots = tabBar.Elements()
        .Where(element => element.Name.LocalName == "ShellContent")
        .ToArray();

    Assert.Equal(new[] { "ซื้อ", "ขาย", "บัญชี" },
        roots.Select(root => AttributeValue(root, "Title")));
    Assert.Equal(new[] { "buying", "selling", "account" },
        roots.Select(root => AttributeValue(root, "Route")));
    Assert.DoesNotContain(roots,
        root => AttributeValue(root, "Route") == "activity");

    var shellCode = File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "Ui", "AppShell.xaml.cs"));
    Assert.Contains("Routing.RegisterRoute(nameof(ActivityPage)", shellCode);
}
```

Delete the old assertion that authenticated home exists outside the tab bar.

- [ ] **Step 2: Add failing preferred-startup tests**

Update the startup test constructor with a stub:

```csharp
private sealed class WorkspacePreferenceStub(TransactionRoleRoute role)
    : IWorkspaceRolePreference
{
    public TransactionRoleRoute GetPreferredRole() => role;
    public void SavePreferredRole(TransactionRoleRoute value) =>
        throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
}
```

Add:

```csharp
[Theory]
[InlineData(TransactionRoleRoute.Buying, "//main/buying")]
[InlineData(TransactionRoleRoute.Selling, "//main/selling")]
public async Task Session_routes_to_preferred_workspace(
    TransactionRoleRoute role, string expected)
{
    var coordinator = new StartupCoordinator(
        new AuthenticationStub(() => Task.FromResult(true)),
        new PendingRegistrationStoreStub(false),
        new MotionPreferenceStub(true),
        new WorkspacePreferenceStub(role));

    var result = await coordinator.StartAsync(_ => Task.CompletedTask);
    Assert.Equal(expected, result.Route);
}
```

Run the two affected test classes. Expected: FAIL.

Also extend the existing startup/static-app test so pending deep links remain ordered after the preferred root is installed:

```csharp
var app = File.ReadAllText(Path.Combine(
    AppContext.BaseDirectory, "Ui", "App.xaml.cs"));
Assert.True(
    app.IndexOf("await shell.GoToAsync(result.Route", StringComparison.Ordinal) <
    app.IndexOf("deepLinks.ResumePendingAsync", StringComparison.Ordinal));
```

- [ ] **Step 3: Replace the Shell roots**

Use this authenticated TabBar shape:

```xml
<TabBar Route="main">
    <ShellContent
        Title="ซื้อ"
        Icon="nav_buy.png"
        Route="buying"
        Shell.NavBarIsVisible="False"
        ContentTemplate="{DataTemplate pages:BuyingTransactionsPage}" />
    <ShellContent
        Title="ขาย"
        Icon="nav_sell.png"
        Route="selling"
        Shell.NavBarIsVisible="False"
        ContentTemplate="{DataTemplate pages:SellingTransactionsPage}" />
    <ShellContent
        Title="บัญชี"
        Icon="nav_account.png"
        Route="account"
        Shell.NavBarIsVisible="False"
        ContentTemplate="{DataTemplate pages:AccountPage}" />
</TabBar>
```

Remove the authenticated `home` ShellContent and Activity ShellContent. Keep anonymous `welcome` and `signin` roots unchanged.

- [ ] **Step 4: Add role icons in the existing line style**

`nav_buy.svg`:

```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">
  <path d="M16 5v13m-5-5 5 5 5-5" fill="none" stroke="#667085" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"/>
  <path d="M7 21v2a3 3 0 0 0 3 3h12a3 3 0 0 0 3-3v-2" fill="none" stroke="#667085" stroke-width="2.4" stroke-linecap="round"/>
</svg>
```

`nav_sell.svg` uses the same tray and reverses the arrow:

```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">
  <path d="M16 19V6m-5 5 5-5 5 5" fill="none" stroke="#667085" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"/>
  <path d="M7 21v2a3 3 0 0 0 3 3h12a3 3 0 0 0 3-3v-2" fill="none" stroke="#667085" stroke-width="2.4" stroke-linecap="round"/>
</svg>
```

- [ ] **Step 5: Register Activity and save actual root selections**

Inject `IWorkspaceRolePreference` into `AppShell`. Register Activity once, then handle Shell navigation:

```csharp
public AppShell(IWorkspaceRolePreference workspaceRoles)
{
    InitializeComponent();
    Routing.RegisterRoute(nameof(ActivityPage), typeof(ActivityPage));
    Navigated += (_, args) =>
    {
        var route = args.Current.Location.OriginalString;
        if (AuthenticatedHomeRoutes.TryParseRoot(route, out var role))
            workspaceRoles.SavePreferredRole(role);
    };
}
```

Keep the existing pushed registrations for `SignUpPage`, `VerifyCodePage`, `CompleteRegistrationPage`, `TransactionDetailPage`, `ShippingLabelPage`, `CreateOfferPage`, `SellerOfferPage`, `PayoutSettingsPage`, `ChangeEmailPage`, `VerifyEmailChangePage`, `ChangeNamePage`, and `VerifyNameChangePage`.

Because current deep links push a detail or seller-offer route rather than selecting a role root, they do not change the preference.

- [ ] **Step 6: Route authenticated entry points directly**

Inject `IWorkspaceRolePreference` into `StartupCoordinator`, `VerifyCodeViewModel`, and `CompleteRegistrationViewModel`.

For a valid session, return:

```csharp
AuthenticatedHomeRoutes.Root(workspaceRoles.GetPreferredRole())
```

After successful sign-in or registration, navigate to the same expression. In `App.xaml.cs`, replace the exact `Home` equality check with:

```csharp
if (AuthenticatedHomeRoutes.IsAuthenticatedRoot(result.Route))
    _ = InitializeAuthenticatedServicesAsync();
```

- [ ] **Step 7: Update dependency injection**

```csharp
builder.Services.AddSingleton<IWorkspaceRolePreference, WorkspaceRolePreference>();
builder.Services.AddSingleton<TransactionWorkspaceViewModelFactory>();
builder.Services.AddSingleton<BuyingTransactionsPage>();
builder.Services.AddSingleton<SellingTransactionsPage>();
builder.Services.AddTransient<ActivityPage>();
```

Remove singleton registrations for `TransactionsViewModel`, `TransactionsPage`, and the old root `ActivityPage`. Do not remove authenticated-home registrations until Task 6.

Extend the existing sign-out session-boundary test with:

```csharp
var workspaceRoles = new WorkspaceRolePreference(session);
workspaceRoles.SavePreferredRole(TransactionRoleRoute.Selling);

await account.SignOutAsync();

Assert.Equal(
    TransactionRoleRoute.Buying,
    workspaceRoles.GetPreferredRole());
```

Use its existing `account` and `session` variables and keep its current assertions. This proves explicit logout clears the presentation preference through the existing `session.Reset()` call without changing `AccountViewModel`.

- [ ] **Step 8: Run focused and full mobile tests**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~StartupCoordinatorTests|FullyQualifiedName~Shell_exposes_buy_sell_account"
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore
```

Expected: PASS.

- [ ] **Step 9: Commit Task 3**

```bash
git add src/Toklong.Mobile/AppShell.xaml src/Toklong.Mobile/AppShell.xaml.cs src/Toklong.Mobile/App.xaml.cs src/Toklong.Mobile/Core/StartupCoordinator.cs src/Toklong.Mobile/ViewModels/VerifyCodeViewModel.cs src/Toklong.Mobile/ViewModels/CompleteRegistrationViewModel.cs src/Toklong.Mobile/MauiProgram.cs src/Toklong.Mobile/Resources/Images/nav_buy.svg src/Toklong.Mobile/Resources/Images/nav_sell.svg tests/Toklong.Mobile.Core.Tests/StartupCoordinatorTests.cs tests/Toklong.Mobile.Core.Tests/ViewModelSessionBoundaryTests.cs tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs
git commit -m "feat: add native buy and sell roots"
```

---

### Task 4: Shared Root Header and Action-First Workspace UI

**Files:**
- Create: `src/Toklong.Mobile/Controls/RootPageHeaderView.xaml`
- Create: `src/Toklong.Mobile/Controls/RootPageHeaderView.xaml.cs`
- Modify: `src/Toklong.Mobile/Pages/TransactionsPage.xaml`
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj`

**Interfaces:**
- Produces: bindable `Title`, `Subtitle`, and `AccentColor` properties on `RootPageHeaderView`.
- Produces: built-in Activity navigation using `nameof(ActivityPage)`.
- Consumes: fixed-role `IsBuying`, `IsSelling`, `ModeTitle`, seller summaries, spotlight, and existing commands.

- [ ] **Step 1: Write failing header and workspace structure tests**

```csharp
[Fact]
public void Fixed_role_workspace_has_header_and_no_role_switch()
{
    var page = Load("Ui", "Pages", "TransactionsPage.xaml");
    Assert.Contains(page.Descendants(), element =>
        element.Name.LocalName == "RootPageHeaderView" &&
        AttributeValue(element, "Title") == "{Binding ModeTitle}");
    Assert.DoesNotContain(page.Descendants(), element =>
        AttributeValue(element, "AutomationId") == "TransactionRoleModeSwitch");

    var create = page.Descendants(Maui + "Button").Single(button =>
        AttributeValue(button, "Command") == "{Binding CreateOfferCommand}");
    Assert.Equal("{Binding IsBuying}", AttributeValue(create, "IsVisible"));
    Assert.Equal("Fill", AttributeValue(create, "HorizontalOptions"));

    var spotlight = page.Descendants(Maui + "Border").Single(element =>
        AttributeValue(element, "AutomationId") == "ActionSpotlightCard");
    var sellerSummary = page.Descendants(Maui + "Grid").Single(element =>
        AttributeValue(element, "AutomationId") == "SellerWorkSummary");
    var order = page.Descendants().ToList();
    Assert.True(order.IndexOf(create) < order.IndexOf(spotlight));
    Assert.True(order.IndexOf(sellerSummary) < order.IndexOf(spotlight));
}
```

Add a static code assertion that `RootPageHeaderView.xaml.cs` navigates to `nameof(ActivityPage)` and does not render unread state.

- [ ] **Step 2: Run the new test and verify failure**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~Fixed_role_workspace_has_header_and_no_role_switch"
```

Expected: FAIL.

- [ ] **Step 3: Create the shared header control**

Start the control with `<ContentView x:Class="Toklong.Mobile.Controls.RootPageHeaderView" x:Name="Root" ...>` and use a two-column header with a title stack and transparent bell button:

```xml
<Grid ColumnDefinitions="*,Auto" ColumnSpacing="12">
    <VerticalStackLayout Spacing="3">
        <Label Style="{StaticResource Eyebrow}" Text="{Binding Subtitle, Source={x:Reference Root}}" />
        <Label Style="{StaticResource PageTitle}" Text="{Binding Title, Source={x:Reference Root}}" />
    </VerticalStackLayout>
    <Button
        Grid.Column="1"
        AutomationId="OpenActivityButton"
        WidthRequest="44"
        HeightRequest="44"
        Padding="10"
        BackgroundColor="White"
        BorderColor="{Binding AccentColor, Source={x:Reference Root}}"
        BorderWidth="1"
        CornerRadius="14"
        Clicked="OnActivityClicked"
        ImageSource="nav_activity.png"
        SemanticProperties.Description="กิจกรรม"
        SemanticProperties.Hint="เปิดการอัปเดตจากรายการซื้อและขาย" />
</Grid>
```

In code-behind define the bindable properties and navigation exactly as follows:

```csharp
using Toklong.Mobile.Pages;

namespace Toklong.Mobile.Controls;

public partial class RootPageHeaderView : ContentView
{
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(RootPageHeaderView), "");
    public static readonly BindableProperty SubtitleProperty = BindableProperty.Create(
        nameof(Subtitle), typeof(string), typeof(RootPageHeaderView), "");
    public static readonly BindableProperty AccentColorProperty = BindableProperty.Create(
        nameof(AccentColor), typeof(Color), typeof(RootPageHeaderView), Colors.Blue);

    public RootPageHeaderView() => InitializeComponent();

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public Color AccentColor
    {
        get => (Color)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

private async void OnActivityClicked(object? sender, EventArgs args) =>
    await Shell.Current.GoToAsync(nameof(ActivityPage));
}
```

Do not add badge markup.

- [ ] **Step 4: Recompose `TransactionsPage.xaml`**

- Replace `รายการของคุณ`, its subtitle, and `TransactionRoleModeSwitch` with `RootPageHeaderView` using `Title="{Binding ModeTitle}"`, `Subtitle="{Binding ModeSubtitle}"`, and `AccentColor="{Binding WorkspaceAccentColor}"`.
- Move the existing buyer `+ สร้างดีลซื้อ` button immediately below the header, make it full width, and keep `IsVisible="{Binding IsBuying}"`.
- Keep seller summary before the seller spotlight.
- Keep the current spotlight, buyer filters, seller filters, transaction cards, pull refresh, five-second visible refresh, loading, and error bindings.
- Keep all previously added accessibility overlay buttons and semantic descriptions.
- Change only the no-record copy at the all-filter root to `ยังไม่มีรายการซื้อ` or `ยังไม่มีรายการขาย`; filtered empty states may remain `ยังไม่มีรายการในสถานะนี้`.

- [ ] **Step 5: Link the new control into static tests**

Add `RootPageHeaderView.xaml` and `.xaml.cs` to `Toklong.Mobile.Core.Tests.csproj` as `None` files under `Ui/Controls/` so static tests can inspect them.

- [ ] **Step 6: Run tests and iOS XAML compile**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj -p:TargetFrameworks=net10.0-ios -f net10.0-ios -c Debug -p:RuntimeIdentifier=iossimulator-arm64 --no-restore -t:Compile
```

Expected: tests PASS; iOS compile succeeds. The existing media-picker deprecation warning may remain.

- [ ] **Step 7: Commit Task 4**

```bash
git add src/Toklong.Mobile/Controls/RootPageHeaderView.xaml src/Toklong.Mobile/Controls/RootPageHeaderView.xaml.cs src/Toklong.Mobile/Pages/TransactionsPage.xaml tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj
git commit -m "feat: add action-first transaction roots"
```

---

### Task 5: Account Header and Pushed Activity Hub

**Files:**
- Modify: `src/Toklong.Mobile/Pages/AccountPage.xaml`
- Modify: `src/Toklong.Mobile/Pages/ActivityPage.xaml`
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`

**Interfaces:**
- Consumes: `RootPageHeaderView` and registered `ActivityPage` route.
- Preserves: existing `AccountViewModel`, `ActivityViewModel`, refresh, feed-item opening, and error behavior.

- [ ] **Step 1: Write failing Account and Activity chrome tests**

```csharp
[Fact]
public void Account_exposes_global_activity_header()
{
    var account = Load("Ui", "Pages", "AccountPage.xaml");
    Assert.Contains(account.Descendants(), element =>
        element.Name.LocalName == "RootPageHeaderView" &&
        AttributeValue(element, "Title") == "บัญชี");
}

[Fact]
public void Activity_is_a_pushed_page_with_back_chrome_and_no_tab_bar()
{
    var activity = Load("Ui", "Pages", "ActivityPage.xaml");
    Assert.Equal("กิจกรรม", AttributeValue(activity.Root!, "Title"));
    Assert.Equal("True", AttributeValue(activity.Root!, "Shell.NavBarIsVisible"));
    Assert.Equal("False", AttributeValue(activity.Root!, "Shell.TabBarIsVisible"));
    Assert.Equal("{Binding Items}", AttributeValue(
        activity.Descendants(Maui + "CollectionView").Single(), "ItemsSource"));
}
```

- [ ] **Step 2: Run the two tests and verify failure**

Run the mobile test project filtered to the two test names.

Expected: FAIL.

- [ ] **Step 3: Replace Account's duplicated top labels**

At the start of `AccountPage` content, replace the existing eyebrow border and `บัญชี` title with:

```xml
<controls:RootPageHeaderView
    Title="บัญชี"
    Subtitle="ข้อมูลของคุณ"
    AccentColor="{StaticResource BrandBlue}" />
```

Add the controls namespace if absent. Leave every account card, command, and binding unchanged.

- [ ] **Step 4: Convert Activity to pushed-page chrome**

Set on the Activity root:

```xml
Title="กิจกรรม"
Shell.NavBarIsVisible="True"
Shell.TabBarIsVisible="False"
```

Keep the existing `RefreshView`, `Items`, `RefreshCommand`, `OpenCommand`, loading/error copy, and item template. Remove only a duplicate large `การแจ้งเตือน` page title if it competes with the native title; retain the explanatory `อัปเดตจากทุกรายการ` badge.

- [ ] **Step 5: Run mobile tests and iOS compile**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj -p:TargetFrameworks=net10.0-ios -f net10.0-ios -c Debug -p:RuntimeIdentifier=iossimulator-arm64 --no-restore -t:Compile
```

Expected: PASS and compile success.

- [ ] **Step 6: Commit Task 5**

```bash
git add src/Toklong.Mobile/Pages/AccountPage.xaml src/Toklong.Mobile/Pages/ActivityPage.xaml tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs
git commit -m "feat: move activity into global root header"
```

---

### Task 6: Remove the Obsolete Authenticated Chooser and Update Contracts

**Files:**
- Delete: `src/Toklong.Mobile/Pages/AuthenticatedHomePage.xaml`
- Delete: `src/Toklong.Mobile/Pages/AuthenticatedHomePage.xaml.cs`
- Delete: `src/Toklong.Mobile/ViewModels/AuthenticatedHomeViewModel.cs`
- Delete: `tests/Toklong.Mobile.Core.Tests/AuthenticatedHomeViewModelTests.cs`
- Modify: `src/Toklong.Mobile/MauiProgram.cs`
- Modify: `src/Toklong.Mobile/Core/AuthenticatedHomeRoutes.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj`
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`
- Modify: `docs/02_UI_UX_AND_CONTENT_SPEC.md`
- Modify: `docs/05_ACCEPTANCE_TESTS.md`

**Interfaces:**
- Consumes: direct authenticated roots from Task 3.
- Removes: obsolete `AuthenticatedHomeRoutes.Home`, home page/ViewModel registrations, and two-card chooser tests.
- Produces: binding product/acceptance documentation for native role roots.

- [ ] **Step 1: Add failing obsolete-path assertions**

```csharp
[Fact]
public void Authenticated_navigation_has_no_second_role_chooser()
{
    var shell = Load("Ui", "AppShell.xaml");
    var program = File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "Ui", "MauiProgram.cs"));

    Assert.DoesNotContain(shell.Descendants(), element =>
        AttributeValue(element, "Route") == "home");
    Assert.DoesNotContain("AuthenticatedHomePage", program);
    Assert.DoesNotContain("AuthenticatedHomeViewModel", program);
}
```

Run the test. Expected: FAIL until DI registrations and obsolete code are removed.

- [ ] **Step 2: Remove obsolete source and registration**

Delete the four obsolete implementation/test files, remove their DI registrations, remove the home ViewModel compile link from the test project, and delete `AuthenticatedHomeRoutes.Home` plus temporary query-role wrappers now that all callers use `Root`.

Search for leftovers:

```bash
rg -n "AuthenticatedHomePage|AuthenticatedHomeViewModel|AuthenticatedHomeRoutes\.Home|TransactionRoleModeSwitch|TryParseRole|ApplyRoleNavigation" src tests
```

Expected: no production references and only intentional historical/spec references.

- [ ] **Step 3: Replace the authenticated navigation section in the UI spec**

Replace `Authenticated role home` and `Transaction list modes` with exact requirements:

```markdown
### Authenticated root navigation

The native bottom bar contains `ซื้อ`, `ขาย`, and `บัญชี` in that order.
`กิจกรรม` is a top-right action on all three roots and opens as a pushed page
with Back navigation. The transaction roots do not render another `ซื้อ | ขาย`
switch.

First authenticated use opens `ซื้อ`. Later ordinary launches restore the last
explicitly selected Buy/Sell root. `บัญชี` does not replace that preference,
explicit logout clears it, and deep links take precedence without overwriting it.
```

Retain the existing buyer-first offer wizard and seller workspace requirements.

- [ ] **Step 4: Replace affected acceptance scenarios**

Update A0.0.0.3 and A0.0.4.4 so they assert:

- native Buy and Sell roots never mix records;
- first use opens Buy;
- last selected Buy/Sell root restores;
- Account does not change the preference;
- Activity opens from every root and returns correctly;
- the in-page role switch and authenticated chooser do not exist; and
- deep links still open the exact authorized destination.

Update A0.0.6 to refer to the selected Buy or Sell native root while preserving its scroll-offset, refresh, and bottom-tab requirements.

- [ ] **Step 5: Run mobile tests and documentation consistency searches**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore
rg -n "authenticated home|Authenticated role home|one top-level.*ซื้อ.*ขาย.*switch|TransactionRoleModeSwitch" docs/02_UI_UX_AND_CONTENT_SPEC.md docs/05_ACCEPTANCE_TESTS.md
git diff --check
```

Expected: tests PASS; search returns no active obsolete requirement; diff check is clean.

- [ ] **Step 6: Commit Task 6**

```bash
git add src/Toklong.Mobile/Pages/AuthenticatedHomePage.xaml src/Toklong.Mobile/Pages/AuthenticatedHomePage.xaml.cs src/Toklong.Mobile/ViewModels/AuthenticatedHomeViewModel.cs src/Toklong.Mobile/MauiProgram.cs src/Toklong.Mobile/Core/AuthenticatedHomeRoutes.cs tests/Toklong.Mobile.Core.Tests/AuthenticatedHomeViewModelTests.cs tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs docs/02_UI_UX_AND_CONTENT_SPEC.md docs/05_ACCEPTANCE_TESTS.md
git commit -m "docs: adopt native buy and sell navigation"
```

---

### Task 7: Final Regression and Platform Verification

**Files:**
- Modify only if a verification failure identifies a navigation-scoped defect.

**Interfaces:**
- Verifies all outputs from Tasks 1–6.
- Does not authorize unrelated cleanup or refactoring.

- [ ] **Step 1: Validate XML and whitespace**

```bash
xmllint --noout src/Toklong.Mobile/AppShell.xaml src/Toklong.Mobile/Controls/RootPageHeaderView.xaml src/Toklong.Mobile/Pages/TransactionsPage.xaml src/Toklong.Mobile/Pages/AccountPage.xaml src/Toklong.Mobile/Pages/ActivityPage.xaml
git diff --check
```

Expected: both commands succeed with no output.

- [ ] **Step 2: Run every test project**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore --logger "console;verbosity=minimal"
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj --no-restore --logger "console;verbosity=minimal"
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj --no-restore --logger "console;verbosity=minimal"
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj --no-restore --logger "console;verbosity=minimal"
dotnet test tests/Toklong.Crm.Tests/Toklong.Crm.Tests.csproj --no-restore --logger "console;verbosity=minimal"
dotnet test tests/Toklong.Shippop.Certification/Toklong.Shippop.Certification.csproj --no-restore --logger "console;verbosity=minimal"
```

Expected: zero failures; environment-gated PostgreSQL and live Shippop cases may remain skipped.

- [ ] **Step 3: Compile iOS XAML and C#**

```bash
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj -p:TargetFrameworks=net10.0-ios -f net10.0-ios -c Debug -p:RuntimeIdentifier=iossimulator-arm64 --no-restore -t:Compile
```

Expected: build succeeds; the existing `IMediaPicker.PickPhotoAsync` deprecation warning may remain.

- [ ] **Step 4: Check and compile Android when the workload is available**

```bash
dotnet workload list
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj -p:TargetFrameworks=net10.0-android -f net10.0-android -c Debug --no-restore -t:Compile
```

Expected on a configured machine: compile succeeds. On the current machine, record the known `maui-android` workload/SDK absence rather than changing product code or installing workloads without approval.

- [ ] **Step 5: Perform the two-device navigation smoke**

On one narrow iPhone and one narrow Android device/simulator, verify:

1. bottom order is Buy, Sell, Account;
2. first use opens Buy;
3. Sell restores after relaunch when explicitly selected;
4. Account does not replace Sell as the remembered role;
5. Activity opens and returns from every root;
6. Buy and Sell retain separate scroll/filter/loading state;
7. a transaction deep link opens its authorized destination without changing the saved role;
8. VoiceOver/TalkBack announce each tab and the Activity bell once;
9. 200% text and long Thai deadlines do not clip; and
10. iOS home-indicator and Android gesture insets do not cover content.

- [ ] **Step 6: Review the final diff against scope**

```bash
git status --short
git diff --stat HEAD~6..HEAD
git log -7 --oneline
```

Confirm no backend transaction, payment, carrier, dispute, refund, payout, color, or font files changed. Confirm the pre-existing UI clarity edits were preserved rather than overwritten.

- [ ] **Step 7: Commit only verification fixes, if any**

If verification required navigation-scoped corrections, stage their exact files and commit:

```bash
git commit -m "fix: harden buy sell root navigation"
```

If verification is clean, create no empty commit.

---

## Completion Report Requirements

At handoff, report:

1. the Buy/Sell/Account Shell structure and global Activity behavior;
2. preferred-role restoration, logout clearing, and deep-link precedence;
3. independent buyer/seller ViewModel and refresh state;
4. tests added or updated and exact pass/skip totals;
5. iOS and Android validation status, including missing local workload limits;
6. confirmation that domain transitions and provider behavior did not change;
7. assumptions and any remaining device-only findings; and
8. the next smallest vertical slice, which is authoritative unread Activity state only if product requirements approve it.
