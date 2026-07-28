# Connected Status Tokens Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the mixed transaction-progress artwork with accessible Connected Tokens that use TOKLONG-owned rounded glyphs for buyer, seller, physical and digital flows.

**Architecture:** Keep transaction-state completion logic in `AppTransaction`, but expose each milestone through a small immutable `TransactionProgressStep` presentation record plus two connector colors. Render those values in one reusable `TransactionProgressView`; all artwork is local SVG, and the transaction-detail page only hosts the component.

**Tech Stack:** .NET 10, C# records, .NET MAUI XAML, local SVG `MauiImage` assets, xUnit, `System.Xml.Linq`.

## Global Constraints

- Do not change transaction states, transition rules, payment truth, fulfillment eligibility, payout eligibility or deadlines.
- Completed token fill and label are `#087C68`; completed glyphs are white.
- The agreement glyph may use Mint `#65D6BF` only as a decorative brand detail.
- Incomplete token fill is white; incomplete outline/connectors are `#E4EAF1`; incomplete glyph/label is `#98A2B3`.
- An active but incomplete milestone remains gray.
- A connector becomes green only when the milestone at its destination is completed.
- Do not add floating number/check badges, tap behavior or animation.
- Physical and digital fulfillment use different glyphs.
- Screen-reader descriptions must include the Thai label plus `เสร็จแล้ว` or `ยังไม่เสร็จ`.
- Money remains integer satang plus ISO currency; this slice does not add or calculate money.

---

## File map

- Create `src/Toklong.Mobile/Core/TransactionProgressStep.cs`: immutable milestone presentation contract.
- Modify `src/Toklong.Mobile/Core/AppTransaction.cs`: maps existing role, fulfillment type and completion state to three milestones and two connector colors.
- Modify `tests/Toklong.Mobile.Core.Tests/TransactionPresentationTests.cs`: verifies role/fulfillment asset mapping, connector completion and Thai accessibility semantics.
- Create twelve `src/Toklong.Mobile/Resources/Images/progress_*.svg` files: six semantic glyphs, each with completed and disabled variants.
- Modify `tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj`: copies the new status assets and progress control XAML into the test output.
- Modify `tests/Toklong.Mobile.Core.Tests/BrandAssetConsistencyTests.cs`: parses every progress SVG and enforces the approved geometry/palette.
- Create `src/Toklong.Mobile/Controls/TransactionProgressView.xaml`: owns the three circular tokens, two connectors and labels.
- Create `src/Toklong.Mobile/Controls/TransactionProgressView.xaml.cs`: exposes the `AppTransaction` bindable property.
- Modify `src/Toklong.Mobile/Pages/TransactionDetailPage.xaml`: replaces the repeated status markup with the reusable control.
- Modify `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`: verifies the page/control boundary, accessibility bindings and absence of badges/taps.
- Modify `docs/02_UI_UX_AND_CONTENT_SPEC.md`: records the approved Connected Tokens visual rules.

### Task 1: Model the three milestone presentations

**Files:**

- Create: `src/Toklong.Mobile/Core/TransactionProgressStep.cs`
- Modify: `src/Toklong.Mobile/Core/AppTransaction.cs:430-574`
- Test: `tests/Toklong.Mobile.Core.Tests/TransactionPresentationTests.cs:190-290`

**Interfaces:**

- Consumes: `AppTransaction.Role`, `AppTransaction.FulfillmentType`, `AppTransaction.ProgressCompletedThrough`, and the existing role-specific progress labels.
- Produces: `TransactionProgressStep ProgressOne`, `ProgressTwo`, `ProgressThree`; `string ProgressConnectorOneColor`; `string ProgressConnectorTwoColor`.

- [ ] **Step 1: Write failing presentation tests**

Add these focused tests and replace the old badge-specific unit tests. Keep the
legacy presentation properties temporarily so the existing page still builds
until Task 3:

