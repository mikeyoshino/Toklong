# TOKLONG Rail Morph Icon Family Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the generic transaction-progress pictograms with an eighteen-asset TOKLONG Rail Morph family whose completed appearance adapts to buyer or seller role without changing transaction behavior.

**Architecture:** Keep state-to-milestone derivation in `AppTransaction` and make that presentation model select semantic glyph, role variant, token colors, and destination-connector colors. Keep `TransactionProgressView` a passive three-token XAML control. Enforce the local SVG geometry, palette, naming, size, and accessibility contracts through the existing mobile-core test project.

**Tech Stack:** .NET 10, C#, .NET MAUI XAML, repository-owned SVG resources, xUnit, iOS Simulator

## Global Constraints

- This is presentation-only: do not change transaction states, `ProgressCompletedThrough`, payment truth, fulfillment eligibility, dispute behavior, payout eligibility, deadlines, or audit events.
- Use exactly six semantics: `agreement`, `payment`, `physical_handoff`, `physical_receipt`, `digital_handoff`, and `payout`.
- Use exactly three variants per semantic: `buyer_completed`, `seller_completed`, and `disabled`.
- Every source asset uses `viewBox="0 0 48 48"`, stays inside coordinates `6..42`, uses rounded caps and joins, and has no stroke wider than `3`.
- Buyer completed uses `#145FC7`, `#2B7FFF`, and node `#65D6BF`; seller completed uses `#6548C7`, `#8067DE`, and node `#65D6BF`.
- Disabled uses rail `#98A2B3` and node `#D6DCE5`, with no buyer blue, seller purple, mint, or success green.
- Buyer completed token fill is `#EAF4FF`; its outline, label, and completed destination connector are `#145FC7`.
- Seller completed token fill is `#F1ECFF`; its outline, label, and completed destination connector are `#6548C7`.
- Incomplete token fill is `#FFFFFF`; its outline and connector are `#E4EAF1`; its label is `#98A2B3`.
- A connector becomes role-colored only when its destination milestone is completed; an active-but-incomplete milestone remains neutral.
- Preserve three `48 × 48` circular tokens, `30 × 30` images, title size `15`, label size `12`, two rounded connectors, Thai semantic descriptions, and no tap behavior or animation.
- Do not add stock document, card, banknote, truck, parcel, wallet, bank, currency, credential, or brand-network symbols.
- MAUI references compiled SVG resources with the `.png` extension.

---

## File Structure

- `src/Toklong.Mobile/Core/AppTransaction.cs` — owns role/fulfillment/state-to-progress presentation mapping and role-adaptive token/connector colors.
- `src/Toklong.Mobile/Resources/Images/progress_*.svg` — owns the eighteen Rail Morph drawings; no runtime download or system-symbol fallback.
- `src/Toklong.Mobile/Controls/TransactionProgressView.xaml` — remains the passive, uniformly sized three-token renderer.
- `tests/Toklong.Mobile.Core.Tests/TransactionPresentationTests.cs` — verifies semantic asset selection, colors, connector completion, and Thai descriptions.
- `tests/Toklong.Mobile.Core.Tests/BrandAssetConsistencyTests.cs` — verifies the complete asset manifest, SVG geometry, and per-variant palettes.
- `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs` — verifies the fixed visual and accessibility contract.
- `docs/02_UI_UX_AND_CONTENT_SPEC.md` — records the role-adaptive Rail Morph rule as product UI guidance.

### Task 1: Role-adaptive progress presentation

**Files:**
- Modify: `tests/Toklong.Mobile.Core.Tests/TransactionPresentationTests.cs:192-280`
- Modify: `src/Toklong.Mobile/Core/AppTransaction.cs:495-570`
- Modify: `docs/02_UI_UX_AND_CONTENT_SPEC.md:15-21`

**Interfaces:**
- Consumes: existing `AppTransaction.Role`, `AppTransaction.FulfillmentType`, and `AppTransaction.ProgressCompletedThrough`.
- Produces: unchanged public properties `ProgressOne`, `ProgressTwo`, `ProgressThree`, `ProgressConnectorOneColor`, and `ProgressConnectorTwoColor`; completed assets use `progress_<semantic>_<buyer|seller>_completed.png`.

