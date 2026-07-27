# Review-Sheet Cost Summary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redesign Create Offer to the approved mobile layout and make
`ตรวจข้อมูลก่อนส่ง` the only place that displays the server-priced Buyer
Protection breakdown.

**Architecture:** Keep the authenticated pricing endpoint, reusable `BuyerCostPreview`, and integer-satang API adapter unchanged. Replace background debounce with one cancellable request initiated by the review action; open the existing review sheet only after the response still matches the current validated item price.

**Tech Stack:** .NET 10, .NET MAUI XAML, C#, xUnit, integer-satang money.

## Global Constraints

- The server remains the only owner of fee tiers and calculation.
- The review sheet opens only after a matching server response succeeds.
- Reviewing price creates no transaction, snapshot, acceptance, notification, payment, refund, payout, or audit event.
- Physical copy uses `ยอดก่อนค่าจัดส่ง` and `รอผู้ขายเลือก`.
- Digital copy uses `ยอดเมื่อผู้ขายตอบรับ` and `ไม่มีค่าจัดส่ง`.
- The cost section states `ยังไม่ตัดเงินในขั้นตอนนี้`.
- Remove `BuyerCostPreviewSummary`, `BuyerCostPreviewSheet`, `BuyerCostPreviewFormSpacer`, and `กำหนดส่งสินค้า`.
- Preserve the existing review summary, condition selection, defect input, and final submit behavior.
- Use the approved header, essential-first form order, borderless/backgroundless
  price section, and progressive-disclosure layout.
- Use Medium 500 for labels and Regular 400 for descriptions and values.
- Preserve every existing behavior and automation/semantic contract.

---

## File Structure

- `src/Toklong.Mobile/ViewModels/CreateOfferViewModel.cs`: request fresh pricing from the review action, cancellation, matching-price guard, and retryable error state.
- `src/Toklong.Mobile/Pages/CreateOfferPage.xaml`: move the cost breakdown into `QuickDealReviewSheet` and remove the three obsolete price/deadline surfaces.
- `src/Toklong.Mobile/Pages/CreateOfferPage.xaml.cs`: remove focus handling for the deleted price sheet and cancel only active review pricing on navigation.
- `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`: enforce the one-sheet layout and absence of obsolete cards.
- `docs/05_ACCEPTANCE_TESTS.md`: describe the review-only pricing behavior.

### Task 1: Lock the review-only layout with a failing test

**Files:**
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`

**Interfaces:**
- Consumes: `QuickDealReviewSheet` from `CreateOfferPage.xaml`.
- Produces: layout contract for `ReviewCostSummary`, current cost bindings, and removed automation IDs/copy.

- [ ] **Step 1: Replace the old standalone-preview layout test**

Replace `CreateOfferShowsAccessibleBuyerCostPreviewAfterValidPrice` with:

```csharp
[Fact]
public void CreateOfferReviewContainsTheOnlyBuyerCostBreakdown()
{
    var createOffer = Load(
        "Ui",
        "Pages",
        "CreateOfferPage.xaml");
    var reviewSheet = createOffer
        .Descendants(Maui + "Grid")
        .Single(grid =>
            AttributeValue(grid, "AutomationId") ==
            "QuickDealReviewSheet");
    var costSummary = reviewSheet
        .Descendants(Maui + "Border")
        .Single(border =>
            AttributeValue(border, "AutomationId") ==
            "ReviewCostSummary");

    Assert.DoesNotContain(
        createOffer.Descendants(),
        element =>
            AttributeValue(element, "AutomationId") is
                "BuyerCostPreviewSummary" or
                "BuyerCostPreviewSheet" or
                "BuyerCostPreviewFormSpacer");
    Assert.Contains(
        costSummary.Descendants(Maui + "Label"),
        label =>
            AttributeValue(label, "Text") ==
            "{Binding CostProtectionFeeText}");
    Assert.Contains(
        costSummary.Descendants(Maui + "Label"),
        label =>
            AttributeValue(label, "Text") ==
            "{Binding CostShippingText}");
    Assert.Contains(
        costSummary.Descendants(Maui + "Label"),
        label =>
            AttributeValue(label, "Text") ==
            "{Binding CostTotalText}");
    Assert.Contains(
        costSummary.Descendants(Maui + "Label"),
        label =>
            AttributeValue(label, "Text") ==
            "ยังไม่ตัดเงินในขั้นตอนนี้");
    Assert.DoesNotContain(
        reviewSheet.Descendants(Maui + "Label"),
        label =>
            AttributeValue(label, "Text") is
                "กำหนดส่งสินค้า" or
                "ผู้ขายต้องส่งภายใน 3 วันหลังระบบยืนยันยอดชำระ");
}
```

Also assert that `ReviewQuickDealButton` binds to `ReviewCommand` and has an
`IsReviewPricing` trigger which disables the button and changes its text to
`กำลังคำนวณค่าใช้จ่าย...`.

Add a separate visual-contract test that verifies:

- the custom header contains `สร้างข้อเสนอ`,
  `ส่งให้ผู้ขายตรวจและตอบรับ`, and two progress segments;
- the price section has no background or outer border and the amount input
  retains `RefinedAmountBorder`;
- the form-label style is Medium rather than Bold and helper text is Regular;
- the essential fields precede the optional/progressive-disclosure section;
- the page hides the native Shell navigation bar.

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --no-restore --nologo \
  --filter FullyQualifiedName~CreateOfferReviewContainsTheOnlyBuyerCostBreakdown
```

