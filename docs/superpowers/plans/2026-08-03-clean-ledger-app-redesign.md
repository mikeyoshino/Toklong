# Clean Ledger App Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the authenticated mobile shell with the approved `ซื้อ · สร้างดีล · ขาย` Clean Ledger experience and migrate the existing mobile screens to its shared visual system without changing transaction truth or authorization.

**Architecture:** Keep the existing separate Buy and Sell pages and `TransactionsViewModel` instances, but replace the native Shell `TabBar` with two hidden top-level Shell roots wrapped by a reusable `AuthenticatedRootFrame`. Centralize the semantic palette and transaction-detail surfaces, reuse the existing authoritative `TransactionStatePresenter`/`AppTransaction` presentation, and keep every new navigation action presentation-only.

**Tech Stack:** .NET 10, .NET MAUI XAML, C# 14, xUnit 2.9.3, existing Shell navigation, existing `IMobileAnalytics`, Noto Sans Thai.

## Global Constraints

- Read and preserve the binding requirements in `docs/00_PRODUCT_BRIEF.md` through `docs/07_REGULATORY_SOURCE_NOTES.md` before implementation.
- MVP remains buyer-first: `สร้างดีล` always starts a buyer-created private offer and never a seller listing.
- The center action visible label is `สร้างดีล`, its accessibility name is `สร้างข้อเสนอซื้อ`, and its destination heading is `สร้างดีลซื้อ`.
- Buy and Sell lists remain role-isolated and ordered newest first within the selected filter.
- Ordinary authenticated startup opens Buy; authorized deep links still take precedence.
- Activity and Account are pushed pages with Back navigation and no authenticated bottom action bar.
- No navigation, animation, guidance, or client event may mark payment, delivery, refund, or payout successful.
- Money remains integer satang from server-backed projections; XAML and new presentation controls perform no arithmetic.
- Seller screens must not expose buyer-only parcel-protection values or buyer total.
- Contextual guidance is deterministic one-way presentation. It has no prompt, chat history, AI request, state transition, or binding dispute authority.
- Approved consumer-facing provider instructions such as Stripe PromptPay refund action copy remain allowed; raw provider state, webhook, reconciliation, hash, and schema terminology remain hidden.
- Minimum touch target is `44 × 44` points; the raised create button is at least `64 × 64` points.
- Root transition is `180 ms` with at most `6` points of vertical movement; create press response is `120 ms`, scaling `1.0 → 0.96 → 1.0`.
- Reduced Motion removes both effects and adds no delay.
- No new NuGet package or backend endpoint is required.
- Each task must leave the mobile project buildable and the mobile core test project passing.

---

## Planned File Structure

### New focused units

- `src/Toklong.Mobile/Core/CleanLedgerPalette.cs` — string constants shared by pure presentation models and tests.
- `src/Toklong.Mobile/Core/WorkspaceNavigationAnalytics.cs` — approved low-cardinality workspace and create-entry analytics.
- `src/Toklong.Mobile/Controls/AuthenticatedRootFrame.xaml(.cs)` — content slot plus fixed Buy/Create/Sell action bar and reduced-motion behavior.
- `src/Toklong.Mobile/Controls/RoleTransactionHeader.xaml(.cs)` — role-labelled buyer/seller transaction header.
- `src/Toklong.Mobile/Controls/DealGuidanceCard.xaml(.cs)` — renders existing authoritative status guidance without commands.
- `tests/Toklong.Mobile.Core.Tests/CleanLedgerPaletteTests.cs` — exact palette contract.
- `tests/Toklong.Mobile.Core.Tests/WorkspaceNavigationAnalyticsTests.cs` — analytics allow-list contract.

### Existing units retained

- `BuyingTransactionsPage` and `SellingTransactionsPage` remain separate singleton pages.
- `TransactionWorkspaceViewModelFactory` continues creating distinct fixed-role ViewModels.
- `TransactionStatePresenter`, `AppTransaction.StatusGuidance`, `TransactionFilter`, and `SellerWorkspaceState` remain the only state-to-presentation classifiers.
- The existing type-selection page and three-step `CreateOfferViewModel` continue owning offer creation.

---

### Task 1: Semantic Clean Ledger Palette and Shared Styles

**Files:**
- Create: `src/Toklong.Mobile/Core/CleanLedgerPalette.cs`
- Modify: `src/Toklong.Mobile/Core/SellerColorPalette.cs`
- Modify: `src/Toklong.Mobile/Core/AppTransaction.cs:229-272`
- Modify: `src/Toklong.Mobile/Theming/SellerColorPaletteColors.cs`
- Modify: `src/Toklong.Mobile/App.xaml:10-240`
- Create: `tests/Toklong.Mobile.Core.Tests/CleanLedgerPaletteTests.cs`
- Delete: `tests/Toklong.Mobile.Core.Tests/SellerGraphiteNavyColorTests.cs`
- Create: `tests/Toklong.Mobile.Core.Tests/CleanLedgerRoleColorTests.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`

**Interfaces:**
- Consumes: existing `AppTransaction` role-presentation properties and MAUI resource dictionary.
- Produces: `CleanLedgerPalette` constants and resource keys `CleanLedgerRootBackground`, `TrustNavy`, `BuyerBlue`, `BuyerBlueSoft`, `SellerIndigo`, `SellerIndigoSoft`, `VerifiedMint`, `DeadlineRust`, `LedgerSurfaceCard`, `LedgerPrimaryButton`, and `LedgerSummaryCard`.

- [ ] **Step 1: Write failing palette and resource tests**

```csharp
[Fact]
public void Clean_ledger_palette_matches_the_approved_tokens()
{
    Assert.Equal("#F6F8FA", CleanLedgerPalette.MistBackground);
    Assert.Equal("#12364F", CleanLedgerPalette.TrustNavy);
    Assert.Equal("#1988D3", CleanLedgerPalette.BuyerBlue);
    Assert.Equal("#E9F6FF", CleanLedgerPalette.BuyerBlueSoft);
    Assert.Equal("#55508A", CleanLedgerPalette.SellerIndigo);
    Assert.Equal("#EFEDFB", CleanLedgerPalette.SellerIndigoSoft);
    Assert.Equal("#65C8B4", CleanLedgerPalette.VerifiedMint);
    Assert.Equal("#BD563A", CleanLedgerPalette.DeadlineRust);
    Assert.Equal("#112337", CleanLedgerPalette.Ink);
    Assert.Equal("#647589", CleanLedgerPalette.MutedInk);
    Assert.Equal("#DCE5EC", CleanLedgerPalette.Line);
}

[Fact]
public void App_resources_expose_semantic_clean_ledger_styles()
{
    var app = Load("Ui", "App.xaml");
    var keys = app.Descendants()
        .Select(element => AttributeValue(element, "Key"))
        .Where(value => value is not null)
        .ToHashSet(StringComparer.Ordinal);

    Assert.Contains("CleanLedgerRootBackground", keys);
    Assert.Contains("LedgerSurfaceCard", keys);
    Assert.Contains("LedgerPrimaryButton", keys);
    Assert.Contains("LedgerSummaryCard", keys);
}
```

