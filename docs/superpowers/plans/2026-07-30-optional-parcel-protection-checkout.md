# Optional Parcel Protection Checkout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make parcel protection an optional Buyer-only checkout decision, defer the exact outbound SHIPPOP booking until that decision is durable, and create a PaymentIntent only after the matching booking succeeds.

**Architecture:** Seller acceptance freezes the parcel, carrier service, delivery fee, and one-hour Buyer payment deadline without booking a shipment. A Buyer-only parcel-protection annex records the election and internal price split, then queues the existing durable shipping operation; the worker revalidates the signed selection before any provider mutation and records the exact reservation. The payment application service remains the sole PaymentIntent entry point and rejects physical transactions until the matching booking is complete.

**Tech Stack:** .NET 9, C# 13, MediatR, EF Core/PostgreSQL, ASP.NET Core Minimal APIs, .NET MAUI/XAML, xUnit, Stripe PaymentSheet, SHIPPOP HTTP API.

## Global Constraints

- All monetary values are integer satang with ISO currency `THB`; do not use floating point.
- The TOKLONG parcel-protection service fee is exactly `1_500` satang per accepted optional-protection election.
- Normal Buyer copy uses `ความคุ้มครองพัสดุ`; it does not name SHIPPOP, expose provider package codes, show an uncovered amount, or use a green promotional callout.
- The choice surface shows one maximum coverage amount and one combined Buyer price only.
- The Buyer payment summary shows the combined parcel-protection price but not its maximum coverage amount.
- Seller API and UI surfaces expose neither parcel-protection price, provider cost, service fee, nor coverage limits.
- If item price is at or below included coverage, do not ask the Buyer and charge no optional-protection price.
- If an add-on is unavailable or the Buyer declines it, the Buyer may continue with included coverage and no optional-protection charge.
- The Buyer election is a Buyer-only checkout annex and does not require Seller re-acceptance.
- Persist the election, combined customer price, provider cost, TOKLONG service fee, included and selected limits, terms version, provider option reference, quote timestamps, and Buyer election timestamp.
- Persist a durable idempotent booking intent before calling SHIPPOP.
- Do not create a PaymentIntent until the exact unconfirmed outbound booking succeeds.
- If the protection price, limit, or terms changes before booking, invalidate the election and require explicit Buyer reconfirmation; never substitute silently.
- Quote or booking work does not extend `BuyerPaymentDeadlineAt`.
- Continue requiring parcel weight and all three dimensions until account-specific certification proves omission safe.
- SHIPPOP optional-protection capability remains disabled unless the certification suite proves its account-specific request, response, limits, pricing, and replay behavior.
- Do not claim or display an included coverage amount until that amount is certified for the selected account/service; an unknown limit is represented internally as zero and never described as free full coverage.
- No client request, redirect, slip, screenshot, or database assumption may mark payment successful.
- Every new domain mutation writes an immutable audit event and every API mutation enforces Buyer or Seller authorization explicitly.

---

### Task 1: Add the Buyer-only parcel-protection domain annex

**Files:**
- Create: `src/Toklong.Domain/Transactions/ParcelProtection.cs`
- Modify: `src/Toklong.Domain/Transactions/SaleTransaction.cs:10-220`
- Modify: `src/Toklong.Domain/Transactions/SaleTransaction.cs:1069-1580`
- Modify: `src/Toklong.Domain/Transactions/SaleTransaction.cs:3970-4043`
- Test: `tests/Toklong.Domain.Tests/Transactions/ShippingMoneyTests.cs`
- Test: `tests/Toklong.Domain.Tests/Transactions/SaleTransactionTests.cs`

**Interfaces:**
- Consumes: existing `SaleTransaction.AcceptBuyerOffer`, `SaleTransaction.QueueManagedShipment`, `TransactionTransitionService`, and integer-satang transaction totals.
- Produces:
  - `ParcelProtectionElectionStatus`
  - `ParcelProtectionSelection`
  - `SaleTransaction.RecordParcelProtectionElection`
  - `SaleTransaction.RecordParcelProtectionAvailabilityPresented`
  - `SaleTransaction.InvalidateParcelProtectionElection`
  - `SaleTransaction.ParcelProtectionBookingReady`

- [ ] **Step 1: Write failing domain tests for all election paths**

Add tests with these exact assertions:

```csharp
[Fact]
public void Included_coverage_does_not_charge_or_prompt()
{
    var transaction = AcceptedPhysicalOffer(priceSatang: 90_000);

    transaction.RecordParcelProtectionElection(
        buyerId: transaction.BuyerId!.Value,
        new ParcelProtectionSelection(
            ParcelProtectionElectionStatus.NotApplicable,
            CustomerPriceSatang: 0,
            ProviderCostSatang: 0,
            ToklongServiceFeeSatang: 0,
            IncludedCoverageLimitSatang: 100_000,
            SelectedCoverageLimitSatang: 100_000,
            TermsVersion: "parcel-protection-2026-07-30",
            ProviderOptionReference: null,
            QuotedAt: Start,
            ExpiresAt: Start.AddHours(1)),
        Start.AddMinutes(1));

    Assert.Equal(
        ParcelProtectionElectionStatus.NotApplicable,
        transaction.ParcelProtectionElection);
    Assert.Equal(0, transaction.ParcelInsuranceFeeSatang);
    Assert.Equal(
        transaction.PriceSatang +
        transaction.ShippingFeeSatang +
        transaction.BuyerProtectionFeeSatang,
        transaction.BuyerTotalSatang);
}

[Fact]
public void Accepted_add_on_stores_internal_split_and_combined_buyer_price()
{
    var transaction = AcceptedPhysicalOffer(priceSatang: 450_000);

    transaction.RecordParcelProtectionElection(
        transaction.BuyerId!.Value,
        new ParcelProtectionSelection(
            ParcelProtectionElectionStatus.Accepted,
            CustomerPriceSatang: 6_000,
            ProviderCostSatang: 4_500,
            ToklongServiceFeeSatang: 1_500,
            IncludedCoverageLimitSatang: 100_000,
            SelectedCoverageLimitSatang: 450_000,
            TermsVersion: "parcel-protection-2026-07-30",
            ProviderOptionReference: "protected-option",
            QuotedAt: Start,
            ExpiresAt: Start.AddHours(1)),
        Start.AddMinutes(1));

    Assert.Equal(6_000, transaction.ParcelInsuranceFeeSatang);
    Assert.Equal(4_500, transaction.ParcelProtectionProviderCostSatang);
    Assert.Equal(1_500, transaction.ParcelProtectionServiceFeeSatang);
    Assert.Equal(450_000, transaction.ParcelProtectionSelectedCoverageSatang);
    Assert.Equal(
        transaction.PriceSatang +
        transaction.ShippingFeeSatang +
        transaction.BuyerProtectionFeeSatang +
        6_000,
        transaction.BuyerTotalSatang);
    Assert.Equal(
        transaction.PriceSatang - transaction.PlatformFeeSatang,
        transaction.SellerExpectedNetSatang);
}

[Fact]
public void Declined_add_on_keeps_included_coverage_without_charge()
{
    var transaction = AcceptedPhysicalOffer(priceSatang: 450_000);

    transaction.RecordParcelProtectionElection(
        transaction.BuyerId!.Value,
        new ParcelProtectionSelection(
            ParcelProtectionElectionStatus.Declined,
            CustomerPriceSatang: 0,
            ProviderCostSatang: 0,
            ToklongServiceFeeSatang: 0,
            IncludedCoverageLimitSatang: 100_000,
            SelectedCoverageLimitSatang: 100_000,
            TermsVersion: "parcel-protection-2026-07-30",
            ProviderOptionReference: null,
            QuotedAt: Start,
            ExpiresAt: Start.AddHours(1)),
        Start.AddMinutes(1));

    Assert.Equal(0, transaction.ParcelInsuranceFeeSatang);
    Assert.Equal(100_000, transaction.ParcelProtectionSelectedCoverageSatang);
}

[Fact]
public void Seller_or_changed_terms_cannot_write_the_buyer_annex()
{
    var transaction = AcceptedPhysicalOffer(priceSatang: 450_000);
    var selection = AcceptedSelection();

    Assert.Throws<DomainException>(() =>
        transaction.RecordParcelProtectionElection(
            Guid.NewGuid(),
            selection,
            Start.AddMinutes(1)));

    transaction.RecordParcelProtectionElection(
        transaction.BuyerId!.Value,
        selection,
        Start.AddMinutes(1));

    Assert.Throws<DomainException>(() =>
        transaction.RecordParcelProtectionElection(
            transaction.BuyerId.Value,
            selection with
            {
                CustomerPriceSatang = 6_100
            },
            Start.AddMinutes(2)));
}
```

Also assert that `Accepted` rejects a customer price that is not exactly provider cost plus `1_500`, zero/negative limits, an option expiry after the payment deadline, and a selected limit below included coverage. Assert that `Declined`, `NotApplicable`, and `Unavailable` reject non-zero charges or a provider option reference. `Unavailable` alone may persist zero included/selected limits when SHIPPOP has not certified an included amount; consumer copy must not describe that zero as coverage.

- [ ] **Step 2: Run the focused domain tests and confirm red**

Run:

```bash
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj \
  --filter "FullyQualifiedName~ShippingMoneyTests|FullyQualifiedName~SaleTransactionTests" \
  --no-restore
```

Expected: compilation fails because `ParcelProtectionElectionStatus`, `ParcelProtectionSelection`, and the new aggregate methods do not exist.

- [ ] **Step 3: Add the annex types**

Create:

```csharp
namespace Toklong.Domain.Transactions;

public enum ParcelProtectionElectionStatus
{
    Pending,
    Accepted,
    Declined,
    NotApplicable,
    Unavailable,
    ReconfirmationRequired
}

public sealed record ParcelProtectionSelection(
    ParcelProtectionElectionStatus Election,
    long CustomerPriceSatang,
    long ProviderCostSatang,
    long ToklongServiceFeeSatang,
    long IncludedCoverageLimitSatang,
    long SelectedCoverageLimitSatang,
    string TermsVersion,
    string? ProviderOptionReference,
    DateTimeOffset QuotedAt,
    DateTimeOffset ExpiresAt);
```

- [ ] **Step 4: Persist annex values and enforce money, role, time, and immutability rules**

Add these properties to `SaleTransaction`:

```csharp
public ParcelProtectionElectionStatus
    ParcelProtectionElection { get; private set; } =
        ParcelProtectionElectionStatus.Pending;
public long ParcelProtectionProviderCostSatang { get; private set; }
public long ParcelProtectionServiceFeeSatang { get; private set; }
public long ParcelProtectionIncludedCoverageSatang { get; private set; }
public long ParcelProtectionSelectedCoverageSatang { get; private set; }
public string? ParcelProtectionTermsVersion { get; private set; }
public string? ParcelProtectionOptionReference { get; private set; }
public DateTimeOffset? ParcelProtectionQuotedAt { get; private set; }
public DateTimeOffset? ParcelProtectionExpiresAt { get; private set; }
public DateTimeOffset? ParcelProtectionBuyerElectedAt { get; private set; }
public bool ParcelProtectionBookingReady =>
    FulfillmentType == FulfillmentType.DigitalHandoff ||
    ShippingReservedAt.HasValue;
```

Implement `RecordParcelProtectionElection` so it:

