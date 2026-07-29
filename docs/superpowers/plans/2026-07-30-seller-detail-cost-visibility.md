# Seller Detail Cost Visibility Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove shipping fee, parcel insurance fee, and insured value from the seller transaction detail while retaining product price, shipping service, and seller net amount.

**Architecture:** Make a presentation-only change inside the existing seller-only `SellerPayoutDisclosure`. Keep all API fields, immutable transaction data, calculations, buyer disclosure, state transitions, and audit behavior unchanged.

**Tech Stack:** .NET 10, .NET MAUI XAML, xUnit, iOS Simulator.

## Global Constraints

- The buyer cost disclosure remains unchanged.
- The API response and immutable paid transaction snapshot remain unchanged.
- The seller disclosure keeps product price, shipping service, and seller net amount.
- Removed rows must not remain in the seller accessibility tree.

---

### Task 1: Remove Seller-Only Cost Rows

**Files:**
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`
- Modify: `src/Toklong.Mobile/Pages/TransactionDetailPage.xaml`

**Interfaces:**
- Consumes: `SellerPayoutDisclosure` and `BuyerCostDisclosure` automation identifiers.
- Produces: a seller disclosure without `ShippingFeeText`, `ParcelInsuranceFeeText`, or `ShippingDeclaredValueText`.

- [ ] **Step 1: Write the failing regression assertions**

Update `TransactionDetail_ShowsProtectionAndTotalOnlyToBuyer` after locating
`sellerPayout`:

```csharp
foreach (var sellerHiddenBinding in new[]
         {
             "{Binding Transaction.ShippingFeeText}",
             "{Binding Transaction.ParcelInsuranceFeeText}",
             "{Binding Transaction.ShippingDeclaredValueText}"
         })
{
    Assert.DoesNotContain(
        sellerPayout.Descendants(Maui + "Label"),
        label =>
            AttributeValue(label, "Text") ==
            sellerHiddenBinding);
}

foreach (var sellerVisibleBinding in new[]
         {
             "{Binding Transaction.ItemPriceText}",
             "{Binding Transaction.ShippingServiceText}",
             "{Binding Transaction.SellerNetText}"
         })
{
    Assert.Contains(
        sellerPayout.Descendants(Maui + "Label"),
        label =>
            AttributeValue(label, "Text") ==
            sellerVisibleBinding);
}
```

- [ ] **Step 2: Run the focused test and verify it fails**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore --filter TransactionDetail_ShowsProtectionAndTotalOnlyToBuyer
```

Expected: FAIL because the seller disclosure still contains the three removed
bindings.

- [ ] **Step 3: Remove only the three seller rows**

In `SellerPayoutDisclosure`, delete the complete `Grid` elements whose value
labels bind to:

```xml
{Binding Transaction.ShippingFeeText}
{Binding Transaction.ParcelInsuranceFeeText}
{Binding Transaction.ShippingDeclaredValueText}
```

Do not alter `BuyerCostDisclosure`, `ItemPriceText`,
`ShippingServiceText`, or `SellerNetText`.

- [ ] **Step 4: Run focused and full verification**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore --filter TransactionDetail_ShowsProtectionAndTotalOnlyToBuyer
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj -f net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64 --no-restore
```

Expected: focused test passes, full suite has zero failures, and the iOS build
has zero errors.

- [ ] **Step 5: Install and inspect the seller simulator**

Install the built app on the seller simulator, open transaction
`fa10209e-d0c9-48d1-9ad5-1614b5842618` through
`toklong://transaction/fa10209e-d0c9-48d1-9ad5-1614b5842618`, and confirm the
seller disclosure shows only product price, shipping service, and seller net
amount.

- [ ] **Step 6: Commit the implementation**

```bash
git add src/Toklong.Mobile/Pages/TransactionDetailPage.xaml tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs docs/superpowers/plans/2026-07-30-seller-detail-cost-visibility.md
git commit -m "fix: simplify seller transaction costs"
```
