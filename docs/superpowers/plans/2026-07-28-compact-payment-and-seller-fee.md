# Compact Payment and Seller Fee Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove buyer-protection amounts from seller-facing mobile views and replace the duplicated accepted-offer payment card with one compact buyer payment action attached to the exact cost summary.

**Architecture:** Keep all API contracts, monetary snapshots, and transaction transitions unchanged. Add one presentation-only buyer-role flag to `AppTransaction`, then make focused XAML and seller view-model reductions. Protect the layout with XML artifact tests and existing behavior tests before updating the UX and acceptance documentation.

**Tech Stack:** .NET 10, C# records, .NET MAUI XAML, xUnit, LINQ to XML, iOS Simulator build.

## Global Constraints

- Buyer-protection fee calculation and payer do not change.
- Money remains integer satang plus ISO currency; no floating-point arithmetic.
- Buyer offer review and the accepted-offer pre-payment breakdown continue to show the exact buyer-protection fee.
- Seller-facing views do not show the buyer-protection amount or a reconstructable buyer total; they continue to show item price, applicable shipping, and exact expected seller net.
- The accepted-offer buyer state shows the locked delivery address once.
- Payment controls keep one primary action and remain gated by the existing required confirmation.
- Client action never marks payment successful; provider confirmation remains authoritative.
- No API, immutable snapshot, authorization, webhook, dispute, refund, payout, or domain-transition behavior changes.
- Consumer copy does not introduce internal terms such as webhook, state machine, settlement, hash, or provider-confirmed.

---

## File Structure

- `src/Toklong.Mobile/Core/AppTransaction.cs`
  - Produces explicit buyer/seller presentation flags for XAML role visibility.
- `src/Toklong.Mobile/Pages/SellerOfferPage.xaml`
  - Removes the buyer-protection row and buyer-total row from seller acceptance.
- `src/Toklong.Mobile/ViewModels/SellerOfferViewModel.cs`
  - Removes presentation properties that become unused after seller UI reduction.
- `src/Toklong.Mobile/Pages/TransactionDetailPage.xaml`
  - Separates buyer cost disclosure from seller payout disclosure, retains one address, and places payment confirmation/action inline.
- `tests/Toklong.Mobile.Core.Tests/TransactionPresentationTests.cs`
  - Behavior-tests buyer/seller role flags.
- `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`
  - Artifact-tests seller fee privacy, exact buyer disclosure, compact payment structure, address uniqueness, copy, and semantics.
- `docs/02_UI_UX_AND_CONTENT_SPEC.md`
  - Records the final seller and buyer presentation rules.
- `docs/05_ACCEPTANCE_TESTS.md`
  - Adds acceptance criteria for fee visibility and non-duplicated checkout content.

---

### Task 1: Add explicit buyer-role presentation

**Files:**
- Modify: `src/Toklong.Mobile/Core/AppTransaction.cs:214`
- Test: `tests/Toklong.Mobile.Core.Tests/TransactionPresentationTests.cs`

**Interfaces:**
- Consumes: `AppTransaction.Role : AppTransactionRole`.
- Produces: `AppTransaction.IsBuyerRole : bool`.
- Preserves: `AppTransaction.IsSellerRole : bool`.

- [ ] **Step 1: Write the failing role-visibility test**

Add this test beside `RoleLabelsStayShort`:

```csharp
[Fact]
public void RoleVisibilityFlagsAreMutuallyExclusive()
{
    var buyer = CreateItem(null);
    var seller = buyer with { Role = AppTransactionRole.Seller };

    Assert.True(buyer.IsBuyerRole);
    Assert.False(buyer.IsSellerRole);
    Assert.False(seller.IsBuyerRole);
    Assert.True(seller.IsSellerRole);
}
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --no-restore -p:NuGetAudit=false \
  --filter FullyQualifiedName~RoleVisibilityFlagsAreMutuallyExclusive
```

Expected: compile failure because `AppTransaction` has no `IsBuyerRole`.

- [ ] **Step 3: Add the minimal presentation property**

In `AppTransaction`, place the role flags together:

```csharp
public bool IsBuyerRole => Role == AppTransactionRole.Buyer;

public bool IsSellerRole => Role == AppTransactionRole.Seller;
```