Expected: failure because `ReviewCostSummary` does not exist and the obsolete
price surfaces are still present.

### Task 2: Make review fetch fresh pricing and own the cost UI

**Files:**
- Modify: `src/Toklong.Mobile/ViewModels/CreateOfferViewModel.cs`
- Modify: `src/Toklong.Mobile/Pages/CreateOfferPage.xaml`
- Modify: `src/Toklong.Mobile/Pages/CreateOfferPage.xaml.cs`
- Test: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`

**Interfaces:**
- Consumes: `ITransactionService.GetBuyerCostPreviewAsync(long, CancellationToken)` and `BuyerCostPreviewRequestTracker`.
- Produces: `IsReviewPricing`, asynchronous `ReviewCommand`, existing cost text properties, and `CancelReviewPricing()`.

- [ ] **Step 1: Replace background price loading with review-action loading**

In `CreateOfferViewModel`:

- remove `isCostPreviewSheetOpen`, `costPreviewCancellation`,
  `IsCostPreviewSheetOpen`, `OpenCostPreviewCommand`,
  `CloseCostPreviewCommand`, `ScheduleCostPreview`,
  `LoadCostPreviewAsync`, `ResumeCostPreview`, and their open/close helpers;
- add `CancellationTokenSource? reviewPricingCancellation` and
  `bool isReviewPricing`;
- keep `BuyerCostPreview`, its formatted properties, and the request tracker;
- make the amount setter invalidate any preview without calling the API:

```csharp
public string AmountBaht
{
    get => amountBaht;
    set
    {
        if (!SetProperty(ref amountBaht, value ?? ""))
            return;
        InvalidateReviewPricing();
    }
}
```

Expose:

```csharp
public bool IsReviewPricing
{
    get => isReviewPricing;
    private set => SetProperty(ref isReviewPricing, value);
}

public ICommand ReviewCommand =>
    new AsyncCommand(OpenReviewAsync);
```

Implement review loading:

```csharp
private async Task OpenReviewAsync()
{
    Message = "";
    if (!ValidateQuickDeal(out _, out var amount))
        return;

    var itemPriceSatang = checked((long)(amount * 100m));
    InvalidateReviewPricing();
    var requestVersion = costPreviewTracker.Begin();
    var cancellation = new CancellationTokenSource();
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
            !TryGetPreviewPriceSatang(out var currentPriceSatang) ||
            currentPriceSatang != itemPriceSatang)
            return;

        CostPreview = preview;
        OnPropertyChanged(nameof(ReviewSummary));
        OnPropertyChanged(nameof(FormattedReviewAmount));
        OnPropertyChanged(nameof(ReviewDeliveryText));
        IsReviewSheetOpen = true;
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
            reviewPricingCancellation.Dispose();
            reviewPricingCancellation = null;
            IsReviewPricing = false;
        }
    }
}
```

`InvalidateReviewPricing()` must cancel/dispose the active source, invalidate
the tracker, clear `CostPreview`, and close the review when form values change.
`CloseReview()` must call it after closing. Changing fulfillment must invalidate
the preview after raising its ordinary UI properties.

Expose `CancelReviewPricing()` for page lifecycle cancellation without showing
an error.

- [ ] **Step 2: Move the existing price markup into the review sheet**

In `CreateOfferPage.xaml`:

- delete `BuyerCostPreviewFormSpacer`;
- delete the complete `BuyerCostPreviewSummary` border;
- delete the complete `BuyerCostPreviewSheet` grid;
- add a `Border AutomationId="ReviewCostSummary"` immediately after the
  agreement-summary border inside `QuickDealReviewSheet`;
- reuse the existing price rows and bindings:
  `CostItemPriceText`, `CostProtectionFeeText`, `CostShippingText`,
  `CostSummaryLabel`, and `CostTotalText`;
- keep the pale-blue Buyer Protection row and no-charge disclosure;
- remove the `กำหนดส่งสินค้า` border completely.

Update `ReviewQuickDealButton` with:

```xml
<DataTrigger
    TargetType="Button"
    Binding="{Binding IsReviewPricing}"
    Value="True">
    <Setter Property="IsEnabled" Value="False" />
    <Setter Property="Text" Value="กำลังคำนวณค่าใช้จ่าย..." />