- [ ] **Step 2: Run the focused tests and verify failure**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter "FullyQualifiedName~CleanLedgerPaletteTests|FullyQualifiedName~UiLayoutConsistencyTests.App_resources_expose_semantic_clean_ledger_styles"
```

Expected: FAIL because `CleanLedgerPalette` and the resource keys do not exist.

- [ ] **Step 3: Implement the semantic palette and role aliases**

```csharp
namespace Toklong.Mobile.Core;

public static class CleanLedgerPalette
{
    public const string MistBackground = "#F6F8FA";
    public const string TrustNavy = "#12364F";
    public const string BuyerBlue = "#1988D3";
    public const string BuyerBlueSoft = "#E9F6FF";
    public const string SellerIndigo = "#55508A";
    public const string SellerIndigoSoft = "#EFEDFB";
    public const string VerifiedMint = "#65C8B4";
    public const string DeadlineRust = "#BD563A";
    public const string Ink = "#112337";
    public const string MutedInk = "#647589";
    public const string Line = "#DCE5EC";
    public const string Surface = "#FFFFFF";
}
```

Update `SellerColorPalette` to use `SellerIndigo`/`SellerIndigoSoft`, with the approved gradient `#302D56 → #45416F → #55508A`. Update buyer role values in `AppTransaction` to `BuyerBlue`, `BuyerBlueSoft`, and the approved gradient `#12364F → #14608A → #1988D3`.

Add exact resources and styles to `App.xaml`:

```xml
<Color x:Key="CleanLedgerRootBackground">#F6F8FA</Color>
<Color x:Key="TrustNavy">#12364F</Color>
<Color x:Key="BuyerBlue">#1988D3</Color>
<Color x:Key="BuyerBlueSoft">#E9F6FF</Color>
<Color x:Key="SellerIndigo">#55508A</Color>
<Color x:Key="SellerIndigoSoft">#EFEDFB</Color>
<Color x:Key="VerifiedMint">#65C8B4</Color>
<Color x:Key="DeadlineRust">#BD563A</Color>

<Style x:Key="LedgerSurfaceCard" TargetType="Border">
    <Setter Property="BackgroundColor" Value="White" />
    <Setter Property="Stroke" Value="#DCE5EC" />
    <Setter Property="StrokeThickness" Value="1" />
    <Setter Property="StrokeShape" Value="RoundRectangle 18" />
</Style>
<Style x:Key="LedgerPrimaryButton" TargetType="Button">
    <Setter Property="BackgroundColor" Value="{StaticResource TrustNavy}" />
    <Setter Property="TextColor" Value="White" />
    <Setter Property="FontAttributes" Value="Bold" />
    <Setter Property="MinimumHeightRequest" Value="52" />
    <Setter Property="CornerRadius" Value="14" />
</Style>
<Style x:Key="LedgerSummaryCard" TargetType="Border">
    <Setter Property="StrokeThickness" Value="0" />
    <Setter Property="StrokeShape" Value="RoundRectangle 20" />
</Style>
```

- [ ] **Step 4: Run palette, presentation, and XAML resource tests**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter "FullyQualifiedName~CleanLedgerPaletteTests|FullyQualifiedName~CleanLedgerRoleColorTests|FullyQualifiedName~TransactionPresentationTests|FullyQualifiedName~UiLayoutConsistencyTests"
```

Expected: PASS with seller expectations updated to indigo and buyer expectations updated to Clean Ledger blue.

- [ ] **Step 5: Commit the palette slice**

```bash
git add src/Toklong.Mobile/Core/CleanLedgerPalette.cs src/Toklong.Mobile/Core/SellerColorPalette.cs src/Toklong.Mobile/Core/AppTransaction.cs src/Toklong.Mobile/Theming/SellerColorPaletteColors.cs src/Toklong.Mobile/App.xaml tests/Toklong.Mobile.Core.Tests/CleanLedgerPaletteTests.cs tests/Toklong.Mobile.Core.Tests/CleanLedgerRoleColorTests.cs tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs
git rm tests/Toklong.Mobile.Core.Tests/SellerGraphiteNavyColorTests.cs
git commit -m "feat: add clean ledger mobile palette"
```

### Task 2: Workspace Routes, Commands, and Safe Analytics

**Files:**
- Create: `src/Toklong.Mobile/Core/WorkspaceNavigationAnalytics.cs`
- Modify: `src/Toklong.Mobile/Core/AuthenticatedHomeRoutes.cs`
- Delete: `src/Toklong.Mobile/Core/WorkspaceRolePreference.cs`
- Modify: `src/Toklong.Mobile/ViewModels/TransactionsViewModel.cs`
- Modify: `src/Toklong.Mobile/App.xaml.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/AuthenticatedHomeRoutesTests.cs`
- Delete: `tests/Toklong.Mobile.Core.Tests/WorkspaceRolePreferenceTests.cs`
- Create: `tests/Toklong.Mobile.Core.Tests/WorkspaceNavigationAnalyticsTests.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/ViewModelSessionBoundaryTests.cs`

**Interfaces:**
- Consumes: `TransactionRoleRoute`, `RoleFilter`, `IMobileAnalytics`, `AsyncCommand`, and Shell navigation.
- Produces: `WorkspaceNavigationAnalytics.Opened(TransactionRoleRoute, WorkspaceNavigationSource)`, `WorkspaceNavigationAnalytics.CreateOfferStarted(RoleFilter)`, `OpenBuyingCommand`, and `OpenSellingCommand`.

- [ ] **Step 1: Write failing route, analytics, and single-flight command tests**

```csharp
[Theory]
[InlineData(TransactionRoleRoute.Buying, "//buying")]
[InlineData(TransactionRoleRoute.Selling, "//selling")]
public void Root_returns_hidden_shell_root(
    TransactionRoleRoute role,
    string expected) =>
    Assert.Equal(expected, AuthenticatedHomeRoutes.Root(role));

[Fact]
public void Workspace_events_contain_only_role_and_approved_source()
{
    var opened = WorkspaceNavigationAnalytics.Opened(
        TransactionRoleRoute.Selling,
        WorkspaceNavigationSource.BottomAction);
    var create = WorkspaceNavigationAnalytics.CreateOfferStarted(
        RoleFilter.Selling);

    Assert.Equal("workspace_opened", opened.Name);
    Assert.Equal(
        new Dictionary<string, string>
        {
            ["role"] = "selling",
            ["source"] = "bottom_action"
        },
        opened.Properties);
    Assert.Equal("create_offer_started", create.Name);
    Assert.Equal("selling", create.Properties["source_role"]);
}
```

Extend the existing fixed-role ViewModel test to execute the new root commands and assert only the opposite role navigates. Execute `CreateOfferCommand` twice before the first navigation completes and assert one `ProductTypeSelectionPage` route.

```csharp
[Fact]
public async Task Create_offer_entry_is_single_flight_from_selling()
{
    var navigationGate = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    Shell.Current = new Shell { Navigate = _ => navigationGate.Task };
    var viewModel = new TransactionsViewModel(
        new SequencedTransactionService(),
        new NoOpDeepLinks(),
        new RecordingAnalytics(),
        new AuthenticatedSessionBoundary(),
        RoleFilter.Selling);

    viewModel.CreateOfferCommand.Execute(null);
    viewModel.CreateOfferCommand.Execute(null);
    Assert.False(viewModel.CreateOfferCommand.CanExecute(null));

    navigationGate.SetResult();
    await Task.Yield();
    await Task.Yield();

    Assert.Equal(
        [nameof(ProductTypeSelectionPage)],
        Shell.Current.Routes);
}
```

- [ ] **Step 2: Run the focused tests and verify failure**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter "FullyQualifiedName~AuthenticatedHomeRoutesTests|FullyQualifiedName~WorkspaceNavigationAnalyticsTests|FullyQualifiedName~ViewModelSessionBoundaryTests.Fixed_role"
```