```csharp
[Fact]
public void ConnectedProgressMapsBuyerPhysicalGlyphsAndSemantics()
{
    var item = CreateItem(null) with
    {
        Role = AppTransactionRole.Buyer,
        FulfillmentType = AppFulfillmentType.Physical,
        State = "AwaitingSellerAcceptance"
    };

    Assert.Equal("progress_agreement_completed.png", item.ProgressOne.Icon);
    Assert.Equal("progress_payment_disabled.png", item.ProgressTwo.Icon);
    Assert.Equal("progress_parcel_received_disabled.png", item.ProgressThree.Icon);
    Assert.Equal("สร้างข้อตกลง เสร็จแล้ว", item.ProgressOne.SemanticDescription);
    Assert.Equal("จ่ายเงิน ยังไม่เสร็จ", item.ProgressTwo.SemanticDescription);
    Assert.Equal("#087C68", item.ProgressOne.BackgroundColor);
    Assert.Equal("#FFFFFF", item.ProgressTwo.BackgroundColor);
    Assert.Equal("#E4EAF1", item.ProgressTwo.StrokeColor);
}

[Theory]
[InlineData(
    AppTransactionRole.Buyer,
    AppFulfillmentType.Digital,
    "progress_payment_disabled.png",
    "progress_digital_handoff_disabled.png")]
[InlineData(
    AppTransactionRole.Seller,
    AppFulfillmentType.Physical,
    "progress_parcel_handoff_disabled.png",
    "progress_payout_disabled.png")]
[InlineData(
    AppTransactionRole.Seller,
    AppFulfillmentType.Digital,
    "progress_digital_handoff_disabled.png",
    "progress_payout_disabled.png")]
public void ConnectedProgressUsesRoleAndFulfillmentGlyphs(
    AppTransactionRole role,
    AppFulfillmentType fulfillmentType,
    string expectedSecond,
    string expectedThird)
{
    var item = CreateItem(null) with
    {
        Role = role,
        FulfillmentType = fulfillmentType,
        State = "AwaitingSellerAcceptance"
    };

    Assert.Equal(expectedSecond, item.ProgressTwo.Icon);
    Assert.Equal(expectedThird, item.ProgressThree.Icon);
}

[Fact]
public void ConnectedProgressColorsOnlyCompletedDestinationSegments()
{
    var firstComplete = CreateItem(null) with
    {
        Role = AppTransactionRole.Buyer,
        State = "AwaitingSellerAcceptance"
    };
    var secondComplete = firstComplete with { State = "PaidAwaitingShipment" };
    var thirdComplete = firstComplete with { State = "PayoutPending" };

    Assert.Equal("#E4EAF1", firstComplete.ProgressConnectorOneColor);
    Assert.Equal("#E4EAF1", firstComplete.ProgressConnectorTwoColor);
    Assert.Equal("#087C68", secondComplete.ProgressConnectorOneColor);
    Assert.Equal("#E4EAF1", secondComplete.ProgressConnectorTwoColor);
    Assert.Equal("#087C68", thirdComplete.ProgressConnectorOneColor);
    Assert.Equal("#087C68", thirdComplete.ProgressConnectorTwoColor);
}

[Fact]
public void ActiveButIncompleteConnectedTokenStaysGray()
{
    var item = CreateItem(null) with
    {
        Role = AppTransactionRole.Buyer,
        State = "DeliveredDisputeWindow"
    };

    Assert.Equal(3, item.ProgressActiveStep);
    Assert.Equal("progress_parcel_received_disabled.png", item.ProgressThree.Icon);
    Assert.Equal("#FFFFFF", item.ProgressThree.BackgroundColor);
    Assert.Equal("#E4EAF1", item.ProgressThree.StrokeColor);
    Assert.Equal("#98A2B3", item.ProgressThree.LabelColor);
    Assert.Equal("ได้รับของ ยังไม่เสร็จ", item.ProgressThree.SemanticDescription);
}
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~TransactionPresentationTests" --no-restore
```

