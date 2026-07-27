# Buyer Create-Offer Cost Summary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show a server-calculated Buyer Protection preview in a sticky TOKLONG-styled summary bar and B1 bottom sheet while a buyer creates an offer.

**Architecture:** Add one authenticated read-only Mobile API endpoint backed by the existing `IPaymentFeePolicy`. The native client requests a debounced preview, rejects stale responses, and presents integer-satang values through a small reusable core model; offer creation and checkout remain independently authoritative.

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, .NET MAUI XAML, C#, xUnit, integer-satang money.

## Global Constraints

- Do not duplicate marginal tiers or use floating-point money arithmetic in Mobile.
- Use existing `App.xaml` colors, typography, spacing, and 44-point touch-target resources.
- The bar appears only after a valid matching server response.
- Physical copy says `ยอดก่อนค่าจัดส่ง`; digital copy says `ยอดเมื่อผู้ขายตอบรับ`.
- Both summary surfaces state that no payment has been collected.
- Preview creates no transaction, payment, notification, snapshot, acceptance, or audit event.
- Existing offer creation, seller acceptance, checkout, and pricing validation remain authoritative.

---

## File Structure

- `src/Toklong.Api/Api/MobileApi.cs`: authenticated preview route and response DTO.
- `src/Toklong.Mobile/Core/BuyerCostPreview.cs`: reusable integer-money presentation model and stale-response tracker.
- `src/Toklong.Mobile/Core/ITransactionService.cs`: preview-service contract.
- `src/Toklong.Mobile/Services/ApiTransactionService.cs`: authenticated API adapter.
- `src/Toklong.Mobile/ViewModels/CreateOfferViewModel.cs`: debounce, cancellation, visibility, and sheet commands.
- `src/Toklong.Mobile/Pages/CreateOfferPage.xaml`: sticky bar and B1 sheet using current design tokens.
- `src/Toklong.Mobile/Pages/CreateOfferPage.xaml.cs`: lifecycle cancellation, platform back, and focus movement.
- `tests/Toklong.Api.Tests/Api/MobilePricingPreviewApiTests.cs`: endpoint authentication and tier-boundary integration tests.
- `tests/Toklong.Mobile.Core.Tests/BuyerCostPreviewTests.cs`: checked integer totals, copy, and stale-response tests.
- `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`: XAML design-token, touch-target, and content assertions.
- `docs/05_ACCEPTANCE_TESTS.md`: executable acceptance scenarios for create-offer preview.

### Task 1: Authenticated server pricing preview

**Files:**
- Create: `tests/Toklong.Api.Tests/Api/MobilePricingPreviewApiTests.cs`
- Modify: `src/Toklong.Api/Api/MobileApi.cs`

**Interfaces:**
- Consumes: `IPaymentFeePolicy.GetDisclosure(long itemPriceSatang)`
- Produces: `GET /api/mobile/pricing/buyer-protection?itemPriceSatang={long}` returning `MobileBuyerProtectionPreviewResponse`

- [ ] **Step 1: Write failing authenticated API tests**

Create an integration test using `MobileApiFactory` and the existing OTP signup
flow. Assert unauthenticated requests return `401`, then assert these exact
integer-satang results:

```csharp
[Theory]
[InlineData(100_000, 5_900)]
[InlineData(500_000, 20_000)]
[InlineData(1_500_000, 55_000)]
[InlineData(3_000_000, 100_000)]
public async Task Preview_returns_server_calculated_fee(
    long itemPriceSatang,
    long expectedFeeSatang)
{
    using var client = factory.CreateClient();
    client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue(
            "Bearer",
            await RegisterBuyerAsync(client));

    var result = await client.GetFromJsonAsync<PreviewResponse>(
        $"/api/mobile/pricing/buyer-protection?itemPriceSatang={itemPriceSatang}");

    Assert.NotNull(result);
    Assert.Equal(itemPriceSatang, result.ItemPriceSatang);
    Assert.Equal(expectedFeeSatang, result.BuyerProtectionFeeSatang);
    Assert.Equal(
        checked(itemPriceSatang + expectedFeeSatang),
        result.TotalBeforeShippingSatang);
    Assert.Equal("THB", result.Currency);
    Assert.Equal("buyer-protection-v2", result.FeePolicyVersion);
}
```