- [ ] **Step 4: Run the focused and complete Mobile Core suites**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --no-restore -p:NuGetAudit=false \
  --filter FullyQualifiedName~RoleVisibilityFlagsAreMutuallyExclusive
```

Expected: 1 passing test.

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --no-restore -p:NuGetAudit=false
```

Expected: all Mobile Core tests pass.

- [ ] **Step 5: Commit the role flag**

```bash
git add src/Toklong.Mobile/Core/AppTransaction.cs \
  tests/Toklong.Mobile.Core.Tests/TransactionPresentationTests.cs
git commit -m "feat: expose buyer transaction presentation role"
```

---

### Task 2: Remove buyer-protection disclosure from seller views

**Files:**
- Modify: `src/Toklong.Mobile/Pages/SellerOfferPage.xaml:50-79,240-270`
- Modify: `src/Toklong.Mobile/ViewModels/SellerOfferViewModel.cs:35-48,230-250,590-620`
- Modify: `src/Toklong.Mobile/Pages/TransactionDetailPage.xaml:214-290`
- Test: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`

**Interfaces:**
- Consumes: `AppTransaction.IsBuyerRole`, `AppTransaction.IsSellerRole`, `AppTransaction.ItemPriceText`, `AppTransaction.ShippingFeeText`, `AppTransaction.FeeText`, `AppTransaction.FormattedAmount`, and `AppTransaction.SellerNetText`.
- Produces: Seller acceptance with item/shipping/net only; buyer transaction disclosure with item/protection/shipping/total.
- Preserves: `SellerOfferInvitation.BuyerProtectionFeeSatang` and acceptance request values for server integrity checks.

- [ ] **Step 1: Write failing seller-privacy artifact tests**

Add these tests to `UiLayoutConsistencyTests`:

```csharp
[Fact]
public void SellerOffer_HidesBuyerProtectionAndBuyerTotal()
{
    var sellerOffer = Load("Ui", "Pages", "SellerOfferPage.xaml");
    var labelTexts = sellerOffer
        .Descendants(Maui + "Label")
        .Select(label => AttributeValue(label, "Text"))
        .ToArray();

    Assert.DoesNotContain("ค่าคุ้มครองที่ผู้ซื้อจ่าย", labelTexts);
    Assert.DoesNotContain("{Binding FeeText}", labelTexts);
    Assert.DoesNotContain("ยอดที่ผู้ซื้อชำระ", labelTexts);
    Assert.DoesNotContain("{Binding BuyerTotalText}", labelTexts);
    Assert.Contains("ยอดที่คาดว่าจะได้รับ", labelTexts);
    Assert.Contains("{Binding NetText}", labelTexts);
    Assert.Contains("ค่าจัดส่งที่ผู้ซื้อจ่าย", labelTexts);
}

[Fact]
public void TransactionDetail_ShowsProtectionAndTotalOnlyToBuyer()
{
    var detail = Load("Ui", "Pages", "TransactionDetailPage.xaml");
    var buyerCost = detail
        .Descendants(Maui + "VerticalStackLayout")
        .Single(stack =>
            AttributeValue(stack, "AutomationId") ==
                "BuyerCostDisclosure");

    Assert.Equal(
        "{Binding Transaction.IsBuyerRole}",
        AttributeValue(buyerCost, "IsVisible"));
    Assert.Contains(
        buyerCost.Descendants(Maui + "Label"),
        label =>
            AttributeValue(label, "Text") ==
                "{Binding Transaction.ItemPriceText}");
    Assert.Contains(
        buyerCost.Descendants(Maui + "Label"),
        label =>
            AttributeValue(label, "Text") ==
                "ค่าคุ้มครองผู้ซื้อ");
    Assert.Contains(
        buyerCost.Descendants(Maui + "Label"),
        label =>
            AttributeValue(label, "Text") ==
                "{Binding Transaction.FeeText}");
    Assert.Contains(
        buyerCost.Descendants(Maui + "Label"),
        label =>
            AttributeValue(label, "Text") ==
                "{Binding Transaction.FormattedAmount}");

    var sellerPayout = detail
        .Descendants(Maui + "VerticalStackLayout")
        .Single(stack =>
            AttributeValue(stack, "AutomationId") ==
                "SellerPayoutDisclosure");
    Assert.Equal(
        "{Binding Transaction.IsSellerRole}",
        AttributeValue(sellerPayout, "IsVisible"));
    Assert.Contains(
        sellerPayout.Descendants(Maui + "Label"),
        label =>
            AttributeValue(label, "Text") ==
                "{Binding Transaction.ItemPriceText}");
    Assert.Contains(
        sellerPayout.Descendants(Maui + "Label"),
        label =>
            AttributeValue(label, "Text") ==
                "{Binding Transaction.SellerNetText}");
    Assert.DoesNotContain(
        sellerPayout.Descendants(Maui + "Label"),
        label =>
            AttributeValue(label, "Text") ==
                "{Binding Transaction.FeeText}");
}
```

- [ ] **Step 2: Run the new tests and verify RED**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --no-restore -p:NuGetAudit=false \
  --filter "FullyQualifiedName~SellerOffer_HidesBuyerProtectionAndBuyerTotal|FullyQualifiedName~TransactionDetail_ShowsProtectionAndTotalOnlyToBuyer"
```

