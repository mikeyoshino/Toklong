# Authenticated Home and Create-Deal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the approved authenticated `ซื้อ / ขาย` home and convert the existing buyer offer form into a three-step mobile wizard without changing buyer-first domain behavior.

**Architecture:** Add small route/role and wizard-state types to `Toklong.Mobile.Core` so navigation decisions and wizard progression are unit-testable without MAUI. Keep `TransactionsPage` as the transaction root, use an explicit role query to override its remembered mode, and keep all offer inputs in the existing `CreateOfferViewModel`; only presentation state and validation timing change. The existing preview and create-offer APIs remain authoritative, and only final submit creates an offer.

**Tech Stack:** .NET 10, C# 13, .NET MAUI XAML, Shell navigation, xUnit, existing Toklong mobile services and design resources.

## Global Constraints

- Work directly on `main`; do not create a worktree.
- TOKLONG remains buyer-first: the buyer creates the private offer and the seller only reviews, accepts/declines, fulfills, and tracks payout.
- Do not add seller-created listings, seller-created sales links, marketplace discovery, bidding, chat, services, wallets, crypto, subscriptions, or multi-currency.
- A role-home choice is navigation state, not a permanent account role.
- Use the real `brand_mark` asset and visible `ซื้อ` / `ขาย` labels; never communicate role by color alone.
- Existing deep links continue opening the exact transaction directly.
- No generic clipboard or pasted-link opener is added to the transaction root.
- Physical is the default offer type; Digital hides address and shipping UI.
- Money remains integer satang at API/domain boundaries; no new floating-point money arithmetic.
- Wizard steps are presentation state only and never become backend transaction states or audit events.
- Do not persist an incomplete offer draft across page closure or app restart.
- No offer, snapshot, notification, payment, or audit transition exists before final submit succeeds.
- Exact exit copy:
  - `ยังสร้างข้อเสนอไม่เสร็จ`
  - `ถ้าออกตอนนี้ ข้อมูลที่กรอกไว้จะหาย`
  - `กลับไปกรอกต่อ`
  - `ออกจากหน้านี้`
- Required final action: `ส่งข้อเสนอให้ผู้ขาย`.
- Preserve existing payment, provider confirmation, shipment, dispute, payout, immutable snapshot, and authorization rules.

---

## File Structure

### New files

- `src/Toklong.Mobile/Core/AuthenticatedHomeRoutes.cs` — canonical home and role-specific transaction routes plus strict role parsing.
- `src/Toklong.Mobile/Core/CreateOfferWizardState.cs` — presentation-only three-step progression and dirty state.
- `src/Toklong.Mobile/Pages/AuthenticatedHomePage.xaml` — approved centered-brand authenticated home.
- `src/Toklong.Mobile/Pages/AuthenticatedHomePage.xaml.cs` — Shell navigation from the home actions.
- `tests/Toklong.Mobile.Core.Tests/AuthenticatedHomeRoutesTests.cs` — route and role parsing coverage.
- `tests/Toklong.Mobile.Core.Tests/CreateOfferWizardStateTests.cs` — wizard progression and dirty-state coverage.

### Modified files

- `src/Toklong.Mobile/Core/StartupCoordinator.cs` — authenticated sessions route to home.
- `src/Toklong.Mobile/App.xaml.cs` — initialize authenticated services from the home route.
- `src/Toklong.Mobile/AppShell.xaml` — register the authenticated home as a hidden root.
- `src/Toklong.Mobile/MauiProgram.cs` — register the home page.
- `src/Toklong.Mobile/ViewModels/VerifyCodeViewModel.cs` — successful login routes to home.
- `src/Toklong.Mobile/ViewModels/CompleteRegistrationViewModel.cs` — completed registration routes to home.
- `src/Toklong.Mobile/Pages/TransactionsPage.xaml.cs` — accept an explicit role navigation query.
- `src/Toklong.Mobile/ViewModels/TransactionsViewModel.cs` — apply explicit buyer/seller mode while retaining ordinary remembered-mode behavior.
- `src/Toklong.Mobile/ViewModels/CreateOfferViewModel.cs` — split validation by step, expose step state/commands, retain preview and submit behavior.
- `src/Toklong.Mobile/Pages/CreateOfferPage.xaml` — replace the long form/review sheet with three full-page step containers.
- `src/Toklong.Mobile/Pages/CreateOfferPage.xaml.cs` — handle validation focus, within-wizard back navigation, and the approved dirty-exit dialog.
- `tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj` — copy page code-behind files into the test output for deterministic source-contract tests.
- `tests/Toklong.Mobile.Core.Tests/StartupCoordinatorTests.cs` — authenticated startup route expectations.
- `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs` — authenticated home and wizard hierarchy/accessibility checks.
- `docs/02_UI_UX_AND_CONTENT_SPEC.md` — record the approved authenticated home and three-step create-offer hierarchy.
- `docs/05_ACCEPTANCE_TESTS.md` — add acceptance scenarios for explicit role routing, wizard validation, and dirty exit.

---

### Task 1: Canonical authenticated-home and role routes

**Files:**
- Create: `src/Toklong.Mobile/Core/AuthenticatedHomeRoutes.cs`
- Create: `tests/Toklong.Mobile.Core.Tests/AuthenticatedHomeRoutesTests.cs`
- Modify: `src/Toklong.Mobile/Core/StartupCoordinator.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/StartupCoordinatorTests.cs`

**Interfaces:**
- Produces:
  - `enum TransactionRoleRoute { Buying, Selling }`
  - `AuthenticatedHomeRoutes.Home`
  - `AuthenticatedHomeRoutes.Transactions(TransactionRoleRoute role)`
  - `AuthenticatedHomeRoutes.TryParseRole(string? value, out TransactionRoleRoute role)`
- Consumed by Tasks 2 and 3.

- [ ] **Step 1: Write failing route tests**

Create `tests/Toklong.Mobile.Core.Tests/AuthenticatedHomeRoutesTests.cs`:

```csharp
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class AuthenticatedHomeRoutesTests
{
    [Theory]
    [InlineData(TransactionRoleRoute.Buying, "//transactions?role=buying")]
    [InlineData(TransactionRoleRoute.Selling, "//transactions?role=selling")]
    public void Transactions_builds_explicit_role_route(
        TransactionRoleRoute role,
        string expected) =>
        Assert.Equal(expected, AuthenticatedHomeRoutes.Transactions(role));

    [Theory]
    [InlineData("buying", TransactionRoleRoute.Buying)]
    [InlineData("selling", TransactionRoleRoute.Selling)]
    public void TryParseRole_accepts_only_canonical_values(
        string value,
        TransactionRoleRoute expected)
    {
        Assert.True(
            AuthenticatedHomeRoutes.TryParseRole(value, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("buyer")]
    [InlineData("BUYING")]
    public void TryParseRole_rejects_missing_or_noncanonical_values(
        string? value) =>
        Assert.False(
            AuthenticatedHomeRoutes.TryParseRole(value, out _));
}
```

Change the authenticated expectations in
`tests/Toklong.Mobile.Core.Tests/StartupCoordinatorTests.cs`:

```csharp
Assert.Equal(AuthenticatedHomeRoutes.Home, result.Route);
```

Apply that expectation to:

- `StartAsync_WithSession_PlaysMotionAndRoutesToTransactions` (rename it to
  `StartAsync_WithSession_PlaysMotionAndRoutesToAuthenticatedHome`).