```csharp
public void RecordParcelProtectionElection(
    Guid buyerId,
    ParcelProtectionSelection selection,
    DateTimeOffset now)
{
    if (BuyerId != buyerId)
        throw new DomainException(
            "บัญชีผู้ซื้อนี้ไม่มีสิทธิ์เลือกความคุ้มครองพัสดุ");
    if (State != TransactionState.SellerAcceptedAwaitingPayment)
        throw new DomainException(
            "รายการนี้ยังเลือกความคุ้มครองพัสดุไม่ได้");
    EnsureBuyerPaymentWindowOpen(now);
    if (ParcelProtectionBuyerElectedAt.HasValue &&
        ParcelProtectionElection !=
            ParcelProtectionElectionStatus.ReconfirmationRequired)
        throw new DomainException(
            "บันทึกตัวเลือกแล้ว หากต้องการเปลี่ยนให้เริ่มตรวจราคาใหม่");

    ValidateParcelProtectionSelection(selection, now);
    ParcelProtectionElection = selection.Election;
    ParcelInsuranceFeeSatang = selection.CustomerPriceSatang;
    ParcelProtectionProviderCostSatang =
        selection.ProviderCostSatang;
    ParcelProtectionServiceFeeSatang =
        selection.ToklongServiceFeeSatang;
    ParcelProtectionIncludedCoverageSatang =
        selection.IncludedCoverageLimitSatang;
    ParcelProtectionSelectedCoverageSatang =
        selection.SelectedCoverageLimitSatang;
    ParcelProtectionTermsVersion =
        Required(selection.TermsVersion, "เวอร์ชันเงื่อนไขความคุ้มครอง");
    ParcelProtectionOptionReference =
        CleanOptional(
            selection.ProviderOptionReference,
            160,
            "เลขอ้างอิงความคุ้มครอง");
    ParcelProtectionQuotedAt = selection.QuotedAt;
    ParcelProtectionExpiresAt = selection.ExpiresAt;
    ParcelProtectionBuyerElectedAt = now;
    BuyerTotalSatang = checked(
        PriceSatang +
        ShippingFeeSatang +
        BuyerProtectionFeeSatang +
        ParcelInsuranceFeeSatang);
    _auditEvents.Add(new AuditEvent(
        Guid.NewGuid(),
        Id,
        ActorRole.Buyer,
        buyerId.ToString("N"),
        State,
        State,
        $"parcel_protection.{selection.Election.ToString().ToLowerInvariant()}",
        now,
        Id.ToString("N"),
        $"parcel-protection-election:{Id:N}:{selection.ExpiresAt.ToUnixTimeSeconds()}",
        ParcelProtectionAuditMetadata()));
}
```

`ValidateParcelProtectionSelection` must enforce the Global Constraints exactly. Add `InvalidateParcelProtectionElection(string reasonCode, DateTimeOffset now)` that accepts only a non-empty sanitized reason, sets `ReconfirmationRequired`, clears all prices/reference/timestamps except included coverage, recalculates the total without optional protection, and writes `parcel_protection.reconfirmation_required`.

Add an idempotent
`RecordParcelProtectionAvailabilityPresented(Guid buyerId, bool addOnAvailable,
string idempotencyKey, DateTimeOffset now)` method. It writes
`parcel_protection.offered` or `parcel_protection.unavailable`, contains no
address/contact/provider credential, and returns without adding a second event
for the same key. Domain tests must assert this audit behavior and assert that
an election cannot mutate after `BeginCheckout` has created the paid snapshot.

Increment `AgreementSnapshotSchemaVersion` from `9` to `10`. Add the Buyer annex only to the final agreement snapshot created by `BeginCheckout`; do not add it to the Seller acceptance core snapshot. Preserve read compatibility for schema versions 8 and 9.

The method above records the first election. Task 7A adds the separate
cancel-and-rebook workflow for changing a persisted election before a
PaymentIntent exists; do not weaken this method to overwrite an active booking.

- [ ] **Step 5: Run domain tests and confirm green**

Run the same focused command from Step 2.

Expected: all selected tests pass, totals use integer satang, and existing paid snapshot integrity tests remain green.

- [ ] **Step 6: Commit**

```bash
git add src/Toklong.Domain/Transactions/ParcelProtection.cs \
  src/Toklong.Domain/Transactions/SaleTransaction.cs \
  tests/Toklong.Domain.Tests/Transactions/ShippingMoneyTests.cs \
  tests/Toklong.Domain.Tests/Transactions/SaleTransactionTests.cs
git commit -m "feat: add buyer parcel protection annex"
```

---

### Task 2: Allow an outbound managed shipment with or without optional protection

**Files:**
- Modify: `src/Toklong.Domain/Transactions/ManagedShipment.cs:24-186`
- Modify: `src/Toklong.Domain/Transactions/ShippingOperation.cs:5-225`
- Modify: `src/Toklong.Application/Features/Shipping/ManagedShippingOperationQueue.cs`
- Test: `tests/Toklong.Domain.Tests/Transactions/ManagedShipmentTests.cs`
- Test: `tests/Toklong.Application.Tests/Shipping/DurableShippingOperationProcessingTests.cs`

**Interfaces:**
- Consumes: `ParcelProtectionSelection` and existing booking fingerprints.
- Produces:
  - nullable `ManagedShipmentDraft.InsuranceCode`
  - `ShippingOperationStatus.Superseded`
  - `ShippingOperation.Supersede`
  - booking fingerprints that include election, provider cost, coverage limit, terms version, and option reference.

- [ ] **Step 1: Write failing managed-shipment and operation tests**

```csharp
[Fact]
public void Outbound_booking_can_use_included_coverage_without_optional_fee()
{
    var shipment = ManagedShipment.CreateOutbound(
        TransactionId,
        Draft(
            insuranceFeeSatang: 0,
            declaredValueSatang: 100_000,
            insuranceCode: null),
        Start);

    Assert.Equal(0, shipment.InsuranceFeeSatang);
    Assert.Equal(100_000, shipment.DeclaredValueSatang);
    Assert.Null(shipment.InsuranceCode);
}

[Fact]
public void Insurance_tuple_must_be_all_zero_or_fully_populated()
{
    Assert.Throws<DomainException>(() =>
        ManagedShipment.CreateOutbound(
            TransactionId,
            Draft(
                insuranceFeeSatang: 4_500,
                declaredValueSatang: 450_000,
                insuranceCode: null),
            Start));
}

[Fact]
public void Superseded_operation_cannot_be_claimed()
{
    var operation = QueuedOperation();
    operation.Claim("worker", Start, TimeSpan.FromMinutes(5));
    operation.Supersede(
        "worker",
        "parcel-protection-quote-changed",
        Start.AddSeconds(1));

    Assert.Equal(ShippingOperationStatus.Superseded, operation.Status);
    Assert.Throws<DomainException>(() =>
        operation.Claim(
            "worker",
            Start.AddMinutes(6),
            TimeSpan.FromMinutes(5)));
}
```

- [ ] **Step 2: Run focused tests and confirm red**

```bash
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj \
  --filter "FullyQualifiedName~ManagedShipmentTests" --no-restore
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter "FullyQualifiedName~DurableShippingOperationProcessingTests" \
  --no-restore
```

Expected: draft nullability and `Superseded` APIs do not compile.

- [ ] **Step 3: Relax only the optional-insurance tuple**

Change the draft signature:

```csharp
public sealed record ManagedShipmentDraft(
    string Provider,
    string OriginPrivateSnapshotReference,
    string DestinationPrivateSnapshotReference,
    string ParcelName,
    int WeightGrams,
    int WidthCentimeters,
    int LengthCentimeters,
    int HeightCentimeters,
    string CarrierCode,
    string ServiceCode,
    string ServiceName,
    long BaseShippingFeeSatang,
    long InsuranceFeeSatang,
    long DeclaredValueSatang,
    string? InsuranceCode,
    string QuoteReference,
    DateTimeOffset QuoteExpiresAt,
    string ParcelProtectionTermsVersion,
    string? ParcelProtectionOptionReference);
```

Store `InsuranceCode` as nullable. Require `BaseShippingFeeSatang > 0`. Accept either:

```csharp
var hasOptionalProtection =
    draft.InsuranceFeeSatang > 0 ||
    !string.IsNullOrWhiteSpace(draft.InsuranceCode);
if (draft.InsuranceFeeSatang < 0 ||
    draft.DeclaredValueSatang < 0 ||
    hasOptionalProtection &&
    (draft.InsuranceFeeSatang <= 0 ||
     draft.DeclaredValueSatang <= 0 ||
     string.IsNullOrWhiteSpace(draft.InsuranceCode)))
    throw new DomainException(
        "ข้อมูลความคุ้มครองพัสดุไม่ครบ");
```

Included coverage may have zero provider cost and no code while retaining a positive declared coverage limit. Add the protection terms/reference to `ManagedShipment` and to `ManagedShippingOperationQueue.BookingFingerprint`.

- [ ] **Step 4: Add the terminal superseded operation state**

```csharp
public enum ShippingOperationStatus
{
    Pending,
    Processing,
    RetryScheduled,
    OutcomeUnknown,
    Succeeded,
    NeedsReview,
    Superseded
}

public void Supersede(
    string workerId,
    string sanitizedReasonCode,
    DateTimeOffset now)
{
    EnsureProcessingLease(workerId, now);
    Status = ShippingOperationStatus.Superseded;
    LastSanitizedErrorCode = Required(
        sanitizedReasonCode,
        "reason code",
        100);
    CompletedAt = now;
    ClearLease();
    Version++;
}
```

Do not include `Superseded` in claimable or open-exception queries.

- [ ] **Step 5: Run focused tests and confirm green**

Run both commands from Step 2.

Expected: all selected tests pass and the fingerprint changes when any protection field changes.

- [ ] **Step 6: Commit**

```bash
git add src/Toklong.Domain/Transactions/ManagedShipment.cs \
  src/Toklong.Domain/Transactions/ShippingOperation.cs \
  src/Toklong.Application/Features/Shipping/ManagedShippingOperationQueue.cs \
  tests/Toklong.Domain.Tests/Transactions/ManagedShipmentTests.cs \
  tests/Toklong.Application.Tests/Shipping/DurableShippingOperationProcessingTests.cs
git commit -m "feat: support optional protection in shipping intents"
```

---

### Task 3: Persist the annex and migrate historical transactions safely

**Files:**
- Modify: `src/Toklong.Infrastructure/Persistence/ToklongDbContext.cs:100-175`
- Modify: `src/Toklong.Infrastructure/Persistence/ToklongDbContext.cs:606-678`
- Create: `src/Toklong.Infrastructure/Persistence/Migrations/20260730090000_OptionalParcelProtectionCheckout.cs`
- Create: `src/Toklong.Infrastructure/Persistence/Migrations/20260730090000_OptionalParcelProtectionCheckout.Designer.cs`
- Modify: `src/Toklong.Infrastructure/Persistence/Migrations/ToklongDbContextModelSnapshot.cs`
- Test: `tests/Toklong.Application.Tests/Persistence/OptionalParcelProtectionMigrationTests.cs`

**Interfaces:**
- Consumes: Task 1 aggregate properties and Task 2 nullable shipment fields.
- Produces: PostgreSQL columns and enum string mappings for the Buyer annex and superseded operation status.

- [ ] **Step 1: Write a failing migration compatibility test**

The test must insert one pre-migration row with `ParcelInsuranceFeeSatang = 4_500`, migrate forward, and assert:

```csharp
Assert.Equal(
    ParcelProtectionElectionStatus.Accepted,
    stored.ParcelProtectionElection);
Assert.Equal(4_500, stored.ParcelInsuranceFeeSatang);
Assert.Equal(4_500, stored.ParcelProtectionProviderCostSatang);
Assert.Equal(0, stored.ParcelProtectionServiceFeeSatang);
Assert.Equal("legacy-full-value-v1", stored.ParcelProtectionTermsVersion);
```

Also insert a row with zero legacy insurance and assert it becomes `Pending` without inventing a coverage limit.

- [ ] **Step 2: Run the persistence test and confirm red**

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter "FullyQualifiedName~OptionalParcelProtectionMigrationTests" \
  --no-restore