Expected: both tests fail because seller labels still exist and the named disclosures do not.

- [ ] **Step 3: Remove seller-only fee and total rows**

In `SellerOfferPage.xaml`:

- delete the grid containing `ค่าคุ้มครองที่ผู้ซื้อจ่าย` and `{Binding FeeText}`;
- retain `ยอดที่คาดว่าจะได้รับ` with `{Binding NetText}`;
- retain `ราคาสินค้า` and `ค่าจัดส่งที่ผู้ซื้อจ่าย` in the shipping quote section; and
- delete the grid containing `ยอดที่ผู้ซื้อชำระ` and `{Binding BuyerTotalText}`.

In `SellerOfferViewModel.cs`, delete:

```csharp
public string FeeText => invitation is null
    ? ""
    : MoneyFormatter.Format(
        invitation.BuyerProtectionFeeSatang,
        "THB");
```

Delete `BuyerTotalText` and only its `OnPropertyChanged` calls. Keep
`BuyerProtectionFeeSatang` in the acceptance request and keep
`ShippingFeeText`.

- [ ] **Step 4: Split buyer cost and seller payout disclosures**

In `TransactionDetailPage.xaml`:

1. Give the seller cost/net stack
   `AutomationId="SellerPayoutDisclosure"` and retain
   `IsVisible="{Binding Transaction.IsSellerRole}"`.
2. Put item price, optional shipping charge/service, and
   `ยอดที่คุณจะได้รับ` in that seller-only stack.
3. Do not include `Transaction.FeeText` or `Transaction.FormattedAmount` in
   the seller-only stack.
4. Build a separate buyer-only stack that contains item price, protection fee,
   optional shipping charge/service, and exact total:

```xml
<VerticalStackLayout
    AutomationId="BuyerCostDisclosure"
    IsVisible="{Binding Transaction.IsBuyerRole}"
    Spacing="7">
    <Grid ColumnDefinitions="*,Auto">
        <Label
            FontSize="13"
            Text="ราคาสินค้า"
            TextColor="{StaticResource Muted}" />
        <Label
            Grid.Column="1"
            FontSize="13"
            Text="{Binding Transaction.ItemPriceText}" />
    </Grid>
    <Grid ColumnDefinitions="*,Auto">
        <Label
            FontSize="13"
            Text="ค่าคุ้มครองผู้ซื้อ"
            TextColor="{StaticResource Muted}" />
        <Label
            Grid.Column="1"
            FontSize="13"
            Text="{Binding Transaction.FeeText}" />
    </Grid>
    <Grid
        IsVisible="{Binding Transaction.HasShippingFee}"
        ColumnDefinitions="*,Auto">
        <Label
            FontSize="13"
            Text="ค่าจัดส่ง"
            TextColor="{StaticResource Muted}" />
        <Label
            Grid.Column="1"
            FontSize="13"
            Text="{Binding Transaction.ShippingFeeText}" />
    </Grid>
    <Grid ColumnDefinitions="*,Auto">
        <Label
            FontAttributes="Bold"
            FontSize="14"
            Text="ยอดชำระทั้งหมด" />
        <Label
            Grid.Column="1"
            FontAttributes="Bold"
            FontSize="14"
            Text="{Binding Transaction.FormattedAmount}"
            TextColor="{StaticResource BrandBlueDeep}" />
    </Grid>
</VerticalStackLayout>
```