Add rejection assertions for `99_999` and `3_000_001`.

- [ ] **Step 2: Run the focused tests and verify failure**

Run:

```bash
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj \
  --filter FullyQualifiedName~MobilePricingPreviewApiTests
```

Expected: `404 Not Found` for the new route.

- [ ] **Step 3: Add the minimal authenticated endpoint**

Map the route inside the existing authenticated group:

```csharp
authenticated.MapGet(
    "/pricing/buyer-protection",
    GetBuyerProtectionPreview);
```

Add a handler that requires a buyer identity and delegates all calculation:

```csharp
private static IResult GetBuyerProtectionPreview(
    long itemPriceSatang,
    ClaimsPrincipal principal,
    IPaymentFeePolicy feePolicy)
{
    _ = PartyIds.From(principal).BuyerId
        ?? throw new DomainException(
            "บัญชีนี้ยังไม่มีโปรไฟล์ผู้ซื้อ กรุณาสมัครสมาชิก");
    var fees = feePolicy.GetDisclosure(itemPriceSatang);
    return Results.Ok(new MobileBuyerProtectionPreviewResponse(
        itemPriceSatang,
        fees.BuyerProtectionFeeSatang,
        fees.PlatformFeeSatang,
        fees.SellerExpectedNetSatang,
        checked(itemPriceSatang + fees.BuyerProtectionFeeSatang),
        "THB",
        fees.PolicyVersion));
}
```

Define the response record beside the existing Mobile API records.

- [ ] **Step 4: Run API tests**

Run the focused test command from Step 2, then:

```bash
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj
```

Expected: all API tests pass.

- [ ] **Step 5: Commit the server slice**

```bash
git add src/Toklong.Api/Api/MobileApi.cs \
  tests/Toklong.Api.Tests/Api/MobilePricingPreviewApiTests.cs
git commit -m "feat: expose buyer protection preview"
```

### Task 2: Reusable mobile cost-preview model and API adapter

**Files:**
- Create: `src/Toklong.Mobile/Core/BuyerCostPreview.cs`
- Create: `tests/Toklong.Mobile.Core.Tests/BuyerCostPreviewTests.cs`
- Modify: `src/Toklong.Mobile/Core/ITransactionService.cs`
- Modify: `src/Toklong.Mobile/Services/ApiTransactionService.cs`

**Interfaces:**
- Consumes: `MobileBuyerProtectionPreviewResponse` JSON fields from Task 1.
- Produces: `Task<BuyerCostPreview> GetBuyerCostPreviewAsync(long itemPriceSatang, CancellationToken cancellationToken = default)`.

- [ ] **Step 1: Write failing mobile-core tests**

Test the exact display model:

```csharp
var preview = new BuyerCostPreview(
    1_000_000,
    37_500,
    0,
    1_000_000,
    1_037_500,
    "THB",
    "buyer-protection-v2");

Assert.Equal("฿10,000.00", preview.ItemPriceText);
Assert.Equal("฿375.00", preview.BuyerProtectionFeeText);
Assert.Equal("฿10,375.00", preview.TotalBeforeShippingText);
Assert.Equal("ยอดก่อนค่าจัดส่ง", preview.SummaryLabel(true));
Assert.Equal("ยอดเมื่อผู้ขายตอบรับ", preview.SummaryLabel(false));
Assert.Equal("รอผู้ขายเลือก", preview.ShippingText(true));
Assert.Equal("ไม่มีค่าจัดส่ง", preview.ShippingText(false));
```

Test that constructing a record whose supplied total differs from
`itemPriceSatang + buyerProtectionFeeSatang` throws, and test
`BuyerCostPreviewRequestTracker` accepts only its latest version.