Expected: compilation fails because `ProgressOne`, `ProgressTwo`,
`ProgressThree` and connector color properties do not exist.

- [ ] **Step 3: Add the immutable presentation contract**

Create `TransactionProgressStep.cs`:

```csharp
namespace Toklong.Mobile.Core;

public sealed record TransactionProgressStep(
    string Label,
    string Icon,
    string BackgroundColor,
    string StrokeColor,
    string LabelColor,
    string SemanticDescription);
```

- [ ] **Step 4: Implement the minimal milestone mapping**

In `AppTransaction`, retain the existing `ProgressCompletedThrough`,
`ProgressActiveStep` and role-specific label properties. Add this new
presentation surface:

```csharp
private const string ProgressComplete = "#087C68";
private const string ProgressIncomplete = "#E4EAF1";
private const string ProgressMuted = "#98A2B3";

public TransactionProgressStep ProgressOne =>
    CreateProgressStep(1, ProgressOneLabel, "agreement");

public TransactionProgressStep ProgressTwo =>
    CreateProgressStep(
        2,
        ProgressTwoLabel,
        Role == AppTransactionRole.Buyer
            ? "payment"
            : FulfillmentType == AppFulfillmentType.Physical
                ? "parcel_handoff"
                : "digital_handoff");

public TransactionProgressStep ProgressThree =>
    CreateProgressStep(
        3,
        ProgressThreeLabel,
        Role == AppTransactionRole.Seller
            ? "payout"
            : FulfillmentType == AppFulfillmentType.Physical
                ? "parcel_received"
                : "digital_handoff");

public string ProgressConnectorOneColor =>
    ProgressCompletedThrough >= 2
        ? ProgressComplete
        : ProgressIncomplete;

public string ProgressConnectorTwoColor =>
    ProgressCompletedThrough >= 3
        ? ProgressComplete
        : ProgressIncomplete;

private TransactionProgressStep CreateProgressStep(
    int step,
    string label,
    string glyph)
{
    var completed = step <= ProgressCompletedThrough;
    var suffix = completed ? "completed" : "disabled";

    return new TransactionProgressStep(
        label,
        $"progress_{glyph}_{suffix}.png",
        completed ? ProgressComplete : "#FFFFFF",
        completed ? ProgressComplete : ProgressIncomplete,
        completed ? ProgressComplete : ProgressMuted,
        $"{label} {(completed ? "เสร็จแล้ว" : "ยังไม่เสร็จ")}");
}
```

Do not remove the legacy marker/background/foreground/icon/color properties in
this task: `TransactionDetailPage.xaml` still consumes them. Task 3 removes
them in the same commit that replaces the old page markup. Do not change the
state-to-completion mappings.

- [ ] **Step 5: Run the focused and full mobile-core tests**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~TransactionPresentationTests" --no-restore
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --no-restore
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj \
  -f net10.0-ios \
  -p:RuntimeIdentifier=iossimulator-arm64 \
  --no-restore -m:1 -nr:false
```

Expected: all tests and the iOS simulator build pass; obsolete unit assertions
against badge markers have been removed rather than weakened, while the
temporary legacy properties keep the not-yet-migrated XAML valid.

- [ ] **Step 6: Commit the presentation model**

```bash
git add \
  src/Toklong.Mobile/Core/TransactionProgressStep.cs \
  src/Toklong.Mobile/Core/AppTransaction.cs \
  tests/Toklong.Mobile.Core.Tests/TransactionPresentationTests.cs