- `StartAsync_ResolvesSessionWhileMotionIsStillPlaying`.
- `StartAsync_prefers_authenticated_session_over_pending_registration`.

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~AuthenticatedHomeRoutesTests|FullyQualifiedName~StartupCoordinatorTests"
```

Expected: FAIL because `AuthenticatedHomeRoutes` and
`TransactionRoleRoute` do not exist, and startup still returns
`//transactions`.

- [ ] **Step 3: Add the minimal route implementation**

Create `src/Toklong.Mobile/Core/AuthenticatedHomeRoutes.cs`:

```csharp
namespace Toklong.Mobile.Core;

public enum TransactionRoleRoute
{
    Buying,
    Selling
}

public static class AuthenticatedHomeRoutes
{
    public const string Home = "//home";

    public static string Transactions(TransactionRoleRoute role) =>
        role switch
        {
            TransactionRoleRoute.Buying =>
                "//transactions?role=buying",
            TransactionRoleRoute.Selling =>
                "//transactions?role=selling",
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        };

    public static bool TryParseRole(
        string? value,
        out TransactionRoleRoute role)
    {
        role = value switch
        {
            "buying" => TransactionRoleRoute.Buying,
            "selling" => TransactionRoleRoute.Selling,
            _ => default
        };
        return value is "buying" or "selling";
    }
}
```

In `src/Toklong.Mobile/Core/StartupCoordinator.cs`, replace only the
authenticated branch:

```csharp
session.HasSession
    ? AuthenticatedHomeRoutes.Home
    : pending.HasPending
        ? AuthenticationRoutes.CompleteRegistration
        : "//welcome"
```

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the Task 1 command again.

Expected: PASS for all route and startup tests.

- [ ] **Step 5: Commit Task 1**

```bash
git add \
  src/Toklong.Mobile/Core/AuthenticatedHomeRoutes.cs \
  src/Toklong.Mobile/Core/StartupCoordinator.cs \
  tests/Toklong.Mobile.Core.Tests/AuthenticatedHomeRoutesTests.cs \
  tests/Toklong.Mobile.Core.Tests/StartupCoordinatorTests.cs
git commit -m "feat: route authenticated users through role home"
```

---

### Task 2: Authenticated role-home page and authentication completion routes

**Files:**
- Create: `src/Toklong.Mobile/Pages/AuthenticatedHomePage.xaml`
- Create: `src/Toklong.Mobile/Pages/AuthenticatedHomePage.xaml.cs`
- Modify: `src/Toklong.Mobile/AppShell.xaml`
- Modify: `src/Toklong.Mobile/MauiProgram.cs`
- Modify: `src/Toklong.Mobile/App.xaml.cs`
- Modify: `src/Toklong.Mobile/ViewModels/VerifyCodeViewModel.cs`
- Modify: `src/Toklong.Mobile/ViewModels/CompleteRegistrationViewModel.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`

**Interfaces:**
- Consumes `AuthenticatedHomeRoutes` and `TransactionRoleRoute` from Task 1.
- Produces the hidden Shell root `home` and its three navigation actions.

- [ ] **Step 1: Write failing authenticated-home XAML tests**

Add to `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`:

```csharp
[Fact]
public void AuthenticatedHome_UsesRealCenteredBrandAndBuyerFirstActions()
{
    var home = Load(
        "Ui",
        "Pages",
        "AuthenticatedHomePage.xaml");
    var labels = home
        .Descendants(Maui + "Label")
        .Select(label => AttributeValue(label, "Text"))
        .ToArray();
    var buttons = home.Descendants(Maui + "Button").ToArray();

    Assert.Contains(
        home.Descendants(),
        element =>
            element.Name.LocalName == "CenteredAuthBrandView");
    Assert.Contains("เริ่มดีลอย่างมั่นใจ", labels);
    Assert.Contains(
        "สร้างข้อเสนอซื้อ หรือจัดการรายการขาย",
        labels);
    Assert.Contains(
        buttons,
        button =>
            AttributeValue(button, "AutomationId") ==
                "OpenBuyingHomeButton" &&
            AttributeValue(
                button,
                "SemanticProperties.Description") ==
                "ซื้อ สร้างข้อเสนอ ตรวจรายละเอียด และติดตามรายการ");
    Assert.Contains(
        buttons,
        button =>
            AttributeValue(button, "AutomationId") ==
                "OpenSellingHomeButton" &&
            AttributeValue(
                button,
                "SemanticProperties.Description") ==
                "ขาย ตรวจข้อเสนอ ส่งสินค้า และติดตามยอดรับ");
    Assert.Contains(
        buttons,
        button =>
            AttributeValue(button, "AutomationId") ==
                "OpenAllTransactionsButton" &&
            AttributeValue(button, "Text") ==
                "รายการของฉัน");
    Assert.DoesNotContain("เข้าสู่ระบบ", labels);
    Assert.DoesNotContain("สมัครสมาชิก", labels);
    Assert.DoesNotContain("สร้างลิงก์ขาย", labels);
}

[Fact]
public void Shell_RegistersAuthenticatedHomeOutsideTheMainTabBar()
{
    var shell = Load("Ui", "AppShell.xaml");
    var home = shell
        .Descendants(Maui + "ShellContent")
        .Single(element =>
            AttributeValue(element, "Route") == "home");

    Assert.Equal(
        "{DataTemplate pages:AuthenticatedHomePage}",
        AttributeValue(home, "ContentTemplate"));
    Assert.Equal(
        "False",
        AttributeValue(home, "Shell.FlyoutItemIsVisible"));
    Assert.DoesNotContain(
        home.Ancestors(),
        element => element.Name.LocalName == "TabBar");
}
```