- [ ] **Step 2: Run mobile-core tests and verify failure**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter FullyQualifiedName~BuyerCostPreviewTests
```

Expected: compilation failure because the types do not exist.

- [ ] **Step 3: Implement the model and tracker**

Create `BuyerCostPreview` with integer-satang validation, formatted properties
through `MoneyFormatter`, and physical/digital label methods. Create
`BuyerCostPreviewRequestTracker`:

```csharp
public sealed class BuyerCostPreviewRequestTracker
{
    private long currentVersion;
    public long Begin() => Interlocked.Increment(ref currentVersion);
    public void Invalidate() => Interlocked.Increment(ref currentVersion);
    public bool IsCurrent(long version) =>
        Interlocked.Read(ref currentVersion) == version;
}
```

- [ ] **Step 4: Add the service contract and HTTP adapter**

Add to `ITransactionService`:

```csharp
Task<BuyerCostPreview> GetBuyerCostPreviewAsync(
    long itemPriceSatang,
    CancellationToken cancellationToken = default);
```

Implement in `ApiTransactionService` with an authenticated GET request and
`ReadFromJsonAsync<BuyerCostPreview>`.

- [ ] **Step 5: Run mobile-core tests**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj
```

Expected: all mobile-core tests pass.

- [ ] **Step 6: Commit the reusable mobile boundary**

```bash
git add src/Toklong.Mobile/Core/BuyerCostPreview.cs \
  src/Toklong.Mobile/Core/ITransactionService.cs \
  src/Toklong.Mobile/Services/ApiTransactionService.cs \
  tests/Toklong.Mobile.Core.Tests/BuyerCostPreviewTests.cs
git commit -m "feat: add mobile buyer cost preview model"
```

### Task 3: Sticky summary and B1 bottom sheet

**Files:**
- Modify: `src/Toklong.Mobile/ViewModels/CreateOfferViewModel.cs`
- Modify: `src/Toklong.Mobile/Pages/CreateOfferPage.xaml`
- Modify: `src/Toklong.Mobile/Pages/CreateOfferPage.xaml.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`

**Interfaces:**
- Consumes: `ITransactionService.GetBuyerCostPreviewAsync` and `BuyerCostPreviewRequestTracker`.
- Produces: `HasCostPreview`, cost text bindings, `IsCostPreviewSheetOpen`, `OpenCostPreviewCommand`, `CloseCostPreviewCommand`, and `CancelCostPreview()`.

- [ ] **Step 1: Write failing XAML consistency tests**

Add a test that locates:

```csharp
var summary = createOffer.Descendants(Maui + "Border")
    .Single(x => AttributeValue(x, "AutomationId") ==
        "BuyerCostPreviewSummary");
var sheet = createOffer.Descendants(Maui + "Grid")
    .Single(x => AttributeValue(x, "AutomationId") ==
        "BuyerCostPreviewSheet");
```

Assert:

- summary visibility binds to `HasCostPreview`;
- the summary uses `BrandBlueDeep`, a minimum height of at least 64, and the
  texts `ยอดก่อนค่าจัดส่ง`, `ดูรายละเอียด`, and
  `ยังไม่มีการเรียกเก็บเงิน`;
- the sheet visibility binds to `IsCostPreviewSheetOpen`;
- the sheet uses existing `Ink`, `Muted`, `Line`, `BrandBlueDeep`,
  `RefinedHelperText`, and `#730F172A`;
- the sheet contains separate item, shipping, protection, and total rows;
- the close button is at least 44 by 44;
- a conditional spacer reserves at least 96 points below the form.

- [ ] **Step 2: Run the focused layout test and verify failure**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter FullyQualifiedName~CreateOfferShowsServerPricedCostPreview
```

Expected: failure because the automation IDs do not exist.

- [ ] **Step 3: Add debounced view-model state**

In `AmountBaht` setter, invalidate and cancel the previous request. Parse using
the existing decimal parser, require 1,000–30,000 THB with at most two decimal
places, delay 350 ms, and request integer satang from the service. Apply only
when the tracker version and current parsed satang still match.

Expose:

```csharp
public bool HasCostPreview => costPreview is not null;
public string CostItemPriceText => costPreview?.ItemPriceText ?? "";
public string CostProtectionFeeText =>
    costPreview?.BuyerProtectionFeeText ?? "";