git commit -m "feat: model connected transaction progress"
```

### Task 2: Add the TOKLONG-owned progress glyph family

**Files:**

- Create:
  - `src/Toklong.Mobile/Resources/Images/progress_agreement_completed.svg`
  - `src/Toklong.Mobile/Resources/Images/progress_agreement_disabled.svg`
  - `src/Toklong.Mobile/Resources/Images/progress_payment_completed.svg`
  - `src/Toklong.Mobile/Resources/Images/progress_payment_disabled.svg`
  - `src/Toklong.Mobile/Resources/Images/progress_parcel_handoff_completed.svg`
  - `src/Toklong.Mobile/Resources/Images/progress_parcel_handoff_disabled.svg`
  - `src/Toklong.Mobile/Resources/Images/progress_parcel_received_completed.svg`
  - `src/Toklong.Mobile/Resources/Images/progress_parcel_received_disabled.svg`
  - `src/Toklong.Mobile/Resources/Images/progress_digital_handoff_completed.svg`
  - `src/Toklong.Mobile/Resources/Images/progress_digital_handoff_disabled.svg`
  - `src/Toklong.Mobile/Resources/Images/progress_payout_completed.svg`
  - `src/Toklong.Mobile/Resources/Images/progress_payout_disabled.svg`
- Modify: `tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj`
- Modify: `tests/Toklong.Mobile.Core.Tests/BrandAssetConsistencyTests.cs`

**Interfaces:**

- Consumes: the exact `.png` asset names emitted by Task 1; MAUI converts the
  source SVG files to runtime image resources.
- Produces: twelve parseable `48 × 48` local SVG assets with completed and
  disabled palettes.

- [ ] **Step 1: Copy the asset family into the test output**

Add this item to the test project:

```xml
<None Include="../../src/Toklong.Mobile/Resources/Images/progress_*.svg"
      Link="Brand/%(Filename)%(Extension)"
      CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 2: Write the failing asset-contract test**

Add to `BrandAssetConsistencyTests`:

```csharp
public static TheoryData<string> ProgressAssets => new()
{
    "progress_agreement_completed.svg",
    "progress_agreement_disabled.svg",
    "progress_payment_completed.svg",
    "progress_payment_disabled.svg",
    "progress_parcel_handoff_completed.svg",
    "progress_parcel_handoff_disabled.svg",
    "progress_parcel_received_completed.svg",
    "progress_parcel_received_disabled.svg",
    "progress_digital_handoff_completed.svg",
    "progress_digital_handoff_disabled.svg",
    "progress_payout_completed.svg",
    "progress_payout_disabled.svg"
};

[Theory]
[MemberData(nameof(ProgressAssets))]
public void ProgressAssetUsesApprovedRoundedGeometry(string fileName)
{
    var document = XDocument.Load(BrandPath(fileName));
    var svg = document.Root!;
    var content = Read(fileName);

    Assert.Equal("0 0 48 48", (string?)svg.Attribute("viewBox"));
    Assert.Contains("stroke-linecap=\"round\"", content);
    Assert.DoesNotContain("<text", content, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("#2B7FFF", content, StringComparison.OrdinalIgnoreCase);

    if (fileName.EndsWith("_completed.svg", StringComparison.Ordinal))
    {
        Assert.Contains("#FFFFFF", content, StringComparison.OrdinalIgnoreCase);
    }
    else
    {
        Assert.Contains("#98A2B3", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#65D6BF", content, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 3: Run the asset test and verify RED**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~ProgressAssetUsesApprovedRoundedGeometry" \
  --no-restore
```

Expected: the theory has no copied asset files, or file loading fails for the
first expected asset.

- [ ] **Step 4: Create the six completed glyphs**

Use these exact SVG bodies. They share the approved view box, rounded stroke and
optical weight:

`progress_agreement_completed.svg`

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 48 48">
  <g fill="none" stroke-linecap="round">
    <path d="M7 18c0-5 4-9 9-9h11c5 0 9 4 9 9s-4 9-9 9h-3" stroke="#FFFFFF" stroke-width="5"/>
    <path d="M41 30c0 5-4 9-9 9H21c-5 0-9-4-9-9s4-9 9-9h3" stroke="#A7E9DC" stroke-width="5"/>
    <circle cx="24" cy="24" r="4" fill="#65D6BF" stroke="#FFFFFF" stroke-width="2"/>
  </g>