Add the carrier/service row inside each role stack only when it is applicable.
This keeps digital buyer disclosure complete even when
`HasShippingFee == false`. The seller still sees item price, optional shipping
charge/service, and `SellerNetText`, but not `FeeText` or
`FormattedAmount`.

- [ ] **Step 5: Run the focused tests and full Mobile Core suite**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --no-restore -p:NuGetAudit=false \
  --filter "FullyQualifiedName~SellerOffer_HidesBuyerProtectionAndBuyerTotal|FullyQualifiedName~TransactionDetail_ShowsProtectionAndTotalOnlyToBuyer"
```

Expected: 2 passing tests.

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --no-restore -p:NuGetAudit=false
```

Expected: all Mobile Core tests pass.

- [ ] **Step 6: Compile the iOS XAML**

Run:

```bash
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj \
  -p:TargetFrameworks=net10.0-ios -f net10.0-ios \
  -r iossimulator-arm64 --no-restore -p:NuGetAudit=false
```

Expected: build succeeds with zero errors.

- [ ] **Step 7: Commit seller disclosure changes**

```bash
git add src/Toklong.Mobile/Pages/SellerOfferPage.xaml \
  src/Toklong.Mobile/ViewModels/SellerOfferViewModel.cs \
  src/Toklong.Mobile/Pages/TransactionDetailPage.xaml \
  tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs
git commit -m "feat: hide buyer protection cost from sellers"
```

---

### Task 3: Compact the accepted-offer buyer payment action

**Files:**
- Modify: `src/Toklong.Mobile/Pages/TransactionDetailPage.xaml:150-330,400-472`
- Test: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`

**Interfaces:**
- Consumes: `TransactionDetailViewModel.IsPaymentAction`, `AcceptedTerms`, `PrimaryActionCommand`, `Transaction.FormattedAmount`, and `Transaction.DeliveryAddressText`.
- Produces: `BuyerPaymentControls` as one inline action section; one delivery-address binding in the page.
- Preserves: `ExecutePrimaryActionAsync` confirmation gate and Stripe PaymentSheet behavior.

- [ ] **Step 1: Write the failing compact-payment artifact test**

Add this test to `UiLayoutConsistencyTests`:

```csharp
[Fact]
public void BuyerPayment_IsInlineAndRendersTheLockedAddressOnce()
{
    var detail = Load("Ui", "Pages", "TransactionDetailPage.xaml");
    var labels = detail
        .Descendants(Maui + "Label")
        .Select(label => AttributeValue(label, "Text"))
        .ToArray();
    var payment = detail
        .Descendants(Maui + "VerticalStackLayout")
        .Single(stack =>
            AttributeValue(stack, "AutomationId") ==
                "BuyerPaymentControls");

    Assert.Equal(
        "{Binding IsPaymentAction}",
        AttributeValue(payment, "IsVisible"));
    Assert.DoesNotContain("เช็กให้ครบก่อนจ่าย", labels);
    Assert.DoesNotContain(
        "ระบบใช้อีเมลจากบัญชีของคุณส่งใบเสร็จและขั้นตอนคืนเงิน",
        labels);
    Assert.DoesNotContain("ที่อยู่จัดส่งที่ล็อกกับดีล", labels);
    Assert.DoesNotContain(
        "ที่อยู่นี้เลือกไว้ตั้งแต่สร้างดีลและแก้ในขั้นชำระไม่ได้ หากไม่ถูกต้องให้สร้างข้อเสนอใหม่",
        labels);
    Assert.Single(
        detail.Descendants(Maui + "Label"),
        label =>
            AttributeValue(label, "Text") ==
                "{Binding Transaction.DeliveryAddressText}");
    Assert.Contains(
        payment.Descendants(Maui + "CheckBox"),
        checkBox =>
            AttributeValue(checkBox, "IsChecked") ==
                "{Binding AcceptedTerms}" &&
            AttributeValue(
                checkBox,
                "SemanticProperties.Description") ==
                "ยืนยันว่าได้ตรวจรายละเอียดและเงื่อนไขแล้ว");
    Assert.Contains(
        payment.Descendants(Maui + "Button"),
        button =>
            AttributeValue(button, "Command") ==
                "{Binding PrimaryActionCommand}" &&
            AttributeValue(button, "Text") ==
                "{Binding Transaction.FormattedAmount, StringFormat='ชำระ {0}'}" &&
            AttributeValue(
                button,
                "SemanticProperties.Description") ==
                "{Binding Transaction.FormattedAmount, StringFormat='เปิดหน้าจ่ายเงินยอด {0}'}");
}
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --no-restore -p:NuGetAudit=false \
  --filter FullyQualifiedName~BuyerPayment_IsInlineAndRendersTheLockedAddressOnce