```

Expected: the migration and mapped columns do not exist.

- [ ] **Step 3: Add EF mappings and generate the migration**

Map:

```csharp
transaction.Property(x => x.ParcelProtectionElection)
    .HasConversion<string>()
    .HasMaxLength(32);
transaction.Property(x => x.ParcelProtectionTermsVersion)
    .HasMaxLength(80);
transaction.Property(x => x.ParcelProtectionOptionReference)
    .HasMaxLength(160);
```

Map `ManagedShipment.InsuranceCode` and `ManagedShipment.ParcelProtectionOptionReference` as nullable strings of maximum 80 and 160 characters, and map `ParcelProtectionTermsVersion` to maximum 80.

Generate with:

```bash
dotnet ef migrations add OptionalParcelProtectionCheckout \
  --project src/Toklong.Infrastructure/Toklong.Infrastructure.csproj \
  --startup-project src/Toklong.Api/Toklong.Api.csproj \
  --output-dir Persistence/Migrations
```

Rename the generated migration prefix to `20260730090000` only if EF generated a different local timestamp, updating the `[Migration]` attribute consistently.

- [ ] **Step 4: Add explicit historical backfill SQL**

The migration `Up` must execute:

```sql
UPDATE transactions
SET
  "ParcelProtectionElection" = 'Accepted',
  "ParcelProtectionProviderCostSatang" = "ParcelInsuranceFeeSatang",
  "ParcelProtectionServiceFeeSatang" = 0,
  "ParcelProtectionTermsVersion" = 'legacy-full-value-v1',
  "ParcelProtectionBuyerElectedAt" = "BuyerAcceptedAt"
WHERE "ParcelInsuranceFeeSatang" > 0;
```

Do not fabricate included/selected limits for legacy rows. The `Down` migration drops only the new columns and does not rewrite historical totals.

- [ ] **Step 5: Run migration and model tests**

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter "FullyQualifiedName~OptionalParcelProtectionMigrationTests" \
  --no-restore
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter "FullyQualifiedName~Persistence" --no-restore
```

Expected: both commands pass and EF reports no model/migration drift.

- [ ] **Step 6: Commit**

```bash
git add src/Toklong.Infrastructure/Persistence/ToklongDbContext.cs \
  src/Toklong.Infrastructure/Persistence/Migrations \
  tests/Toklong.Application.Tests/Persistence/OptionalParcelProtectionMigrationTests.cs
git commit -m "feat: persist parcel protection checkout annex"
```

---

### Task 4: Separate delivery quotes from optional-protection quotes

**Files:**
- Modify: `src/Toklong.Application/Abstractions/IShippingQuoteProvider.cs`
- Create: `src/Toklong.Application/Abstractions/IParcelProtectionQuoteProvider.cs`
- Create: `src/Toklong.Application/Pricing/ParcelProtectionPricingPolicy.cs`
- Modify: `src/Toklong.Infrastructure/Services/DevelopmentShippingQuoteProvider.cs`
- Modify: `src/Toklong.Infrastructure/Services/ShippopShippingProvider.cs:12-356`
- Modify: `src/Toklong.Infrastructure/Services/ShippopShippingProvider.cs:880-960`
- Modify: `src/Toklong.Infrastructure/DependencyInjection.cs:120-175`
- Modify: `src/Toklong.Infrastructure/ProductionConfigurationValidator.cs:74-155`
- Modify: `src/Toklong.Api/appsettings.json:79-135`
- Test: `tests/Toklong.Application.Tests/Shipping/ShippopShippingProviderTests.cs`
- Test: `tests/Toklong.Application.Tests/Security/ProductionConfigurationValidatorTests.cs`
- Test: `tests/Toklong.Application.Tests/Shipping/ProviderShipmentProcessingTests.cs`

**Interfaces:**
- Consumes: existing signed delivery quote and `ShipmentReservationRequest`.
- Produces:

```csharp
public sealed record ParcelProtectionQuoteRequest(
    ShippingQuoteRequest Shipment,
    string CarrierCode,
    string ServiceCode,
    string DeliveryQuoteReference,
    long ItemPriceSatang);

public sealed record ProviderParcelProtectionOption(
    string Provider,
    string OptionReference,
    long IncludedCoverageLimitSatang,
    long SelectedCoverageLimitSatang,
    long ProviderCostSatang,
    string TermsVersion,
    string InsuranceCode,
    DateTimeOffset QuotedAt,
    DateTimeOffset ExpiresAt);

public sealed record ParcelProtectionAvailability(
    long IncludedCoverageLimitSatang,
    ProviderParcelProtectionOption? AddOn,
    bool ProviderCapabilityCertified);

public interface IParcelProtectionQuoteProvider
{
    Task<ParcelProtectionAvailability> GetAvailabilityAsync(
        ParcelProtectionQuoteRequest request,
        CancellationToken cancellationToken);

    Task<ProviderParcelProtectionOption> ValidateOptionAsync(
        ParcelProtectionQuoteRequest request,
        string optionReference,
        CancellationToken cancellationToken);
}

public sealed class ParcelProtectionOptionChangedException(
    string sanitizedReasonCode)
    : InvalidOperationException(sanitizedReasonCode);
```

- [ ] **Step 1: Write failing provider-boundary tests**

Cover:

```csharp
[Fact]
public async Task Development_provider_returns_no_add_on_within_included_limit()
{
    var availability = await provider.GetAvailabilityAsync(
        ProtectionRequest(itemPriceSatang: 90_000),
        CancellationToken.None);

    Assert.Equal(100_000, availability.IncludedCoverageLimitSatang);
    Assert.Null(availability.AddOn);
}

[Fact]
public async Task Development_provider_returns_signed_add_on_above_limit()
{
    var availability = await provider.GetAvailabilityAsync(
        ProtectionRequest(itemPriceSatang: 450_000),
        CancellationToken.None);

    Assert.Equal(450_000, availability.AddOn!.SelectedCoverageLimitSatang);
    Assert.Equal(4_500, availability.AddOn.ProviderCostSatang);
}

[Fact]
public async Task Shippop_uncertified_protection_fails_closed_without_blocking_delivery()
{
    var availability = await shippop.GetAvailabilityAsync(
        ProtectionRequest(itemPriceSatang: 450_000),
        CancellationToken.None);

    Assert.False(availability.ProviderCapabilityCertified);
    Assert.Null(availability.AddOn);
}

[Fact]
public async Task Booking_without_add_on_does_not_require_insurance_capability()
{
    var reservation = await shippop.ReserveAsync(
        ReservationRequest(
            insuranceFeeSatang: 0,
            insuranceCode: null),
        CancellationToken.None);

    Assert.Equal(0, reservation.InsuranceFeeSatang);
}
```

Assert that forged, expired, wrong-service, wrong-parcel, and wrong-item-value option references fail. Assert the signed option binds provider cost, both limits, terms version, insurance code, parcel fingerprint, carrier/service, and expiry.

- [ ] **Step 2: Run focused provider tests and confirm red**

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter "FullyQualifiedName~ShippopShippingProviderTests|FullyQualifiedName~ProviderShipmentProcessingTests|FullyQualifiedName~ProductionConfigurationValidatorTests" \
  --no-restore
```

Expected: the new provider contract is missing and booking still requires full-value insurance.

- [ ] **Step 3: Add the pricing policy**

```csharp
public interface IParcelProtectionPricingPolicy
{
    const long ServiceFeeSatang = 1_500;

    ParcelProtectionPrice Price(
        long providerCostSatang);
}

public sealed record ParcelProtectionPrice(
    long ProviderCostSatang,
    long ToklongServiceFeeSatang,
    long CustomerPriceSatang);

public sealed class ParcelProtectionPricingPolicy
    : IParcelProtectionPricingPolicy
{
    public ParcelProtectionPrice Price(
        long providerCostSatang)
    {
        if (providerCostSatang <= 0)
            throw new DomainException(
                "ราคาความคุ้มครองจากผู้ให้บริการไม่ถูกต้อง");
        return new ParcelProtectionPrice(
            providerCostSatang,
            IParcelProtectionPricingPolicy.ServiceFeeSatang,
            checked(
                providerCostSatang +
                IParcelProtectionPricingPolicy.ServiceFeeSatang));
    }
}
```

Register it as a singleton.

- [ ] **Step 4: Implement deterministic Development behavior and SHIPPOP fail-closed behavior**

Development uses included coverage `100_000` satang, selected coverage equal to item price, provider cost `max(100, itemPriceSatang / 100)`, terms version `development-parcel-protection-v1`, and a signed/in-memory option reference bound to the complete request.

Add `IncludedCoverageSatang` and `OptionalProtectionEnabled` to each `ShippopServiceProfile`. `ShippopShippingProvider.GetAvailabilityAsync` returns the configured certified included limit and no add-on unless `OptionalProtectionEnabled`, `InsuranceEnabled`, and a non-empty certification reference are all true. The initial checked-in `appsettings.json` keeps `IncludedCoverageSatang: 0` and `OptionalProtectionEnabled: false` for every service, so normal UI makes no included-coverage claim until Task 10 records evidence.

Do not guess undocumented SHIPPOP insurance fields. The enabled branch must throw:

```csharp
throw new InvalidOperationException(
    "SHIPPOP optional parcel protection is not certified for this service profile.");
```

until the certification task replaces that guard with recorded, account-specific parsing.

`ValidateOptionAsync` throws
`ParcelProtectionOptionChangedException("parcel-protection-option-changed")`
only when a previously valid stored option expired or its provider price,
limits, or terms changed. Forged references, parcel/service mismatches, and
unsupported response contracts use their existing fail-closed validation path
and must not be presented as a normal Buyer price refresh.

- [ ] **Step 5: Make provider booking conditional on the actual selection**

Remove the unconditional `full-value insurance` capability check from `ReserveAsync`. Require `InsuranceEnabled` only when `request.Quote.InsuranceFeeSatang > 0` or an insurance code is present. Ensure `ShipmentPayload` omits optional insurance fields for included-only bookings and includes the exact provider cost, declared coverage limit, and code for accepted add-ons.

Production validation must require:

```csharp
if (profile.OptionalProtectionEnabled &&
    (!profile.InsuranceEnabled ||
     profile.IncludedCoverageSatang <= 0 ||
     profile.MaximumCoverageSatang <=
         profile.IncludedCoverageSatang))
    errors.Add(
        $"Shippop service {serviceCode} optional protection configuration is incomplete");
```

Remove the rules that every booking requires insurance and that every enabled service must cover the global maximum. Keep HTTPS, operation lookup, service allow-list, and certification-reference requirements.

- [ ] **Step 6: Run focused provider tests and confirm green**

Run the Step 2 command.

Expected: all tests pass; production delivery booking may be enabled with optional protection disabled, while no uncertified add-on can be sold.

- [ ] **Step 7: Commit**

```bash
git add src/Toklong.Application/Abstractions \
  src/Toklong.Application/Pricing/ParcelProtectionPricingPolicy.cs \
  src/Toklong.Infrastructure/Services/DevelopmentShippingQuoteProvider.cs \
  src/Toklong.Infrastructure/Services/ShippopShippingProvider.cs \
  src/Toklong.Infrastructure/DependencyInjection.cs \
  src/Toklong.Infrastructure/ProductionConfigurationValidator.cs \
  src/Toklong.Api/appsettings.json \
  tests/Toklong.Application.Tests/Shipping \
  tests/Toklong.Application.Tests/Security/ProductionConfigurationValidatorTests.cs