Expected: FAIL on old `//main/...` routes and missing navigation analytics/commands.

- [ ] **Step 3: Implement route constants, analytics, and commands**

```csharp
public enum WorkspaceNavigationSource
{
    Startup,
    BottomAction,
    DeepLink
}

public static class WorkspaceNavigationAnalytics
{
    public static MobileAnalyticsEvent Opened(
        TransactionRoleRoute role,
        WorkspaceNavigationSource source) =>
        new("workspace_opened", new Dictionary<string, string>
        {
            ["role"] = role == TransactionRoleRoute.Buying
                ? "buying"
                : "selling",
            ["source"] = source switch
            {
                WorkspaceNavigationSource.Startup => "startup",
                WorkspaceNavigationSource.BottomAction => "bottom_action",
                WorkspaceNavigationSource.DeepLink => "deep_link",
                _ => throw new ArgumentOutOfRangeException(nameof(source))
            }
        });

    public static MobileAnalyticsEvent CreateOfferStarted(RoleFilter role) =>
        new("create_offer_started", new Dictionary<string, string>
        {
            ["source_role"] = role == RoleFilter.Buying
                ? "buying"
                : "selling"
        });
}
```

Set `AuthenticatedHomeRoutes.Buying = "//buying"` and `Selling = "//selling"`; remove all preferred-role persistence. In `TransactionsViewModel`, create both root commands and replace the create command with `AsyncCommand`:

```csharp
OpenBuyingCommand = new AsyncCommand(
    () => OpenWorkspaceAsync(TransactionRoleRoute.Buying));
OpenSellingCommand = new AsyncCommand(
    () => OpenWorkspaceAsync(TransactionRoleRoute.Selling));
CreateOfferCommand = new AsyncCommand(OpenProductTypeSelectionAsync);

private async Task OpenWorkspaceAsync(TransactionRoleRoute target)
{
    if ((target == TransactionRoleRoute.Buying && IsBuying) ||
        (target == TransactionRoleRoute.Selling && IsSelling))
        return;

    analytics.Track(WorkspaceNavigationAnalytics.Opened(
        target,
        WorkspaceNavigationSource.BottomAction));
    await Shell.Current.GoToAsync(AuthenticatedHomeRoutes.Root(target));
}

private async Task OpenProductTypeSelectionAsync()
{
    analytics.Track(WorkspaceNavigationAnalytics.CreateOfferStarted(roleFilter));
    analytics.Track(CreateOfferAnalytics.TypeSelectionOpened());
    await Shell.Current.GoToAsync(nameof(ProductTypeSelectionPage));
}
```

Inject `IMobileAnalytics` into `App` and track `Startup` only after the authenticated Buy route is installed. Do not track product, transaction, identity, or amount fields.

- [ ] **Step 4: Run route, analytics, session-boundary, and startup tests**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter "FullyQualifiedName~AuthenticatedHomeRoutesTests|FullyQualifiedName~WorkspaceNavigationAnalyticsTests|FullyQualifiedName~MobileAnalyticsEventTests|FullyQualifiedName~ViewModelSessionBoundaryTests|FullyQualifiedName~StartupCoordinatorTests"
```

Expected: PASS; no preference-backed state remains.

- [ ] **Step 5: Commit the navigation model slice**

```bash
git add src/Toklong.Mobile/Core/WorkspaceNavigationAnalytics.cs src/Toklong.Mobile/Core/AuthenticatedHomeRoutes.cs src/Toklong.Mobile/ViewModels/TransactionsViewModel.cs src/Toklong.Mobile/App.xaml.cs tests/Toklong.Mobile.Core.Tests/AuthenticatedHomeRoutesTests.cs tests/Toklong.Mobile.Core.Tests/WorkspaceNavigationAnalyticsTests.cs tests/Toklong.Mobile.Core.Tests/ViewModelSessionBoundaryTests.cs
git rm src/Toklong.Mobile/Core/WorkspaceRolePreference.cs tests/Toklong.Mobile.Core.Tests/WorkspaceRolePreferenceTests.cs
git commit -m "feat: define clean ledger workspace navigation"
```

### Task 3: Custom Authenticated Root Frame and Shell Structure

**Files:**
- Create: `src/Toklong.Mobile/Controls/AuthenticatedRootFrame.xaml`
- Create: `src/Toklong.Mobile/Controls/AuthenticatedRootFrame.xaml.cs`
- Modify: `src/Toklong.Mobile/AppShell.xaml`
- Modify: `src/Toklong.Mobile/AppShell.xaml.cs`
- Modify: `src/Toklong.Mobile/Pages/TransactionsPage.xaml`
- Modify: `src/Toklong.Mobile/Pages/TransactionsPage.xaml.cs`
- Modify: `src/Toklong.Mobile/Pages/BuyingTransactionsPage.cs`
- Modify: `src/Toklong.Mobile/Pages/SellingTransactionsPage.cs`
- Modify: `src/Toklong.Mobile/Controls/RootPageHeaderView.xaml`
- Modify: `src/Toklong.Mobile/Controls/RootPageHeaderView.xaml.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj`
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`

**Interfaces:**
- Consumes: `RoleFilter`, `IStartupMotionPreference`, `OpenBuyingCommand`, `OpenSellingCommand`, and `CreateOfferCommand`.
- Produces: `AuthenticatedRootFrame.Body`, `.SelectedRole`, `.OpenBuyingCommand`, `.CreateOfferCommand`, `.OpenSellingCommand`, `.RevealAsync(bool)`, and two hidden Shell roots.

- [ ] **Step 1: Write failing XAML structure and accessibility tests**

```csharp
[Fact]
public void Authenticated_shell_uses_hidden_roots_without_native_tabbar()
{
    var shell = Load("Ui", "AppShell.xaml");
    Assert.Empty(shell.Descendants(Maui + "TabBar"));
    Assert.Contains(shell.Descendants(Maui + "ShellContent"), item =>
        AttributeValue(item, "Route") == "buying");
    Assert.Contains(shell.Descendants(Maui + "ShellContent"), item =>
        AttributeValue(item, "Route") == "selling");
}

[Fact]
public void Root_frame_exposes_buy_create_sell_in_that_order()
{
    var frame = Load("Ui", "Controls", "AuthenticatedRootFrame.xaml");
    var buttons = frame.Descendants(Maui + "Button").ToArray();
    Assert.Equal(
        ["ซื้อ", "สร้างข้อเสนอซื้อ", "ขาย"],
        buttons.Select(button =>
            AttributeValue(button, "SemanticProperties.Description")));
    Assert.Equal("64", AttributeValue(
        buttons[1], "MinimumWidthRequest"));
    Assert.Equal("64", AttributeValue(
        buttons[1], "MinimumHeightRequest"));
}

[Fact]
public void Root_header_shows_one_toklong_identity_and_two_global_actions()
{
    var header = Load("Ui", "Controls", "RootPageHeaderView.xaml");
    Assert.Single(header.Descendants(Maui + "Image"), image =>
        AttributeValue(image, "SemanticProperties.Description") ==
            "โลโก้ TOKLONG");
    Assert.Contains(header.Descendants(Maui + "Button"), button =>
        AttributeValue(button, "AutomationId") == "OpenActivityButton");
    Assert.Contains(header.Descendants(Maui + "Button"), button =>
        AttributeValue(button, "AutomationId") == "OpenAccountButton");
}
```

