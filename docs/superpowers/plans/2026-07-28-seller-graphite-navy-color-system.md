# Seller Graphite Navy Color System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace saturated purple seller-role surfaces with the approved Graphite Navy palette and make all three seller summary tiles white with semantic borders and non-color selected markers.

**Architecture:** Add one code-native `SellerColorPalette` authority that is consumed by the mobile presentation model and XAML through `x:Static`. Apply the palette only to seller-role surfaces and seller-only assets; retain buyer and status colors. Keep summary-tile behavior in the existing page and change only its presentation contract.

**Tech Stack:** .NET 10, .NET MAUI XAML, C# records/presentation properties, xUnit, XML contract tests, SVG assets, iOS Simulator.

## Global Constraints

- This is presentation-only: do not change transaction data, classification, actions, state transitions, money, shipping, disputes, analytics, or authorization.
- Preserve buyer colors exactly: role `#145FC7`, header `#3C8AF1 → #236DCE → #185CB9`, background `#EAF4FF`.
- Seller palette is exact: role `#3B5266`, header `#4B6073 → #3D5163 → #304354`, surface `#EDF2F5`, border `#C8D4DC`, secondary `#DCE7EC`, badge `#F3F7F9`, accent `#8DE8D2`.
- Summary tiles remain white in normal and selected states. Selection uses a thicker semantic border, a visible dot, a subtle shadow, and the existing selected-state semantic announcement.
- Summary semantic colors are exact: รอตอบ border `#DDB866` / text `#8A5100`; ต้องส่ง border `#9BAEBC` / text `#3B5266`; กำลังไปต่อ border `#9CC4EC` / text `#145FC7`.
- Do not dim unselected tiles and do not rely on color alone.
- Keep amber for new-offer urgency, blue for in-progress work, red for problems, and mint as a small accent.
- Seller surfaces continue to show item price only; never expose buyer protection fee or buyer total.
- SHIPPOP-managed seller records remain status-only and never expose manual Add Tracking.
- Preserve exact deadline wrapping and existing accessibility semantics.
- Do not globally replace purple. Change only seller-role surfaces and seller-only assets named in this plan.
- Preserve and do not stage the user-owned dirty files: `src/Toklong.Mobile/Core/TransactionStatePresenter.cs`, the unrelated simulator-session hunk in `src/Toklong.Mobile/MauiProgram.cs`, `tests/Toklong.Mobile.Core.Tests/TransactionPresentationTests.cs`, `src/Toklong.Mobile/Core/DevelopmentSimulatorMobileSessionStore.cs`, and `tests/Toklong.Mobile.Core.Tests/DevelopmentSimulatorMobileSessionStoreTests.cs`.

---

## File Structure

- Create `src/Toklong.Mobile/Core/SellerColorPalette.cs` as the single code/XAML palette authority.
- Create `tests/Toklong.Mobile.Core.Tests/SellerGraphiteNavyColorTests.cs` for exact palette and presentation-model contracts without touching the user-owned dirty `TransactionPresentationTests.cs`.
- Modify `src/Toklong.Mobile/Core/AppTransaction.cs` to consume the palette for seller role and completed-progress presentation.
- Modify the six `progress_*_seller_completed.svg` files and `ui_shipping_label.svg`; these are seller-only vector assets.
- Modify `tests/Toklong.Mobile.Core.Tests/BrandAssetConsistencyTests.cs` for the seller SVG palette.
- Modify `src/Toklong.Mobile/Pages/AuthenticatedHomePage.xaml`, `TransactionsPage.xaml`, `TransactionDetailPage.xaml`, and `ShippingLabelPage.xaml` for seller-role surfaces.
- Modify `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs` for XAML structure, palette references, white summary tiles, and selected-state accessibility.

---

### Task 1: Establish the Graphite Navy palette authority

**Files:**
- Create: `src/Toklong.Mobile/Core/SellerColorPalette.cs`
- Create: `tests/Toklong.Mobile.Core.Tests/SellerGraphiteNavyColorTests.cs`
- Modify: `src/Toklong.Mobile/Core/AppTransaction.cs:154-180`
- Modify: `src/Toklong.Mobile/Core/AppTransaction.cs:567-568`
- Modify: `tests/Toklong.Mobile.Core.Tests/BrandAssetConsistencyTests.cs:126-165`
- Modify: `src/Toklong.Mobile/Resources/Images/progress_agreement_seller_completed.svg`
- Modify: `src/Toklong.Mobile/Resources/Images/progress_digital_handoff_seller_completed.svg`
- Modify: `src/Toklong.Mobile/Resources/Images/progress_payment_seller_completed.svg`
- Modify: `src/Toklong.Mobile/Resources/Images/progress_payout_seller_completed.svg`
- Modify: `src/Toklong.Mobile/Resources/Images/progress_physical_handoff_seller_completed.svg`
- Modify: `src/Toklong.Mobile/Resources/Images/progress_physical_receipt_seller_completed.svg`