git commit -m "feat: separate delivery and parcel protection quotes"
```

---

### Task 5: Make Seller acceptance freeze delivery only and remove Seller-visible protection data

**Files:**
- Modify: `src/Toklong.Application/Features/Offers/RespondToBuyerOffer/RespondToBuyerOffer.cs:13-335`
- Modify: `src/Toklong.Application/Features/Shipping/GetShippingQuotes/GetShippingQuotes.cs`
- Modify: `src/Toklong.Api/Api/MobileApi.cs:780-890`
- Modify: `src/Toklong.Api/Api/MobileApi.cs:1380-1484`
- Modify: `src/Toklong.Api/Api/MobileApi.cs:1799-1916`
- Modify: `src/Toklong.Mobile/Core/ISellerOfferService.cs`
- Modify: `src/Toklong.Mobile/Services/ApiSellerOfferService.cs`
- Modify: `src/Toklong.Mobile/ViewModels/SellerOfferViewModel.cs`
- Modify: `src/Toklong.Mobile/Pages/SellerOfferPage.xaml`
- Test: `tests/Toklong.Application.Tests/Offers/BuyerOfferFlowTests.cs`
- Test: `tests/Toklong.Api.Tests/Api/MobileSellerOfferApiTests.cs`
- Test: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`

**Interfaces:**
- Consumes: base `ShippingQuoteOption` and existing Seller acceptance command.
- Produces: Seller selection containing only parcel, service quote reference, and disclosed shipping fee; accepted physical transaction with no managed shipment or booking operation yet.

- [ ] **Step 1: Write failing Seller-flow and privacy tests**

```csharp
[Fact]
public async Task Seller_acceptance_does_not_book_or_queue_outbound_shipping()
{
    var accepted = await handler.Handle(
        AcceptCommand(quoteReference),
        CancellationToken.None);
    var stored = await repository.GetByIdAsync(
        accepted.Id,
        CancellationToken.None);

    Assert.Equal(
        TransactionState.SellerAcceptedAwaitingPayment,
        stored!.State);
    Assert.Empty(stored.ManagedShipments);
    Assert.Empty(stored.ShippingOperations);
    Assert.Null(stored.ShippingPurchaseReference);
    Assert.Equal(0, stored.ParcelInsuranceFeeSatang);
}

[Fact]
public async Task Seller_quote_and_transaction_json_never_contain_protection_fields()
{
    var quoteJson = await GetSellerQuoteJsonAsync();
    var transactionJson = await GetSellerTransactionJsonAsync();

    Assert.DoesNotContain("InsuranceFee", quoteJson);
    Assert.DoesNotContain("DeclaredValue", quoteJson);
    Assert.DoesNotContain("InsuranceCode", quoteJson);
    Assert.DoesNotContain("ParcelProtection", transactionJson);
    Assert.DoesNotContain("ParcelInsurance", transactionJson);
    Assert.DoesNotContain("ShippingDeclaredValue", transactionJson);
}
```

Add a mobile layout assertion that `SellerOfferPage.xaml` contains no binding or label containing `Insurance`, `ประกัน`, `ความคุ้มครองพัสดุ`, or `DeclaredValue`.

- [ ] **Step 2: Run focused tests and confirm red**

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter "FullyQualifiedName~BuyerOfferFlowTests" --no-restore
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj \
  --filter "FullyQualifiedName~MobileSellerOfferApiTests" --no-restore
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~UiLayoutConsistencyTests" --no-restore
```

Expected: Seller acceptance queues `BookOutbound`, and Seller DTOs still carry insurance fields.

- [ ] **Step 3: Remove protection disclosures from Seller contracts**

Use:

```csharp
public sealed record SellerShippingSelectionInput(
    bool UseSavedOrigin,
    string? AddressLine,
    int? ProvinceId,
    int? DistrictId,
    int? SubdistrictId,
    bool RememberOrigin,
    int WeightGrams,
    int WidthCentimeters,
    int LengthCentimeters,
    int HeightCentimeters,
    string QuoteReference,
    long DisclosedShippingFeeSatang);
```

Make `MobileShippingQuoteResponse` end at `ExpiresAt`. Mirror the same shape in `MobileShippingQuote`, `SellerShippingSelection`, `ApiSellerOfferService`, and `SellerOfferViewModel`. Its display text is exactly carrier/service plus delivery fee.

- [ ] **Step 4: Defer managed booking**

Delete the SHIPPOP-specific `ManagedShipment.CreateOutbound` and `ShippingOperation.Queue` branch from `AcceptBuyerOfferHandler`. Always call `transaction.AcceptBuyerOffer`.

Update `ApplyAcceptedShippingQuote` so Seller acceptance:

```csharp
ShippingFeeSatang = shipping.FeeSatang;
ParcelInsuranceFeeSatang = 0;
ShippingDeclaredValueSatang = 0;
ShippingInsuranceCode = null;
BuyerTotalSatang = checked(
    PriceSatang +
    ShippingFeeSatang +
    BuyerProtectionFeeSatang);
```

Remove the full-value and provider-reservation checks. Retain address, parcel dimensions, carrier/service, signed quote, expiry, and deadline validation.

- [ ] **Step 5: Enforce role-shaped transaction serialization**

Change the final protection fields in `MobileTransactionResponse` to nullable with `JsonIgnoreCondition.WhenWritingNull`. Populate them only for Buyer role. Do not expose provider cost or service fee through this general transaction DTO for either role; those remain internal and appear only in the Buyer checkout-annex endpoint.

- [ ] **Step 6: Run focused tests and confirm green**

Run all three Step 2 commands.

Expected: Seller acceptance reaches `SellerAcceptedAwaitingPayment` immediately, no provider mutation exists, and Seller JSON/UI contains no protection data.

- [ ] **Step 7: Commit**

```bash
git add src/Toklong.Application/Features/Offers/RespondToBuyerOffer \
  src/Toklong.Application/Features/Shipping/GetShippingQuotes \
  src/Toklong.Api/Api/MobileApi.cs \
  src/Toklong.Mobile/Core/ISellerOfferService.cs \
  src/Toklong.Mobile/Services/ApiSellerOfferService.cs \
  src/Toklong.Mobile/ViewModels/SellerOfferViewModel.cs \
  src/Toklong.Mobile/Pages/SellerOfferPage.xaml \
  tests/Toklong.Application.Tests/Offers/BuyerOfferFlowTests.cs \
  tests/Toklong.Api.Tests/Api/MobileSellerOfferApiTests.cs \
  tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs
git commit -m "feat: defer shipment booking until buyer checkout"
```

---

### Task 6: Add Buyer checkout quote and election application services

**Files:**
- Create: `src/Toklong.Application/Features/Checkout/GetParcelProtection/GetParcelProtection.cs`
- Create: `src/Toklong.Application/Features/Checkout/PrepareParcelProtection/PrepareParcelProtection.cs`
- Create: `src/Toklong.Application/Features/Checkout/ChooseParcelProtection/ChooseParcelProtection.cs`
- Modify: `src/Toklong.Application/Transactions/TransactionView.cs`
- Test: `tests/Toklong.Application.Tests/Checkout/ParcelProtectionCheckoutTests.cs`

**Interfaces:**
- Consumes:
  - `IParcelProtectionQuoteProvider.GetAvailabilityAsync`
  - `IParcelProtectionPricingPolicy.Price`
  - `SaleTransaction.RecordParcelProtectionElection`
  - `ManagedShipment.CreateOutbound`
  - `ShippingOperation.Queue`
- Produces:

```csharp
public sealed record GetParcelProtectionQuery(
    Guid TransactionId,
    Guid BuyerId)
    : IRequest<BuyerParcelProtectionView>;

public sealed record PrepareParcelProtectionCommand(
    Guid TransactionId,
    Guid BuyerId,
    string IdempotencyKey)
    : IRequest<BuyerParcelProtectionView>;

public sealed record BuyerParcelProtectionView(
    bool RequiresChoice,
    bool AddOnAvailable,
    long IncludedCoverageLimitSatang,
    long? MaximumCoverageLimitSatang,
    long? CustomerPriceSatang,
    string? OptionReference,
    string TermsVersion,
    DateTimeOffset? ExpiresAt,
    string Election,
    bool BookingReady,
    bool ReconfirmationRequired);

public sealed record ChooseParcelProtectionCommand(
    Guid TransactionId,
    Guid BuyerId,
    bool AddProtection,
    string? OptionReference,
    long? DisclosedCustomerPriceSatang,
    string IdempotencyKey)
    : IRequest<ChooseParcelProtectionResult>;

public sealed record ChooseParcelProtectionResult(
    TransactionView Transaction,
    string BookingStatus);
```

- [ ] **Step 1: Write failing checkout application tests**

Cover these cases:

```csharp
[Fact]
public async Task Within_included_limit_queues_unprotected_booking_without_prompt()
{
    var view = await prepare.Handle(
        new PrepareParcelProtectionCommand(
            TransactionId,
            BuyerId,
            "prepare-included-coverage"),
        CancellationToken.None);
    Assert.False(view.RequiresChoice);

    var result = await command.Handle(
        Choose(
            addProtection: false,
            optionReference: null,
            disclosedCustomerPriceSatang: null),
        CancellationToken.None);

    Assert.Equal("preparing_shipping", result.BookingStatus);
    Assert.Single(stored.ManagedShipments);
    Assert.Single(stored.ShippingOperations);
    Assert.Equal(0, stored.ManagedShipments.Single().InsuranceFeeSatang);
}

[Fact]
public async Task Accepted_add_on_revalidates_price_and_queues_exact_booking()
{
    var result = await command.Handle(
        Choose(
            addProtection: true,
            optionReference: "protected-option",
            disclosedCustomerPriceSatang: 6_000),
        CancellationToken.None);

    var shipment = stored.ManagedShipments.Single();
    Assert.Equal(4_500, shipment.InsuranceFeeSatang);
    Assert.Equal(450_000, shipment.DeclaredValueSatang);
    Assert.Equal("DEV_FULL_VALUE", shipment.InsuranceCode);
    Assert.Equal(6_000, stored.ParcelInsuranceFeeSatang);
    Assert.Equal("preparing_shipping", result.BookingStatus);
}

[Fact]
public async Task Changed_price_does_not_queue_booking()
{
    await Assert.ThrowsAsync<DomainException>(() =>
        command.Handle(
            Choose(
                addProtection: true,
                optionReference: "protected-option",
                disclosedCustomerPriceSatang: 5_900),
            CancellationToken.None));

    Assert.Empty(stored.ManagedShipments);
    Assert.Empty(stored.ShippingOperations);
}
```

Also test Buyer authorization, digital transactions (`NotApplicable`, no shipment operation), unavailable add-on, explicit decline, expired payment deadline, expired quote, duplicate idempotency key, a second different choice before invalidation, and quote/terms/limit mismatch.
Assert that prepare writes exactly one `parcel_protection.offered` or
`parcel_protection.unavailable` audit event for the same idempotency key.

- [ ] **Step 2: Run checkout tests and confirm red**

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter "FullyQualifiedName~ParcelProtectionCheckoutTests" \
  --no-restore
```

Expected: handlers and response types do not exist.

- [ ] **Step 3: Implement the read model without mutations**

`PrepareParcelProtectionHandler` must:

1. Load transaction and require `transaction.BuyerId == request.BuyerId`.
2. Return `NotApplicable` immediately for digital fulfillment.
3. Require `SellerAcceptedAwaitingPayment`.
4. Rebuild `ShippingQuoteRequest` from the accepted immutable transaction fields.
5. Call `GetAvailabilityAsync`.
6. Set `RequiresChoice = PriceSatang > IncludedCoverageLimitSatang && AddOn is not null`.
7. Apply the pricing policy only to a non-null add-on.
8. Call `RecordParcelProtectionAvailabilityPresented` to write one idempotent
   `parcel_protection.offered` event when a choice is
   returned, or `parcel_protection.unavailable` when the item exceeds included
   coverage but no certified add-on exists.
9. Save only the audit event; never record an election, queue an operation, or
   create a provider booking.