Add the new XAML and code-behind to the test project's copied `None` items.

- [ ] **Step 2: Run the XAML structure tests and verify failure**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter "FullyQualifiedName~UiLayoutConsistencyTests.Authenticated_shell|FullyQualifiedName~UiLayoutConsistencyTests.Root_frame"
```

Expected: FAIL because the native `TabBar` still exists and the frame files are absent.

- [ ] **Step 3: Implement the root frame and hidden Shell roots**

Use this XAML structure (retain exact accessibility order):

```xml
<ContentView x:Class="Toklong.Mobile.Controls.AuthenticatedRootFrame"
             x:Name="Root"
             xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:core="clr-namespace:Toklong.Mobile.Core">
    <Grid RowDefinitions="*,Auto">
        <ContentView x:Name="BodyHost"
                     Content="{Binding Body, Source={x:Reference Root}}" />
        <Border Grid.Row="1" Padding="18,8"
                BackgroundColor="#F7FFFFFF"
                Stroke="#DCE5EC">
            <Grid ColumnDefinitions="*,*,*">
                <Button x:Name="BuyButton" Text="ซื้อ" ImageSource="nav_buy.png"
                        ContentLayout="Top,4"
                        MinimumHeightRequest="52"
                        Command="{Binding OpenBuyingCommand, Source={x:Reference Root}}"
                        SemanticProperties.Description="ซื้อ" />
                <Grid Grid.Column="1" RowDefinitions="Auto,Auto"
                      TranslationY="-24">
                    <Button x:Name="CreateButton"
                            MinimumWidthRequest="64"
                            MinimumHeightRequest="64"
                            Text="+"
                            Command="{Binding CreateOfferCommand, Source={x:Reference Root}}"
                            Pressed="OnCreatePressed"
                            Released="OnCreateReleased"
                            SemanticProperties.Description="สร้างข้อเสนอซื้อ"
                            SemanticProperties.Hint="เริ่มสร้างข้อเสนอซื้อส่วนตัว" />
                    <Label Grid.Row="1" Text="สร้างดีล"
                           InputTransparent="True"
                           AutomationProperties.IsInAccessibleTree="False" />
                </Grid>
                <Button x:Name="SellButton" Grid.Column="2" Text="ขาย" ImageSource="nav_sell.png"
                        ContentLayout="Top,4"
                        MinimumHeightRequest="52"
                        Command="{Binding OpenSellingCommand, Source={x:Reference Root}}"
                        SemanticProperties.Description="ขาย" />
            </Grid>
        </Border>
    </Grid>
</ContentView>
```

Implement bindable properties with exact names and motion methods:

```csharp
public static readonly BindableProperty BodyProperty =
    BindableProperty.Create(
        nameof(Body),
        typeof(View),
        typeof(AuthenticatedRootFrame));
public static readonly BindableProperty SelectedRoleProperty =
    BindableProperty.Create(
        nameof(SelectedRole),
        typeof(RoleFilter),
        typeof(AuthenticatedRootFrame),
        RoleFilter.Buying,
        propertyChanged: static (bindable, _, _) =>
            ((AuthenticatedRootFrame)bindable).UpdateSelectedState());
public static readonly BindableProperty OpenBuyingCommandProperty =
    BindableProperty.Create(
        nameof(OpenBuyingCommand),
        typeof(ICommand),
        typeof(AuthenticatedRootFrame));
public static readonly BindableProperty CreateOfferCommandProperty =
    BindableProperty.Create(
        nameof(CreateOfferCommand),
        typeof(ICommand),
        typeof(AuthenticatedRootFrame));
public static readonly BindableProperty OpenSellingCommandProperty =
    BindableProperty.Create(
        nameof(OpenSellingCommand),
        typeof(ICommand),
        typeof(AuthenticatedRootFrame));

public View? Body
{
    get => (View?)GetValue(BodyProperty);
    set => SetValue(BodyProperty, value);
}
public RoleFilter SelectedRole
{
    get => (RoleFilter)GetValue(SelectedRoleProperty);
    set => SetValue(SelectedRoleProperty, value);
}
public ICommand? OpenBuyingCommand
{
    get => (ICommand?)GetValue(OpenBuyingCommandProperty);
    set => SetValue(OpenBuyingCommandProperty, value);
}
public ICommand? CreateOfferCommand
{
    get => (ICommand?)GetValue(CreateOfferCommandProperty);
    set => SetValue(CreateOfferCommandProperty, value);
}
public ICommand? OpenSellingCommand
{
    get => (ICommand?)GetValue(OpenSellingCommandProperty);
    set => SetValue(OpenSellingCommandProperty, value);
}

public async Task RevealAsync(bool reducedMotion)
{
    reducedMotionEnabled = reducedMotion;
    if (Body is null || reducedMotion)
        return;

    Body.Opacity = 0;
    Body.TranslationY = 6;
    await Task.WhenAll(
        Body.FadeToAsync(1, 180),
        Body.TranslateToAsync(0, 0, 180, Easing.CubicOut));
}

private async void OnCreatePressed(object? sender, EventArgs args)
{
    if (!reducedMotionEnabled)
        await CreateButton.ScaleToAsync(0.96, 120, Easing.CubicOut);
}

private async void OnCreateReleased(object? sender, EventArgs args)
{
    if (!reducedMotionEnabled)
        await CreateButton.ScaleToAsync(1.0, 120, Easing.CubicOut);
}

private void UpdateSelectedState()
{
    SemanticProperties.SetDescription(
        BuyButton,
        SelectedRole == RoleFilter.Buying ? "ซื้อ เลือกอยู่" : "ซื้อ");
    SemanticProperties.SetDescription(
        SellButton,
        SelectedRole == RoleFilter.Selling ? "ขาย เลือกอยู่" : "ขาย");
}
```

Replace `AppShell.xaml`'s `TabBar` with hidden top-level `ShellContent` roots `buying` and `selling`. Register `AccountPage` as a pushed route in `AppShell.xaml.cs`. Wrap `TransactionsPage` content in `AuthenticatedRootFrame` and bind all three commands. Inject `IStartupMotionPreference` through `BuyingTransactionsPage`/`SellingTransactionsPage` into the base page and call `RootFrame.RevealAsync(...)` from `OnAppearing`.

Restructure `RootPageHeaderView` into two rows: a 28-point `brand_mark.png`
with the single accessibility description `โลโก้ TOKLONG` on the top left,
44-point Activity and Account actions on the top right, then the existing
subtitle/title below. Route Account to `nameof(AccountPage)`. Use no inferred
badge, and exclude the visible `TOKLONG` word label from the accessibility tree
so the identity is announced once.

- [ ] **Step 4: Run XAML, route, and mobile build checks**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter "FullyQualifiedName~UiLayoutConsistencyTests|FullyQualifiedName~AuthenticatedHomeRoutesTests"
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj -f net10.0-maccatalyst --no-restore
```