</svg>
```

`progress_payment_completed.svg`

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 48 48">
  <g fill="none" stroke="#FFFFFF" stroke-width="5" stroke-linecap="round">
    <path d="M10 24h28"/><path d="M24 10v28"/>
    <circle cx="24" cy="24" r="7" stroke-width="3"/>
  </g>
</svg>
```

`progress_parcel_handoff_completed.svg`

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 48 48">
  <g fill="none" stroke="#FFFFFF" stroke-width="4" stroke-linecap="round" stroke-linejoin="round">
    <path d="M8 17l16-8 16 8-16 8z"/><path d="M8 17v15l16 8 16-8V17M24 25v15"/>
    <path d="M31 8h9v9"/>
  </g>
</svg>
```

`progress_parcel_received_completed.svg`

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 48 48">
  <g fill="none" stroke="#FFFFFF" stroke-width="4" stroke-linecap="round" stroke-linejoin="round">
    <path d="M8 17l16-8 16 8-16 8z"/><path d="M8 17v15l16 8 16-8V17M24 25v15"/>
    <path d="M31 31l4 4 7-9"/>
  </g>
</svg>
```

`progress_digital_handoff_completed.svg`

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 48 48">
  <g fill="none" stroke="#FFFFFF" stroke-width="5" stroke-linecap="round" stroke-linejoin="round">
    <path d="M8 16h19c5 0 9 4 9 9"/><path d="M40 32H21c-5 0-9-4-9-9"/>
    <path d="M31 20l5 5 5-5"/><path d="M17 28l-5-5-5 5"/>
  </g>
</svg>
```

`progress_payout_completed.svg`

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 48 48">
  <g fill="none" stroke="#FFFFFF" stroke-width="5" stroke-linecap="round" stroke-linejoin="round">
    <path d="M8 24h24"/><path d="M25 16l8 8-8 8"/>
    <path d="M39 13v22"/>
    <circle cx="39" cy="24" r="4" fill="#65D6BF" stroke="#FFFFFF" stroke-width="2"/>
  </g>
</svg>
```

- [ ] **Step 5: Create the six disabled variants**

For each completed SVG, create the matching `_disabled.svg` with the same
geometry and these exact mechanical color changes:

```text
#FFFFFF -> #98A2B3
#A7E9DC -> #98A2B3
#65D6BF -> #98A2B3
```

For agreement and payout, remove the `fill` from the small confirmation circle
and keep its stroke `#98A2B3`, so inactive assets contain no Mint. Do not change
any `d`, `cx`, `cy`, `r`, `stroke-width`, `stroke-linecap`, `stroke-linejoin` or
`viewBox` value.

- [ ] **Step 6: Run asset and full mobile-core tests**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~BrandAssetConsistencyTests" --no-restore
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --no-restore
```

Expected: all asset files parse, palette assertions pass and the full
mobile-core suite passes.

- [ ] **Step 7: Commit the asset family**

```bash
git add \
  src/Toklong.Mobile/Resources/Images/progress_*.svg \
  tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  tests/Toklong.Mobile.Core.Tests/BrandAssetConsistencyTests.cs
git commit -m "feat: add connected progress glyphs"
```

### Task 3: Render the reusable Connected Tokens component

**Files:**

- Create: `src/Toklong.Mobile/Controls/TransactionProgressView.xaml`
- Create: `src/Toklong.Mobile/Controls/TransactionProgressView.xaml.cs`
- Modify: `src/Toklong.Mobile/Core/AppTransaction.cs:491-574`
- Modify: `src/Toklong.Mobile/Pages/TransactionDetailPage.xaml:825-953`
- Modify: `tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj`
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`
- Modify: `docs/02_UI_UX_AND_CONTENT_SPEC.md:13-18`

**Interfaces:**