`GetParcelProtectionHandler` reads only the persisted annex and booking/change
status for app resume and polling. It performs no provider call and no write.

- [ ] **Step 4: Implement the election and durable booking intent**

`ChooseParcelProtectionHandler` must:

1. Validate a 16–80 character idempotency key containing only ASCII letters, digits, colon, dash, or underscore.
2. Recheck Buyer role, state, payment deadline, base quote, parcel fingerprint, and service.
3. Resolve `NotApplicable`, `Unavailable`, `Declined`, or `Accepted`.
4. For `Accepted`, revalidate the provider option and require the disclosed combined price to equal `provider cost + 1_500`.
5. Call `RecordParcelProtectionElection`.
6. Construct `ManagedShipmentDraft` with provider cost, not combined Buyer price.
7. Queue `BookOutbound` with idempotency key:

```csharp
var bookingKey =
    $"book-outbound:{transaction.Id:N}:{request.IdempotencyKey}";
```

8. Write `parcel_protection.booking_intent_created` with shipment ID,
   selection status, terms version, and integer amounts but no address,
   contact, raw quote, or provider credential.
9. Save transaction, annex, managed shipment, operation, and audit events in
   one unit of work before returning.

If a duplicate idempotency key already produced the same fingerprint, return the existing transaction and `preparing_shipping`; if it has a different fingerprint, reject it.

- [ ] **Step 5: Run checkout tests and confirm green**

Run the Step 2 command.

Expected: all cases pass, no provider mutation occurs inside either handler, and one election produces one durable operation.

- [ ] **Step 6: Commit**

```bash
git add src/Toklong.Application/Features/Checkout/GetParcelProtection \
  src/Toklong.Application/Features/Checkout/PrepareParcelProtection \
  src/Toklong.Application/Features/Checkout/ChooseParcelProtection \
  src/Toklong.Application/Transactions/TransactionView.cs \
  tests/Toklong.Application.Tests/Checkout/ParcelProtectionCheckoutTests.cs
git commit -m "feat: add buyer parcel protection checkout commands"
```

---

### Task 7: Revalidate and complete the exact booking before PaymentIntent creation

**Files:**
- Modify: `src/Toklong.Application/Features/Shipping/ProcessShippingOperations/ProcessShippingOperations.cs:25-285`
- Modify: `src/Toklong.Application/Features/Checkout/PreparePaymentSheet/PreparePaymentSheet.cs`
- Modify: `src/Toklong.Domain/Transactions/SaleTransaction.cs:1157-1245`
- Test: `tests/Toklong.Application.Tests/Shipping/DurableShippingOperationProcessingTests.cs`
- Test: `tests/Toklong.Application.Tests/Payments/PaymentDeadlineTests.cs`
- Test: `tests/Toklong.Api.Tests/Api/StripeWebhookApiTests.cs`

**Interfaces:**
- Consumes: Task 6 durable `BookOutbound` operation and Buyer annex.
- Produces:
  - `SaleTransaction.CompleteBuyerCheckoutShipmentBooking`
  - `SaleTransaction.InvalidateParcelProtectionElection`
  - physical-payment readiness gate.

- [ ] **Step 1: Write failing worker and payment-ordering tests**

```csharp
[Fact]
public async Task Payment_intent_is_not_created_while_booking_is_pending()
{
    await Assert.ThrowsAsync<DomainException>(() =>
        paymentHandler.Handle(
            new PreparePaymentSheetCommand(
                TransactionId,
                BuyerId,
                AcceptedTerms: true),
            CancellationToken.None));

    Assert.Equal(0, paymentProvider.PrepareCalls);
}

[Fact]
public async Task Matching_booking_enables_payment_without_changing_deadline()
{
    var deadline = stored.BuyerPaymentDeadlineAt;
    await worker.Handle(
        new ProcessNextShippingOperationCommand("worker"),
        CancellationToken.None);

    Assert.True(stored.ParcelProtectionBookingReady);
    Assert.Equal(deadline, stored.BuyerPaymentDeadlineAt);

    await paymentHandler.Handle(
        new PreparePaymentSheetCommand(
            TransactionId,
            BuyerId,
            AcceptedTerms: true),
        CancellationToken.None);
    Assert.Equal(1, paymentProvider.PrepareCalls);
}

[Fact]
public async Task Changed_option_is_superseded_before_provider_mutation()
{
    protectionProvider.ChangeTermsVersion(
        "parcel-protection-2026-08-01");

    await worker.Handle(
        new ProcessNextShippingOperationCommand("worker"),
        CancellationToken.None);

    Assert.Equal(0, shipmentProvider.ReserveCalls);
    Assert.Equal(
        ParcelProtectionElectionStatus.ReconfirmationRequired,
        stored.ParcelProtectionElection);
    Assert.Equal(
        ShippingOperationStatus.Superseded,
        stored.ShippingOperations.Single().Status);
}
```

Also test changed provider cost, included limit, selected limit, option expiry, reservation mismatch, outcome-unknown behavior, duplicate worker delivery, and Stripe webhook amount equality after optional protection is accepted or declined.

- [ ] **Step 2: Run focused worker/payment tests and confirm red**

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter "FullyQualifiedName~DurableShippingOperationProcessingTests|FullyQualifiedName~PaymentDeadlineTests" \
  --no-restore
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj \
  --filter "FullyQualifiedName~StripeWebhookApiTests" --no-restore
```

Expected: PaymentIntent can be prepared before booking, and the worker does not revalidate the Buyer option.

- [ ] **Step 3: Revalidate before provider mutation**

In `BookAsync`, before `ReserveAsync`, rebuild `ParcelProtectionQuoteRequest`. If election is `Accepted`, call `ValidateOptionAsync` and compare every stored field. Treat an expired/invalid option exception as a changed option before entering the existing generic `DomainException` handler:

```csharp
ProviderParcelProtectionOption validated;
try
{
    validated = await parcelProtectionQuotes.ValidateOptionAsync(
        protectionRequest,
        transaction.ParcelProtectionOptionReference!,
        cancellationToken);
}
catch (ParcelProtectionOptionChangedException)
{
    transaction.InvalidateParcelProtectionElection(
        "parcel-protection-quote-changed",
        clock.UtcNow);
    operation.Supersede(
        workerId,
        "parcel-protection-quote-changed",
        clock.UtcNow);
    return;
}

if (!MatchesStoredSelection(transaction, validated))
{
    transaction.InvalidateParcelProtectionElection(
        "parcel-protection-quote-changed",
        clock.UtcNow);
    operation.Supersede(
        workerId,
        "parcel-protection-quote-changed",
        clock.UtcNow);
    return;
}
```

For `Declined`, `Unavailable`, or `NotApplicable`, require zero optional provider cost and no insurance code. Do not call the optional-protection provider.

- [ ] **Step 4: Complete booking without replaying Seller acceptance**

Replace `CompleteManagedSellerAcceptance` in the outbound booking path with:

```csharp
transaction.CompleteBuyerCheckoutShipmentBooking(
    shipment.Id,
    reservation.Provider,
    reservation.PurchaseReference,
    reservation.ProviderTrackingCode,
    reservation.CourierTrackingCode,
    reservation.CarrierCode,
    reservation.ServiceCode,
    reservation.FeeSatang,
    reservation.InsuranceFeeSatang,
    reservation.DeclaredValueSatang,
    reservation.InsuranceCode,
    reservation.ReservedAt,
    clock.UtcNow);
```

This method requires `SellerAcceptedAwaitingPayment`, exact shipment/election matching, and an open payment deadline. It records reservation fields and `parcel_protection.booking_succeeded` but does not change transaction state, Seller acceptance time, or Buyer payment deadline.
Every definite failure, outcome-unknown, superseded quote, and
provider-result mismatch also writes one sanitized
`parcel_protection.booking_outcome` audit event. Reservation mismatch continues
to `NeedsReview`, and no PaymentIntent is created.

- [ ] **Step 5: Gate PaymentIntent creation**

Before `paymentIntents.PrepareAsync`, add:

```csharp
if (transaction.FulfillmentType ==
        FulfillmentType.PhysicalShipment &&
    !transaction.ParcelProtectionBookingReady)
    throw new DomainException(
        transaction.ParcelProtectionElection ==
            ParcelProtectionElectionStatus.ReconfirmationRequired
            ? "ข้อมูลความคุ้มครองเปลี่ยน กรุณาตรวจและเลือกใหม่ก่อนชำระ"
            : "กำลังเตรียมรายการจัดส่ง กรุณารอสักครู่แล้วลองอีกครั้ง");
```

Also reject `Pending` election. Keep the existing Stripe idempotent reuse path once a PaymentIntent exists.

- [ ] **Step 6: Run focused tests and confirm green**

Run both Step 2 commands.

Expected: no PaymentIntent exists before the exact booking, reconfirmation never calls `ReserveAsync`, deadline remains unchanged, and Stripe amount verification includes only the final combined Buyer price.

- [ ] **Step 7: Commit**

```bash
git add src/Toklong.Application/Features/Shipping/ProcessShippingOperations \
  src/Toklong.Application/Features/Checkout/PreparePaymentSheet \
  src/Toklong.Domain/Transactions/SaleTransaction.cs \
  tests/Toklong.Application.Tests/Shipping/DurableShippingOperationProcessingTests.cs \
  tests/Toklong.Application.Tests/Payments/PaymentDeadlineTests.cs \
  tests/Toklong.Api.Tests/Api/StripeWebhookApiTests.cs
git commit -m "feat: require matching shipping booking before payment"
```

---

### Task 7A: Support Buyer changes with durable cancel-and-rebook

**Files:**
- Create: `src/Toklong.Domain/Transactions/ParcelProtectionChangeRequest.cs`
- Modify: `src/Toklong.Domain/Transactions/SaleTransaction.cs`
- Modify: `src/Toklong.Domain/Transactions/ShippingOperation.cs`
- Modify: `src/Toklong.Application/Features/Checkout/ChooseParcelProtection/ChooseParcelProtection.cs`
- Modify: `src/Toklong.Application/Features/Shipping/ProcessShippingOperations/ProcessShippingOperations.cs`
- Modify: `src/Toklong.Infrastructure/Persistence/ToklongDbContext.cs`
- Create: `src/Toklong.Infrastructure/Persistence/Migrations/20260730100000_ParcelProtectionRebooking.cs`
- Create: `src/Toklong.Infrastructure/Persistence/Migrations/20260730100000_ParcelProtectionRebooking.Designer.cs`
- Modify: `src/Toklong.Infrastructure/Persistence/Migrations/ToklongDbContextModelSnapshot.cs`
- Test: `tests/Toklong.Application.Tests/Checkout/ParcelProtectionChangeTests.cs`
- Test: `tests/Toklong.Application.Tests/Shipping/DurableShippingOperationProcessingTests.cs`

**Interfaces:**
- Consumes: a persisted Buyer election, managed-shipment attempt, durable cancel operation, and Task 7 payment gate.
- Produces:

```csharp
public enum ParcelProtectionChangeStatus
{
    AwaitingCancellation,
    AwaitingRebooking,
    Completed
}