</DataTrigger>
```

- [ ] **Step 2.1: Apply the approved Create Offer visual hierarchy**

In `CreateOfferPage.xaml` and shared resources:

- add the custom rounded header and progress indicator;
- reorder seller phone, product name, price, and delivery address above
  secondary actions;
- keep the price group transparent and borderless while retaining one amount
  input border;
- render fulfillment, optional photo, optional details, and AI assistance as
  secondary actions below the essential fields;
- set form labels to Medium and descriptions/values to Regular;
- retain all bindings, commands, validation, semantic descriptions, and
  automation IDs.

- [ ] **Step 3: Remove deleted-sheet focus code**

In `CreateOfferPage.xaml.cs`:

- remove the `PropertyChanged` subscription that references
  `IsCostPreviewSheetOpen`, `CostPreviewCloseButton`, `AmountEntry`, and
  `CostPreviewSummaryBar`;
- remove the cost-sheet branch from `OnBackButtonPressed`;
- remove `viewModel.ResumeCostPreview()` from `OnAppearing`;
- replace `viewModel.CancelCostPreview()` with
  `viewModel.CancelReviewPricing()` in `OnDisappearing`.

- [ ] **Step 4: Run focused test and iOS build**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --no-restore --nologo \
  --filter FullyQualifiedName~CreateOfferReviewContainsTheOnlyBuyerCostBreakdown
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj \
  -f net10.0-ios -r iossimulator-arm64 --no-restore --nologo
```

Expected: the focused test passes and the iOS build completes with zero errors.
Existing third-party NU1608 warnings may remain.

- [ ] **Step 5: Commit the behavior**

```bash
git add -- \
  src/Toklong.Mobile/ViewModels/CreateOfferViewModel.cs \
  src/Toklong.Mobile/Pages/CreateOfferPage.xaml \
  src/Toklong.Mobile/Pages/CreateOfferPage.xaml.cs \
  tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs
git commit -m "fix: show buyer costs in offer review"
```

### Task 3: Acceptance documentation and verification

**Files:**
- Modify: `docs/05_ACCEPTANCE_TESTS.md`

**Interfaces:**
- Consumes: completed review-only pricing behavior.
- Produces: acceptance criteria and final verification evidence.

- [ ] **Step 1: Update acceptance scenario A0.0.4.3**

Replace sticky-summary and separate-detail-sheet expectations with:

- pricing is requested when `ตรวจข้อมูลก่อนส่ง` is selected;
- review remains closed until the exact latest server preview succeeds;
- the review sheet is the only price-breakdown surface;
- the shipment deadline card is absent;
- no-charge, physical/digital copy, and no-financial-write rules remain.

- [ ] **Step 2: Run all required checks**

```bash
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj --no-restore --nologo
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj --no-restore --nologo
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj --no-restore --nologo
dotnet test tests/Toklong.Crm.Tests/Toklong.Crm.Tests.csproj --no-restore --nologo
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore --nologo
dotnet build src/Toklong.Web/Toklong.Web.csproj --no-restore --nologo
dotnet build src/Toklong.Api/Toklong.Api.csproj --no-restore --nologo
dotnet build src/Toklong.Crm/Toklong.Crm.csproj --no-restore --nologo
dotnet build src/Toklong.Worker/Toklong.Worker.csproj --no-restore --nologo
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj \
  -f net10.0-ios -r iossimulator-arm64 --no-restore --nologo
git diff --check
```

- [ ] **Step 3: Inspect the local simulator**

With the Stripe Test Mode API ready on port 5181, install the fresh simulator
build. Enter `10,000`, tap `ตรวจข้อมูลก่อนส่ง`, and verify:

- no sticky blue price bar appears on the form;
- one review sheet opens with item price `฿10,000`, Buyer Protection `฿375`,
  shipping `รอผู้ขายเลือก`, and total `฿10,375`;
- `ยังไม่ตัดเงินในขั้นตอนนี้` is visible;
- no `กำหนดส่งสินค้า` card appears;
- the condition selector and both final actions remain reachable.

- [ ] **Step 4: Commit documentation and push**

```bash
git add -- docs/05_ACCEPTANCE_TESTS.md
git commit -m "docs: cover review-only buyer pricing"
git push origin main
```