- Consumes: `AppTransaction ProgressOne/Two/Three` and
  `ProgressConnectorOneColor/ProgressConnectorTwoColor` from Task 1.
- Produces: `TransactionProgressView.Transaction`, a bindable
  `AppTransaction?` property used once by `TransactionDetailPage`.

- [ ] **Step 1: Copy the new control XAML into UI tests**

Add to the existing control items in the test project:

```xml
<None Include="../../src/Toklong.Mobile/Controls/TransactionProgressView.xaml"
      Link="Ui/Controls/TransactionProgressView.xaml"
      CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 2: Write the failing layout-contract test**

Add to `UiLayoutConsistencyTests`:

```csharp
[Fact]
public void TransactionDetailUsesAccessibleConnectedProgressTokens()
{
    var detail = Load("Ui", "Pages", "TransactionDetailPage.xaml");
    var progressHost = detail
        .Descendants()
        .Single(element =>
            element.Name.LocalName == "TransactionProgressView");

    Assert.Equal(
        "{Binding Transaction}",
        AttributeValue(progressHost, "Transaction"));

    var progress = Load("Ui", "Controls", "TransactionProgressView.xaml");
    var tokens = progress
        .Descendants(Maui + "Border")
        .Where(element =>
            AttributeValue(element, "AutomationId") is
                "ProgressTokenOne" or
                "ProgressTokenTwo" or
                "ProgressTokenThree")
        .ToArray();
    var connectors = progress
        .Descendants(Maui + "Border")
        .Where(element =>
            AttributeValue(element, "AutomationId") is
                "ProgressConnectorOne" or
                "ProgressConnectorTwo")
        .ToArray();

    Assert.Equal(3, tokens.Length);
    Assert.All(tokens, token =>
    {
        Assert.Equal("48", AttributeValue(token, "WidthRequest"));
        Assert.Equal("48", AttributeValue(token, "HeightRequest"));
        Assert.Equal("RoundRectangle 24", AttributeValue(token, "StrokeShape"));
        Assert.NotNull(
            AttributeValue(token, "SemanticProperties.Description"));
    });
    Assert.Equal(2, connectors.Length);
    Assert.Empty(progress.Descendants(Maui + "TapGestureRecognizer"));
    Assert.DoesNotContain(
        progress.Descendants(Maui + "Label"),
        label =>
            AttributeValue(label, "Text") is
                "{Binding ProgressOneMarker}" or
                "{Binding ProgressTwoMarker}" or
                "{Binding ProgressThreeMarker}");
}
```

- [ ] **Step 3: Run the layout test and verify RED**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~TransactionDetailUsesAccessibleConnectedProgressTokens" \
  --no-restore
```

Expected: the test fails because the control file and host element do not yet
exist.

- [ ] **Step 4: Create the bindable control contract**

Create `TransactionProgressView.xaml.cs`:

```csharp
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Controls;

public partial class TransactionProgressView : ContentView
{
    public static readonly BindableProperty TransactionProperty =
        BindableProperty.Create(
            nameof(Transaction),
            typeof(AppTransaction),
            typeof(TransactionProgressView));

    public TransactionProgressView()
    {
        InitializeComponent();
    }

    public AppTransaction? Transaction
    {
        get => (AppTransaction?)GetValue(TransactionProperty);
        set => SetValue(TransactionProperty, value);
    }
}
```

- [ ] **Step 5: Create the Connected Tokens XAML**