- [ ] **Step 2: Run XAML tests and verify RED**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~AuthenticatedHome|FullyQualifiedName~Shell_RegistersAuthenticatedHome"
```

Expected: FAIL because `AuthenticatedHomePage.xaml` and the `home` Shell
content do not exist.

- [ ] **Step 3: Add the home page with exact approved copy**

Create `src/Toklong.Mobile/Pages/AuthenticatedHomePage.xaml` with this
structure:

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<ContentPage
    x:Class="Toklong.Mobile.Pages.AuthenticatedHomePage"
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:controls="clr-namespace:Toklong.Mobile.Controls"
    SafeAreaEdges="Container"
    Shell.NavBarIsVisible="False"
    Shell.TabBarIsVisible="False">
    <ContentPage.Background>
        <RadialGradientBrush Center="0.5,0.03" Radius="0.95">
            <GradientStop Color="#DCEFFF" Offset="0" />
            <GradientStop Color="#F7FBFF" Offset="0.42" />
            <GradientStop Color="#FFFFFF" Offset="1" />
        </RadialGradientBrush>
    </ContentPage.Background>
    <ScrollView>
        <VerticalStackLayout
            MaximumWidthRequest="520"
            Padding="24,52,24,32"
            Spacing="24">
            <controls:CenteredAuthBrandView
                HorizontalOptions="Center" />
            <VerticalStackLayout Spacing="6">
                <Label
                    HorizontalTextAlignment="Center"
                    Style="{StaticResource AuthTitle}"
                    Text="เริ่มดีลอย่างมั่นใจ" />
                <Label
                    HorizontalTextAlignment="Center"
                    Style="{StaticResource RefinedBodyText}"
                    Text="สร้างข้อเสนอซื้อ หรือจัดการรายการขาย"
                    TextColor="{StaticResource InkSoft}" />
            </VerticalStackLayout>
            <Border
                BackgroundColor="{StaticResource BrandBlue}"
                StrokeShape="RoundRectangle 20">
                <Grid MinimumHeightRequest="112">
                    <VerticalStackLayout
                        Padding="20"
                        InputTransparent="True"
                        Spacing="6">
                        <Label Text="ซื้อ" TextColor="White" />
                        <Label
                            Text="สร้างข้อเสนอ ตรวจรายละเอียด และติดตามรายการ"
                            TextColor="White" />
                    </VerticalStackLayout>
                    <Button
                        AutomationId="OpenBuyingHomeButton"
                        BackgroundColor="Transparent"
                        Clicked="OnBuyingClicked"
                        SemanticProperties.Description="ซื้อ สร้างข้อเสนอ ตรวจรายละเอียด และติดตามรายการ"
                        Text="" />
                </Grid>
            </Border>
            <Border
                BackgroundColor="#6255D9"
                StrokeShape="RoundRectangle 20">
                <Grid MinimumHeightRequest="112">
                    <VerticalStackLayout
                        Padding="20"
                        InputTransparent="True"
                        Spacing="6">
                        <Label Text="ขาย" TextColor="White" />
                        <Label
                            Text="ตรวจข้อเสนอ ส่งสินค้า และติดตามยอดรับ"
                            TextColor="White" />
                    </VerticalStackLayout>
                    <Button
                        AutomationId="OpenSellingHomeButton"
                        BackgroundColor="Transparent"
                        Clicked="OnSellingClicked"
                        SemanticProperties.Description="ขาย ตรวจข้อเสนอ ส่งสินค้า และติดตามยอดรับ"
                        Text="" />
                </Grid>
            </Border>
            <Button
                AutomationId="OpenAllTransactionsButton"
                Style="{StaticResource RefinedInlineButton}"
                Clicked="OnTransactionsClicked"
                Text="รายการของฉัน" />
        </VerticalStackLayout>
    </ScrollView>
</ContentPage>
```

The transparent button spans each whole visual card. It is the only
accessible action inside that card; keep the decorative labels
`InputTransparent` and set
`AutomationProperties.IsInAccessibleTree="False"` on their containing stack,
so VoiceOver announces one control with the complete title and description,
not three separate targets. Give the overlay button a visible pressed/focused
state in its `VisualStateManager`.

Create `src/Toklong.Mobile/Pages/AuthenticatedHomePage.xaml.cs`:

```csharp
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Pages;

public partial class AuthenticatedHomePage : ContentPage
{
    public AuthenticatedHomePage() => InitializeComponent();

    private async void OnBuyingClicked(
        object? sender,
        EventArgs eventArgs) =>
        await Shell.Current.GoToAsync(
            AuthenticatedHomeRoutes.Transactions(
                TransactionRoleRoute.Buying));

    private async void OnSellingClicked(
        object? sender,
        EventArgs eventArgs) =>
        await Shell.Current.GoToAsync(
            AuthenticatedHomeRoutes.Transactions(
                TransactionRoleRoute.Selling));

    private async void OnTransactionsClicked(
        object? sender,
        EventArgs eventArgs) =>
        await Shell.Current.GoToAsync("//transactions");
}
```

- [ ] **Step 4: Register the home root and update all authenticated entry points**

Add this hidden `ShellContent` before the `TabBar` in
`src/Toklong.Mobile/AppShell.xaml`:

```xml
<ShellContent
    Route="home"
    Shell.NavBarIsVisible="False"
    ContentTemplate="{DataTemplate pages:AuthenticatedHomePage}"
    Shell.FlyoutItemIsVisible="False" />
```

Register the singleton page in `src/Toklong.Mobile/MauiProgram.cs`:

```csharp
builder.Services.AddSingleton<AuthenticatedHomePage>();
```

Replace successful authentication routes in both
`VerifyCodeViewModel.cs` and `CompleteRegistrationViewModel.cs`:

```csharp
await Shell.Current.GoToAsync(AuthenticatedHomeRoutes.Home);
```

In `src/Toklong.Mobile/App.xaml.cs`, initialize authenticated services when
the startup route is the authenticated home:

```csharp
if (result.Route == AuthenticatedHomeRoutes.Home)
    _ = InitializeAuthenticatedServicesAsync();
```

- [ ] **Step 5: Run focused tests and compile the mobile target**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~AuthenticatedHome|FullyQualifiedName~Shell_RegistersAuthenticatedHome"
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj \
  -f net10.0-ios \
  -p:RuntimeIdentifier=iossimulator-arm64 \
  -p:CodesignKey=
```

Expected: XAML tests PASS and the iOS simulator build exits 0.

- [ ] **Step 6: Commit Task 2**

```bash
git add \
  src/Toklong.Mobile/Pages/AuthenticatedHomePage.xaml \
  src/Toklong.Mobile/Pages/AuthenticatedHomePage.xaml.cs \
  src/Toklong.Mobile/AppShell.xaml \
  src/Toklong.Mobile/MauiProgram.cs \
  src/Toklong.Mobile/App.xaml.cs \
  src/Toklong.Mobile/ViewModels/VerifyCodeViewModel.cs \
  src/Toklong.Mobile/ViewModels/CompleteRegistrationViewModel.cs \
  tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs
git commit -m "feat: add authenticated buyer seller home"
```

---

### Task 3: Explicit role override for the existing transaction root

**Files:**
- Modify: `src/Toklong.Mobile/Pages/TransactionsPage.xaml.cs`
- Modify: `src/Toklong.Mobile/ViewModels/TransactionsViewModel.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/AuthenticatedHomeRoutesTests.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj`

**Interfaces:**
- Consumes `AuthenticatedHomeRoutes.TryParseRole`.
- Produces `TransactionsViewModel.ApplyRoleNavigation(TransactionRoleRoute role)`.

- [ ] **Step 1: Add failing parser and page-contract tests**

Extend `AuthenticatedHomeRoutesTests`:

```csharp
[Fact]
public void Buying_and_selling_routes_do_not_alias_each_other()
{
    Assert.NotEqual(
        AuthenticatedHomeRoutes.Transactions(
            TransactionRoleRoute.Buying),
        AuthenticatedHomeRoutes.Transactions(
            TransactionRoleRoute.Selling));
}
```

Add this static page contract to `UiLayoutConsistencyTests`:

```csharp
[Fact]
public void TransactionsPage_AcceptsAnExplicitRoleQuery()
{
    var code = File.ReadAllText(
        Path.Combine(
            AppContext.BaseDirectory,
            "Ui",
            "Pages",
            "TransactionsPage.xaml.cs"));

    Assert.Contains(
        "IQueryAttributable",
        code,
        StringComparison.Ordinal);
    Assert.Contains(
        "ApplyRoleNavigation",
        code,
        StringComparison.Ordinal);
}
```

Add this deterministic source link to
`tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj`:

```xml
<None Include="../../src/Toklong.Mobile/Pages/TransactionsPage.xaml.cs"
      Link="Ui/Pages/TransactionsPage.xaml.cs"
      CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 2: Run focused tests and verify RED**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~TransactionsPage_AcceptsAnExplicitRoleQuery|FullyQualifiedName~Buying_and_selling_routes"
```

Expected: the page contract FAILS because `TransactionsPage` does not
implement `IQueryAttributable`.

- [ ] **Step 3: Expose role application without changing remembered default behavior**

In `TransactionsViewModel.cs`, add:

```csharp
public void ApplyRoleNavigation(TransactionRoleRoute role) =>
    SelectRole(
        role == TransactionRoleRoute.Buying
            ? RoleFilter.Buying
            : RoleFilter.Selling);