- [ ] **Step 1: Replace the presentation expectations with role-aware failing tests**

Update the existing tests to assert the new semantic names and palettes. Keep the current state fixtures so no domain behavior is redefined:

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

    Assert.Equal("progress_agreement_buyer_completed.png", item.ProgressOne.Icon);
    Assert.Equal("progress_payment_disabled.png", item.ProgressTwo.Icon);
    Assert.Equal("progress_physical_receipt_disabled.png", item.ProgressThree.Icon);
    Assert.Equal("สร้างข้อตกลง เสร็จแล้ว", item.ProgressOne.SemanticDescription);
    Assert.Equal("จ่ายเงิน ยังไม่เสร็จ", item.ProgressTwo.SemanticDescription);
    Assert.Equal("#EAF4FF", item.ProgressOne.BackgroundColor);
    Assert.Equal("#145FC7", item.ProgressOne.StrokeColor);
    Assert.Equal("#145FC7", item.ProgressOne.LabelColor);
    Assert.Equal("#FFFFFF", item.ProgressTwo.BackgroundColor);
    Assert.Equal("#E4EAF1", item.ProgressTwo.StrokeColor);
    Assert.Equal("#98A2B3", item.ProgressTwo.LabelColor);
}

[Fact]
public void ConnectedProgressUsesSellerCompletedVariantAndPalette()
{
    var item = CreateItem(null) with
    {
        Role = AppTransactionRole.Seller,
        FulfillmentType = AppFulfillmentType.Physical,
        State = "PaidAwaitingShipment"
    };

    Assert.Equal("progress_agreement_seller_completed.png", item.ProgressOne.Icon);
    Assert.Equal("progress_physical_handoff_seller_completed.png", item.ProgressTwo.Icon);
    Assert.Equal("progress_payout_disabled.png", item.ProgressThree.Icon);
    Assert.Equal("#F1ECFF", item.ProgressTwo.BackgroundColor);
    Assert.Equal("#6548C7", item.ProgressTwo.StrokeColor);
    Assert.Equal("#6548C7", item.ProgressTwo.LabelColor);
}
```

Change the theory data to these exact disabled mappings:

```csharp
[InlineData(AppTransactionRole.Buyer, AppFulfillmentType.Digital,
    "progress_payment_disabled.png", "progress_digital_handoff_disabled.png")]
[InlineData(AppTransactionRole.Seller, AppFulfillmentType.Physical,
    "progress_physical_handoff_disabled.png", "progress_payout_disabled.png")]
[InlineData(AppTransactionRole.Seller, AppFulfillmentType.Digital,
    "progress_digital_handoff_disabled.png", "progress_payout_disabled.png")]
```

Change the connector assertions to buyer blue and add seller purple:

```csharp
Assert.Equal("#145FC7", secondComplete.ProgressConnectorOneColor);
Assert.Equal("#145FC7", thirdComplete.ProgressConnectorOneColor);
Assert.Equal("#145FC7", thirdComplete.ProgressConnectorTwoColor);

var seller = thirdComplete with { Role = AppTransactionRole.Seller };
Assert.Equal("#6548C7", seller.ProgressConnectorOneColor);
Assert.Equal("#6548C7", seller.ProgressConnectorTwoColor);
```

Keep `ActiveButIncompleteConnectedTokenStaysGray`, but expect
`progress_physical_receipt_disabled.png`.

- [ ] **Step 2: Run the focused tests and verify the old green/generic mapping fails**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~ConnectedProgress" --no-restore
```

Expected: FAIL because completed icons still end in `_completed.png`, physical assets still use `parcel_*`, and completed colors are still `#087C68`.

- [ ] **Step 3: Implement the minimal role-adaptive mapping**

Replace the semantic selections and progress constants/helpers in
`AppTransaction` with:

```csharp
public TransactionProgressStep ProgressTwo =>
    CreateProgressStep(
        2,
        ProgressTwoLabel,
        Role == AppTransactionRole.Buyer
            ? "payment"
            : FulfillmentType == AppFulfillmentType.Physical
                ? "physical_handoff"
                : "digital_handoff");

public TransactionProgressStep ProgressThree =>
    CreateProgressStep(
        3,
        ProgressThreeLabel,
        Role == AppTransactionRole.Seller
            ? "payout"
            : FulfillmentType == AppFulfillmentType.Physical
                ? "physical_receipt"
                : "digital_handoff");

private const string BuyerProgress = "#145FC7";
private const string BuyerProgressBackground = "#EAF4FF";
private const string SellerProgress = "#6548C7";
private const string SellerProgressBackground = "#F1ECFF";
private const string ProgressIncomplete = "#E4EAF1";
private const string ProgressMuted = "#98A2B3";

private string CompletedProgressColor =>
    Role == AppTransactionRole.Buyer
        ? BuyerProgress
        : SellerProgress;

private string CompletedProgressBackground =>
    Role == AppTransactionRole.Buyer
        ? BuyerProgressBackground
        : SellerProgressBackground;

private string CompletedProgressVariant =>
    Role == AppTransactionRole.Buyer
        ? "buyer_completed"
        : "seller_completed";
```

Use `CompletedProgressColor` for a connector only when its destination step is
complete:

```csharp
public string ProgressConnectorOneColor =>
    ProgressCompletedThrough >= 2
        ? CompletedProgressColor
        : ProgressIncomplete;

public string ProgressConnectorTwoColor =>
    ProgressCompletedThrough >= 3
        ? CompletedProgressColor
        : ProgressIncomplete;
```

Replace `CreateProgressStep` with:

```csharp
private TransactionProgressStep CreateProgressStep(
    int step,
    string label,
    string glyph)
{
    var completed = step <= ProgressCompletedThrough;
    var suffix = completed
        ? CompletedProgressVariant
        : "disabled";

    return new TransactionProgressStep(
        label,
        $"progress_{glyph}_{suffix}.png",
        completed ? CompletedProgressBackground : "#FFFFFF",
        completed ? CompletedProgressColor : ProgressIncomplete,
        completed ? CompletedProgressColor : ProgressMuted,
        $"{label} {(completed ? "เสร็จแล้ว" : "ยังไม่เสร็จ")}");
}
```

- [ ] **Step 4: Run the focused presentation suite**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~ConnectedProgress" --no-restore
```

Expected: PASS, including seller physical, seller digital, buyer physical, buyer
digital, connector destination, active-incomplete, and Thai semantic assertions.

- [ ] **Step 5: Update the UI specification**

Replace the green connected-token rule in `docs/02_UI_UX_AND_CONTENT_SPEC.md`
with:

```markdown
- In the three-step progress card, use Connected Tokens: three `48 × 48`
  circular milestones joined by two rounded connectors. Completed buyer tokens,
  labels, and destination connectors use Buyer Blue (`#145FC7`) on
  `#EAF4FF`; completed seller tokens, labels, and destination connectors use
  Seller Purple (`#6548C7`) on `#F1ECFF`. Active-but-incomplete and future
  tokens remain neutral (`#98A2B3`, `#E4EAF1`, and white) because the main
  status card communicates the current action. Use the TOKLONG Rail Morph
  family with role-specific completed artwork and distinct physical/digital
  fulfillment glyphs. Do not show floating number/check badges, tap behavior,
  or progress animation.
```

- [ ] **Step 6: Commit the role-adaptive mapping**

```bash
git add src/Toklong.Mobile/Core/AppTransaction.cs \
  tests/Toklong.Mobile.Core.Tests/TransactionPresentationTests.cs \
  docs/02_UI_UX_AND_CONTENT_SPEC.md
git commit -m "feat: map role-adaptive progress icons"
```

### Task 2: Eighteen-asset Rail Morph family

**Files:**
- Modify: `tests/Toklong.Mobile.Core.Tests/BrandAssetConsistencyTests.cs:8-115`
- Create: `src/Toklong.Mobile/Resources/Images/progress_{agreement,payment,physical_handoff,physical_receipt,digital_handoff,payout}_{buyer_completed,seller_completed,disabled}.svg`
- Delete: the six old generic `progress_{agreement,payment,parcel_handoff,parcel_received,digital_handoff,payout}_completed.svg` files and the two renamed `progress_{parcel_handoff,parcel_received}_disabled.svg` files.
- Replace in place: `progress_{agreement,payment,digital_handoff,payout}_disabled.svg`.

**Interfaces:**
- Consumes: Task 1 runtime names `progress_<semantic>_<variant>.png`; the project already includes `Resources/Images/progress_*.svg` through its MAUI image wildcard.
- Produces: eighteen valid local SVG resources with stable semantic groups `rail-primary`, `rail-secondary`, and `rail-node`.

- [ ] **Step 1: Replace the asset manifest with the exact eighteen expected names**

Generate `ProgressAssets` from these constants so missing and extra names are
easy to diagnose:

```csharp
private static readonly string[] ProgressSemantics =
[
    "agreement",
    "payment",
    "physical_handoff",
    "physical_receipt",
    "digital_handoff",
    "payout"
];