public sealed class ParcelProtectionChangeRequest
{
    public Guid Id { get; private set; }
    public Guid TransactionId { get; private set; }
    public Guid PreviousManagedShipmentId { get; private set; }
    public ParcelProtectionChangeStatus Status { get; private set; }
    public ParcelProtectionElectionStatus DesiredElection { get; private set; }
    public long DesiredCustomerPriceSatang { get; private set; }
    public long DesiredProviderCostSatang { get; private set; }
    public long DesiredServiceFeeSatang { get; private set; }
    public long DesiredIncludedCoverageSatang { get; private set; }
    public long DesiredSelectedCoverageSatang { get; private set; }
    public string DesiredTermsVersion { get; private set; }
    public string? DesiredOptionReference { get; private set; }
    public DateTimeOffset DesiredQuotedAt { get; private set; }
    public DateTimeOffset DesiredExpiresAt { get; private set; }
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
}
```

- [ ] **Step 1: Write failing change, cancellation, and privacy tests**

```csharp
[Fact]
public async Task Buyer_can_change_a_pending_booking_intent_before_payment()
{
    await ChooseAcceptedProtectionAsync();

    await handler.Handle(
        Choose(
            addProtection: false,
            optionReference: null,
            disclosedCustomerPriceSatang: null,
            idempotencyKey: "change-before-claim"),
        CancellationToken.None);

    Assert.Equal(
        ShippingOperationStatus.Superseded,
        stored.ShippingOperations.First().Status);
    Assert.Equal(
        ParcelProtectionElectionStatus.Declined,
        stored.ParcelProtectionElection);
    Assert.Equal(0, stored.ParcelInsuranceFeeSatang);
    Assert.Equal(2, stored.ManagedShipments.Count);
}

[Fact]
public async Task Buyer_change_after_reservation_cancels_before_rebooking()
{
    await ChooseAndBookAcceptedProtectionAsync();

    await handler.Handle(
        Choose(
            addProtection: false,
            optionReference: null,
            disclosedCustomerPriceSatang: null,
            idempotencyKey: "change-after-reservation"),
        CancellationToken.None);

    Assert.Single(stored.ParcelProtectionChangeRequests);
    Assert.Contains(
        stored.ShippingOperations,
        operation =>
            operation.OperationType ==
                ShippingOperationType.CancelOutbound);
    Assert.DoesNotContain(
        stored.ShippingOperations,
        operation =>
            operation.OperationType ==
                ShippingOperationType.BookOutbound &&
            operation.CreatedAt >
                stored.ParcelProtectionChangeRequests.Single().CreatedAt);

    await RunCancelWorkerAsync();

    Assert.Contains(
        stored.ShippingOperations,
        operation =>
            operation.OperationType ==
                ShippingOperationType.BookOutbound &&
            operation.CreatedAt >
                stored.ParcelProtectionChangeRequests.Single().CreatedAt);
    Assert.Equal(0, paymentProvider.PrepareCalls);
}

[Fact]
public async Task Buyer_cannot_change_after_payment_intent_exists()
{
    await ChooseBookAndPreparePaymentAsync();

    await Assert.ThrowsAsync<DomainException>(() =>
        handler.Handle(
            ChooseDeclined("too-late"),
            CancellationToken.None));
}
```

Also test a processing or outcome-unknown booking rejects change until lookup resolves it, duplicate change idempotency, cancellation definite failure, cancellation outcome unknown, changed option expiry during rebooking, old cancelled attempts remaining queryable, and Seller/API projections containing no change-request fields.

- [ ] **Step 2: Run focused change tests and confirm red**

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter "FullyQualifiedName~ParcelProtectionChangeTests|FullyQualifiedName~DurableShippingOperationProcessingTests" \
  --no-restore
```

Expected: the change aggregate, pending-operation supersede path, and rebooking orchestration do not exist.

- [ ] **Step 3: Add safe pre-mutation superseding**

Add:

```csharp
public void SupersedeBeforeMutation(
    string actorId,
    string sanitizedReasonCode,
    DateTimeOffset now)
{
    Required(actorId, "actor", 120);
    if (Status is not (
            ShippingOperationStatus.Pending or
            ShippingOperationStatus.RetryScheduled))
        throw new DomainException(
            "เปลี่ยนตัวเลือกไม่ได้ระหว่างตรวจสอบผลกับผู้ให้บริการ");
    Status = ShippingOperationStatus.Superseded;
    LastSanitizedErrorCode = Required(
        sanitizedReasonCode,
        "reason code",
        100);
    CompletedAt = now;
    ClearLease();
    Version++;
}
```

Never use this method for `Processing`, `OutcomeUnknown`, or `NeedsReview`.

- [ ] **Step 4: Preserve every outbound attempt**

Remove the unique `(TransactionId, Direction)` index from `managed_shipments` and replace it with:

```csharp
shipment.HasIndex(x => new
{
    x.TransactionId,
    x.Direction,
    x.Status
});
```

Add:

```csharp
public ManagedShipment? CurrentOutboundShipment =>
    _managedShipments
        .Where(item =>
            item.Direction == ShipmentDirection.Outbound &&
            item.Status != ManagedShipmentStatus.Cancelled)
        .OrderByDescending(item => item.CreatedAt)
        .FirstOrDefault();
```

Replace every `Single`/`SingleOrDefault` outbound lookup in domain, application, API, repositories, label generation, tracking polling, cancellation, and tests with `CurrentOutboundShipment` or an explicit shipment ID. Historical cancelled attempts retain provider references and audit evidence.

- [ ] **Step 5: Create and persist the change request**

`ParcelProtectionChangeRequest.Create(Guid transactionId, Guid previousManagedShipmentId, ParcelProtectionSelection desiredSelection, string idempotencyKey, DateTimeOffset now)` validates the desired selection with the same money, limit, terms, and expiry rules as the initial annex. Configure a private `_parcelProtectionChangeRequests` collection on `SaleTransaction`, map it to `parcel_protection_change_requests`, add unique indexes on `IdempotencyKey` and on `(TransactionId, Status)` for active statuses, and make requests append-only except for their controlled status transitions.

Generate the migration with:

```bash
dotnet ef migrations add ParcelProtectionRebooking \
  --project src/Toklong.Infrastructure/Toklong.Infrastructure.csproj \
  --startup-project src/Toklong.Api/Toklong.Api.csproj \
  --output-dir Persistence/Migrations
```

Use migration prefix `20260730100000` consistently and include the managed-shipment index replacement in this migration.

- [ ] **Step 6: Orchestrate change according to provider-mutation state**

Update `ChooseParcelProtectionHandler`:

- If there is no outbound attempt, record the new election normally.
- If the current booking operation is `Pending` or `RetryScheduled`, supersede it, mark the old unreserved shipment cancelled locally, apply the new election, and queue a new shipment/booking intent.
- If it is `Processing`, `OutcomeUnknown`, or `NeedsReview`, reject the change with `กำลังตรวจสอบรายการจัดส่ง กรุณารอผลก่อนเปลี่ยนตัวเลือก`.
- If the current shipment is reserved and no PaymentIntent exists, create a `ParcelProtectionChangeRequest` and queue `CancelOutbound`; do not overwrite the active annex yet.
- If `PaymentReference` exists or state is `PaymentPending`, reject the change.

When `CancelAsync` succeeds for an outbound shipment with an active change request:

```csharp
transaction.CompleteParcelProtectionCancellation(
    shipment.Id,
    clock.UtcNow);
var replacement =
    transaction.CreateReplacementOutboundShipment(
        changeRequest.Id,
        clock.UtcNow);
var booking = ShippingOperation.Queue(
    transaction.Id,
    replacement.Id,
    ShippingOperationType.BookOutbound,
    $"book-outbound-change:{changeRequest.Id:N}",
    ManagedShippingOperationQueue.BookingFingerprint(
        replacement),
    clock.UtcNow);
transaction.QueueReplacementOutboundShipment(
    replacement,
    booking,
    clock.UtcNow);
```

Only after replacement booking succeeds does the aggregate apply the desired election, recalculate Buyer total, mark the change `Completed`, and write `parcel_protection.changed`. A cancellation or rebooking uncertainty blocks PaymentIntent and remains visible to operations.

- [ ] **Step 7: Run focused tests and confirm green**

Run the Step 2 command.

Expected: Buyer can change before PaymentIntent, no provider mutation is duplicated, previous shipment attempts remain auditable, and payment stays blocked through cancellation/rebooking.

- [ ] **Step 8: Commit**

```bash
git add src/Toklong.Domain/Transactions \
  src/Toklong.Application/Features/Checkout/ChooseParcelProtection \
  src/Toklong.Application/Features/Shipping/ProcessShippingOperations \
  src/Toklong.Infrastructure/Persistence \
  tests/Toklong.Application.Tests/Checkout/ParcelProtectionChangeTests.cs \
  tests/Toklong.Application.Tests/Shipping/DurableShippingOperationProcessingTests.cs
git commit -m "feat: support parcel protection rebooking before payment"
```

---

### Task 8: Expose Buyer-only checkout endpoints with idempotency and role privacy

**Files:**
- Modify: `src/Toklong.Api/Api/MobileApi.cs:160-230`
- Modify: `src/Toklong.Api/Api/MobileApi.cs:760-910`
- Modify: `src/Toklong.Api/Api/MobileApi.cs:1790-1930`
- Test: `tests/Toklong.Api.Tests/Api/MobileParcelProtectionApiTests.cs`

**Interfaces:**
- Consumes: Task 6 MediatR query/command.
- Produces:
  - `GET /api/mobile/transactions/{transactionId}/parcel-protection`
  - `POST /api/mobile/transactions/{transactionId}/parcel-protection/prepare`
  - `POST /api/mobile/transactions/{transactionId}/parcel-protection-election`

- [ ] **Step 1: Write failing API authorization, shape, and idempotency tests**

Test:

```csharp
[Fact]
public async Task Buyer_can_read_one_combined_price_and_one_maximum()
{
    using var request = new HttpRequestMessage(
        HttpMethod.Post,
        $"/api/mobile/transactions/{TransactionId}/parcel-protection/prepare");
    request.Headers.Add(
        "Idempotency-Key",
        "prepare-protection-choice");
    var response = await BuyerClient.SendAsync(request);
    var json = await response.Content.ReadAsStringAsync();

    Assert.True(response.IsSuccessStatusCode);
    Assert.Contains("\"maximumCoverageLimitSatang\":450000", json);
    Assert.Contains("\"customerPriceSatang\":6000", json);
    Assert.DoesNotContain("providerCost", json);
    Assert.DoesNotContain("toklongServiceFee", json);
    Assert.DoesNotContain("shippop", json, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public async Task Seller_cannot_read_or_write_buyer_protection_annex()
{
    Assert.Equal(
        HttpStatusCode.Forbidden,
        (await SellerClient.GetAsync(ProtectionPath)).StatusCode);
    Assert.Equal(
        HttpStatusCode.Forbidden,
        (await SellerClient.PostAsJsonAsync(
            ElectionPath,
            AcceptedRequest())).StatusCode);
}

[Fact]
public async Task Election_requires_idempotency_key()
{
    var response = await BuyerClient.PostAsJsonAsync(
        ElectionPath,
        AcceptedRequest());
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}
```

Also test cross-Buyer access, expired session, same-key replay, conflicting same-key payload, no provider/internal split in response, and `409 Conflict` for reconfirmation-required price/terms changes.

- [ ] **Step 2: Run API tests and confirm red**

```bash
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj \
  --filter "FullyQualifiedName~MobileParcelProtectionApiTests" \
  --no-restore
```

Expected: all three routes return 404.

- [ ] **Step 3: Map Buyer-only endpoints and DTOs**

```csharp
authenticated.MapGet(
    "/transactions/{transactionId:guid}/parcel-protection",
    GetParcelProtectionAsync);
authenticated.MapPost(
    "/transactions/{transactionId:guid}/parcel-protection/prepare",
    PrepareParcelProtectionAsync);
authenticated.MapPost(
    "/transactions/{transactionId:guid}/parcel-protection-election",
    ChooseParcelProtectionAsync);

public sealed record MobileParcelProtectionResponse(
    bool RequiresChoice,
    bool AddOnAvailable,
    long IncludedCoverageLimitSatang,
    long? MaximumCoverageLimitSatang,
    long? CustomerPriceSatang,
    string? OptionReference,
    string TermsVersion,
    DateTimeOffset? ExpiresAt,
    string Election,
    bool BookingReady,
    bool ReconfirmationRequired);

public sealed record MobileParcelProtectionElectionRequest(
    bool AddProtection,
    string? OptionReference,
    long? DisclosedCustomerPriceSatang);

public sealed record MobileParcelProtectionElectionResponse(
    string BookingStatus);
```