Expected: PASS; MAUI XAML source generation accepts the new frame and no native `TabBar` remains.

- [ ] **Step 5: Commit the root-frame slice**

```bash
git add src/Toklong.Mobile/Controls/AuthenticatedRootFrame.xaml src/Toklong.Mobile/Controls/AuthenticatedRootFrame.xaml.cs src/Toklong.Mobile/AppShell.xaml src/Toklong.Mobile/AppShell.xaml.cs src/Toklong.Mobile/Pages/TransactionsPage.xaml src/Toklong.Mobile/Pages/TransactionsPage.xaml.cs src/Toklong.Mobile/Pages/BuyingTransactionsPage.cs src/Toklong.Mobile/Pages/SellingTransactionsPage.cs src/Toklong.Mobile/Controls/RootPageHeaderView.xaml src/Toklong.Mobile/Controls/RootPageHeaderView.xaml.cs tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs
git commit -m "feat: add buy create sell root frame"
```

### Task 4: Clean Ledger Buy and Sell Workspaces

**Files:**
- Modify: `src/Toklong.Mobile/ViewModels/TransactionsViewModel.cs`
- Modify: `src/Toklong.Mobile/Pages/TransactionsPage.xaml`
- Modify: `src/Toklong.Mobile/Core/SpotlightEmptyStatePresentation.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/ViewModelSessionBoundaryTests.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/SpotlightGradientPresentationTests.cs`

**Interfaces:**
- Consumes: existing `TransactionFilter`, `SellerWorkspaceState`, and fixed-role transaction collections.
- Produces: `ActiveTransactionCount`, `ActiveTransactionCountText`, `ShowInitialSkeleton`, buyer/seller summary presentation, and the redesigned role-only root XAML.

- [ ] **Step 1: Write failing summary, skeleton, and role-isolation tests**

```csharp
[Fact]
public async Task Workspace_summary_counts_only_active_matching_role_records()
{
    var service = new SequencedTransactionService();
    service.EnqueueResult(
        new AppTransaction(
            Guid.Parse("00000000-0000-0000-0000-000000000A01"),
            "กล้อง", 100_000, "THB", AppTransactionRole.Buyer,
            AppFulfillmentType.Physical,
            "SellerAcceptedAwaitingPayment", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1), "ผู้ขาย"),
        new AppTransaction(
            Guid.Parse("00000000-0000-0000-0000-000000000A02"),
            "รองเท้า", 200_000, "THB", AppTransactionRole.Buyer,
            AppFulfillmentType.Physical,
            "PaidOut", DateTimeOffset.UtcNow, null, "ผู้ขาย"),
        new AppTransaction(
            Guid.Parse("00000000-0000-0000-0000-000000000A03"),
            "กระเป๋า", 300_000, "THB", AppTransactionRole.Seller,
            AppFulfillmentType.Physical,
            "PaidAwaitingShipment", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(72), "ผู้ซื้อ"));
    var buyer = new TransactionsViewModel(
        service,
        new NoOpDeepLinks(),
        new RecordingAnalytics(),
        new AuthenticatedSessionBoundary(),
        RoleFilter.Buying);

    await buyer.LoadAsync();

    Assert.Equal(1, buyer.ActiveTransactionCount);
    Assert.Equal("1 ดีล", buyer.ActiveTransactionCountText);
    Assert.All(
        buyer.Transactions.Append(buyer.SpotlightTransaction!),
        item => Assert.Equal(AppTransactionRole.Buyer, item.Role));
}

[Fact]
public void Workspace_xaml_uses_clean_ledger_summary_and_stable_spotlight()
{
    var page = Load("Ui", "Pages", "TransactionsPage.xaml");
    Assert.Contains(page.Descendants(), element =>
        AttributeValue(element, "AutomationId") == "WorkspaceSummaryCard");
    Assert.Contains(page.Descendants(), element =>
        AttributeValue(element, "AutomationId") == "WorkspaceInitialSkeleton");
    Assert.DoesNotContain(page.Descendants(Maui + "Button"), button =>
        AttributeValue(button, "Text") == "+ สร้างดีลซื้อ");
}
```

- [ ] **Step 2: Run the focused workspace tests and verify failure**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter "FullyQualifiedName~ViewModelSessionBoundaryTests.Workspace_summary|FullyQualifiedName~UiLayoutConsistencyTests.Workspace_xaml|FullyQualifiedName~UiLayoutConsistencyTests.HomeActionSpotlight"
```

Expected: FAIL on missing active summary and skeleton properties/markup.

- [ ] **Step 3: Implement active summaries and restyle the root**

Add pure derived properties to the ViewModel and raise them after successful load, failure reset, and session reset:

```csharp
public int ActiveTransactionCount =>
    TransactionFilter.Apply(
            allTransactions,
            roleFilter,
            BucketFilter.All)
        .Count(item =>
            item.Presentation.Bucket != TransactionBucket.Completed);

public string ActiveTransactionCountText =>
    $"{ActiveTransactionCount} ดีล";

public bool ShowInitialSkeleton => IsBusy && !hasSuccessfulLoad;
```

In `TransactionsPage.xaml`:

- set the page background to `CleanLedgerRootBackground`;
- keep `RootPageHeaderView` first;
- add `WorkspaceSummaryCard` with role gradient, `ActiveTransactionCountText`, and role-specific aggregate labels;
- keep seller actionable counts and the complete filter below the summary;
- remove the old top create button because the root frame owns creation;
- preserve `ActionSpotlightCard` and `ActionSpotlightEmptyState` equal minimum heights;
- show the skeleton only on initial load, not pull refresh;
- preserve inline retry and already loaded records after a refresh error; and
- reserve no manual tab-bar padding because `AuthenticatedRootFrame` owns the safe bottom row.

Use visible copy `พื้นที่ของผู้ซื้อ`/`รายการซื้อ` and `พื้นที่ของผู้ขาย`/`รายการขาย`; do not add a combined total or wallet/balance wording.

- [ ] **Step 4: Run workspace and session-safety tests**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter "FullyQualifiedName~ViewModelSessionBoundaryTests|FullyQualifiedName~TransactionFilterTests|FullyQualifiedName~SellerWorkspaceStateTests|FullyQualifiedName~SellerWorkSummaryTests|FullyQualifiedName~UiLayoutConsistencyTests|FullyQualifiedName~SpotlightGradientPresentationTests"
```

Expected: PASS with independent role data, filters, errors, and spotlight state.

- [ ] **Step 5: Commit the workspace slice**

```bash
git add src/Toklong.Mobile/ViewModels/TransactionsViewModel.cs src/Toklong.Mobile/Pages/TransactionsPage.xaml src/Toklong.Mobile/Core/SpotlightEmptyStatePresentation.cs tests/Toklong.Mobile.Core.Tests/ViewModelSessionBoundaryTests.cs tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs tests/Toklong.Mobile.Core.Tests/SpotlightGradientPresentationTests.cs
git commit -m "feat: redesign buy and sell workspaces"
```