private static readonly string[] ProgressVariants =
[
    "buyer_completed",
    "seller_completed",
    "disabled"
];

public static TheoryData<string> ProgressAssets
{
    get
    {
        var data = new TheoryData<string>();
        foreach (var semantic in ProgressSemantics)
        foreach (var variant in ProgressVariants)
            data.Add($"progress_{semantic}_{variant}.svg");
        return data;
    }
}
```

Add a manifest test that rejects obsolete SVGs:

```csharp
[Fact]
public void ProgressAssetManifestContainsOnlyRailMorphFamily()
{
    var actual = Directory
        .GetFiles(BrandDirectory(), "progress_*.svg")
        .Select(Path.GetFileName)
        .Order(StringComparer.Ordinal)
        .ToArray();
    var expected = ProgressSemantics
        .SelectMany(semantic => ProgressVariants.Select(
            variant => $"progress_{semantic}_{variant}.svg"))
        .Order(StringComparer.Ordinal)
        .ToArray();

    Assert.Equal(expected, actual);
}
```

If the test class currently exposes only `BrandPath`, add:

```csharp
private static string BrandDirectory() =>
    Path.Combine(AppContext.BaseDirectory, "Brand");
```

- [ ] **Step 2: Replace the geometry test with explicit structural and palette contracts**

For every member-data asset, parse the SVG and assert:

```csharp
var primary = document.Descendants()
    .Single(element => (string?)element.Attribute("id") == "rail-primary");
var secondary = document.Descendants()
    .Single(element => (string?)element.Attribute("id") == "rail-secondary");
var node = document.Descendants()
    .Single(element => (string?)element.Attribute("id") == "rail-node");