Read Buyer ID only from `PartyIds.From(principal).BuyerId`. Read `Idempotency-Key` from the request header; do not accept it from JSON. Return `Results.Conflict` for a stale option that requires reconfirmation.
The prepare route sends `PrepareParcelProtectionCommand`; the GET route sends
`GetParcelProtectionQuery` and is used only for resume/polling.

- [ ] **Step 4: Run API tests and confirm green**

Run the Step 2 command.

Expected: all tests pass and Seller clients cannot infer any annex value.

- [ ] **Step 5: Commit**

```bash
git add src/Toklong.Api/Api/MobileApi.cs \
  tests/Toklong.Api.Tests/Api/MobileParcelProtectionApiTests.cs
git commit -m "feat: expose buyer parcel protection checkout api"
```

---

### Task 9: Add the one-time mobile checkout choice and booking-wait state

**Files:**
- Modify: `src/Toklong.Mobile/Core/ITransactionService.cs`
- Modify: `src/Toklong.Mobile/Core/AppTransaction.cs`
- Create: `src/Toklong.Mobile/Core/ParcelProtectionAnalytics.cs`
- Modify: `src/Toklong.Mobile/Services/ApiTransactionService.cs`
- Modify: `src/Toklong.Mobile/ViewModels/TransactionDetailViewModel.cs`
- Modify: `src/Toklong.Mobile/Pages/TransactionDetailPage.xaml`
- Modify: `src/Toklong.Mobile/Services/StripePaymentSheetService.cs`
- Test: `tests/Toklong.Mobile.Core.Tests/TransactionPresentationTests.cs`
- Test: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`
- Test: `tests/Toklong.Mobile.Core.Tests/MobileAnalyticsEventTests.cs`

**Interfaces:**
- Consumes: Task 8 Buyer endpoints and existing Stripe PaymentSheet.
- Produces:

```csharp
public sealed record BuyerParcelProtection(
    bool RequiresChoice,
    bool AddOnAvailable,
    long IncludedCoverageLimitSatang,
    long? MaximumCoverageLimitSatang,
    long? CustomerPriceSatang,
    string? OptionReference,
    string TermsVersion,
    DateTimeOffset? ExpiresAt,
    string Election,
    bool BookingReady,
    bool ReconfirmationRequired);

Task<BuyerParcelProtection> GetParcelProtectionAsync(
    Guid transactionId,
    CancellationToken cancellationToken = default);

Task<BuyerParcelProtection> PrepareParcelProtectionAsync(
    Guid transactionId,
    string idempotencyKey,
    CancellationToken cancellationToken = default);

Task<string> ChooseParcelProtectionAsync(
    Guid transactionId,
    bool addProtection,
    string? optionReference,
    long? disclosedCustomerPriceSatang,
    string idempotencyKey,
    CancellationToken cancellationToken = default);
```

- [ ] **Step 1: Write failing ViewModel and XAML contract tests**

Test that:

```csharp
[Fact]
public async Task Pay_opens_choice_once_when_add_on_is_available()
{
    await viewModel.LoadAsync(TransactionId);
    await viewModel.PrimaryActionCommand.ExecuteAsync();

    Assert.True(viewModel.IsParcelProtectionChoiceVisible);
    Assert.Equal("คุ้มครองสูงสุด ฿4,500", viewModel.MaximumCoverageText);
    Assert.Equal("เพิ่ม ฿60", viewModel.ParcelProtectionPriceText);
    Assert.Equal(0, paymentSheet.PresentCalls);
}

[Fact]
public async Task Included_coverage_skips_choice_and_prepares_booking()
{
    protection.RequiresChoice = false;
    await viewModel.PrimaryActionCommand.ExecuteAsync();

    Assert.False(viewModel.IsParcelProtectionChoiceVisible);
    Assert.Equal(1, transactions.ChooseProtectionCalls);
    Assert.Equal(0, paymentSheet.PresentCalls);
    Assert.Equal(
        "กำลังเตรียมรายการจัดส่ง กรุณารอสักครู่",
        viewModel.Message);
}
```

Layout assertions must require:

- title `เพิ่มความคุ้มครองพัสดุไหม`
- body `มูลค่าสินค้าสูงกว่าวงเงินที่รวมมากับการจัดส่ง แนะนำเพิ่มความคุ้มครองก่อนชำระเงิน`
- primary button `เพิ่มความคุ้มครอง` followed by the combined price
- subdued text action `ไม่เพิ่มความคุ้มครอง`
- details action `ดูเงื่อนไขและสินค้าที่ไม่คุ้มครอง`
- selected-state action `เปลี่ยน`
- exactly one maximum-coverage binding and one combined-price binding
- no `SHIPPOP`, `แพ็กเกจ`, `ส่วนที่ไม่คุ้มครอง`, provider cost, service fee, or green callout
- payment summary label `ค่าความคุ้มครองพัสดุ`
- no maximum-coverage binding in the payment summary

Add tests that closing without choosing causes the choice to appear on the
next payment attempt, reopening after a choice resumes the persisted selection
without automatically showing the card, and tapping `เปลี่ยน` invokes Task
7A before PaymentIntent creation.
When a previously accepted option becomes unavailable, require a blocking
reconfirmation card with `ความคุ้มครองที่เลือกใช้ไม่ได้แล้ว` and primary action
`ดำเนินการต่อด้วยวงเงินที่รวมอยู่`; closing the card records no decline and
does not continue to booking or payment.

- [ ] **Step 2: Run mobile core tests and confirm red**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~TransactionPresentationTests|FullyQualifiedName~UiLayoutConsistencyTests" \
  --no-restore
```

Expected: ViewModel properties, service methods, and choice surface are missing.

- [ ] **Step 3: Add service methods and idempotency**

`ApiTransactionService.ChooseParcelProtectionAsync` sends:

```csharp
var request = new HttpRequestMessage(
    HttpMethod.Post,
    $"api/mobile/transactions/{transactionId}/parcel-protection-election")
{
    Content = JsonContent.Create(new
    {
        AddProtection = addProtection,
        OptionReference = optionReference,
        DisclosedCustomerPriceSatang =
            disclosedCustomerPriceSatang
    })
};
request.Headers.Add("Idempotency-Key", idempotencyKey);
```

Create one idempotency key per visible choice lifecycle using:

```csharp
$"mobile:{transactionId:N}:{Guid.NewGuid():N}"
```

Reuse it on a network retry of the same selection; create a new key only after the server reports reconfirmation required.
`PrepareParcelProtectionAsync` sends `POST` to the prepare route with its own
idempotency key. `GetParcelProtectionAsync` sends `GET` and is used only for
resume and booking polling.

- [ ] **Step 4: Implement the one-time decision flow**

On payment action:

1. Require accepted transaction terms.
2. Call `PrepareParcelProtectionAsync` on the first payment attempt; use
   `GetParcelProtectionAsync` when resuming an existing election.
3. If `RequiresChoice`, show the in-page modal/card and stop.
4. If no choice is required, submit `AddProtection = false`.
5. After either selection returns `preparing_shipping`, close the choice, refresh the transaction, and show the booking-wait copy.
6. If the current checkout response already reports `BookingReady`, present Stripe without writing the election again.
7. Otherwise poll `GetParcelProtectionAsync` every 750 milliseconds for at most eight attempts. Present Stripe when `BookingReady` becomes true; after the eighth attempt show the booking-wait copy and let the Buyer retry the payment action without re-submitting the election.
8. If reconfirmation is required, fetch the new option and reopen the choice; do not preserve a prior acceptance visually.

Keep a visible `เปลี่ยน` action in the Buyer summary while the transaction is
`SellerAcceptedAwaitingPayment`; hide it once the API returns
`PaymentPending`. It reopens the choice; if an outbound booking is
already reserved, display `กำลังปรับรายการจัดส่ง` while Task 7A cancels and
rebooks. Add `ดูรายละเอียดความคุ้มครองพัสดุ`; its detail surface may show
included and selected limits, terms version, exclusions, and the authenticated
support route, but no provider brand or internal price split.

The accepted path calls:

```csharp
await transactionService.ChooseParcelProtectionAsync(
    Transaction.Id,
    addProtection: true,
    ParcelProtection.OptionReference,
    ParcelProtection.CustomerPriceSatang,
    parcelProtectionIdempotencyKey);
```

The declined path sends `false`, null reference, and null price.

- [ ] **Step 5: Build the accessible XAML choice surface**

Use a white surface card with neutral blue accents, 44-point minimum tap targets, semantic descriptions containing the exact maximum and price, and focus order: title, explanation, maximum, combined price, primary action, decline action. Keep `ไม่เพิ่ม` readable at WCAG AA contrast; subdued does not mean low-contrast.

Change the Buyer summary label from `ประกันพัสดุ` to `ค่าความคุ้มครองพัสดุ`. Bind it to the combined `ParcelInsuranceFeeSatang`. Do not add maximum coverage to `AppTransaction`.

- [ ] **Step 6: Add coarse, non-sensitive checkout analytics**

Inject `IMobileAnalytics` into `TransactionDetailViewModel` and create:

```csharp
public static class ParcelProtectionAnalytics
{
    public static MobileAnalyticsEvent Offered() =>
        new("parcel_protection_offered", Empty());

    public static MobileAnalyticsEvent Accepted(
        long customerPriceSatang) =>
        new(
            "parcel_protection_accepted",
            new Dictionary<string, object?>
            {
                ["customer_price_satang"] =
                    customerPriceSatang
            });

    public static MobileAnalyticsEvent Declined() =>
        new("parcel_protection_declined", Empty());

    public static MobileAnalyticsEvent Unavailable() =>
        new("parcel_protection_unavailable", Empty());

    public static MobileAnalyticsEvent Changed() =>
        new("parcel_protection_changed", Empty());

    public static MobileAnalyticsEvent PriceChanged() =>
        new("parcel_protection_price_changed", Empty());

    public static MobileAnalyticsEvent CheckoutConverted() =>
        new("parcel_protection_checkout_converted", Empty());

    private static IReadOnlyDictionary<string, object?> Empty() =>
        new Dictionary<string, object?>();
}
```

Track only these coarse values. Tests must prove no address, phone, provider
reference, raw quote, terms text, or credential-shaped key enters an event.
Track `CheckoutConverted` only after Stripe PaymentSheet reports completed;
payment success still depends exclusively on the verified Stripe webhook.

- [ ] **Step 7: Prevent PaymentSheet from bypassing checkout preparation**

Keep `StripePaymentSheetService` responsible only for calling `/payment-sheet` and presenting Stripe. It must not choose protection. `TransactionDetailViewModel` must complete or resume the election/booking flow before calling it.

- [ ] **Step 8: Run mobile tests and build**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --no-restore
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj \
  -f net9.0-ios -p:RuntimeIdentifier=iossimulator-arm64 \
  --no-restore
```

Expected: tests and simulator build pass; the choice appears once, decline remains accessible, and closing Stripe still permits a later payment retry.

- [ ] **Step 9: Commit**

```bash
git add src/Toklong.Mobile/Core/ITransactionService.cs \
  src/Toklong.Mobile/Core/AppTransaction.cs \
  src/Toklong.Mobile/Core/ParcelProtectionAnalytics.cs \
  src/Toklong.Mobile/Services/ApiTransactionService.cs \
  src/Toklong.Mobile/ViewModels/TransactionDetailViewModel.cs \
  src/Toklong.Mobile/Pages/TransactionDetailPage.xaml \
  src/Toklong.Mobile/Services/StripePaymentSheetService.cs \
  tests/Toklong.Mobile.Core.Tests/TransactionPresentationTests.cs \
  tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs \
  tests/Toklong.Mobile.Core.Tests/MobileAnalyticsEventTests.cs
