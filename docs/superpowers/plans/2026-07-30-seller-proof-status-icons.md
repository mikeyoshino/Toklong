# Seller Proof Status Icons Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the seller's ambiguous transaction-status glyphs with the approved proof icons and highlight only the domain-approved current seller step.

**Architecture:** Add seller-only glyph enum values and select them in `AppTransaction`, leaving buyer and digital-handoff glyphs unchanged. Extend the existing MAUI `GraphicsView` drawable with three proof drawings and derive seller current styling from the existing `ProgressActiveStep`.

**Tech Stack:** .NET 10, .NET MAUI `GraphicsView`, C#, xUnit, iOS Simulator.

## Global Constraints

- Buyer progress icons and colors remain unchanged.
- Physical seller progress uses document-check, outbound-delivery, and payout-record-check glyphs.
- Digital seller handoff retains `DigitalHandoff`.
- Seller current styling uses `ProgressActiveStep`; step `0` highlights nothing.
- Shipping must not appear active before provider-confirmed payment.
- Transaction state mapping, payout rules, authorization, and audit behavior remain unchanged.
- The drawing stays outside the accessibility tree; token borders expose Thai semantic descriptions.

---

### Task 1: Seller Proof Glyph Selection and State Presentation

**Files:**
- Modify: `src/Toklong.Mobile/Core/TransactionProgressStep.cs`
- Modify: `src/Toklong.Mobile/Core/AppTransaction.cs:600-711`
- Modify: `tests/Toklong.Mobile.Core.Tests/TransactionPresentationTests.cs:214-410`

**Interfaces:**
- Produces: `TransactionProgressGlyph.SellerAgreementProof`
- Produces: `TransactionProgressGlyph.SellerPhysicalShipmentProof`
- Produces: `TransactionProgressGlyph.SellerPayoutProof`
- Consumes: `AppTransaction.ProgressActiveStep`
- Preserves: `TransactionProgressStep` constructor signature

- [ ] **Step 1: Write failing seller presentation tests**

Add this test to `TransactionPresentationTests`:

```csharp
[Fact]
public void SellerProgressUsesProofGlyphsAndCurrentPalette()
{
    var item = CreateItem(null) with
    {
        Role = AppTransactionRole.Seller,
        FulfillmentType = AppFulfillmentType.Physical,
        State = "PaidAwaitingShipment"
    };

    Assert.Equal(
        TransactionProgressGlyph.SellerAgreementProof,
        item.ProgressOne.Glyph);
    Assert.Equal(
        TransactionProgressGlyph.SellerPhysicalShipmentProof,
        item.ProgressTwo.Glyph);
    Assert.Equal(
        TransactionProgressGlyph.SellerPayoutProof,
        item.ProgressThree.Glyph);
    Assert.Equal(SellerColorPalette.Role, item.ProgressOne.StrokeColor);
    Assert.Equal("#087C68", item.ProgressTwo.StrokeColor);
    Assert.Equal("#EAFBF7", item.ProgressTwo.BackgroundColor);
    Assert.Equal("#087C68", item.ProgressTwo.LabelColor);
    Assert.Equal("ส่งของ ขั้นปัจจุบัน", item.ProgressTwo.SemanticDescription);
    Assert.Equal("#E4EAF1", item.ProgressThree.StrokeColor);
}
```

Update `ConnectedProgressUsesRoleAndFulfillmentGlyphs` to expect:

```csharp
role == AppTransactionRole.Seller
    ? TransactionProgressGlyph.SellerAgreementProof
    : TransactionProgressGlyph.Agreement
```

For seller physical step two expect
`SellerPhysicalShipmentProof`; for seller digital step two retain
`DigitalHandoff`; for seller step three expect `SellerPayoutProof`.

Update `SellerCannotSeeShippingAsActiveBeforeConfirmedPayment` to assert:

```csharp
Assert.Equal(0, item.ProgressActiveStep);
Assert.Equal("#FFFFFF", item.ProgressTwo.BackgroundColor);
Assert.Equal("#E4EAF1", item.ProgressTwo.StrokeColor);
Assert.Equal("ส่งของ ยังไม่เสร็จ", item.ProgressTwo.SemanticDescription);
```

- [ ] **Step 2: Run the focused tests and verify they fail**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~SellerProgressUsesProofGlyphsAndCurrentPalette|FullyQualifiedName~ConnectedProgressUsesRoleAndFulfillmentGlyphs|FullyQualifiedName~SellerCannotSeeShippingAsActiveBeforeConfirmedPayment"
```

Expected: FAIL because the seller-only glyph enum values and current palette do
not exist.

- [ ] **Step 3: Add seller-only glyph values**

Extend `TransactionProgressGlyph` with:

```csharp
SellerAgreementProof,
SellerPhysicalShipmentProof,
SellerPayoutProof
```

Do not rename or remove existing values.

- [ ] **Step 4: Select proof glyphs only for seller physical progress**

Change the three progress properties to:

```csharp
public TransactionProgressStep ProgressOne =>
    CreateProgressStep(
        1,
        ProgressOneLabel,
        Role == AppTransactionRole.Seller
            ? TransactionProgressGlyph.SellerAgreementProof
            : TransactionProgressGlyph.Agreement);

public TransactionProgressStep ProgressTwo =>
    CreateProgressStep(
        2,
        ProgressTwoLabel,
        Role == AppTransactionRole.Buyer
            ? TransactionProgressGlyph.Payment
            : FulfillmentType == AppFulfillmentType.Physical
                ? TransactionProgressGlyph.SellerPhysicalShipmentProof
                : TransactionProgressGlyph.DigitalHandoff);