**Interfaces:**
- Consumes: `AppTransactionRole`, `AppTransaction`, and existing buyer presentation values.
- Produces: public constant strings on `SellerColorPalette`; seller `AppTransaction` role/progress properties backed by those constants; seller-completed SVGs using the same role/header/accent colors.

- [ ] **Step 1: Write the failing palette and presentation tests**

Create `SellerGraphiteNavyColorTests.cs`:

```csharp
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class SellerGraphiteNavyColorTests
{
    [Fact]
    public void Palette_exposes_the_approved_graphite_tokens()
    {
        Assert.Equal("#3B5266", SellerColorPalette.Role);
        Assert.Equal("#4B6073", SellerColorPalette.HeaderStart);
        Assert.Equal("#3D5163", SellerColorPalette.HeaderMiddle);
        Assert.Equal("#304354", SellerColorPalette.HeaderEnd);
        Assert.Equal("#EDF2F5", SellerColorPalette.Surface);
        Assert.Equal("#C8D4DC", SellerColorPalette.Border);
        Assert.Equal("#DCE7EC", SellerColorPalette.Secondary);
        Assert.Equal("#F3F7F9", SellerColorPalette.BadgeSurface);
        Assert.Equal("#8DE8D2", SellerColorPalette.Accent);
        Assert.Equal("#DDB866", SellerColorPalette.NewOfferBorder);
        Assert.Equal("#8A5100", SellerColorPalette.NewOfferText);
        Assert.Equal("#9BAEBC", SellerColorPalette.FulfillmentBorder);
        Assert.Equal("#9CC4EC", SellerColorPalette.InProgressBorder);
    }

    [Fact]
    public void Seller_uses_graphite_while_buyer_palette_is_unchanged()
    {
        var seller = Create(AppTransactionRole.Seller, "PaidOut");
        var buyer = Create(AppTransactionRole.Buyer, "PaidOut");

        Assert.Equal(SellerColorPalette.Role, seller.RoleColor);
        Assert.Equal(SellerColorPalette.Surface, seller.RoleBackground);
        Assert.Equal(SellerColorPalette.HeaderStart, seller.RoleHeaderStart);
        Assert.Equal(SellerColorPalette.HeaderMiddle, seller.RoleHeaderMiddle);
        Assert.Equal(SellerColorPalette.HeaderEnd, seller.RoleHeaderEnd);
        Assert.Equal(SellerColorPalette.Surface, seller.RolePageTint);
        Assert.Equal(SellerColorPalette.BadgeSurface, seller.RolePageMiddle);
        Assert.Equal(SellerColorPalette.Secondary, seller.RoleHeaderSecondary);
        Assert.Equal(SellerColorPalette.Accent, seller.RoleDot);
        Assert.Equal(SellerColorPalette.Surface, seller.ProgressOne.BackgroundColor);
        Assert.Equal(SellerColorPalette.Role, seller.ProgressOne.StrokeColor);
        Assert.Equal(SellerColorPalette.Role, seller.ProgressConnectorOneColor);

        Assert.Equal("#145FC7", buyer.RoleColor);
        Assert.Equal("#EAF4FF", buyer.RoleBackground);
        Assert.Equal("#3C8AF1", buyer.RoleHeaderStart);
        Assert.Equal("#236DCE", buyer.RoleHeaderMiddle);
        Assert.Equal("#185CB9", buyer.RoleHeaderEnd);
    }

    private static AppTransaction Create(
        AppTransactionRole role,
        string state) =>
        new(
            Guid.Parse("00000000-0000-0000-0000-0000000000A1"),
            "กล้องทดสอบ",
            3_000_000,
            "THB",
            role,
            AppFulfillmentType.Physical,
            state,
            DateTimeOffset.Parse("2026-07-28T12:00:00+07:00"),
            DateTimeOffset.Parse("2026-07-29T18:00:00+07:00"),
            "คู่สัญญาทดสอบ");
}
```

Update the seller-completed assertions in
`BrandAssetConsistencyTests.AssertProgressAsset`:

```csharp
Assert.Equal(
    SellerColorPalette.Role,
    Attr(primary, "stroke"),
    ignoreCase: true);
Assert.Equal(
    SellerColorPalette.HeaderStart,
    Attr(secondary, "stroke"),
    ignoreCase: true);
Assert.Equal(
    SellerColorPalette.Accent,
    Attr(node, "fill"),
    ignoreCase: true);
```

- [ ] **Step 2: Run the focused tests to verify RED**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~SellerGraphiteNavyColorTests|FullyQualifiedName~BrandAssetConsistencyTests" \
  --no-restore --nologo
```

Expected: compilation fails because `SellerColorPalette` does not exist. After
adding only a temporary type declaration is prohibited; proceed directly to
the minimal production implementation in Step 3.

- [ ] **Step 3: Add the palette and connect the presentation model**

Create `SellerColorPalette.cs`:

```csharp
namespace Toklong.Mobile.Core;