```

Keep the constructor's existing `Preferences.Default.Get(...)` logic. This is
what ordinary `//transactions` navigation continues to use.

Change the page declaration and add query handling in
`TransactionsPage.xaml.cs`:

```csharp
public partial class TransactionsPage :
    ContentPage,
    IQueryAttributable
{
    public void ApplyQueryAttributes(
        IDictionary<string, object> query)
    {
        if (query.TryGetValue("role", out var raw) &&
            AuthenticatedHomeRoutes.TryParseRole(
                raw?.ToString(),
                out var role))
            viewModel.ApplyRoleNavigation(role);
    }
}
```

Do not treat a missing or invalid role query as buying; leave the remembered
mode unchanged.

- [ ] **Step 4: Run focused and transaction-presentation tests**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~TransactionsPage_AcceptsAnExplicitRoleQuery|FullyQualifiedName~AuthenticatedHomeRoutesTests|FullyQualifiedName~TransactionFilter|FullyQualifiedName~TransactionPresentation"
```

Expected: PASS.

- [ ] **Step 5: Commit Task 3**

```bash
git add \
  src/Toklong.Mobile/Pages/TransactionsPage.xaml.cs \
  src/Toklong.Mobile/ViewModels/TransactionsViewModel.cs \
  tests/Toklong.Mobile.Core.Tests/AuthenticatedHomeRoutesTests.cs \
  tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs \
  tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj
git commit -m "feat: open transaction root in selected role"
```

---

### Task 4: Testable three-step wizard state

**Files:**
- Create: `src/Toklong.Mobile/Core/CreateOfferWizardState.cs`
- Create: `tests/Toklong.Mobile.Core.Tests/CreateOfferWizardStateTests.cs`

**Interfaces:**
- Produces:
  - `enum CreateOfferStep { Deal, Fulfillment, Review }`
  - `CreateOfferWizardState.CurrentStep`
  - `CreateOfferWizardState.IsDirty`
  - `CreateOfferWizardState.MarkDirty()`
  - `CreateOfferWizardState.MoveNext()`
  - `CreateOfferWizardState.MoveBack()`
  - `CreateOfferWizardState.Reset()`
- Consumed by Task 5.

- [ ] **Step 1: Write failing wizard-state tests**

Create `tests/Toklong.Mobile.Core.Tests/CreateOfferWizardStateTests.cs`:

```csharp
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class CreateOfferWizardStateTests
{
    [Fact]
    public void Starts_pristine_on_deal_step()
    {
        var wizard = new CreateOfferWizardState();

        Assert.Equal(CreateOfferStep.Deal, wizard.CurrentStep);
        Assert.False(wizard.IsDirty);
    }

    [Fact]
    public void Moves_forward_and_back_without_losing_dirty_state()
    {
        var wizard = new CreateOfferWizardState();
        wizard.MarkDirty();

        Assert.True(wizard.MoveNext());
        Assert.Equal(
            CreateOfferStep.Fulfillment,
            wizard.CurrentStep);
        Assert.True(wizard.MoveNext());
        Assert.Equal(CreateOfferStep.Review, wizard.CurrentStep);
        Assert.False(wizard.MoveNext());
        Assert.True(wizard.MoveBack());
        Assert.Equal(
            CreateOfferStep.Fulfillment,
            wizard.CurrentStep);
        Assert.True(wizard.IsDirty);
    }

    [Fact]
    public void Cannot_move_before_first_step()
    {
        var wizard = new CreateOfferWizardState();

        Assert.False(wizard.MoveBack());
        Assert.Equal(CreateOfferStep.Deal, wizard.CurrentStep);
    }

    [Fact]
    public void Reset_returns_to_pristine_first_step()
    {
        var wizard = new CreateOfferWizardState();
        wizard.MarkDirty();
        wizard.MoveNext();

        wizard.Reset();

        Assert.Equal(CreateOfferStep.Deal, wizard.CurrentStep);
        Assert.False(wizard.IsDirty);
    }
}
```

- [ ] **Step 2: Run tests and verify RED**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter FullyQualifiedName~CreateOfferWizardStateTests
```

Expected: FAIL because the wizard types do not exist.

- [ ] **Step 3: Add the minimal state type**

Create `src/Toklong.Mobile/Core/CreateOfferWizardState.cs`:

```csharp
namespace Toklong.Mobile.Core;

public enum CreateOfferStep
{
    Deal,
    Fulfillment,
    Review
}

public sealed class CreateOfferWizardState
{
    public CreateOfferStep CurrentStep { get; private set; }
    public bool IsDirty { get; private set; }

    public void MarkDirty() => IsDirty = true;

    public bool MoveNext()
    {
        if (CurrentStep == CreateOfferStep.Review)
            return false;
        CurrentStep++;
        return true;
    }

    public bool MoveBack()
    {
        if (CurrentStep == CreateOfferStep.Deal)
            return false;
        CurrentStep--;
        return true;
    }

    public void Reset()
    {
        CurrentStep = CreateOfferStep.Deal;
        IsDirty = false;
    }
}
```

- [ ] **Step 4: Run tests and verify GREEN**

Run the Task 4 test command again.

Expected: 4/4 PASS.

- [ ] **Step 5: Commit Task 4**

```bash
git add \
  src/Toklong.Mobile/Core/CreateOfferWizardState.cs \
  tests/Toklong.Mobile.Core.Tests/CreateOfferWizardStateTests.cs
git commit -m "feat: model create offer wizard progress"
```

---

### Task 5: Integrate step validation and preview into CreateOfferViewModel

**Files:**
- Modify: `src/Toklong.Mobile/Core/CreateOfferWizardState.cs`
- Modify: `src/Toklong.Mobile/ViewModels/CreateOfferViewModel.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj`

**Interfaces:**
- Consumes `CreateOfferWizardState` from Task 4.
- Produces these bindings:
  - `IsDealStep`, `IsFulfillmentStep`, `IsReviewStep`
  - `ProgressText`
  - `ContinueFromDealCommand`
  - `ContinueFromFulfillmentCommand`
  - `PreviousStepCommand`
  - `IsWizardDirty`
  - `CurrentStep`
  - field-specific error strings and visibility flags
  - `ValidationFailed(CreateOfferValidationTarget)` for focus/scroll
  - `DiscardDraft()` for confirmed destructive exit

- [ ] **Step 1: Add a failing ViewModel wizard contract**

Link the ViewModel source deterministically in
`Toklong.Mobile.Core.Tests.csproj`:

```xml
<None Include="../../src/Toklong.Mobile/ViewModels/CreateOfferViewModel.cs"
      Link="Ui/ViewModels/CreateOfferViewModel.cs"
      CopyToOutputDirectory="PreserveNewest" />
```

Add to `UiLayoutConsistencyTests`:

```csharp
[Fact]
public void CreateOfferViewModel_ExposesStepValidationAndDiscardContract()
{
    var code = File.ReadAllText(
        Path.Combine(
            AppContext.BaseDirectory,
            "Ui",
            "ViewModels",
            "CreateOfferViewModel.cs"));

    Assert.Contains("ContinueFromDealCommand", code);
    Assert.Contains("ContinueFromFulfillmentCommand", code);
    Assert.Contains("PreviousStepCommand", code);
    Assert.Contains("SellerPhoneError", code);
    Assert.Contains("DeliveryAddressError", code);
    Assert.Contains("ValidationFailed", code);
    Assert.Contains("CreateOfferValidationTarget", code);
    Assert.Contains("DiscardDraft()", code);
}
```