Assert.Equal("0 0 48 48", (string?)document.Root!.Attribute("viewBox"));
Assert.Equal("round", (string?)primary.Attribute("stroke-linecap"));
Assert.Equal("round", (string?)primary.Attribute("stroke-linejoin"));
Assert.Equal("round", (string?)secondary.Attribute("stroke-linecap"));
Assert.Equal("round", (string?)secondary.Attribute("stroke-linejoin"));
Assert.InRange(ParseStroke(primary), 2.5m, 3m);
Assert.InRange(ParseStroke(secondary), 2.5m, 3m);
Assert.Equal("circle", node.Name.LocalName);
Assert.InRange(ParseDecimal(node, "r"), 3.5m, 4.5m);
Assert.DoesNotContain("<text", content, StringComparison.OrdinalIgnoreCase);
```

Add an explicit variant branch:

```csharp
if (fileName.EndsWith("_buyer_completed.svg", StringComparison.Ordinal))
{
    Assert.Equal("#145FC7", Attr(primary, "stroke"), ignoreCase: true);
    Assert.Equal("#2B7FFF", Attr(secondary, "stroke"), ignoreCase: true);
    Assert.Equal("#65D6BF", Attr(node, "fill"), ignoreCase: true);
    Assert.DoesNotContain("#6548C7", content, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("#8067DE", content, StringComparison.OrdinalIgnoreCase);
}
else if (fileName.EndsWith("_seller_completed.svg", StringComparison.Ordinal))
{
    Assert.Equal("#6548C7", Attr(primary, "stroke"), ignoreCase: true);
    Assert.Equal("#8067DE", Attr(secondary, "stroke"), ignoreCase: true);
    Assert.Equal("#65D6BF", Attr(node, "fill"), ignoreCase: true);
    Assert.DoesNotContain("#145FC7", content, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("#2B7FFF", content, StringComparison.OrdinalIgnoreCase);
}
else
{
    Assert.Equal("#98A2B3", Attr(primary, "stroke"), ignoreCase: true);
    Assert.Equal("#98A2B3", Attr(secondary, "stroke"), ignoreCase: true);
    Assert.Equal("#D6DCE5", Attr(node, "fill"), ignoreCase: true);
    foreach (var forbidden in new[]
             {
                 "#145FC7", "#2B7FFF", "#6548C7", "#8067DE",
                 "#65D6BF", "#087C68"
             })
        Assert.DoesNotContain(forbidden, content, StringComparison.OrdinalIgnoreCase);
}
```

Define the helpers used above in the same test class:

```csharp
private static string Attr(XElement element, string name) =>
    (string?)element.Attribute(name)
    ?? throw new Xunit.Sdk.XunitException($"Missing {name} on {element.Name}");

private static decimal ParseDecimal(XElement element, string name) =>
    decimal.Parse(
        Attr(element, name),
        System.Globalization.CultureInfo.InvariantCulture);

private static decimal ParseStroke(XElement element) =>
    ParseDecimal(element, "stroke-width");
```

- [ ] **Step 3: Run the asset tests and verify the new manifest is red**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter FullyQualifiedName~BrandAssetConsistencyTests --no-restore
```

Expected: FAIL because the eighteen Rail Morph files do not yet exist and the
obsolete generic asset names remain.

- [ ] **Step 4: Create each semantic SVG from the exact geometry table**

Each file uses this exact structure, substituting only the two `d` values and
three palette values from the tables below:

```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 48 48">
  <path id="rail-primary" d="PATH_PRIMARY" fill="none"
        stroke="PRIMARY_COLOR" stroke-width="3"
        stroke-linecap="round" stroke-linejoin="round"/>
  <path id="rail-secondary" d="PATH_SECONDARY" fill="none"
        stroke="SECONDARY_COLOR" stroke-width="2.75"
        stroke-linecap="round" stroke-linejoin="round"/>
  <circle id="rail-node" cx="24" cy="24" r="4"
          fill="NODE_COLOR" stroke="#FFFFFF" stroke-width="1.5"/>
</svg>
```

Use these exact paths:

| Semantic | `PATH_PRIMARY` | `PATH_SECONDARY` | Meaning |
| --- | --- | --- | --- |
| `agreement` | `M8 28 C12 28 14 18 20 18 C23 18 25 20 28 23 C31 26 33 28 36 28 C38 28 40 26 40 24` | `M8 24 C8 22 10 20 12 20 C15 20 17 22 20 25 C23 28 25 30 28 30 C34 30 36 20 40 20` | opposed interlocking rails |
| `payment` | `M7 20 H17 C21 20 22 24 24 24 C26 24 27 20 31 20 H41` | `M7 28 H17 C21 28 22 24 24 24 C26 24 27 28 31 28 H41` | left-to-right confirmation junction |
| `physical_handoff` | `M8 24 H17 C21 24 22 21 24 18 C27 14 32 12 40 12` | `M8 28 H17 C21 28 22 31 24 34 C27 38 32 40 40 40` | protected center opening outward |
| `physical_receipt` | `M8 12 C16 12 21 16 24 21 C26 24 27 24 31 24 H40` | `M8 40 C16 40 21 36 24 31 C26 28 27 28 31 28 H40` | rails closing around received center |
| `digital_handoff` | `M8 16 C16 16 18 32 24 32 C30 32 32 16 40 16` | `M8 32 C16 32 18 16 24 16 C30 16 32 32 40 32` | mirrored position exchange |
| `payout` | `M8 14 C17 14 18 21 24 24 C30 27 31 34 40 34` | `M8 34 C17 34 18 27 24 24 C30 21 31 14 40 14` | convergence on destination node |

Use these exact palettes:

| Variant | `PRIMARY_COLOR` | `SECONDARY_COLOR` | `NODE_COLOR` | Node stroke |
| --- | --- | --- | --- | --- |
| `buyer_completed` | `#145FC7` | `#2B7FFF` | `#65D6BF` | `#FFFFFF` |
| `seller_completed` | `#6548C7` | `#8067DE` | `#65D6BF` | `#FFFFFF` |
| `disabled` | `#98A2B3` | `#98A2B3` | `#D6DCE5` | `#FFFFFF` |

These paths stay inside `7..41`, share optical center `(24,24)`, use no literal
object symbol, and preserve a four-unit node. Do not add arrows, checkmarks,
numbers, currency marks, boxes, vehicles, cards, or credentials.

- [ ] **Step 5: Remove only the superseded names**

Delete these eight files after the eighteen replacements exist:

```text
progress_agreement_completed.svg
progress_payment_completed.svg
progress_parcel_handoff_completed.svg
progress_parcel_handoff_disabled.svg
progress_parcel_received_completed.svg
progress_parcel_received_disabled.svg
progress_digital_handoff_completed.svg
progress_payout_completed.svg
```

The four unchanged disabled names (`agreement`, `payment`, `digital_handoff`,
and `payout`) were overwritten in Step 4, not deleted. All six resulting
disabled assets must have the `rail-primary`, `rail-secondary`, and
`rail-node` contract.

- [ ] **Step 6: Run asset and presentation tests**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~BrandAssetConsistencyTests|FullyQualifiedName~ConnectedProgress" \
  --no-restore
```

Expected: PASS with exactly eighteen assets and no obsolete `parcel_*` or
generic `_completed` references.

- [ ] **Step 7: Build the iOS simulator target to compile SVG resources**

Run:

```bash
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj \
  -p:TargetFrameworks=net10.0-ios \
  -f net10.0-ios \
  -r iossimulator-arm64 \
  --no-restore
```

Expected: build succeeds with no missing image-resource or SVG parsing error.

- [ ] **Step 8: Commit the Rail Morph assets**

```bash
git add tests/Toklong.Mobile.Core.Tests/BrandAssetConsistencyTests.cs \
  src/Toklong.Mobile/Resources/Images/progress_*.svg
git commit -m "feat: add rail morph progress assets"
```

### Task 3: Fixed component, accessibility, and simulator acceptance

**Files:**
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs:746-815`
- Modify only if a failing contract requires it: `src/Toklong.Mobile/Controls/TransactionProgressView.xaml`

**Interfaces:**
- Consumes: `TransactionProgressStep` values and connector colors produced by Task 1, and the resource names produced by Task 2.
- Produces: a verified passive component contract: three equal tokens, three equal images, three labels, two connectors, semantic descriptions, and no gestures or animation.

- [ ] **Step 1: Strengthen the XAML contract test**

In `TransactionDetailUsesAccessibleConnectedProgressTokens`, retain the existing
title/token/connector assertions and add:

```csharp
var images = progress.Descendants(Maui + "Image").ToArray();
var labels = progress.Descendants(Maui + "Label").ToArray();

Assert.Equal(3, images.Length);
Assert.All(images, image =>
{
    Assert.Equal("30", AttributeValue(image, "WidthRequest"));
    Assert.Equal("30", AttributeValue(image, "HeightRequest"));
    Assert.Equal("False",
        AttributeValue(image, "AutomationProperties.IsInAccessibleTree"));
});

Assert.Equal(3, labels.Length);
Assert.All(labels, label =>
{
    Assert.Equal("12", AttributeValue(label, "FontSize"));
    Assert.Equal("False",
        AttributeValue(label, "AutomationProperties.IsInAccessibleTree"));
});

Assert.Empty(progress.Descendants(Maui + "TapGestureRecognizer"));
Assert.DoesNotContain(
    progress.Descendants(),
    element => element.Name.LocalName.Contains(
        "Animation",
        StringComparison.OrdinalIgnoreCase));
```

- [ ] **Step 2: Run the component contract test**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter FullyQualifiedName~TransactionDetailUsesAccessibleConnectedProgressTokens \
  --no-restore
```

Expected: PASS without production changes because the approved component already
uses `48`, `30`, `15`, and `12`. If it fails, change only the mismatched literal
in `TransactionProgressView.xaml`; do not introduce per-icon sizing, gestures,
or animation.

- [ ] **Step 3: Run the complete repository test suite**

Run:

```bash
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj --no-restore
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj --no-restore
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj --no-restore
dotnet test tests/Toklong.Crm.Tests/Toklong.Crm.Tests.csproj --no-restore
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore
```

Expected: every suite passes. There are no payment, webhook, tracking, dispute,
payout, or state-transition changes, so existing domain suites provide the
regression evidence.

- [ ] **Step 4: Rebuild, install, and inspect buyer and seller simulator states**

Build once:

```bash
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj \
  -p:TargetFrameworks=net10.0-ios \
  -f net10.0-ios \
  -r iossimulator-arm64 \
  --no-restore
```

Confirm the two prepared devices are booted:

```bash
xcrun simctl list devices booted
```

The expected buyer UUID is `09CF9ABB-0DD3-4DD9-ADB5-9BEC2A3A0BB9`; the expected
seller UUID is `986E4296-A499-427B-9908-1AB1B9422944`. Install and launch the
exact build on both:

```bash
xcrun simctl install 09CF9ABB-0DD3-4DD9-ADB5-9BEC2A3A0BB9 \
  src/Toklong.Mobile/bin/Debug/net10.0-ios/iossimulator-arm64/Toklong.Mobile.app
xcrun simctl install 986E4296-A499-427B-9908-1AB1B9422944 \
  src/Toklong.Mobile/bin/Debug/net10.0-ios/iossimulator-arm64/Toklong.Mobile.app
xcrun simctl launch 09CF9ABB-0DD3-4DD9-ADB5-9BEC2A3A0BB9 th.co.toklong.mobile
xcrun simctl launch 986E4296-A499-427B-9908-1AB1B9422944 th.co.toklong.mobile
```

If either UUID is no longer booted, boot that exact prepared device with
`xcrun simctl boot UUID`, then repeat the matching install and launch command.

Verify these exact cases:

1. Buyer physical: agreement is Buyer Blue/Mint; payment and physical receipt
   are neutral when incomplete.
2. Seller physical: completed agreement/handoff is Seller Purple/Mint; payout
   is neutral until complete.
3. A digital transaction uses `digital_handoff`, not a physical glyph.
4. Completed and incomplete tokens remain visually distinct without relying on
   color: different fill, outline, glyph, label color, and Thai semantic text.
5. At default text size, `ตอนนี้ถึงขั้นไหน` is visibly smaller than the earlier
   design and labels do not collide.
6. At one accessibility text size, three labels remain legible and no progress
   control becomes tappable.

Capture one buyer and one seller screenshot:

```bash
xcrun simctl io 09CF9ABB-0DD3-4DD9-ADB5-9BEC2A3A0BB9 \
  screenshot /tmp/toklong-rail-buyer.png
xcrun simctl io 986E4296-A499-427B-9908-1AB1B9422944 \
  screenshot /tmp/toklong-rail-seller.png
```

- [ ] **Step 5: Scan for stale names and forbidden palette leakage**

Run:

```bash
rg -n "progress_(parcel_handoff|parcel_received)|progress_(agreement|payment|digital_handoff|payout)_completed\\.png|#087C68" \
  src/Toklong.Mobile tests/Toklong.Mobile.Core.Tests docs/02_UI_UX_AND_CONTENT_SPEC.md
```

Expected: no obsolete progress asset name, no generic completed runtime name,
and no old success-green progress rule. Other non-progress uses of `#087C68`
must not be changed; if the broad color match finds one, confirm it is unrelated
and narrow the test/search to progress code.

- [ ] **Step 6: Commit the component acceptance contract**

```bash
git add tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs \
  src/Toklong.Mobile/Controls/TransactionProgressView.xaml
git commit -m "test: lock rail morph progress layout"
```

If `TransactionProgressView.xaml` did not change, omit it from `git add`.

## Completion Report

Report:

1. The role-adaptive progress mapping and eighteen Rail Morph assets that changed.
2. That no requirement or state transition changed; only existing milestone presentation was implemented.
3. The presentation, asset-contract, layout/accessibility, full-suite, and iOS-build checks run.
4. The assumptions that existing completion mapping, Thai labels, and role palette remain authoritative.
5. Any unresolved visual issue, simulator limitation, or blocked provider capability; payment/provider capabilities should be “none” for this presentation-only slice.
6. The next smallest vertical slice: user review of buyer/seller screenshots followed only by optical SVG path adjustment, without changing XAML sizes or transaction behavior.