### Task 5: Shared Transaction Header and Guidance Components

**Files:**
- Create: `src/Toklong.Mobile/Controls/RoleTransactionHeader.xaml`
- Create: `src/Toklong.Mobile/Controls/RoleTransactionHeader.xaml.cs`
- Create: `src/Toklong.Mobile/Controls/DealGuidanceCard.xaml`
- Create: `src/Toklong.Mobile/Controls/DealGuidanceCard.xaml.cs`
- Modify: `src/Toklong.Mobile/Pages/TransactionDetailPage.xaml`
- Modify: `src/Toklong.Mobile/Pages/SellerOfferPage.xaml`
- Modify: `src/Toklong.Mobile/Pages/CounterQrPage.xaml`
- Modify: `src/Toklong.Mobile/Pages/ShippingLabelPage.xaml`
- Modify: `tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj`
- Modify: `tests/Toklong.Mobile.Core.Tests/TransactionPresentationTests.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`

**Interfaces:**
- Consumes: one non-null `AppTransaction` projection and existing role/status properties.
- Produces: `RoleTransactionHeader.Transaction` and `DealGuidanceCard.Transaction` bindable properties; neither component exposes a command.

- [ ] **Step 1: Write failing component-boundary and safe-copy tests**

```csharp
[Fact]
public void Transaction_detail_uses_role_header_and_commandless_guidance()
{
    var detail = Load("Ui", "Pages", "TransactionDetailPage.xaml");
    Assert.Contains(detail.Descendants(), element =>
        element.Name.LocalName == "RoleTransactionHeader");
    Assert.Contains(detail.Descendants(), element =>
        element.Name.LocalName == "DealGuidanceCard");

    var guidance = Load("Ui", "Controls", "DealGuidanceCard.xaml");
    Assert.Empty(guidance.Descendants(Maui + "Button"));
    Assert.Empty(guidance.Descendants(Maui + "Entry"));
    Assert.Empty(guidance.Descendants(Maui + "Editor"));
}

[Theory]
[InlineData("PaymentPending", AppTransactionRole.Buyer)]
[InlineData("Disputed", AppTransactionRole.Seller)]
[InlineData("DigitalDeliverySubmitted", AppTransactionRole.Buyer)]
public void Guidance_contains_no_internal_transaction_vocabulary(
    string state,
    AppTransactionRole role)
{
    var guidance = CreateItem(state, role).StatusGuidance;
    Assert.DoesNotContain("webhook", guidance, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("state machine", guidance, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("hash", guidance, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("reconciliation", guidance, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run component and presentation tests and verify failure**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter "FullyQualifiedName~TransactionPresentationTests.Guidance|FullyQualifiedName~UiLayoutConsistencyTests.Transaction_detail_uses_role_header"
```

Expected: FAIL because the shared components do not exist.

- [ ] **Step 3: Implement the shared components and migrate detail XAML**

Both code-behind files expose only this property contract:

```csharp
public static readonly BindableProperty TransactionProperty =
    BindableProperty.Create(
        nameof(Transaction),
        typeof(AppTransaction),
        typeof(RoleTransactionHeader));

public AppTransaction? Transaction
{
    get => (AppTransaction?)GetValue(TransactionProperty);
    set => SetValue(TransactionProperty, value);
}
```

Use this exact property in `DealGuidanceCard.xaml.cs`:

```csharp
public static readonly BindableProperty TransactionProperty =
    BindableProperty.Create(
        nameof(Transaction),
        typeof(AppTransaction),
        typeof(DealGuidanceCard));

public AppTransaction? Transaction
{
    get => (AppTransaction?)GetValue(TransactionProperty);
    set => SetValue(TransactionProperty, value);
}
```

`RoleTransactionHeader.xaml` renders `RoleLabel`, `StatusLabel`, `ProductName`, `RoleAmountLabel`, `RoleAmountText`, `CounterpartyLabel`, and `DeadlineText` using the existing role gradient properties. `DealGuidanceCard.xaml` renders `StatusGuidanceIcon`, `StatusLabel`, and `StatusGuidance` and contains no gesture recognizer or command.

Replace the inline header/status blocks in `TransactionDetailPage.xaml`:

```xml
<controls:RoleTransactionHeader
    AutomationId="RoleTransactionHeader"
    Transaction="{Binding Transaction}" />
<controls:DealGuidanceCard
    AutomationId="DealGuidanceCard"
    Transaction="{Binding Transaction}" />
```

Set the detail background to `CleanLedgerRootBackground` and migrate supporting borders to `LedgerSurfaceCard`. Do not change bindings for payment, protection, address, Counter QR, label, digital handoff, dispute, refund, receipt confirmation, or payout actions.

Apply the same root background, `LedgerSurfaceCard`, and
`LedgerPrimaryButton` tokens to `SellerOfferPage`, `CounterQrPage`, and
`ShippingLabelPage`. Preserve Seller Offer's neutral prepare-sale treatment,
quote loading/error placement, accept/decline order, provider-issued QR
authorization checks, expiry refresh, label download-only behavior, and all
existing code-behind bindings.

- [ ] **Step 4: Run presentation, detail ViewModel, QR, and receipt tests**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter "FullyQualifiedName~TransactionPresentationTests|FullyQualifiedName~TransactionDetail|FullyQualifiedName~CounterQr|FullyQualifiedName~BuyerReceiptConfirmation|FullyQualifiedName~UiLayoutConsistencyTests"
```

Expected: PASS; approved Stripe refund action copy remains, while internal vocabulary stays absent.

- [ ] **Step 5: Commit the transaction-detail slice**

```bash
git add src/Toklong.Mobile/Controls/RoleTransactionHeader.xaml src/Toklong.Mobile/Controls/RoleTransactionHeader.xaml.cs src/Toklong.Mobile/Controls/DealGuidanceCard.xaml src/Toklong.Mobile/Controls/DealGuidanceCard.xaml.cs src/Toklong.Mobile/Pages/TransactionDetailPage.xaml src/Toklong.Mobile/Pages/SellerOfferPage.xaml src/Toklong.Mobile/Pages/CounterQrPage.xaml src/Toklong.Mobile/Pages/ShippingLabelPage.xaml tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj tests/Toklong.Mobile.Core.Tests/TransactionPresentationTests.cs tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs
git commit -m "feat: add clean ledger transaction surfaces"
```

### Task 6: Deal Creation and Authentication Visual Migration

**Files:**
- Modify: `src/Toklong.Mobile/Pages/ProductTypeSelectionPage.xaml`
- Modify: `src/Toklong.Mobile/Pages/CreateOfferPage.xaml`
- Modify: `src/Toklong.Mobile/Pages/WelcomePage.xaml`
- Modify: `src/Toklong.Mobile/Pages/SignInPage.xaml`
- Modify: `src/Toklong.Mobile/Pages/SignUpPage.xaml`
- Modify: `src/Toklong.Mobile/Pages/VerifyCodePage.xaml`
- Modify: `src/Toklong.Mobile/Pages/CompleteRegistrationPage.xaml`
- Modify: `tests/Toklong.Mobile.Core.Tests/AuthenticationLayoutTests.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/CreateOfferWizardStateTests.cs`

**Interfaces:**
- Consumes: existing page ViewModels, validation bindings, and navigation handlers unchanged.
- Produces: Clean Ledger styling on creation/authentication with no new behavior interface.

- [ ] **Step 1: Write failing style-preservation tests**

```csharp
[Fact]
public void Create_flow_uses_clean_ledger_tokens_without_changing_steps()
{
    var create = ReadPage("CreateOfferPage.xaml");
    Assert.Contains("{StaticResource CleanLedgerRootBackground}", create);
    Assert.Contains("{StaticResource LedgerPrimaryButton}", create);
    Assert.Contains("สร้างดีลซื้อ", create);
    Assert.Contains("{Binding IsDealStep}", create);
    Assert.Contains("{Binding IsFulfillmentStep}", create);
    Assert.Contains("{Binding IsReviewStep}", create);
    Assert.DoesNotContain("Shell.TabBarIsVisible=\"True\"", create);
}