- [ ] **Step 2: Run the contract and verify RED**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter FullyQualifiedName~CreateOfferViewModel_ExposesStepValidationAndDiscardContract
```

Expected: FAIL because the current ViewModel exposes the review sheet rather
than step validation and explicit draft discard.

- [ ] **Step 3: Add wizard bindings and commands to the ViewModel**

Add a field:

```csharp
private readonly CreateOfferWizardState wizard = new();
```

Add this UI-only validation target beside `CreateOfferStep` in
`CreateOfferWizardState.cs`:

```csharp
public enum CreateOfferValidationTarget
{
    SellerPhone,
    ProductName,
    ProductPhoto,
    Amount,
    DeliveryAddress,
    Condition,
    KnownDefects,
    CostPreview
}
```

Expose an event on `CreateOfferViewModel`:

```csharp
public event EventHandler<CreateOfferValidationTarget>? ValidationFailed;
```

Expose state:

```csharp
public CreateOfferStep CurrentStep => wizard.CurrentStep;
public bool IsDealStep => CurrentStep == CreateOfferStep.Deal;
public bool IsFulfillmentStep =>
    CurrentStep == CreateOfferStep.Fulfillment;
public bool IsReviewStep => CurrentStep == CreateOfferStep.Review;
public bool IsWizardDirty => wizard.IsDirty;
public string ProgressText => CurrentStep switch
{
    CreateOfferStep.Deal => "ขั้นที่ 1 จาก 3",
    CreateOfferStep.Fulfillment => "ขั้นที่ 2 จาก 3",
    _ => "ขั้นที่ 3 จาก 3"
};
```

Add commands:

```csharp
public ICommand ContinueFromDealCommand =>
    new Command(ContinueFromDeal);
public ICommand ContinueFromFulfillmentCommand =>
    new AsyncCommand(ContinueFromFulfillmentAsync);
public ICommand PreviousStepCommand =>
    new Command(MoveToPreviousStep);
```

Use a shared notifier:

```csharp
private void RaiseWizardProperties()
{
    OnPropertyChanged(nameof(CurrentStep));
    OnPropertyChanged(nameof(IsDealStep));
    OnPropertyChanged(nameof(IsFulfillmentStep));
    OnPropertyChanged(nameof(IsReviewStep));
    OnPropertyChanged(nameof(IsWizardDirty));
    OnPropertyChanged(nameof(ProgressText));
}
```

Split current `ValidateQuickDeal` into exact step validation. Add
`SellerPhoneError`, `ProductNameError`, `ProductPhotoError`, `AmountError`,
`DeliveryAddressError`, `ConditionError`, and `KnownDefectsError` properties,
plus the matching `Has...Error` booleans. Each validator clears only its
step's previous errors, populates every invalid field in that step, then raises
`ValidationFailed` once for the first invalid target:

```csharp
private bool ValidateDealStep(
    out string cleanSellerPhone,
    out decimal amount)
{
    cleanSellerPhone =
        ThaiMobilePhoneInput.Sanitize(SellerPhoneNumber);
    amount = 0;
    CreateOfferValidationTarget? firstInvalid = null;
    SellerPhoneError =
        ThaiMobilePhoneInput.IsValid(cleanSellerPhone)
            ? ""
            : "กรอกเบอร์มือถือผู้ขาย 10 หลัก เช่น 081-234-5678";
    if (SellerPhoneError.Length > 0)
        firstInvalid ??= CreateOfferValidationTarget.SellerPhone;

    ProductNameError = string.IsNullOrWhiteSpace(ProductName)
        ? "ใส่ชื่อสินค้า"
        : "";
    if (ProductNameError.Length > 0)
        firstInvalid ??= CreateOfferValidationTarget.ProductName;

    if (!string.IsNullOrWhiteSpace(selectedPhotoPath) &&
        !File.Exists(selectedPhotoPath))
    {
        draftPhotoStore.Delete(selectedPhotoPath);
        selectedPhotoPath = "";
        SelectedPhotoName = "";
        ProductPhotoError = "ไม่พบรูปที่เลือก กรุณาเลือกรูปใหม่";
        firstInvalid ??= CreateOfferValidationTarget.ProductPhoto;
    }
    if (!TryParseAmount(out amount))
    {
        AmountError = "ใส่ราคาที่ตกลงกันให้ถูกต้อง";
        firstInvalid ??= CreateOfferValidationTarget.Amount;
    }
    else if (amount is < 1_000 or > 30_000 ||
        decimal.Round(amount, 2) != amount)
    {
        AmountError =
            "ราคาต้องอยู่ระหว่าง 1,000–30,000 บาท และมีทศนิยมไม่เกิน 2 ตำแหน่ง";
        firstInvalid ??= CreateOfferValidationTarget.Amount;
    }
    if (firstInvalid is not null)
        ValidationFailed?.Invoke(this, firstInvalid.Value);
    return firstInvalid is null;
}

private bool ValidateFulfillmentStep()
{
    if (!IsPhysical)
        return true;
    if ((HasSavedAddress && UseSavedAddress) ||
        (!string.IsNullOrWhiteSpace(AddressLine) &&
         SelectedProvince is not null &&
         SelectedDistrict is not null &&
         SelectedSubdistrict is not null))
        return true;

    DeliveryAddressError =
        "กรอกบ้านเลขที่และเลือกพื้นที่จัดส่งให้ครบ";
    ValidationFailed?.Invoke(
        this,
        CreateOfferValidationTarget.DeliveryAddress);
    return false;
}
```

Clear a field's inline error as soon as that field changes. Condition and
known-defect validation remains on final submit, but now sets
`ConditionError`/`KnownDefectsError` and raises the matching first-invalid
target rather than only setting the page-level `Message`.

Advance Step 1 synchronously:

```csharp
private void ContinueFromDeal()
{
    Message = "";
    if (!ValidateDealStep(out _, out _))
        return;
    if (wizard.MoveNext())
        RaiseWizardProperties();
}
```

Rename the current review-pricing operation to
`ContinueFromFulfillmentAsync`; validate both earlier values and fulfillment,
request the preview, and only move to review after the current-price guards
pass:

```csharp
private async Task ContinueFromFulfillmentAsync()
{
    Message = "";
    if (!ValidateDealStep(out _, out var amount) ||
        !ValidateFulfillmentStep())
        return;

    var itemPriceSatang = checked((long)(amount * 100m));
    InvalidateReviewPricing();
    var requestVersion = costPreviewTracker.Begin();
    using var cancellation = new CancellationTokenSource();
    reviewPricingCancellation = cancellation;
    IsReviewPricing = true;
    try
    {
        var preview =
            await transactionService.GetBuyerCostPreviewAsync(
                itemPriceSatang,
                cancellation.Token);
        if (!costPreviewTracker.IsCurrent(requestVersion) ||
            cancellation.IsCancellationRequested ||
            preview.ItemPriceSatang != itemPriceSatang ||
            !TryGetPreviewPriceSatang(out var current) ||
            current != itemPriceSatang)
            return;

        CostPreview = preview;
        if (wizard.MoveNext())
            RaiseWizardProperties();
    }
    catch (OperationCanceledException)
    {
    }
    catch
    {
        if (costPreviewTracker.IsCurrent(requestVersion))
            Message =
                "คำนวณค่าคุ้มครองผู้ซื้อไม่ได้ กรุณาลองอีกครั้ง";
    }
    finally
    {
        if (ReferenceEquals(
                reviewPricingCancellation,
                cancellation))
        {
            reviewPricingCancellation = null;
            IsReviewPricing = false;
        }
    }
}
```

Going back from review invalidates the preview before changing step:

```csharp
private void MoveToPreviousStep()
{
    if (wizard.CurrentStep == CreateOfferStep.Review)
        InvalidateReviewPricing();
    Message = "";
    if (wizard.MoveBack())
        RaiseWizardProperties();
}
```

Change `InvalidateReviewPricing()` so it does not force any page visibility
property. Remove `IsReviewSheetOpen` and the old open/close-review commands.

- [ ] **Step 4: Mark the wizard dirty from all user-editable offer values**

In every user-editable setter, after a successful `SetProperty`, call:

```csharp
wizard.MarkDirty();
OnPropertyChanged(nameof(IsWizardDirty));
```

Apply this to:

- `SellerPhoneNumber`
- `ProductName`
- `AmountBaht`
- `AgreementDetails`
- `KnownDefects`
- `SelectedConditionIndex`
- `AddressLine`
- `SelectedProvince`
- `SelectedDistrict`
- `SelectedSubdistrict`
- `RememberAddress`
- photo selection/removal
- fulfillment selection

Do not mark dirty while `LoadAsync` assigns profile/saved-address defaults.
Add a private `bool isInitializing` guard set around the initial load:

```csharp
if (!isInitializing)
{
    wizard.MarkDirty();
    OnPropertyChanged(nameof(IsWizardDirty));
}
```

AI-generated values count as user draft changes and must mark the wizard
dirty. Profile and saved-address hydration during `LoadAsync` do not.

- [ ] **Step 5: Preserve final submit rules and reset only after success**

At the beginning of `SubmitAsync`, require:

```csharp
if (!IsReviewStep ||
    !ValidateDealStep(
        out var cleanSellerPhone,
        out var amount) ||
    !ValidateFulfillmentStep())
    return;