public static class SellerColorPalette
{
    public const string Role = "#3B5266";
    public const string HeaderStart = "#4B6073";
    public const string HeaderMiddle = "#3D5163";
    public const string HeaderEnd = "#304354";
    public const string Surface = "#EDF2F5";
    public const string Border = "#C8D4DC";
    public const string Secondary = "#DCE7EC";
    public const string BadgeSurface = "#F3F7F9";
    public const string Accent = "#8DE8D2";

    public const string NewOfferBorder = "#DDB866";
    public const string NewOfferText = "#8A5100";
    public const string FulfillmentBorder = "#9BAEBC";
    public const string InProgressBorder = "#9CC4EC";
}
```

Replace only seller branches in `AppTransaction`:

```csharp
public string RoleColor =>
    Role == AppTransactionRole.Buyer
        ? "#145FC7"
        : SellerColorPalette.Role;

public string RoleBackground =>
    Role == AppTransactionRole.Buyer
        ? "#EAF4FF"
        : SellerColorPalette.Surface;

public string RoleHeaderStart =>
    Role == AppTransactionRole.Buyer
        ? "#3C8AF1"
        : SellerColorPalette.HeaderStart;

public string RoleHeaderMiddle =>
    Role == AppTransactionRole.Buyer
        ? "#236DCE"
        : SellerColorPalette.HeaderMiddle;

public string RoleHeaderEnd =>
    Role == AppTransactionRole.Buyer
        ? "#185CB9"
        : SellerColorPalette.HeaderEnd;

public string RolePageTint =>
    Role == AppTransactionRole.Buyer
        ? "#DCEFFF"
        : SellerColorPalette.Surface;

public string RolePageMiddle =>
    Role == AppTransactionRole.Buyer
        ? "#F6FAFF"
        : SellerColorPalette.BadgeSurface;

public string RoleHeaderSecondary =>
    Role == AppTransactionRole.Buyer
        ? "#D8E7FF"
        : SellerColorPalette.Secondary;

public string RoleDot =>
    Role == AppTransactionRole.Buyer
        ? "#9CEBD9"
        : SellerColorPalette.Accent;
```

Replace the seller progress constants:

```csharp
private const string SellerProgress = SellerColorPalette.Role;
private const string SellerProgressBackground =
    SellerColorPalette.Surface;
```

In each of the six seller-completed SVG files, use this exact palette:

```xml
<path id="rail-primary" ... stroke="#3B5266" ... />
<path id="rail-secondary" ... stroke="#4B6073" ... />
<circle id="rail-node" ... fill="#8DE8D2" ... />
```

Do not alter buyer-completed or disabled SVG variants.

- [ ] **Step 4: Run focused and adjacent tests to verify GREEN**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~SellerGraphiteNavyColorTests|FullyQualifiedName~BrandAssetConsistencyTests|FullyQualifiedName~SpotlightGradientPresentationTests" \
  --no-restore --nologo
```

Expected: all selected tests pass. Confirm `SpotlightGradientPresentationTests`
still proves that removing a spotlight retains valid six-digit colors.

- [ ] **Step 5: Inspect and commit Task 1**

Run:

```bash
git diff --check
git status --short
git diff -- src/Toklong.Mobile/Core/SellerColorPalette.cs \
  src/Toklong.Mobile/Core/AppTransaction.cs \
  src/Toklong.Mobile/Resources/Images/progress_agreement_seller_completed.svg \
  src/Toklong.Mobile/Resources/Images/progress_digital_handoff_seller_completed.svg \
  src/Toklong.Mobile/Resources/Images/progress_payment_seller_completed.svg \
  src/Toklong.Mobile/Resources/Images/progress_payout_seller_completed.svg \
  src/Toklong.Mobile/Resources/Images/progress_physical_handoff_seller_completed.svg \
  src/Toklong.Mobile/Resources/Images/progress_physical_receipt_seller_completed.svg \
  tests/Toklong.Mobile.Core.Tests/SellerGraphiteNavyColorTests.cs \
  tests/Toklong.Mobile.Core.Tests/BrandAssetConsistencyTests.cs
```

Stage only those files and commit:

```bash
git add src/Toklong.Mobile/Core/SellerColorPalette.cs \
  src/Toklong.Mobile/Core/AppTransaction.cs \
  src/Toklong.Mobile/Resources/Images/progress_agreement_seller_completed.svg \
  src/Toklong.Mobile/Resources/Images/progress_digital_handoff_seller_completed.svg \
  src/Toklong.Mobile/Resources/Images/progress_payment_seller_completed.svg \
  src/Toklong.Mobile/Resources/Images/progress_payout_seller_completed.svg \
  src/Toklong.Mobile/Resources/Images/progress_physical_handoff_seller_completed.svg \
  src/Toklong.Mobile/Resources/Images/progress_physical_receipt_seller_completed.svg \
  tests/Toklong.Mobile.Core.Tests/SellerGraphiteNavyColorTests.cs \
  tests/Toklong.Mobile.Core.Tests/BrandAssetConsistencyTests.cs
git commit -m "feat: define seller graphite navy palette"
```

---

### Task 2: Apply Graphite Navy to seller-role surfaces