[Theory]
[InlineData("WelcomePage.xaml")]
[InlineData("SignInPage.xaml")]
[InlineData("SignUpPage.xaml")]
[InlineData("VerifyCodePage.xaml")]
[InlineData("CompleteRegistrationPage.xaml")]
public void Authentication_pages_use_the_shared_mist_background(string page)
{
    Assert.Contains(
        "{StaticResource CleanLedgerRootBackground}",
        ReadPage(page));
}
```

- [ ] **Step 2: Run layout and wizard tests and verify failure**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter "FullyQualifiedName~AuthenticationLayoutTests|FullyQualifiedName~UiLayoutConsistencyTests.Create_flow|FullyQualifiedName~CreateOfferWizardStateTests"
```

Expected: FAIL because the pages still use radial backgrounds and older button styles.

- [ ] **Step 3: Apply the exact visual migration without behavior edits**

Apply these mappings in every listed page:

```text
RadialGradientBrush page background → BackgroundColor="{StaticResource CleanLedgerRootBackground}"
SurfaceCard on ordinary white sections → LedgerSurfaceCard
RefinedPrimaryButton/PrimaryButton on primary actions → LedgerPrimaryButton
raw #2B7FFF action accents → BuyerBlue
raw page border #E4EAF1 → Line
```

For `CreateOfferPage.xaml`, make the existing header transparent, retain the three 4-point progress segments, retain the exact Back handler, keep fields individually outlined, and keep the AI helper collapsed and Mint-accented:

```xml
<Border AutomationId="CreateOfferHeader"
        Padding="20,12,20,18"
        BackgroundColor="Transparent"
        StrokeThickness="0">
    <!-- existing Back button, สร้างดีลซื้อ title, ProgressText, and segments -->
</Border>

<Border AutomationId="AgreementDraftAssistant"
        BackgroundColor="#EFFBF8"
        Stroke="{StaticResource VerifiedMint}"
        StrokeShape="RoundRectangle 16">
    <!-- existing optional AI draft controls and bindings only -->
</Border>
```

For `ProductTypeSelectionPage.xaml`, keep exactly two large content-sized navigation cards and the transparent icon-only Back header. Do not add a selected state or second type selector. On authentication pages, preserve all approved copy, field order, single six-digit input, Terms/Privacy placement, and startup routing.

- [ ] **Step 4: Run creation, authentication, accessibility, and XAML build tests**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter "FullyQualifiedName~Authentication|FullyQualifiedName~CreateOffer|FullyQualifiedName~Otp|FullyQualifiedName~ThaiMobilePhone|FullyQualifiedName~UiLayoutConsistencyTests"
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj -f net10.0-maccatalyst --no-restore
```

Expected: PASS; no create request, snapshot, notification, or payment behavior changes.

- [ ] **Step 5: Commit the creation/authentication slice**

```bash
git add src/Toklong.Mobile/Pages/ProductTypeSelectionPage.xaml src/Toklong.Mobile/Pages/CreateOfferPage.xaml src/Toklong.Mobile/Pages/WelcomePage.xaml src/Toklong.Mobile/Pages/SignInPage.xaml src/Toklong.Mobile/Pages/SignUpPage.xaml src/Toklong.Mobile/Pages/VerifyCodePage.xaml src/Toklong.Mobile/Pages/CompleteRegistrationPage.xaml tests/Toklong.Mobile.Core.Tests/AuthenticationLayoutTests.cs tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs tests/Toklong.Mobile.Core.Tests/CreateOfferWizardStateTests.cs
git commit -m "feat: apply clean ledger create and auth styling"
```

### Task 7: Activity and Account Pushed-Page Migration

**Files:**
- Modify: `src/Toklong.Mobile/Pages/ActivityPage.xaml`
- Modify: `src/Toklong.Mobile/Pages/AccountPage.xaml`
- Modify: `src/Toklong.Mobile/Pages/AccountPage.xaml.cs`
- Modify: `src/Toklong.Mobile/Pages/PayoutSettingsPage.xaml`
- Modify: `src/Toklong.Mobile/Pages/ChangeEmailPage.xaml`
- Modify: `src/Toklong.Mobile/Pages/VerifyEmailChangePage.xaml`
- Modify: `src/Toklong.Mobile/Pages/ChangeNamePage.xaml`
- Modify: `src/Toklong.Mobile/Pages/VerifyNameChangePage.xaml`
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/ViewModelSessionBoundaryTests.cs`

**Interfaces:**
- Consumes: existing `ActivityViewModel`, `AccountViewModel`, and registered pushed routes.
- Produces: Activity and Account pages with `Shell.NavBarIsVisible="True"`, `Shell.TabBarIsVisible="False"`, titles, Back navigation, and Clean Ledger surfaces.

- [ ] **Step 1: Write failing pushed-page and account-session tests**

```csharp
[Theory]
[InlineData("ActivityPage.xaml", "กิจกรรม")]
[InlineData("AccountPage.xaml", "บัญชี")]
public void Secondary_hubs_are_pushed_pages_without_root_action_bar(
    string file,
    string title)
{
    var page = Load("Ui", "Pages", file);
    Assert.Equal(title, AttributeValue(page.Root!, "Title"));
    Assert.Equal("True", AttributeValue(page.Root!, "Shell.NavBarIsVisible"));
    Assert.Equal("False", AttributeValue(page.Root!, "Shell.TabBarIsVisible"));
    Assert.DoesNotContain(page.Descendants(), element =>
        element.Name.LocalName == "AuthenticatedRootFrame");
}
```

Retain the existing account-switch test asserting old names, emails, payout text, addresses, and transaction records are cleared before another session renders.