```

Also require a current preview that matches the current parsed price. If it is
missing or stale, set the page-level retry message, raise
`ValidationFailed` with `CostPreview`, and do not call the create endpoint.
Keep the existing condition/defect validation and
`CreateBuyerOfferRequest` composition. After the API returns successfully:

```csharp
wizard.Reset();
RaiseWizardProperties();
await Shell.Current.GoToAsync(
    AuthenticatedHomeRoutes.Transactions(
        TransactionRoleRoute.Buying));
```

Then push `TransactionDetailPage` exactly as today. Do not reset the wizard or
delete the selected photo in any failure path.

Add explicit destructive cleanup for a user-confirmed exit:

```csharp
public void DiscardDraft()
{
    CancelReviewPricing();
    draftPhotoStore.Delete(selectedPhotoPath);
    selectedPhotoPath = "";
    SelectedPhotoName = "";
    DiscardAiSource();
    wizard.Reset();
    RaiseWizardProperties();
}
```

This method is never called by `OnDisappearing`; navigation to a picker,
backgrounding, a failed request, or a transient page disappearance must not
silently destroy the draft.

- [ ] **Step 6: Run the focused contract and compile before changing XAML**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter FullyQualifiedName~CreateOfferViewModel_ExposesStepValidationAndDiscardContract
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj \
  -f net10.0-ios \
  -p:RuntimeIdentifier=iossimulator-arm64 \
  -p:CodesignKey=
```

Expected: the focused contract passes and the build exits 0.

- [ ] **Step 7: Commit ViewModel integration**

```bash
git add \
  src/Toklong.Mobile/Core/CreateOfferWizardState.cs \
  src/Toklong.Mobile/ViewModels/CreateOfferViewModel.cs \
  tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs \
  tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj
git commit -m "feat: stage buyer offer creation in three steps"
```

---

### Task 6: Three-step CreateOfferPage and approved exit dialog

**Files:**
- Modify: `src/Toklong.Mobile/Pages/CreateOfferPage.xaml`
- Modify: `src/Toklong.Mobile/Pages/CreateOfferPage.xaml.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj`

**Interfaces:**
- Consumes all Task 5 bindings.
- Keeps the existing AI sheet and create-offer submit command.
- Maps `ValidationFailed` to a named control and scrolls/focuses that first
  invalid input.

- [ ] **Step 1: Add failing copy, progress, and exit-contract tests**

Add to `UiLayoutConsistencyTests`:

```csharp
[Fact]
public void CreateOffer_ShowsApprovedStepTitlesAndThreeSegmentProgress()
{
    var create = Load(
        "Ui",
        "Pages",
        "CreateOfferPage.xaml");
    var labels = create
        .Descendants(Maui + "Label")
        .Select(label => AttributeValue(label, "Text"))
        .ToArray();
    var progress = create
        .Descendants()
        .Single(element =>
            AttributeValue(element, "AutomationId") ==
                "CreateOfferProgress");

    Assert.Contains("ข้อมูลดีล", labels);
    Assert.Contains("การรับสินค้า", labels);
    Assert.Contains("ตรวจและส่ง", labels);
    Assert.Equal(
        3,
        progress.Descendants(Maui + "BoxView").Count());
    Assert.Contains(
        create.Descendants(Maui + "Label"),
        label =>
            AttributeValue(label, "Text") ==
                "{Binding ProgressText}");
}

[Fact]
public void CreateOffer_UsesThreeFullPageSteps()
{
    var create = Load("Ui", "Pages", "CreateOfferPage.xaml");
    var steps = create
        .Descendants()
        .Where(element =>
            AttributeValue(element, "AutomationId") is
                "CreateOfferDealStep" or
                "CreateOfferFulfillmentStep" or
                "CreateOfferReviewStep")
        .ToArray();

    Assert.Equal(3, steps.Length);
    Assert.Equal(
        "{Binding IsDealStep}",
        AttributeValue(steps[0], "IsVisible"));
    Assert.Equal(
        "{Binding IsFulfillmentStep}",
        AttributeValue(steps[1], "IsVisible"));
    Assert.Equal(
        "{Binding IsReviewStep}",
        AttributeValue(steps[2], "IsVisible"));
    Assert.DoesNotContain(
        create.Descendants(),
        element =>
            AttributeValue(element, "AutomationId") ==
                "QuickDealReviewSheet");
}

[Fact]
public void CreateOffer_FinalStepOwnsPreviewAndOnlySubmitAction()
{
    var create = Load("Ui", "Pages", "CreateOfferPage.xaml");
    var review = create
        .Descendants()
        .Single(element =>
            AttributeValue(element, "AutomationId") ==
                "CreateOfferReviewStep");
    var submit = create
        .Descendants(Maui + "Button")
        .Where(button =>
            AttributeValue(button, "Command") ==
                "{Binding SubmitCommand}")
        .ToArray();

    Assert.Single(submit);
    Assert.Contains(submit[0].Ancestors(), node => node == review);
    Assert.Equal(
        "ส่งข้อเสนอให้ผู้ขาย",
        AttributeValue(submit[0], "Text"));
    Assert.Contains(
        review.Descendants(Maui + "Border"),
        border =>
            AttributeValue(border, "AutomationId") ==
                "ReviewCostSummary");
}

[Fact]
public void CreateOffer_UsesApprovedExitCopy()
{
    var code = File.ReadAllText(
        Path.Combine(
            AppContext.BaseDirectory,
            "Ui",
            "Pages",
            "CreateOfferPage.xaml.cs"));

    Assert.Contains("ยังสร้างข้อเสนอไม่เสร็จ", code);
    Assert.Contains(
        "ถ้าออกตอนนี้ ข้อมูลที่กรอกไว้จะหาย",
        code);
    Assert.Contains("กลับไปกรอกต่อ", code);
    Assert.Contains("ออกจากหน้านี้", code);
}

[Fact]
public void CreateOffer_RendersInlineErrorsAndFocusesFirstInvalidField()
{
    var create = Load("Ui", "Pages", "CreateOfferPage.xaml");
    var code = File.ReadAllText(
        Path.Combine(
            AppContext.BaseDirectory,
            "Ui",
            "Pages",
            "CreateOfferPage.xaml.cs"));

    Assert.Contains(
        create.Descendants(Maui + "Label"),
        label =>
            AttributeValue(label, "Text") ==
                "{Binding SellerPhoneError}");
    Assert.Contains(
        create.Descendants(Maui + "Label"),
        label =>
            AttributeValue(label, "Text") ==
                "{Binding DeliveryAddressError}");
    Assert.Contains(
        "ValidationFailed",
        code,
        StringComparison.Ordinal);
    Assert.Contains(
        "ScrollToAsync",
        code,
        StringComparison.Ordinal);
    Assert.Contains(
        ".Focus()",
        code,
        StringComparison.Ordinal);
}
```