Create `TransactionProgressView.xaml` with a root `ContentView` named `Root`.
The outer grid binds to
`{Binding Transaction, Source={x:Reference Root}}`. Its first row contains a
five-column token/connector grid (`48,*,48,*,48`); its second row contains a
separate three-equal-column label grid so Thai labels are not constrained to
the 48-unit token width:

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<ContentView
    x:Class="Toklong.Mobile.Controls.TransactionProgressView"
    x:Name="Root"
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml">
    <Grid
        BindingContext="{Binding Transaction, Source={x:Reference Root}}"
        RowDefinitions="48,Auto"
        RowSpacing="7">
        <Grid ColumnDefinitions="48,*,48,*,48">
            <Border
                AutomationId="ProgressConnectorOne"
                Grid.Column="1"
                HeightRequest="4"
                VerticalOptions="Center"
                BackgroundColor="{Binding ProgressConnectorOneColor}"
                StrokeThickness="0"
                StrokeShape="RoundRectangle 2" />
            <Border
                AutomationId="ProgressConnectorTwo"
                Grid.Column="3"
                HeightRequest="4"
                VerticalOptions="Center"
                BackgroundColor="{Binding ProgressConnectorTwoColor}"
                StrokeThickness="0"
                StrokeShape="RoundRectangle 2" />

            <Border
                AutomationId="ProgressTokenOne"
                Grid.Column="0"
                WidthRequest="48"
                HeightRequest="48"
                BackgroundColor="{Binding ProgressOne.BackgroundColor}"
                Stroke="{Binding ProgressOne.StrokeColor}"
                StrokeThickness="4"
                StrokeShape="RoundRectangle 24"
                SemanticProperties.Description="{Binding ProgressOne.SemanticDescription}">
                <Image
                    AutomationProperties.IsInAccessibleTree="False"
                    WidthRequest="34"
                    HeightRequest="34"
                    Source="{Binding ProgressOne.Icon}" />
            </Border>
            <Border
                AutomationId="ProgressTokenTwo"
                Grid.Column="2"
                WidthRequest="48"
                HeightRequest="48"
                BackgroundColor="{Binding ProgressTwo.BackgroundColor}"
                Stroke="{Binding ProgressTwo.StrokeColor}"
                StrokeThickness="4"
                StrokeShape="RoundRectangle 24"
                SemanticProperties.Description="{Binding ProgressTwo.SemanticDescription}">
                <Image
                    AutomationProperties.IsInAccessibleTree="False"
                    WidthRequest="34"
                    HeightRequest="34"
                    Source="{Binding ProgressTwo.Icon}" />
            </Border>
            <Border
                AutomationId="ProgressTokenThree"
                Grid.Column="4"
                WidthRequest="48"
                HeightRequest="48"
                BackgroundColor="{Binding ProgressThree.BackgroundColor}"
                Stroke="{Binding ProgressThree.StrokeColor}"
                StrokeThickness="4"
                StrokeShape="RoundRectangle 24"
                SemanticProperties.Description="{Binding ProgressThree.SemanticDescription}">
                <Image
                    AutomationProperties.IsInAccessibleTree="False"
                    WidthRequest="34"
                    HeightRequest="34"
                    Source="{Binding ProgressThree.Icon}" />
            </Border>
        </Grid>

        <Grid Grid.Row="1" ColumnDefinitions="*,*,*">
            <Label
                Grid.Column="0"
                AutomationProperties.IsInAccessibleTree="False"
                HorizontalTextAlignment="Center"
                FontSize="12"
                MaxLines="2"
                Text="{Binding ProgressOne.Label}"
                TextColor="{Binding ProgressOne.LabelColor}" />
            <Label
                Grid.Column="1"
                AutomationProperties.IsInAccessibleTree="False"
                HorizontalTextAlignment="Center"
                FontSize="12"
                MaxLines="2"
                Text="{Binding ProgressTwo.Label}"
                TextColor="{Binding ProgressTwo.LabelColor}" />
            <Label
                Grid.Column="2"
                AutomationProperties.IsInAccessibleTree="False"
                HorizontalTextAlignment="Center"
                FontSize="12"
                MaxLines="2"
                Text="{Binding ProgressThree.Label}"
                TextColor="{Binding ProgressThree.LabelColor}" />
        </Grid>
    </Grid>
</ContentView>
```

- [ ] **Step 6: Replace the repeated page markup**

Keep the existing surface card and title. Replace its current three-column
progress grid with:

```xml
<controls:TransactionProgressView
    AutomationId="TransactionProgress"
    Transaction="{Binding Transaction}" />