git commit -m "feat: add optional parcel protection checkout ui"
```

---

### Task 10: Certify the account-specific SHIPPOP capability without enabling guesses

**Files:**
- Modify: `tests/Toklong.Shippop.Certification/ShippopServiceCertificationTests.cs`
- Modify: `docs/SHIPPOP_CERTIFICATION_RUNBOOK.md`
- Modify: `docs/08_SHIPPOP_PRODUCTION_FLOW.md`
- Test: `tests/Toklong.Shippop.Certification/ShippopServiceCertificationTests.cs`

**Interfaces:**
- Consumes: `IParcelProtectionQuoteProvider` and SHIPPOP Dev environment variables.
- Produces: recorded proof for included limit, add-on price, maximum, terms/code, booking match, and safe replay behavior; configuration remains off until every assertion passes.

- [ ] **Step 1: Add opt-in certification tests**

Under `SHIPPOP_CERTIFY=1`, test:

```csharp
[Fact]
public async Task Protection_quote_and_booking_preserve_exact_values()
{
    RequireCertificationEnvironment();
    var availability = await provider.GetAvailabilityAsync(
        ProtectionRequestFromSyntheticAddress(),
        CancellationToken.None);
    var option = Assert.IsType<ProviderParcelProtectionOption>(
        availability.AddOn);
    var validated = await provider.ValidateOptionAsync(
        ProtectionRequestFromSyntheticAddress(),
        option.OptionReference,
        CancellationToken.None);

    Assert.Equal(option.ProviderCostSatang, validated.ProviderCostSatang);
    Assert.Equal(
        option.SelectedCoverageLimitSatang,
        validated.SelectedCoverageLimitSatang);
    Assert.Equal(option.TermsVersion, validated.TermsVersion);
}
```

The suite must also prove included coverage, maximum coverage, integer-satang conversion, exact booking result, no duplicate booking on a safe replay, operation lookup after timeout, cancellation before first scan, and the omission/requirement of weight and each dimension. Record sanitized raw field names and units, never API keys or personal data.

- [ ] **Step 2: Run the suite in skipped mode**

```bash
dotnet test tests/Toklong.Shippop.Certification/Toklong.Shippop.Certification.csproj \
  --no-restore
```

Expected: live tests report `Skipped` when `SHIPPOP_CERTIFY` is absent.

- [ ] **Step 3: Document the exact opt-in command and evidence table**

The runbook must show:

```bash
SHIPPOP_CERTIFY=1 \
SHIPPOP_BASE_URL=http://mkpservice.shippop.dev \
SHIPPOP_ALLOW_INSECURE_HTTP=1 \
SHIPPOP_API_KEY="$SHIPPOP_API_KEY" \
SHIPPOP_ACCOUNT_EMAIL="$SHIPPOP_ACCOUNT_EMAIL" \
SHIPPOP_SERVICE_CODE=EMST \
SHIPPOP_SYNTHETIC_ADDRESS_JSON=/absolute/path/synthetic-address.json \
dotnet test tests/Toklong.Shippop.Certification/Toklong.Shippop.Certification.csproj
```

State that HTTP is Dev-only, credentials come from local environment/secret storage, and no real customer address is permitted.

- [ ] **Step 4: Keep the capability disabled unless the live evidence passes**

Do not change `OptionalProtectionEnabled` to true in checked-in configuration. If the Dev account cannot return a separable optional-protection option after Buyer election, record that as a provider blocker and leave included-only checkout enabled.

- [ ] **Step 5: Commit**

```bash
git add tests/Toklong.Shippop.Certification/ShippopServiceCertificationTests.cs \
  docs/SHIPPOP_CERTIFICATION_RUNBOOK.md \
  docs/08_SHIPPOP_PRODUCTION_FLOW.md
git commit -m "test: certify optional shippop parcel protection"
```

---

### Task 11: Update canonical product, flow, record, UX, and acceptance documentation

**Files:**
- Modify: `docs/00_PRODUCT_BRIEF.md`
- Modify: `docs/01_USER_FLOWS_AND_STATE_MACHINE.md`
- Modify: `docs/02_UI_UX_AND_CONTENT_SPEC.md`
- Modify: `docs/03_BACKEND_TRANSACTION_RECORD.md`
- Modify: `docs/04_PAYMENT_SHIPPING_AND_DISPUTE_RULES.md`
- Modify: `docs/05_ACCEPTANCE_TESTS.md`
- Modify: `docs/06_OPEN_DECISIONS.md`
- Modify: `docs/07_REGULATORY_SOURCE_NOTES.md`
- Modify: `docs/08_SHIPPOP_PRODUCTION_FLOW.md`
- Modify: `docs/08_IMPLEMENTATION.md`

**Interfaces:**
- Consumes: implemented behavior from Tasks 1–10 and the approved design spec.
- Produces: one non-conflicting canonical contract for optional Buyer-funded parcel protection.

- [ ] **Step 1: Replace the old mandatory full-value rule everywhere**

Document the exact flow:

```text
Seller accepts item + parcel + carrier service
→ Buyer reviews checkout
→ system skips the question within included coverage, otherwise asks once
→ Buyer accepts or declines the optional add-on
→ TOKLONG persists election + durable booking intent
→ worker creates the exact unconfirmed booking
→ matching booking enables PaymentIntent creation
→ verified payment confirms the booked shipment
```

Remove statements that Seller acceptance books SHIPPOP, every service needs full-value insurance, Seller sees insurance price/value, or a PaymentIntent may precede booking.

- [ ] **Step 2: Document the immutable Buyer annex and privacy boundary**

`docs/03_BACKEND_TRANSACTION_RECORD.md` must list:

```text
election
customer_price_satang
provider_cost_satang
toklong_service_fee_satang
included_coverage_limit_satang
selected_coverage_limit_satang
protection_terms_version
provider_option_reference
quoted_at
expires_at
buyer_elected_at
```

State that provider cost and TOKLONG fee split are internal accounting fields; Buyer sees the combined price, and Seller sees none of these values.

- [ ] **Step 3: Add acceptance scenarios matching automated tests**

Add Given/When/Then scenarios for:

- included coverage without prompt or charge
- accepted add-on with combined price
- explicit decline
- add-on unavailable
- Seller privacy
- one prompt only
- changed price/limit/terms requires reconfirmation
- exact durable booking precedes PaymentIntent
- booking failure blocks PaymentIntent
- deadline is not extended
- verified Stripe amount includes the final Buyer price
- uncertified SHIPPOP optional capability remains disabled
- dimensions remain required pending certification

- [ ] **Step 4: Mark only real provider facts as decided**

In `docs/06_OPEN_DECISIONS.md`, keep SHIPPOP add-on field names, limits, price units, post-election booking support, and replay lookup as launch blockers until Task 10 has account-specific evidence. In `docs/07_REGULATORY_SOURCE_NOTES.md`, describe the consumer disclosure and terms-version evidence without claiming TOKLONG is an insurer or escrow provider.

- [ ] **Step 5: Scan for contradictory mandatory-insurance copy**

```bash
rg -n "full-value|เต็มมูลค่า|Seller acceptance.*booking|ผู้ขาย.*ประกัน|seller.*insurance" \
  docs/00_PRODUCT_BRIEF.md \
  docs/01_USER_FLOWS_AND_STATE_MACHINE.md \
  docs/02_UI_UX_AND_CONTENT_SPEC.md \
  docs/03_BACKEND_TRANSACTION_RECORD.md \
  docs/04_PAYMENT_SHIPPING_AND_DISPUTE_RULES.md \
  docs/05_ACCEPTANCE_TESTS.md \
  docs/06_OPEN_DECISIONS.md \
  docs/07_REGULATORY_SOURCE_NOTES.md \
  docs/08_SHIPPOP_PRODUCTION_FLOW.md \
  docs/08_IMPLEMENTATION.md
```

Expected: matches exist only in historical/rejected-decision context or in explicit statements that the old mandatory rule no longer applies.

- [ ] **Step 6: Commit**

```bash
git add docs/00_PRODUCT_BRIEF.md \
  docs/01_USER_FLOWS_AND_STATE_MACHINE.md \
  docs/02_UI_UX_AND_CONTENT_SPEC.md \
  docs/03_BACKEND_TRANSACTION_RECORD.md \
  docs/04_PAYMENT_SHIPPING_AND_DISPUTE_RULES.md \
  docs/05_ACCEPTANCE_TESTS.md \
  docs/06_OPEN_DECISIONS.md \
  docs/07_REGULATORY_SOURCE_NOTES.md \
  docs/08_SHIPPOP_PRODUCTION_FLOW.md
git commit -m "docs: align parcel protection checkout rules"
```

---

### Task 12: Run full regression, security, accessibility, and secret checks

**Files:**
- Modify only files required by concrete failures found by the commands below.
- Test: all projects in `Toklong.slnx`

**Interfaces:**
- Consumes: all prior tasks.
- Produces: verified release candidate with no known rule, privacy, payment-ordering, or provider-capability regression.

- [ ] **Step 1: Run formatting and type checks**

```bash
dotnet format Toklong.slnx --verify-no-changes
dotnet build Toklong.slnx --no-restore
```

Expected: both commands exit 0 with no warnings introduced by this feature.

- [ ] **Step 2: Run all unit and integration tests**

```bash
dotnet test Toklong.slnx --no-build --no-restore
```

Expected: all non-opt-in tests pass; SHIPPOP live certification tests are explicitly skipped without `SHIPPOP_CERTIFY=1`.

- [ ] **Step 3: Run the critical state, payment, shipping, and accessibility slices separately**

```bash
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj \
  --filter "FullyQualifiedName~SaleTransaction|FullyQualifiedName~Shipping" \
  --no-build --no-restore
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter "FullyQualifiedName~ParcelProtection|FullyQualifiedName~Payment|FullyQualifiedName~Shipping" \
  --no-build --no-restore
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj \
  --filter "FullyQualifiedName~MobileParcelProtection|FullyQualifiedName~StripeWebhook|FullyQualifiedName~MobileSellerOffer" \
  --no-build --no-restore
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~TransactionPresentation|FullyQualifiedName~UiLayoutConsistency" \
  --no-build --no-restore
```

Expected: every command exits 0.

- [ ] **Step 4: Scan for secrets and forbidden Seller disclosures**

```bash
git grep -n -E \
  'dv[[:alnum:]]{40,}|sk_(live|test)_[[:alnum:]]+|SHIPPOP_API_KEY[[:space:]]*[:=][[:space:]]*[^"$<]' \
  -- ':!docs/superpowers/specs' ':!docs/superpowers/plans'
rg -n "ParcelProtectionProviderCost|ParcelProtectionServiceFee|ShippingDeclaredValue|ParcelInsuranceFee" \
  src/Toklong.Mobile/Pages/SellerOfferPage.xaml \
  src/Toklong.Mobile/ViewModels/SellerOfferViewModel.cs \
  src/Toklong.Mobile/Core/ISellerOfferService.cs
```

Expected: both scans return no committed secret and no Seller disclosure. Environment-variable names and test placeholders are allowed; values are not.

- [ ] **Step 5: Inspect the final diff against the approved spec**

```bash
git diff --check
git status --short
git log --oneline -12
```

Expected: no whitespace errors, only intentional feature files are modified, and each task has one reviewable commit.

- [ ] **Step 6: Commit any concrete regression fixes, otherwise leave the verified tree clean**

If a verification command required a code or test correction:

```bash
git add -u
git commit -m "fix: close parcel protection regression"
```

If every command passed without changes, do not create an empty commit.