**Files:**
- Modify: `src/Toklong.Mobile/Pages/AuthenticatedHomePage.xaml:1-120`
- Modify: `src/Toklong.Mobile/Pages/TransactionsPage.xaml:73-175`
- Modify: `src/Toklong.Mobile/Pages/TransactionsPage.xaml:760-865`
- Modify: `src/Toklong.Mobile/Pages/TransactionDetailPage.xaml:1-180`
- Modify: `src/Toklong.Mobile/Pages/TransactionDetailPage.xaml:555-605`
- Modify: `src/Toklong.Mobile/Pages/ShippingLabelPage.xaml:1-105`
- Modify: `src/Toklong.Mobile/Resources/Images/ui_shipping_label.svg`
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/BrandAssetConsistencyTests.cs`

**Interfaces:**
- Consumes: all constants from `SellerColorPalette` created in Task 1 and existing `AppTransaction.Role*` bindings.
- Produces: home, seller mode, seller compact card, seller detail, and shipping-label XAML that use Graphite Navy without changing bindings or commands.

- [ ] **Step 1: Write failing XAML and asset contract tests**

Add this test to `UiLayoutConsistencyTests`:

```csharp
[Fact]
public void Seller_surfaces_use_graphite_palette_without_changing_buyer()
{
    var home = Load("Ui", "Pages", "AuthenticatedHomePage.xaml");
    var transactions = Load("Ui", "Pages", "TransactionsPage.xaml");
    var detail = Load("Ui", "Pages", "TransactionDetailPage.xaml");
    var label = Load("Ui", "Pages", "ShippingLabelPage.xaml");

    var sellerHome = home.Descendants(Maui + "Border")
        .Single(element =>
            AttributeValue(element, "AutomationId") ==
            "SellerHomeCard");
    Assert.Equal(
        new[]
        {
            "{x:Static core:SellerColorPalette.HeaderStart}",
            "{x:Static core:SellerColorPalette.HeaderMiddle}",
            "{x:Static core:SellerColorPalette.HeaderEnd}"
        },
        sellerHome.Descendants(Maui + "GradientStop")
            .Select(stop => AttributeValue(stop, "Color")));

    var buyerHome = home.Descendants(Maui + "Border")
        .Single(element =>
            AttributeValue(element, "AutomationId") ==
            "BuyerHomeCard");
    Assert.Equal(
        "{StaticResource BrandBlue}",
        AttributeValue(buyerHome, "BackgroundColor"));

    var sellerModeSetter = transactions
        .Descendants(Maui + "Button")
        .Single(button => AttributeValue(button, "Text") == "ขาย")
        .Descendants(Maui + "Setter")
        .Single(setter => AttributeValue(setter, "Property") == "TextColor");
    Assert.Equal(
        "{x:Static core:SellerColorPalette.Role}",
        AttributeValue(sellerModeSetter, "Value"));

    var compactSeller = transactions.Descendants(Maui + "Border")
        .Single(element =>
            AttributeValue(element, "AutomationId") ==
            "CompactSellerTransactionCard");
    Assert.Contains(
        compactSeller.Descendants(Maui + "Label"),
        element =>
            AttributeValue(element, "Text") ==
                "{Binding PrimaryActionLabel}" &&
            AttributeValue(element, "TextColor") ==
                "{x:Static core:SellerColorPalette.Role}");

    var saveButton = label.Descendants(Maui + "Button")
        .Single(element =>
            AttributeValue(element, "AutomationId") ==
            "SaveShippingLabelButton");
    var shareButton = label.Descendants(Maui + "Button")
        .Single(element =>
            AttributeValue(element, "AutomationId") ==
            "ShareOrPrintShippingLabelButton");
    Assert.Equal(
        "{x:Static core:SellerColorPalette.Border}",
        AttributeValue(saveButton, "BorderColor"));
    Assert.Equal(
        "{x:Static core:SellerColorPalette.Role}",
        AttributeValue(saveButton, "TextColor"));
    Assert.Equal(
        "{x:Static core:SellerColorPalette.Role}",
        AttributeValue(shareButton, "BackgroundColor"));

    Assert.Contains(
        detail.Descendants(Maui + "GradientStop"),
        stop =>
            AttributeValue(stop, "Color") ==
            "{Binding Transaction.RoleHeaderStart, FallbackValue=#3C8AF1, TargetNullValue=#3C8AF1}");
}
```

Add a seller-only asset assertion to `BrandAssetConsistencyTests`:

```csharp
[Fact]
public void Shipping_label_icon_uses_graphite_role_color()
{
    var content = File.ReadAllText(
        Path.Combine(BrandDirectory(), "ui_shipping_label.svg"));

    Assert.Contains(
        SellerColorPalette.Role,
        content,
        StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain(
        "#6548C7",
        content,
        StringComparison.OrdinalIgnoreCase);
}
```

Add `ui_shipping_label.svg` to the test project asset item group:

```xml
<None Include="../../src/Toklong.Mobile/Resources/Images/ui_shipping_label.svg"
      Link="Brand/ui_shipping_label.svg"
      CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 2: Run focused tests to verify RED**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~Seller_surfaces_use_graphite_palette_without_changing_buyer|FullyQualifiedName~Shipping_label_icon_uses_graphite_role_color" \
  --no-restore --nologo
```

Expected: failures because the seller home card lacks the automation ID and
gradient, seller XAML still references purple, and the shipping-label SVG still
uses `#6548C7`.

- [ ] **Step 3: Apply the palette to the authenticated home**

Add `xmlns:core="clr-namespace:Toklong.Mobile.Core"` to
`AuthenticatedHomePage.xaml`. Give the existing buyer border
`AutomationId="BuyerHomeCard"`.

Replace the seller border with:

```xml
<Border
    AutomationId="SellerHomeCard"
    Stroke="Transparent"
    StrokeShape="RoundRectangle 20">
    <Border.Background>
        <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
            <GradientStop
                Color="{x:Static core:SellerColorPalette.HeaderStart}"
                Offset="0" />
            <GradientStop
                Color="{x:Static core:SellerColorPalette.HeaderMiddle}"
                Offset="0.64" />
            <GradientStop
                Color="{x:Static core:SellerColorPalette.HeaderEnd}"
                Offset="1" />
        </LinearGradientBrush>
    </Border.Background>
```

Keep the existing child grid and button unchanged. Change the seller badge and
actionable dot only:

```xml
<Border
    AutomationId="SellerNewOfferBadge"
    BackgroundColor="{x:Static core:SellerColorPalette.BadgeSurface}"
    ...>
    <Label
        ...
        TextColor="{x:Static core:SellerColorPalette.Role}" />
</Border>

<BoxView
    BackgroundColor="{x:Static core:SellerColorPalette.Accent}"
    ... />
```

- [ ] **Step 4: Replace explicit seller purple on seller-owned screens**

Use these exact XAML mappings:

| Existing seller use | Replacement |
| --- | --- |
| `#6548C7` seller text/action | `{x:Static core:SellerColorPalette.Role}` |
| `#F1ECFF` seller surface | `{x:Static core:SellerColorPalette.Surface}` |
| `#D7CBFF`, `#D8D1F5`, `#8E7BDA` seller border | `{x:Static core:SellerColorPalette.Border}` |
| `#2A6548C7` compact seller shadow | `Brush="{x:Static core:SellerColorPalette.Role}"` with existing opacity |

Ensure `TransactionDetailPage.xaml` and `ShippingLabelPage.xaml` declare:

```xml
xmlns:core="clr-namespace:Toklong.Mobile.Core"
```

Keep dynamic role bindings on the main detail header:

```xml
Color="{Binding Transaction.RoleHeaderStart, FallbackValue=#3C8AF1, TargetNullValue=#3C8AF1}"
Color="{Binding Transaction.RoleHeaderMiddle, FallbackValue=#236DCE, TargetNullValue=#236DCE}"
Color="{Binding Transaction.RoleHeaderEnd, FallbackValue=#185CB9, TargetNullValue=#185CB9}"
```

Those bindings now resolve Graphite Navy for sellers through Task 1 and retain
buyer blue. Change only the managed shipping-label card's pale background,
border, arrow, and inline action to the palette. Do not recolor generic
agreement, account, shield, location, or note assets that are also shown
outside seller-only contexts.

In `ui_shipping_label.svg`, replace both `#6548C7` strokes with `#3B5266`.

- [ ] **Step 5: Run focused and adjacent tests to verify GREEN**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~Seller_surfaces_use_graphite_palette_without_changing_buyer|FullyQualifiedName~Shipping_label_icon_uses_graphite_role_color|FullyQualifiedName~AuthenticatedHome_UsesCenteredBrandAndBuyerFirstActions|FullyQualifiedName~SellerWorkspace_ShowsSummaryProblemAndPriorityContracts|FullyQualifiedName~TransactionDeadlines_UseFullWidthWrappingRows" \
  --no-restore --nologo
```

Expected: all selected tests pass. The item-price-only and deadline contracts
remain green.

- [ ] **Step 6: Inspect and commit Task 2**

Run:

```bash
git diff --check
git status --short
git diff -- src/Toklong.Mobile/Pages/AuthenticatedHomePage.xaml \
  src/Toklong.Mobile/Pages/TransactionsPage.xaml \
  src/Toklong.Mobile/Pages/TransactionDetailPage.xaml \
  src/Toklong.Mobile/Pages/ShippingLabelPage.xaml \
  src/Toklong.Mobile/Resources/Images/ui_shipping_label.svg \
  tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs \
  tests/Toklong.Mobile.Core.Tests/BrandAssetConsistencyTests.cs \
  tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj
```

Stage only those files and commit:

```bash
git add src/Toklong.Mobile/Pages/AuthenticatedHomePage.xaml \
  src/Toklong.Mobile/Pages/TransactionsPage.xaml \
  src/Toklong.Mobile/Pages/TransactionDetailPage.xaml \
  src/Toklong.Mobile/Pages/ShippingLabelPage.xaml \
  src/Toklong.Mobile/Resources/Images/ui_shipping_label.svg \
  tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs \
  tests/Toklong.Mobile.Core.Tests/BrandAssetConsistencyTests.cs \
  tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj
git commit -m "feat: apply graphite seller surfaces"
```

---

### Task 3: Convert seller summary tiles to white outlined cards

**Files:**
- Modify: `src/Toklong.Mobile/Pages/TransactionsPage.xaml:178-357`
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`

**Interfaces:**
- Consumes: `SellerColorPalette.NewOfferBorder`, `NewOfferText`, `FulfillmentBorder`, `Role`, and `InProgressBorder`; existing selection bindings and commands on `TransactionsViewModel`.
- Produces: three always-white tiles with semantic borders/text and selected-state border, dot, shadow, and unchanged semantic descriptions.

- [ ] **Step 1: Write the failing white-tile and selection contract**

Add this test to `UiLayoutConsistencyTests`:

```csharp
[Fact]
public void Seller_summary_tiles_stay_white_and_show_selection_without_fill()
{
    var page = Load("Ui", "Pages", "TransactionsPage.xaml");
    var expected = new[]
    {
        (
            Tile: "SellerNewOffersTile",
            Border: "{x:Static core:SellerColorPalette.NewOfferBorder}",
            Text: "{x:Static core:SellerColorPalette.NewOfferText}",
            Marker: "SellerNewOffersSelectedMarker",
            Binding: "{Binding IsSellerNewOffersSelected}"),
        (
            Tile: "SellerFulfillmentTile",
            Border: "{x:Static core:SellerColorPalette.FulfillmentBorder}",
            Text: "{x:Static core:SellerColorPalette.Role}",
            Marker: "SellerFulfillmentSelectedMarker",
            Binding: "{Binding IsSellerFulfillmentSelected}"),
        (
            Tile: "SellerInProgressTile",
            Border: "{x:Static core:SellerColorPalette.InProgressBorder}",
            Text: "#145FC7",
            Marker: "SellerInProgressSelectedMarker",
            Binding: "{Binding IsSellerInProgressSelected}")
    };

    foreach (var item in expected)
    {
        var tile = page.Descendants(Maui + "Border")
            .Single(element =>
                AttributeValue(element, "AutomationId") == item.Tile);
        Assert.Equal("White", AttributeValue(tile, "BackgroundColor"));
        Assert.Equal(item.Border, AttributeValue(tile, "Stroke"));
        Assert.Equal("1.5", AttributeValue(tile, "StrokeThickness"));

        var selection = tile.Descendants(Maui + "DataTrigger")
            .Single(trigger =>
                AttributeValue(trigger, "Binding") == item.Binding &&
                AttributeValue(trigger, "Value") == "True");
        Assert.DoesNotContain(
            selection.Descendants(Maui + "Setter"),
            setter =>
                AttributeValue(setter, "Property") ==
                "BackgroundColor");
        Assert.Contains(
            selection.Descendants(Maui + "Setter"),
            setter =>
                AttributeValue(setter, "Property") ==
                    "StrokeThickness" &&
                AttributeValue(setter, "Value") == "2.5");
        Assert.Contains(
            selection.Descendants(Maui + "Setter"),
            setter =>
                AttributeValue(setter, "Property") == "Shadow");

        Assert.All(
            tile.Descendants(Maui + "Label")
                .Where(label =>
                    AttributeValue(label, "Text") is not "●"),
            label => Assert.Equal(
                item.Text,
                AttributeValue(label, "TextColor")));

        var marker = page.Descendants(Maui + "Label")
            .Single(label =>
                AttributeValue(label, "AutomationId") ==
                item.Marker);
        Assert.Equal("●", AttributeValue(marker, "Text"));
        Assert.Equal("False", AttributeValue(marker, "IsVisible"));
        Assert.Contains(
            marker.Descendants(Maui + "DataTrigger"),
            trigger =>
                AttributeValue(trigger, "Binding") ==
                    item.Binding &&
                AttributeValue(trigger, "Value") ==
                    "True");
    }
}
```

Keep the existing assertions that all overlay buttons use the bound semantic
descriptions and the compact minimum tap height.

- [ ] **Step 2: Run the test to verify RED**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~Seller_summary_tiles_stay_white_and_show_selection_without_fill" \
  --no-restore --nologo
```

Expected: failure because current tiles use tinted backgrounds, selected tiles
use a solid purple fill, and no selected marker exists.

- [ ] **Step 3: Implement exact normal and selected tile states**

For the three existing tile borders, use these exact normal properties:

| Automation ID | Background | Stroke | Count/label text |
| --- | --- | --- | --- |
| `SellerNewOffersTile` | `White` | `{x:Static core:SellerColorPalette.NewOfferBorder}` | `{x:Static core:SellerColorPalette.NewOfferText}` |
| `SellerFulfillmentTile` | `White` | `{x:Static core:SellerColorPalette.FulfillmentBorder}` | `{x:Static core:SellerColorPalette.Role}` |
| `SellerInProgressTile` | `White` | `{x:Static core:SellerColorPalette.InProgressBorder}` | `#145FC7` |

Set `StrokeThickness="1.5"` on every tile. Remove every selected-state
`BackgroundColor` setter and every selected-state white `TextColor` setter.

Each tile's selected trigger must set:

```xml
<Setter Property="StrokeThickness" Value="2.5" />
<Setter Property="Shadow">
    <Setter.Value>
        <Shadow
            Brush="{x:Static core:SellerColorPalette.Role}"
            Offset="0,4"
            Opacity="0.13"
            Radius="12" />
    </Setter.Value>
</Setter>
```

Use the tile's text color as the shadow brush for รอตอบ and กำลังไปต่อ:

```text
รอตอบ: {x:Static core:SellerColorPalette.NewOfferText}
ต้องส่ง: {x:Static core:SellerColorPalette.Role}
กำลังไปต่อ: #145FC7
```

Add one marker label as a sibling immediately after each tile border and before
its transparent overlay button. Configure all three markers with:

```xml
<Label
    AutomationProperties.IsInAccessibleTree="False"
    HorizontalOptions="End"
    InputTransparent="True"
    IsVisible="False"
    Margin="0,7,8,0"
    Text="●"
    VerticalOptions="Start">
    <Label.Triggers>
        <DataTrigger
            TargetType="Label"
            Binding="{Binding IsSellerNewOffersSelected}"
            Value="True">
            <Setter Property="IsVisible" Value="True" />
        </DataTrigger>
    </Label.Triggers>
</Label>
```

For the three instances, set exact values:

| Marker automation ID | Binding | Text color |
| --- | --- | --- |
| `SellerNewOffersSelectedMarker` | `{Binding IsSellerNewOffersSelected}` | `{x:Static core:SellerColorPalette.NewOfferText}` |
| `SellerFulfillmentSelectedMarker` | `{Binding IsSellerFulfillmentSelected}` | `{x:Static core:SellerColorPalette.Role}` |
| `SellerInProgressSelectedMarker` | `{Binding IsSellerInProgressSelected}` | `#145FC7` |

Do not change the overlay buttons, commands, semantic descriptions, count
bindings, tile labels, or three-column layout.

- [ ] **Step 4: Run focused and workspace tests to verify GREEN**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~Seller_summary_tiles_stay_white_and_show_selection_without_fill|FullyQualifiedName~SellerWorkspace_ShowsSummaryProblemAndPriorityContracts|FullyQualifiedName~TransactionDeadlines_UseFullWidthWrappingRows" \
  --no-restore --nologo
```

Expected: all selected tests pass. Confirm the existing problem-banner,
item-price-only, semantic-description, and deadline assertions remain green.

- [ ] **Step 5: Build iOS to validate XAML object setters**

Run:

```bash
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj \
  -p:TargetFrameworks=net10.0-ios \
  -f net10.0-ios \
  -r iossimulator-arm64 \
  --no-restore \
  -p:NuGetAudit=false
```

Expected: `Build succeeded`, zero errors. Existing `NU1900` and obsolete
single-photo-picker warnings may remain; no new XAML source-generation error is
allowed.

- [ ] **Step 6: Inspect and commit Task 3**

Run:

```bash
git diff --check
git status --short
git diff -- src/Toklong.Mobile/Pages/TransactionsPage.xaml \
  tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs
```

Stage only those files and commit:

```bash
git add src/Toklong.Mobile/Pages/TransactionsPage.xaml \
  tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs
git commit -m "feat: simplify seller summary colors"
```

---

### Task 4: Full regression and native visual verification

**Files:**
- Verify only; do not create production files.
- Record evidence under the execution workspace selected by
  `superpowers:subagent-driven-development` or
  `superpowers:executing-plans`.

**Interfaces:**
- Consumes: Tasks 1-3.
- Produces: fresh automated, build, and simulator evidence for completion.

- [ ] **Step 1: Run all automated suites**

Run sequentially to avoid shared `obj` file collisions:

```bash
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj --no-restore --nologo
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj --no-restore --nologo
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj --no-restore --nologo
dotnet test tests/Toklong.Crm.Tests/Toklong.Crm.Tests.csproj --no-restore --nologo
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore --nologo
```

Expected baseline after this plan's five new tests: Domain 85, Application 158,
API 42, CRM 45, and Mobile Core 266. Every suite must report zero failed
tests.

- [ ] **Step 2: Run the exact iOS Simulator build**

Run:

```bash
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj \
  -p:TargetFrameworks=net10.0-ios \
  -f net10.0-ios \
  -r iossimulator-arm64 \
  --no-restore \
  -p:NuGetAudit=false
```

Expected: `Build succeeded`, zero errors.

- [ ] **Step 3: Start the interactive test stack and install the app**

Ensure PostgreSQL is running:

```bash
docker compose up -d postgres
```

Start the Stripe Test API in a retained terminal:

```bash
./scripts/run-stripe-test-api.sh
```

Expected: `Toklong.Api + Stripe Test Mode: http://127.0.0.1:5181`, and:

```bash
curl -fsS http://127.0.0.1:5181/health/ready
```

returns `{"status":"ready"}`.

Use the two existing iOS 26.5 devices:

```bash
xcrun simctl boot 09CF9ABB-0DD3-4DD9-ADB5-9BEC2A3A0BB9
xcrun simctl boot DEFEA7C0-152A-4C11-B481-AFC4DF2A685E
xcrun simctl install 09CF9ABB-0DD3-4DD9-ADB5-9BEC2A3A0BB9 \
  src/Toklong.Mobile/bin/Debug/net10.0-ios/iossimulator-arm64/Toklong.Mobile.app
xcrun simctl install DEFEA7C0-152A-4C11-B481-AFC4DF2A685E \
  src/Toklong.Mobile/bin/Debug/net10.0-ios/iossimulator-arm64/Toklong.Mobile.app
xcrun simctl launch 09CF9ABB-0DD3-4DD9-ADB5-9BEC2A3A0BB9 \
  th.co.toklong.mobile
xcrun simctl launch DEFEA7C0-152A-4C11-B481-AFC4DF2A685E \
  th.co.toklong.mobile
```

If either device is already booted, treat simctl's already-booted response as
non-fatal and continue with install/launch.

- [ ] **Step 4: Verify normal-size seller visuals**

Set both devices to the standard size and cold-launch:

```bash
xcrun simctl ui 09CF9ABB-0DD3-4DD9-ADB5-9BEC2A3A0BB9 content_size large
xcrun simctl ui DEFEA7C0-152A-4C11-B481-AFC4DF2A685E content_size large
xcrun simctl terminate 09CF9ABB-0DD3-4DD9-ADB5-9BEC2A3A0BB9 th.co.toklong.mobile
xcrun simctl terminate DEFEA7C0-152A-4C11-B481-AFC4DF2A685E th.co.toklong.mobile
xcrun simctl launch 09CF9ABB-0DD3-4DD9-ADB5-9BEC2A3A0BB9 th.co.toklong.mobile
xcrun simctl launch DEFEA7C0-152A-4C11-B481-AFC4DF2A685E th.co.toklong.mobile
```

Capture authenticated home and seller workspace:

```bash
xcrun simctl io 09CF9ABB-0DD3-4DD9-ADB5-9BEC2A3A0BB9 screenshot \
  /tmp/toklong-graphite-home-default.png
xcrun simctl io DEFEA7C0-152A-4C11-B481-AFC4DF2A685E screenshot \
  /tmp/toklong-graphite-workspace-default.png
```

Visually confirm:

- buyer card is unchanged blue;
- seller card and spotlight use Graphite Navy, not saturated purple;
- badge and secondary text remain legible;
- all three summary tiles are white;
- each tile has the approved border/text color;
- selecting each tile retains white, shows a thicker border and dot, and
  updates the filtered list;
- the red problem banner remains red;
- seller amounts remain item price only; and
- SHIPPOP-managed work has no manual Add Tracking action.

- [ ] **Step 5: Verify Accessibility Large and narrow-width wrapping**

Use iPhone 17 (the narrower of the two active devices):

```bash
xcrun simctl ui DEFEA7C0-152A-4C11-B481-AFC4DF2A685E \
  content_size accessibility-large
xcrun simctl terminate DEFEA7C0-152A-4C11-B481-AFC4DF2A685E \
  th.co.toklong.mobile
xcrun simctl launch DEFEA7C0-152A-4C11-B481-AFC4DF2A685E \
  th.co.toklong.mobile
xcrun simctl io DEFEA7C0-152A-4C11-B481-AFC4DF2A685E screenshot \
  /tmp/toklong-graphite-workspace-accessibility-large.png
```

Confirm the three Thai summary labels remain readable, counts do not clip,
selected markers remain visible, exact deadlines wrap without ellipsis, and
seller home/workspace copy does not overlap.

Restore:

```bash
xcrun simctl ui DEFEA7C0-152A-4C11-B481-AFC4DF2A685E content_size large
```

Do not claim spoken VoiceOver or physical-device verification unless it was
actually performed.

- [ ] **Step 6: Final scope and cleanliness audit**

Run:

```bash
git diff --check
git status --short
git log -5 --oneline
rg -n "#6255D9|#6548C7|#8067DE|#6348C9|#4930A7" \
  src/Toklong.Mobile/Pages/AuthenticatedHomePage.xaml \
  src/Toklong.Mobile/Pages/TransactionsPage.xaml \
  src/Toklong.Mobile/Pages/ShippingLabelPage.xaml \
  src/Toklong.Mobile/Resources/Images/progress_*_seller_completed.svg \
  src/Toklong.Mobile/Resources/Images/ui_shipping_label.svg
```

Expected: the owned seller surfaces/assets contain none of the former seller
purple values. Purple may remain in explicitly out-of-scope shared or generic
assets/pages. Confirm the five user-owned dirty files remain unstaged and
uncommitted.