public string CostTotalText =>
    costPreview?.TotalBeforeShippingText ?? "";
public string CostSummaryLabel =>
    costPreview?.SummaryLabel(IsPhysical) ?? "";
public string CostShippingText =>
    costPreview?.ShippingText(IsPhysical) ?? "";
public bool IsCostPreviewSheetOpen { get; private set; }
public ICommand OpenCostPreviewCommand { get; }
public ICommand CloseCostPreviewCommand { get; }
```

Changing fulfillment type refreshes the summary label and shipping copy.
Clearing or invalidating price hides the preview and closes the sheet.
`CancelCostPreview()` cancels and invalidates outstanding work.

- [ ] **Step 4: Implement the approved XAML**

Keep the current form intact. Add:

- a conditional 104-point spacer after `ReviewQuickDealButton`;
- a bottom-aligned `BuyerCostPreviewSummary` border with the existing blue
  tokens and 12–20 point type roles;
- a `BuyerCostPreviewSheet` overlay matching existing sheet radius, scrim,
  spacing, font weights, and close-button dimensions;
- one tap recognizer on the summary and one on the scrim;
- semantic descriptions that include amount, missing shipping, and no-charge
  status.

- [ ] **Step 5: Add platform back, focus, and lifecycle cancellation**

In page code-behind:

- cancel preview work in `OnDisappearing`;
- close the preview sheet first in `OnBackButtonPressed`;
- react to `IsCostPreviewSheetOpen` changes and move focus to the named close
  button or back to the named summary border through `Dispatcher.Dispatch`.

- [ ] **Step 6: Run focused and complete mobile tests**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj \
  -f net10.0-ios -c Debug \
  -p:RuntimeIdentifier=iossimulator-arm64 --no-restore
```

Expected: tests pass and iOS simulator build has zero errors. Existing
third-party package warnings may remain unchanged.

- [ ] **Step 7: Commit the approved UI**

```bash
git add src/Toklong.Mobile/ViewModels/CreateOfferViewModel.cs \
  src/Toklong.Mobile/Pages/CreateOfferPage.xaml \
  src/Toklong.Mobile/Pages/CreateOfferPage.xaml.cs \
  tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs
git commit -m "feat: show buyer cost preview while creating offer"
```

### Task 4: Acceptance documentation and full verification

**Files:**
- Modify: `docs/05_ACCEPTANCE_TESTS.md`

**Interfaces:**
- Consumes: completed server and mobile behavior from Tasks 1–3.
- Produces: documented acceptance criteria and final verification evidence.

- [ ] **Step 1: Add acceptance scenario A0.0.4.3**

Document valid-price visibility, server ownership of pricing, physical/digital
copy, the B1 sheet, no-charge copy, stale-response rejection, and the rule that
preview creates no transaction or financial state.

- [ ] **Step 2: Run all required automated checks**

```bash
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj
dotnet test tests/Toklong.Crm.Tests/Toklong.Crm.Tests.csproj
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj
dotnet build src/Toklong.Web/Toklong.Web.csproj --no-restore
dotnet build src/Toklong.Api/Toklong.Api.csproj --no-restore
dotnet build src/Toklong.Crm/Toklong.Crm.csproj --no-restore
dotnet build src/Toklong.Worker/Toklong.Worker.csproj --no-restore
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj \
  -f net10.0-ios -c Debug \
  -p:RuntimeIdentifier=iossimulator-arm64 --no-restore
git diff --check
```

- [ ] **Step 3: Inspect the Local simulator**

Run the existing Stripe Test Mode API on port 5181, install the fresh iOS
simulator build, enter `10,000.00`, and verify:

- the sticky bar appears after debounce;
- it shows `฿10,375.00`;
- the form and review action remain reachable;
- B1 opens and closes through close, scrim, and platform back;
- font sizes, weights, and colors match surrounding TOKLONG controls;
- clearing the price removes the preview.

- [ ] **Step 4: Commit the acceptance documentation**

```bash
git add docs/05_ACCEPTANCE_TESTS.md
git commit -m "docs: cover buyer cost preview acceptance"
```