```

Expected: failure because the old card copy and duplicate address still exist
and `BuyerPaymentControls` is absent.

- [ ] **Step 3: Reorder the agreement summary for one address and one action flow**

Inside the existing expandable agreement summary in
`TransactionDetailPage.xaml`, use this reading order:

1. additional agreement details, condition, and defects;
2. fulfillment method, delivery region, and the single full delivery address;
3. shared physical item price, shipping charge, and carrier;
4. buyer-only protection fee and exact total;
5. seller-only exact net amount; and
6. buyer payment controls.

Do not add a second address or an address editor. Retain the current
`HasDeliveryAddress`, `HasDeliveryRegion`, and `HasShippingFee` visibility
bindings.

- [ ] **Step 4: Move confirmation and payment directly below the buyer total**

Delete the complete outer `Border` bound to `IsPaymentAction`, including the
shield, heading, receipt-email helper, repeated address, immutable-address
helper, and Stripe helper.

Place this stack after the buyer cost disclosure:

```xml
<VerticalStackLayout
    AutomationId="BuyerPaymentControls"
    IsVisible="{Binding IsPaymentAction}"
    Spacing="{StaticResource SpacingSm}">
    <Grid ColumnDefinitions="Auto,*" ColumnSpacing="9">
        <CheckBox
            VerticalOptions="Start"
            MinimumWidthRequest="{StaticResource CompactControlMinimumHeight}"
            MinimumHeightRequest="{StaticResource CompactControlMinimumHeight}"
            Color="{StaticResource BrandBlue}"
            IsChecked="{Binding AcceptedTerms}"
            SemanticProperties.Description="ยืนยันว่าได้ตรวจรายละเอียดและเงื่อนไขแล้ว" />
        <Label
            Grid.Column="1"
            VerticalOptions="Center"
            FontSize="13"
            LineBreakMode="WordWrap"
            Text="ฉันตรวจสินค้า ราคา กำหนดส่ง และเวลาแจ้งปัญหาแล้ว" />
    </Grid>
    <Button
        Style="{StaticResource RefinedPrimaryButton}"
        BackgroundColor="{Binding Transaction.RoleColor}"
        Command="{Binding PrimaryActionCommand}"
        SemanticProperties.Description="{Binding Transaction.FormattedAmount, StringFormat='เปิดหน้าจ่ายเงินยอด {0}'}"
        Text="{Binding Transaction.FormattedAmount, StringFormat='ชำระ {0}'}" />
</VerticalStackLayout>
```

Do not wrap this stack in another `Border`; it belongs to the same continuous
surface as the exact buyer total.

- [ ] **Step 5: Run focused layout and existing payment behavior tests**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --no-restore -p:NuGetAudit=false \
  --filter "FullyQualifiedName~BuyerPayment_IsInlineAndRendersTheLockedAddressOnce|FullyQualifiedName~SellerAcceptedOfferRequiresBuyerPaymentAction"
```

Expected: both tests pass.

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --no-restore -p:NuGetAudit=false
```

Expected: all Mobile Core tests pass, including changed-page accessibility and
resource checks.

- [ ] **Step 6: Compile and inspect the iOS layout**

Run:

```bash
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj \
  -p:TargetFrameworks=net10.0-ios -f net10.0-ios \
  -r iossimulator-arm64 --no-restore -p:NuGetAudit=false