Add this link to `Toklong.Mobile.Core.Tests.csproj`:

```xml
<None Include="../../src/Toklong.Mobile/Pages/CreateOfferPage.xaml.cs"
      Link="Ui/Pages/CreateOfferPage.xaml.cs"
      CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 2: Run the create-offer UI tests and verify RED**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~CreateOffer"
```

Expected: FAIL because the XAML still has two progress segments and the code
does not contain the approved exit copy.

- [ ] **Step 3: Restructure XAML into three full-page containers**

Keep the existing header, but change `CreateOfferProgress` to three boxes whose
colors bind to step state through data triggers:

```xml
<Grid
    AutomationId="CreateOfferProgress"
    ColumnDefinitions="*,*,*"
    ColumnSpacing="8">
    <BoxView HeightRequest="4" CornerRadius="2"
             Color="{StaticResource BrandBlue}" />
    <BoxView Grid.Column="1" HeightRequest="4" CornerRadius="2"
             Color="#DCE8F5">
        <BoxView.Triggers>
            <DataTrigger TargetType="BoxView"
                         Binding="{Binding IsDealStep}"
                         Value="False">
                <Setter Property="Color"
                        Value="{StaticResource BrandBlue}" />
            </DataTrigger>
        </BoxView.Triggers>
    </BoxView>
    <BoxView Grid.Column="2" HeightRequest="4" CornerRadius="2"
             Color="#DCE8F5">
        <BoxView.Triggers>
            <DataTrigger TargetType="BoxView"
                         Binding="{Binding IsReviewStep}"
                         Value="True">
                <Setter Property="Color"
                        Value="{StaticResource BrandBlue}" />
            </DataTrigger>
        </BoxView.Triggers>
    </BoxView>
</Grid>
```

Add a visible `{Binding ProgressText}` label to the header.

Create three sibling `ScrollView` containers in grid row 1. Give them
AutomationIds `CreateOfferDealStep`, `CreateOfferFulfillmentStep`, and
`CreateOfferReviewStep`, and bind `IsVisible` to `IsDealStep`,
`IsFulfillmentStep`, and `IsReviewStep`, respectively. Move existing controls
rather than duplicating bindings:

- Step 1: `SellerPhoneSection`, `ProductNameSection`,
  `ItemPriceSection`, `OpenAiAgreementDraftButton`,
  `OptionalProductPhotoField`, and `OptionalDealDetailsDisclosure`.
- Step 2: fulfillment selector and `DeliveryAddressSection`.
- Step 3: review summary, condition buttons, conditional defect editor,
  `ReviewCostSummary`, validation message, and
  `SubmitReviewedOfferButton`.

Name each focus target (`SellerPhoneEntry`, `ProductNameEntry`,
`ProductPhotoButton`, `AmountEntry`, `DeliveryAddressAnchor`,
`ConditionPickerAnchor`, `KnownDefectsEditor`, and `ReviewCostSummary`) and
place its bound error label directly below the affected input. Set
an accessibility description on each visible error. When validation fails,
announce the first error with `SemanticScreenReader.Announce(...)` before
moving focus. Keep the page-level `Message` only for request/network failures.

Use these exact primary actions:

```xml
<Button
    Style="{StaticResource RefinedPrimaryButton}"
    Command="{Binding ContinueFromDealCommand}"
    Text="ถัดไป: การรับสินค้า  →" />

<Button
    Style="{StaticResource RefinedPrimaryButton}"
    Command="{Binding ContinueFromFulfillmentCommand}"
    Text="ถัดไป: ตรวจข้อมูล  →" />

<Button
    AutomationId="SubmitReviewedOfferButton"
    Style="{StaticResource RefinedPrimaryButton}"
    Command="{Binding SubmitCommand}"
    Text="ส่งข้อเสนอให้ผู้ขาย" />
```

Keep `กำลังคำนวณค่าใช้จ่าย...` and `กำลังสร้างข้อเสนอ...` loading triggers.
Remove the full-screen `QuickDealReviewSheet` overlay entirely. Keep
`AiAgreementDraftSheet` unchanged.

- [ ] **Step 4: Implement within-wizard back and safe dirty-exit handling**

In `CreateOfferPage.xaml.cs`, handle back in this order:

1. Close AI sheet.
2. Move from Review to Fulfillment or Fulfillment to Deal.
3. Pop immediately if pristine.
4. Show approved confirmation if dirty.

Use:

```csharp
private async Task HandleBackAsync()
{
    if (viewModel.IsAiSheetOpen)
    {
        viewModel.CloseAiSheetCommand.Execute(null);
        return;
    }
    if (viewModel.CurrentStep != CreateOfferStep.Deal)
    {
        viewModel.PreviousStepCommand.Execute(null);
        return;
    }
    if (!viewModel.IsWizardDirty)
    {
        await Shell.Current.GoToAsync("..");
        return;
    }

    var keepEditing = await DisplayAlert(
        "ยังสร้างข้อเสนอไม่เสร็จ",
        "ถ้าออกตอนนี้ ข้อมูลที่กรอกไว้จะหาย",
        "กลับไปกรอกต่อ",
        "ออกจากหน้านี้");
    if (!keepEditing)
    {
        viewModel.DiscardDraft();
        await Shell.Current.GoToAsync("..");
    }
}
```

The toolbar button calls `await HandleBackAsync()`. For hardware back, return
`true` and dispatch `HandleBackAsync`:

```csharp
protected override bool OnBackButtonPressed()
{
    Dispatcher.Dispatch(async () => await HandleBackAsync());
    return true;
}
```

Subscribe to `viewModel.ValidationFailed` while the page is visible and
unsubscribe while it is not. Map the enum target to the named control, call
the owning step's `ScrollView.ScrollToAsync(target, ScrollToPosition.Center,
true)`, then call `target.Focus()`. For non-text anchors, focus the actionable
button/picker within the section. Dispatch onto the MAUI UI thread.

- [ ] **Step 5: Run all create-offer UI tests and build**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter FullyQualifiedName~CreateOffer
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj \
  -f net10.0-ios \
  -p:RuntimeIdentifier=iossimulator-arm64 \
  -p:CodesignKey=
```