```

Do not add commands or gesture recognizers.

- [ ] **Step 7: Remove the temporary legacy presentation surface**

After the page no longer binds it, remove `ProgressOneMarker`,
`ProgressTwoMarker`, `ProgressThreeMarker`, `ProgressOneBackground`,
`ProgressTwoBackground`, `ProgressThreeBackground`, `ProgressOneForeground`,
`ProgressTwoForeground`, `ProgressThreeForeground`, the three old icon
properties, the three old label-color properties, and the private
`ProgressMarker`, `ProgressBackground`, `ProgressForeground`, `ProgressIcon`
and `ProgressLabelColor` helpers from `AppTransaction`. Keep the new
`TransactionProgressStep` properties, connector colors, role labels,
`ProgressCompletedThrough` and `ProgressActiveStep`.

- [ ] **Step 8: Record the approved component in the product UI spec**

Replace the current progress-card bullet in `docs/02_UI_UX_AND_CONTENT_SPEC.md`
with:

```markdown
- In the three-step progress card, use Connected Tokens: three `48 × 48`
  circular milestones joined by two rounded connectors. Completed tokens,
  destination connectors and labels are green; active-but-incomplete and future
  tokens remain gray because the main status card communicates the current
  action. Use TOKLONG-owned rounded glyphs, role-specific buyer/seller artwork,
  and distinct physical/digital fulfillment glyphs. Do not show floating
  number/check badges, tap behavior, or progress animation.
```

- [ ] **Step 9: Run focused layout tests and the iOS build**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~UiLayoutConsistencyTests" --no-restore
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --no-restore
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj \
  -f net10.0-ios \
  -p:RuntimeIdentifier=iossimulator-arm64 \
  --no-restore -m:1 -nr:false
```

Expected: all tests pass and the iOS simulator build succeeds. If the sandbox
blocks the MSBuild task host, rerun the same build command with the approved
unsandboxed execution path; do not change product code to work around IPC.

- [ ] **Step 10: Inspect the component on buyer and seller simulators**

Install the freshly built app on both existing simulator devices, log into the
buyer and seller test accounts, open the same physical transaction and verify:

```text
Buyer: agreement / payment / parcel received
Seller: agreement / parcel handoff / payout destination
Completed tokens: green
Active but incomplete and future tokens: gray
No floating badges
No clipped Thai labels at the default text size
```

Also open one digital transaction and verify that the fulfillment milestone
uses the rail-handoff glyph, not a parcel.

- [ ] **Step 11: Run repository verification**

Run:

```bash
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj --no-restore
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj --no-restore
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj --no-restore
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore
dotnet test tests/Toklong.Crm.Tests/Toklong.Crm.Tests.csproj --no-restore
git diff --check
git status --short
```

Expected: all five suites pass, `git diff --check` prints nothing, and only the
files in this task are modified.

- [ ] **Step 12: Commit the reusable UI**

```bash
git add \
  src/Toklong.Mobile/Core/AppTransaction.cs \
  src/Toklong.Mobile/Controls/TransactionProgressView.xaml \
  src/Toklong.Mobile/Controls/TransactionProgressView.xaml.cs \
  src/Toklong.Mobile/Pages/TransactionDetailPage.xaml \
  tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs \
  docs/02_UI_UX_AND_CONTENT_SPEC.md
git commit -m "feat: render connected status tokens"
```

## Completion report

Report:

1. the new presentation record, local glyph family and reusable control;
2. that no transaction requirement or state transition changed;
3. focused, accessibility, asset, full-suite and iOS-build results;
4. the assumption that existing labels and completion mappings remain approved;
5. any Simulator authentication or provider limitation that blocked visual
   verification;
6. the next smallest slice: validate Thai labels at accessibility text sizes
   and adjust only spacing if clipping is observed.