public TransactionProgressStep ProgressThree =>
    CreateProgressStep(
        3,
        ProgressThreeLabel,
        Role == AppTransactionRole.Seller
            ? TransactionProgressGlyph.SellerPayoutProof
            : FulfillmentType == AppFulfillmentType.Physical
                ? TransactionProgressGlyph.PhysicalReceipt
                : TransactionProgressGlyph.DigitalHandoff);
```

- [ ] **Step 5: Add seller current-step styling without changing buyer behavior**

Inside `CreateProgressStep`, calculate:

```csharp
var completed = step <= ProgressCompletedThrough;
var current =
    Role == AppTransactionRole.Seller &&
    !completed &&
    step == ProgressActiveStep;
```

Map the three new glyphs to asset-name metadata:

```csharp
TransactionProgressGlyph.SellerAgreementProof =>
    "seller_agreement_proof",
TransactionProgressGlyph.SellerPhysicalShipmentProof =>
    "seller_physical_shipment_proof",
TransactionProgressGlyph.SellerPayoutProof =>
    "seller_payout_proof",
```

Return colors and semantic copy with:

```csharp
var background = completed
    ? CompletedProgressBackground
    : current
        ? "#EAFBF7"
        : "#FFFFFF";
var stroke = completed
    ? CompletedProgressColor
    : current
        ? "#087C68"
        : ProgressIncomplete;
var labelColor = completed
    ? CompletedProgressColor
    : current
        ? "#087C68"
        : ProgressMuted;
var semanticState = completed
    ? "เสร็จแล้ว"
    : current
        ? "ขั้นปัจจุบัน"
        : "ยังไม่เสร็จ";
```

Keep buyer incomplete steps gray because `current` is seller-only.

- [ ] **Step 6: Run focused tests and verify they pass**

Run the command from Step 2.

Expected: all selected tests pass.

---

### Task 2: Draw the Approved Proof Icons

**Files:**
- Modify: `src/Toklong.Mobile/Controls/TransactionProgressView.xaml.cs:85-340`
- Verify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`

**Interfaces:**
- Consumes: the three seller-specific `TransactionProgressGlyph` values from Task 1.
- Produces: 26-by-26 line drawings through the existing `TransactionProgressIconView`.
- Preserves: `TransactionProgressView.xaml`, token size, label layout, and accessibility ownership.

- [ ] **Step 1: Add switch cases for the three seller proof glyphs**

Add:

```csharp
case TransactionProgressGlyph.SellerAgreementProof:
    DrawSellerAgreementProof(canvas, x, y);
    break;
case TransactionProgressGlyph.SellerPhysicalShipmentProof:
    DrawSellerPhysicalShipmentProof(canvas, x, y);
    break;
case TransactionProgressGlyph.SellerPayoutProof:
    DrawSellerPayoutProof(canvas, x, y);
    break;
```

- [ ] **Step 2: Draw document-check agreement proof**

Implement `DrawSellerAgreementProof` with a document outline from
`(x + 4, y + 2)` to `(x + 18, y + 21)`, a folded corner from
`(x + 13, y + 2)` to `(x + 18, y + 7)`, and a check from
`(x + 7, y + 13)` through `(x + 10, y + 16)` to `(x + 16, y + 9)`.
Use only `PathF`, `DrawPath`, and the shared rounded 2.1-point stroke.

- [ ] **Step 3: Draw outbound physical-shipment proof**

Implement `DrawSellerPhysicalShipmentProof` with:

```csharp
canvas.DrawRoundedRectangle(x + 2, y + 8, 12, 9, 2);
```

Add a cab path from `(x + 14, y + 11)` to `(x + 21, y + 17)`, wheels centered
at `(x + 7, y + 19)` and `(x + 18, y + 19)`, plus an outgoing arrow above the
vehicle from `(x + 5, y + 4)` to `(x + 15, y + 4)` with its arrowhead at
`(x + 15, y + 4)`.

- [ ] **Step 4: Draw payout-record proof**

Implement `DrawSellerPayoutProof` with a rounded record from
`(x + 3, y + 2)` sized `17` by `19`, horizontal record lines, and a lower-right
check from `(x + 12, y + 15)` through `(x + 15, y + 18)` to
`(x + 21, y + 11)`. Do not draw a currency symbol, coin, banknote, or wallet.

- [ ] **Step 5: Run full verification**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj -f net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64 --no-restore
```

Expected: the full suite has zero failures and the iOS build has zero errors.

- [ ] **Step 6: Install and visually verify**

Install the built app on seller simulator
`FBE4866E-8265-4264-8053-23A4828AC85C`, open:

```text
toklong://transaction/fa10209e-d0c9-48d1-9ad5-1614b5842618
```

Verify:

- step one uses document-check;
- step two uses outbound delivery;
- step three uses payout-record-check;
- the current incomplete step is teal only when `ProgressActiveStep` identifies
  it;
- labels remain centered below their tokens;
- buyer and shipping progress visuals remain unchanged.

- [ ] **Step 7: Review the diff before any implementation commit**

Run:

```bash
git diff -- src/Toklong.Mobile/Core/TransactionProgressStep.cs src/Toklong.Mobile/Core/AppTransaction.cs src/Toklong.Mobile/Controls/TransactionProgressView.xaml.cs tests/Toklong.Mobile.Core.Tests/TransactionPresentationTests.cs
```

Because the main worktree already contains user changes, do not commit these
files until the exact diff has been reviewed and the user chooses the branch
completion option.