- [ ] **Step 2: Run the focused tests and verify failure**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter "FullyQualifiedName~UiLayoutConsistencyTests.Secondary_hubs|FullyQualifiedName~ViewModelSessionBoundaryTests.Account_switch"
```

Expected: FAIL because Account is still a native root without pushed-page chrome.

- [ ] **Step 3: Migrate Activity and Account presentation**

Set exact page attributes:

```xml
Title="บัญชี"
BackgroundColor="{StaticResource CleanLedgerRootBackground}"
Shell.NavBarIsVisible="True"
Shell.TabBarIsVisible="False"
```

Remove `RootPageHeaderView` from Account because Shell provides the pushed-page title/Back chrome. Keep every account section and command, migrating ordinary cards to `LedgerSurfaceCard`. Apply the same background and card style to Activity while retaining its feed, refresh, retry, empty state, and item commands.

Keep `AccountPage` singleton registration so account state and existing session-boundary clearing behavior remain stable. Do not add unread, verification, payout, or provider badges without existing authoritative properties.

Migrate `PayoutSettingsPage`, `ChangeEmailPage`, `VerifyEmailChangePage`,
`ChangeNamePage`, and `VerifyNameChangePage` to the same background, surface,
input, and primary-action tokens. Preserve every current phone-proof challenge,
cooldown, resend, session-generation, payout-capability disclaimer, and Back
route; do not add new status badges or provider claims.

- [ ] **Step 4: Run account, activity, email/name, and session tests**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter "FullyQualifiedName~Account|FullyQualifiedName~Activity|FullyQualifiedName~EmailChange|FullyQualifiedName~NameChange|FullyQualifiedName~ViewModelSessionBoundaryTests|FullyQualifiedName~UiLayoutConsistencyTests"
```

Expected: PASS; Back remains available even when either hub fails to load.

- [ ] **Step 5: Commit the secondary-hub slice**

```bash
git add src/Toklong.Mobile/Pages/ActivityPage.xaml src/Toklong.Mobile/Pages/AccountPage.xaml src/Toklong.Mobile/Pages/AccountPage.xaml.cs src/Toklong.Mobile/Pages/PayoutSettingsPage.xaml src/Toklong.Mobile/Pages/ChangeEmailPage.xaml src/Toklong.Mobile/Pages/VerifyEmailChangePage.xaml src/Toklong.Mobile/Pages/ChangeNamePage.xaml src/Toklong.Mobile/Pages/VerifyNameChangePage.xaml tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs tests/Toklong.Mobile.Core.Tests/ViewModelSessionBoundaryTests.cs
git commit -m "feat: redesign activity and account hubs"
```

### Task 8: Binding Documentation, Full Verification, and Visual Review

**Files:**
- Modify: `docs/02_UI_UX_AND_CONTENT_SPEC.md`
- Modify: `docs/05_ACCEPTANCE_TESTS.md`
- Modify: `docs/10_MOBILE_APP_SPEC.md`
- Verify: all changed mobile and mobile-test files

**Interfaces:**
- Consumes: completed Clean Ledger implementation and approved design spec.
- Produces: binding documentation aligned with shipped navigation and passing repository verification evidence.

- [ ] **Step 1: Add failing documentation contract tests**

Add to `UiLayoutConsistencyTests`:

```csharp
[Fact]
public void Binding_docs_describe_the_shipped_root_navigation()
{
    var repositoryRoot = FindRepositoryRoot();
    var uiSpec = File.ReadAllText(Path.Combine(
        repositoryRoot, "docs", "02_UI_UX_AND_CONTENT_SPEC.md"));
    var acceptance = File.ReadAllText(Path.Combine(
        repositoryRoot, "docs", "05_ACCEPTANCE_TESTS.md"));

    Assert.Contains("ซื้อ | + สร้างดีล | ขาย", uiSpec);
    Assert.Contains("สร้างข้อเสนอซื้อ", uiSpec);
    Assert.Contains("Account", uiSpec);
    Assert.Contains("center action", acceptance, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("buyer-created", acceptance, StringComparison.OrdinalIgnoreCase);
}

private static string FindRepositoryRoot()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
         directory is not null;
         directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Toklong.slnx")))
            return directory.FullName;
    }

    throw new DirectoryNotFoundException("Could not locate Toklong.slnx.");
}
```

- [ ] **Step 2: Run the documentation contract and verify failure**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter "FullyQualifiedName~UiLayoutConsistencyTests.Binding_docs"
```

Expected: FAIL because the binding docs still describe `ซื้อ`, `ขาย`, and `บัญชี` as three native bottom tabs.

- [ ] **Step 3: Update the binding documentation with exact shipped behavior**

Update `docs/02_UI_UX_AND_CONTENT_SPEC.md` to state:

```text
Authenticated root action bar: ซื้อ | + สร้างดีล | ขาย
Center accessible name: สร้างข้อเสนอซื้อ
Ordinary authenticated entry: ซื้อ
Top-right actions: กิจกรรม and บัญชี
Pushed pages hide the authenticated root action bar and retain Back navigation.
The center action always creates a buyer offer and never a seller listing.
```

Add acceptance scenarios to `docs/05_ACCEPTANCE_TESTS.md` for exact element order, role isolation, single-flight creation from both roots, Account/Activity Back behavior, 44/64-point targets, Dynamic Type, Reduced Motion, and the rule that navigation/guidance creates no transaction or financial transition. Remove superseded navigation wording from `docs/10_MOBILE_APP_SPEC.md`.

- [ ] **Step 4: Run formatting and focused mobile verification**

Run:

```bash
dotnet format Toklong.slnx --verify-no-changes
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj -f net10.0-maccatalyst --no-restore
```

Expected: all commands exit `0`. If formatting reports changes, run `dotnet format Toklong.slnx`, inspect only intended formatting edits, then rerun `--verify-no-changes`.

- [ ] **Step 5: Run complete required regression suites**

Run:

```bash
dotnet test Toklong.slnx --no-restore
```

Expected: all Domain, Application, API, CRM, Mobile Core, and SHIPPOP certification unit/integration tests pass. Confirm specifically that payment signature/idempotency/replay, carrier delivery-time/idempotency, dispute payout blocking, and digital no-auto-release tests remain green.

- [ ] **Step 6: Perform the device-size and accessibility review**

Run the Debug app at a narrow phone viewport and verify this checklist:

```text
[ ] Buy, Create Deal, Sell are visible in that order.
[ ] Center button does not overlap spotlight/list content or device safe area.
[ ] Activity and Account remain 44-point targets.
[ ] Buy and Sell selected state is visible and announced.
[ ] Large Thai text wraps without hiding primary actions.
[ ] Reduced Motion produces no root/create animation delay.
[ ] Buyer and seller lists never mix.
[ ] Create from Sell still opens the buyer product-type page once.
[ ] Transaction details show exact server-derived deadlines.
[ ] No screen claims payment, delivery, refund, or payout from client state.
```

For Mac Catalyst preview:

```bash
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj -f net10.0-maccatalyst --no-restore
```

Use the existing 440-by-790 debug window sizing in `App.xaml.cs`; do not add screenshot-only production code.

- [ ] **Step 7: Commit documentation and final verification fixes**

```bash
git add docs/02_UI_UX_AND_CONTENT_SPEC.md docs/05_ACCEPTANCE_TESTS.md docs/10_MOBILE_APP_SPEC.md tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs
git commit -m "docs: align mobile specs with clean ledger navigation"
```

## Completion Gate

Do not claim completion until all of the following are true:

- the mobile core test project passes in full;
- the MAUI Mac Catalyst target builds;
- the full solution test command passes or any environment-only exclusion is documented with its exact command/output;
- Buy/Sell data, filters, errors, and session state remain isolated;
- `สร้างดีล` is single-flight and buyer-only from both roots;
- Activity and Account are pushed pages with Back;
- changed screens pass the documented accessibility review;
- all binding docs match the shipped UI; and
- `git status --short` contains no unintended files or secrets.