Expected: all filtered tests PASS and build exits 0.

- [ ] **Step 6: Commit Task 6**

```bash
git add \
  src/Toklong.Mobile/Pages/CreateOfferPage.xaml \
  src/Toklong.Mobile/Pages/CreateOfferPage.xaml.cs \
  tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs \
  tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj
git commit -m "feat: redesign buyer offer as three step wizard"
```

---

### Task 7: Update product UX and acceptance documentation

**Files:**
- Modify: `docs/02_UI_UX_AND_CONTENT_SPEC.md`
- Modify: `docs/05_ACCEPTANCE_TESTS.md`

**Interfaces:**
- Documents the exact behavior delivered by Tasks 1–6.

- [ ] **Step 1: Update the UX specification**

In `docs/02_UI_UX_AND_CONTENT_SPEC.md`, replace the prior authenticated-start
and create-offer review-sheet wording with:

```markdown
### Authenticated role home

After registered-phone verification, show the centered TOKLONG brand,
`เริ่มดีลอย่างมั่นใจ`, and
`สร้างข้อเสนอซื้อ หรือจัดการรายการขาย`.
`ซื้อ` opens the existing transaction root in buying mode. `ขาย` opens it in
selling mode. These are navigation choices, not account roles. Do not show
login/register actions or a seller-created-link action.

### Three-step buyer offer creation

1. `ข้อมูลดีล`: seller phone, product name, agreed item price, and optional
   AI/photo/details.
2. `การรับสินค้า`: physical/digital choice and the applicable locked delivery
   address.
3. `ตรวจและส่ง`: condition, conditional defect text, server cost preview,
   summary, and the only create action `ส่งข้อเสนอให้ผู้ขาย`.

The wizard keeps values only while the page is open. A dirty exit says
`ยังสร้างข้อเสนอไม่เสร็จ`,
`ถ้าออกตอนนี้ ข้อมูลที่กรอกไว้จะหาย`,
`กลับไปกรอกต่อ`, and `ออกจากหน้านี้`.
```

Remove text that requires a two-step progress bar or review bottom sheet.
Retain buyer-first, server preview, physical/digital, AI, photo, privacy, and
shipping rules.

- [ ] **Step 2: Add acceptance scenarios**

Add to `docs/05_ACCEPTANCE_TESTS.md`:

```markdown
### A0.0.4.4 — Authenticated home routes by chosen transaction role

**Given** an authenticated account has both buyer and seller transactions
**When** the user taps `ซื้อ` on the authenticated home
**Then** the existing transaction root opens with buying selected
**When** the user returns home and taps `ขาย`
**Then** the same root opens with selling selected
**And** no seller-created link action is shown.

### A0.0.4.5 — Buyer offer wizard creates only on final submit

**Given** an authenticated buyer opens `สร้างข้อเสนอ`
**When** the buyer completes `ข้อมูลดีล` and `การรับสินค้า`
**Then** no transaction, snapshot, notification, payment, or audit transition
exists
**When** preview fails
**Then** entered values remain and `ลองอีกครั้ง` is available
**When** the buyer reaches `ตรวจและส่ง` and taps
`ส่งข้อเสนอให้ผู้ขาย`
**Then** exactly one buyer-created offer is created.

### A0.0.4.6 — Dirty wizard exit uses plain warning copy

**Given** the buyer changed an offer value
**When** the buyer attempts to leave from the first step
**Then** the app shows `ยังสร้างข้อเสนอไม่เสร็จ`
and `ถ้าออกตอนนี้ ข้อมูลที่กรอกไว้จะหาย`
**And** `กลับไปกรอกต่อ` preserves values
**And** `ออกจากหน้านี้` discards only the in-memory wizard.
```

- [ ] **Step 3: Check documentation consistency**

Run:

```bash
rg -n \
  'QuickDealReviewSheet|สองขั้น|สร้างลิงก์ขาย|seller-created link|review bottom sheet' \
  docs/02_UI_UX_AND_CONTENT_SPEC.md \
  docs/05_ACCEPTANCE_TESTS.md
```

Expected: no active requirement mandates the old two-step/review-sheet UI or a
seller-created link. Historical text, if retained, must be explicitly marked
superseded.

- [ ] **Step 4: Commit Task 7**

```bash
git add \
  docs/02_UI_UX_AND_CONTENT_SPEC.md \
  docs/05_ACCEPTANCE_TESTS.md
git commit -m "docs: specify role home and three step offers"
```

---

### Task 8: Full verification and simulator review

**Files:**
- No planned source changes; fix only failures caused by Tasks 1–7.

**Interfaces:**
- Verifies the completed vertical slice.

- [ ] **Step 1: Run formatting and secret/copy hygiene checks**

Run:

```bash
git diff --check
rg -n \
  'สร้างลิงก์ขาย|วางลิงก์รายการ|seller-created' \
  src/Toklong.Mobile \
  -g '*.xaml' -g '*.cs'
```

Expected: `git diff --check` exits 0; prohibited seller-first/clipboard copy is
absent from the mobile UI.

- [ ] **Step 2: Run every test project**

Run:

```bash
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj
dotnet test tests/Toklong.Crm.Tests/Toklong.Crm.Tests.csproj
```

Expected: all test projects PASS with zero failed tests.

- [ ] **Step 3: Build and package the iOS simulator app**

Run:

```bash
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj \
  -f net10.0-ios \
  -p:RuntimeIdentifier=iossimulator-arm64 \
  -p:CodesignKey=
```

Expected: build exits 0 and produces:

```text
src/Toklong.Mobile/bin/Debug/net10.0-ios/iossimulator-arm64/Toklong.Mobile.app
```

- [ ] **Step 4: Install and inspect on the booted iPhone simulator**

Run:

```bash
xcrun simctl install booted \
  src/Toklong.Mobile/bin/Debug/net10.0-ios/iossimulator-arm64/Toklong.Mobile.app
xcrun simctl launch booted th.co.toklong.mobile
xcrun simctl io booted screenshot /tmp/toklong-authenticated-home-wizard.png
```

Verify visually:

- Real TOKLONG logo is centered on authenticated home.
- Home contains no login/register action.
- `ซื้อ` is blue and `ขาย` is purple with visible role labels.
- Buyer and seller cards open the existing transaction root in the requested
  mode.
- Create-offer steps do not horizontally overflow.
- Required fields and each primary action are visible without being covered by
  keyboard or bottom bars.
- Review is a full page, not a sheet.
- Dynamic Type and VoiceOver labels remain usable.

- [ ] **Step 5: Review final diff and commit any verification-only fixes**

Run:

```bash
git status --short
git diff --stat
git diff --check
```

If verification required source fixes, rerun the affected focused test, the
full Mobile Core suite, and the iOS build before committing:

```bash
git commit -m "fix: complete authenticated deal flow verification"
```

Stage only the source files shown by `git status --short` that were changed to
fix a reproduced verification failure, then use the commit command above. If
no fixes were required, do not create an empty commit.

---

## Completion Report Requirements

At implementation handoff, report:

1. What changed.
2. Which requirement or state transitions were implemented.
3. Which tests were added or updated.
4. Assumptions made.
5. Open decisions or blocked provider capabilities.
6. The next smallest vertical slice.

Also report the exact verification commands and their fresh pass/fail counts,
the iOS simulator artifact path, the simulator/device inspected, and the final
commit list.