```

Expected: build succeeds with zero errors.

Install the resulting app on the buyer simulator and inspect a
`SellerAcceptedAwaitingPayment` transaction at default and large text sizes.
Verify one address, no empty card gap, one confirmation, and one payment
button with the exact total.

- [ ] **Step 7: Commit compact payment layout**

```bash
git add src/Toklong.Mobile/Pages/TransactionDetailPage.xaml \
  tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs
git commit -m "feat: compact buyer payment review"
```

---

### Task 4: Update product documentation and run full verification

**Files:**
- Modify: `docs/02_UI_UX_AND_CONTENT_SPEC.md:180-215,475-510`
- Modify: `docs/05_ACCEPTANCE_TESTS.md:410-465`

**Interfaces:**
- Consumes: the implemented seller privacy and buyer payment layout.
- Produces: normative UX and acceptance coverage matching the app.

- [ ] **Step 1: Update the UX specification**

In Scene 2, replace the seller fee/buyer-total wording with:

```markdown
- The same offer as read-only, its exact response deadline, item price,
  destination province/postal code, fixed fulfillment rule, applicable
  shipping charge, and expected seller net. Do not show the
  buyer-protection amount or buyer total to the seller.
```

In Scene 3, replace the `เช็กให้ครบก่อนจ่าย` card requirement with:

```markdown
- Show the complete locked delivery address once in the agreement details.
- Show the exact item price, buyer-protection fee, shipping charge, and total
  in one buyer-only breakdown.
- Place the required confirmation and exact-total payment button directly
  below that breakdown, without a separate pre-payment card.
```

In Checkout, state that the full locked address is shown exactly once and that
the payment action must not repeat it.

- [ ] **Step 2: Add acceptance criteria**

Extend A0.1.1 with:

```markdown
**And** the accepted-offer buyer screen shows the locked full address exactly
once
**And** the buyer sees item price, buyer-protection fee, shipping charge, and
exact total before payment
**And** confirmation and the exact-total payment action follow that breakdown
without a separate pre-payment card
**And** seller offer and seller transaction views do not show the
buyer-protection amount or buyer total
**And** seller views still show applicable shipping information and exact
expected net payout.
```

- [ ] **Step 3: Run documentation and repository hygiene checks**

Run:

```bash
rg -n "เช็กให้ครบก่อนจ่าย|ค่าคุ้มครองที่ผู้ซื้อจ่าย|ยอดที่ผู้ซื้อชำระ" \
  src/Toklong.Mobile docs/02_UI_UX_AND_CONTENT_SPEC.md \
  docs/05_ACCEPTANCE_TESTS.md
```

Expected: no obsolete seller/payment-card copy in mobile XAML; documentation
mentions old copy only when explicitly stating it must not appear.

Run:

```bash
git diff --check
```

Expected: no whitespace errors.

- [ ] **Step 4: Run all test projects sequentially**

Run each command separately:

```bash
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj \
  --no-restore -p:NuGetAudit=false
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --no-restore -p:NuGetAudit=false
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj \
  --no-restore -p:NuGetAudit=false
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --no-restore -p:NuGetAudit=false
dotnet test tests/Toklong.Crm.Tests/Toklong.Crm.Tests.csproj \
  --no-restore -p:NuGetAudit=false
```

Expected: all projects pass. Run sequentially to avoid shared .NET output file
locks.

- [ ] **Step 5: Run the final iOS simulator build**

```bash
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj \
  -p:TargetFrameworks=net10.0-ios -f net10.0-ios \
  -r iossimulator-arm64 --no-restore -p:NuGetAudit=false
```

Expected: zero build errors. Record any pre-existing warnings separately.

- [ ] **Step 6: Commit docs and verified final state**

```bash
git add docs/02_UI_UX_AND_CONTENT_SPEC.md \
  docs/05_ACCEPTANCE_TESTS.md
git commit -m "docs: specify compact buyer payment review"
```

Run:

```bash
git status --short
git log --oneline -5
```

Expected: clean working tree and the role, seller disclosure, compact payment,
and documentation commits at the top of `main`.
