# SHIPPOP Production Shipping Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace synchronous SHIPPOP mutations with a retry-safe managed
shipping flow, add full-value parcel-insurance accounting, fail-closed tracking
and exceptions, provider-managed returns, and the approved four-stage mobile
shipping presentation.

**Architecture:** `SaleTransaction` owns immutable agreement money and
transition authority while `ManagedShipment` owns outbound/return shipping
snapshots. API commands atomically commit a `ShippingOperation`; the existing
Worker leases and executes it outside the database transaction, then applies
the provider result and audit event in a new transaction. SHIPPOP remains a
server-only adapter, and all enabled production service codes default to off
until account-specific certification.

**Tech Stack:** .NET 10, C# 14, MediatR 12, EF Core 10 with PostgreSQL, .NET
MAUI, xUnit 2.9.

## Global Constraints

- Work directly on `main`; do not create a worktree.
- Preserve unrelated dirty files and stage only files named by the active task.
- Use integer satang and ISO currency codes for every monetary value.
- Never use client input, a redirect, a slip, or a database assumption to mark
  payment, refund, or payout successful.
- Only trusted carrier delivery time may start the 72-hour physical inspection
  and payout-hold window.
- `complete` without trusted delivery time must become
  `TrackingUnverified`; poll time is never a substitute.
- Any open dispute, carrier exception, insurance case, unknown mutation
  outcome, or unverified return blocks payout.
- The seller must not see Buyer Protection fee or buyer total.
- The buyer sees separate item, shipping, parcel-insurance, Buyer Protection,
  and total rows before payment.
- New physical snapshots use schema version 9 and agreement-core schema version
  7; paid snapshots are immutable.
- SHIPPOP credentials remain server-side secrets and must never enter Git,
  mobile payloads, logs, analytics, or test fixtures.
- Production services `EMST`, `FLE`, `KRYX`, and `KRYS` are drop-off only and
  remain disabled until each service passes certification.
- Do not expose an unsigned SHIPPOP webhook endpoint.

---

## File map

### Domain

- `src/Toklong.Domain/Transactions/ManagedShipment.cs` — immutable outbound or
  return parcel snapshot plus trusted provider lifecycle fields.
- `src/Toklong.Domain/Transactions/ShippingOperation.cs` — durable mutation
  state, lease, retry safety, and sanitized error data.
- `src/Toklong.Domain/Transactions/ProviderShippingAdjustment.cs` — append-only
  post-payment provider cost.
- `src/Toklong.Domain/Transactions/ShippingInsuranceCase.cs` — authorized CRM
  claim record.
- `src/Toklong.Domain/Transactions/SaleTransaction.cs` — agreement money,
  acceptance intent/completion, shipping exception and return/refund gates.
- `src/Toklong.Domain/Transactions/TransactionState.cs` and
  `TransactionTransitionService.cs` — allow-listed shipping review states and
  authorized transitions.

### Application

- `src/Toklong.Application/Abstractions/IShippingQuoteProvider.cs` — insured
  quote and mutation/reconciliation contracts.
- `src/Toklong.Application/Abstractions/IShippingOperationRepository.cs` —
  atomic add, lease, reload, and completion operations.
- `src/Toklong.Application/Features/Offers/RespondToBuyerOffer/RespondToBuyerOffer.cs`
  — persist acceptance intent plus outbound booking operation.
- `src/Toklong.Application/Features/Shipping/ProcessShippingOperations/ProcessShippingOperations.cs`
  — lease and execute one durable mutation.
- `src/Toklong.Application/Features/Shipping/ProcessProviderShipments/ProcessProviderShipments.cs`
  — safe tracking reads only.
- `src/Toklong.Application/Features/Shipping/ManageShippingExceptions/ManageShippingExceptions.cs`
  — authorized CRM exception, insurance, adjustment, and return commands.
- `src/Toklong.Application/Transactions/TransactionView.cs` — role-safe
  insurance, managed shipment, operation, and carrier event projection.

### Infrastructure and Worker

- `src/Toklong.Infrastructure/Persistence/ToklongDbContext.cs` — EF mappings.
- `src/Toklong.Infrastructure/Persistence/ShippingOperationRepository.cs` —
  PostgreSQL-safe leasing and operation queries.
- `src/Toklong.Infrastructure/Persistence/TransactionRepository.cs` — load
  managed shipment children and tracking reads.
- `src/Toklong.Infrastructure/Persistence/Migrations/20260729*_ShippopProductionShipping.cs`
  and model snapshot — schema.
- `src/Toklong.Infrastructure/Services/ShippopShippingProvider.cs` — insured
  quote fingerprint, strict timestamps, sanitized outcomes, return direction.
- `src/Toklong.Infrastructure/Services/DevelopmentShippingQuoteProvider.cs` —
  deterministic equivalent for tests and simulator.
- `src/Toklong.Infrastructure/DependencyInjection.cs` — repository, options,
  kill switches, and adapter registration.
- `src/Toklong.Infrastructure/ProductionConfigurationValidator.cs` — reject
  unsafe production enablement.
- `src/Toklong.Worker/ShippingOperationsWorker.cs` — configurable jittered
  operation and tracking loops.
- `src/Toklong.Worker/appsettings.json` and `src/Toklong.Api/appsettings.json` —
  non-secret disabled defaults.

### Mobile and CRM

- `src/Toklong.Mobile/Core/AppTransaction.cs` — role-safe shipping model and
  four milestones.
- `src/Toklong.Mobile/Controls/ShippingProgressView.xaml(.cs)` — reusable,
  accessible four-step progress.
- `src/Toklong.Mobile/Pages/TransactionDetailPage.xaml` — separate shipping
  card and journey disclosure.
- `src/Toklong.Mobile/ViewModels/TransactionDetailViewModel.cs` — journey
  expansion and presentation properties.
- `src/Toklong.Crm/Shipping/CrmShippingOperations.cs` and
  `src/Toklong.Crm/Components/Pages/ShippingCaseDetail.razor` — authorized
  review and return-resolution commands.

### Tests and certification

- Domain tests cover lifecycle invariants and transition authorization.
- Application tests cover operation atomicity, leases, retries, money,
  expiration cleanup, exceptions, and return gates.
- Provider tests cover request/response validation and trusted timestamps.
- Mobile tests cover role privacy, accessibility, and four-stage presentation.
- `scripts/shippop-certify.sh` invokes a server-side certification test project
  with environment secrets; it never prints secrets or writes raw payloads.

---

### Task 1: Make trusted delivery timestamps fail closed

**Files:**
- Modify: `src/Toklong.Application/Abstractions/IShippingQuoteProvider.cs`
- Modify: `src/Toklong.Infrastructure/Services/ShippopShippingProvider.cs`
- Modify: `src/Toklong.Application/Features/Shipping/ProcessProviderShipments/ProcessProviderShipments.cs`
- Test: `tests/Toklong.Application.Tests/Shipping/ShippopShippingProviderTests.cs`
- Test: `tests/Toklong.Application.Tests/Shipping/ProviderShipmentProcessingTests.cs`

**Interfaces:**
- Produces: `ShipmentTrackingUpdate.OccurredAt` becomes nullable.
- Produces: `ShipmentTrackingUpdate.HasTrustedOccurredAt`.
- Consumes: existing `SaleTransaction.RecordCarrierEvent(...)`.

- [ ] **Step 1: Write the provider regression test**

Add a test where SHIPPOP returns `order_status = "complete"` with no POD state:

```csharp
[Fact]
public async Task Complete_without_pod_time_is_unverified_and_has_no_event_time()
{
    var provider = Provider(_ => Task.FromResult(Json("""
        {
          "status": true,
          "order_status": "complete",
          "courier_code": "EMST",
          "tracking_code": "SP-NO-POD",
          "courier_tracking_code": "EF123456789TH",
          "states": [{ "status": "010", "datetime": "2026-07-26 09:00:00" }]
        }
        """)));

    var update = await provider.GetTrackingAsync(
        "SP-NO-POD", "THAIPOST", default);

    Assert.Equal("unverified", update.EventType);
    Assert.Null(update.OccurredAt);
}
```

- [ ] **Step 2: Run the test and confirm the unsafe fallback**

Run:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter FullyQualifiedName~ShippopShippingProviderTests.Complete_without_pod
```

Expected: FAIL because current code maps to `delivered` and substitutes another
carrier event or `clock.UtcNow`.

- [ ] **Step 3: Make event time nullable and normalize incomplete delivery**

Change the contract to:

```csharp
public sealed record ShipmentTrackingUpdate(
    string ProviderTrackingCode,
    string? CourierTrackingCode,
    string CarrierCode,
    string ProviderStatus,
    string? EventType,
    string EventId,
    DateTimeOffset? OccurredAt)
{
    public bool HasTrustedOccurredAt => OccurredAt.HasValue;
}
```

In `GetTrackingAsync`, map `complete` to `delivered` only when
`LatestEventTime(root, "delivered")` finds a POD timestamp; otherwise return
`EventType = "unverified"` and `OccurredAt = null`. Build the deterministic
event ID from provider status plus the literal `"missing-time"` when time is
absent. Do not fall back to the latest non-POD state, `datetime_shipping`, or
`clock.UtcNow`.

- [ ] **Step 4: Make reconciliation reject missing event time**

Before `RecordCarrierEvent`, require `update.OccurredAt.HasValue`. For
`unverified` with no time, use the Worker reconciliation time only as the audit
receipt time while calling a new domain method:

```csharp
transaction.RecordUnverifiedCarrierEvidence(
    shipmentProvider.ProviderName,
    update.EventId,
    update.ProviderStatus,
    clock.UtcNow,
    transitions);
```

That method may transition to `TrackingUnverified`, but must not set
`DeliveredAt`, `DisputeWindowStartsAt`, or `DisputeWindowEndsAt`.

- [ ] **Step 5: Run focused tracking tests**

Run:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter "FullyQualifiedName~ShippopShippingProviderTests|FullyQualifiedName~ProviderShipmentProcessingTests"
```

Expected: PASS, including assertions that missing POD time never starts the
inspection window.

- [ ] **Step 6: Commit**

```bash
git add src/Toklong.Application/Abstractions/IShippingQuoteProvider.cs \
  src/Toklong.Infrastructure/Services/ShippopShippingProvider.cs \
  src/Toklong.Application/Features/Shipping/ProcessProviderShipments/ProcessProviderShipments.cs \
  src/Toklong.Domain/Transactions/SaleTransaction.cs \
  tests/Toklong.Application.Tests/Shipping/ShippopShippingProviderTests.cs \
  tests/Toklong.Application.Tests/Shipping/ProviderShipmentProcessingTests.cs
git commit -m "fix: require trusted SHIPPOP delivery time"
```

### Task 2: Add insured quote and immutable shipping money

**Files:**
- Modify: `src/Toklong.Application/Abstractions/IShippingQuoteProvider.cs`
- Modify: `src/Toklong.Domain/Transactions/SaleTransaction.cs`
- Modify: `src/Toklong.Application/Transactions/TransactionView.cs`
- Modify: `src/Toklong.Infrastructure/Services/DevelopmentShippingQuoteProvider.cs`
- Modify: `src/Toklong.Infrastructure/Services/ShippopShippingProvider.cs`
- Test: `tests/Toklong.Domain.Tests/Transactions/ShippingMoneyTests.cs`
- Test: `tests/Toklong.Application.Tests/Shipping/ShippopShippingProviderTests.cs`
- Test: `tests/Toklong.Application.Tests/Offers/BuyerOfferFlowTests.cs`

**Interfaces:**
- Produces: `ShippingQuoteOption` insurance fields.
- Produces: snapshot version 9 and agreement-core version 7.
- Produces: `SaleTransaction.ParcelInsuranceFeeSatang`,
  `ShippingDeclaredValueSatang`, and `ShippingInsuranceCode`.

- [ ] **Step 1: Add failing integer-money and seller-net tests**

Test a 120,000-satang item, 5,200-satang shipping, 1,100-satang insurance, and
5,900-satang Buyer Protection:

```csharp
Assert.Equal(132_200, transaction.BuyerTotalSatang);
Assert.Equal(1_100, transaction.ParcelInsuranceFeeSatang);
Assert.Equal(120_000, transaction.ShippingDeclaredValueSatang);
Assert.Equal(120_000, transaction.SellerExpectedNetSatang);
Assert.Equal(9, transaction.SnapshotSchemaVersion);
```

Also assert negative insurance, coverage below item price, and checked overflow
throw `DomainException`.

- [ ] **Step 2: Extend quote and reservation contracts**

Use these exact fields:

```csharp
public sealed record ShippingQuoteOption(
    string Provider,
    string QuoteReference,
    string CarrierCode,
    string ServiceCode,
    string ServiceName,
    long ShippingFeeSatang,
    long InsuranceFeeSatang,
    long DeclaredValueSatang,
    string InsuranceCode,
    DateTimeOffset ExpiresAt)
{
    public long ProviderTotalSatang =>
        checked(ShippingFeeSatang + InsuranceFeeSatang);
}
```

`ShipmentReservation` repeats all four money/insurance values returned by the
provider. Quote fingerprints bind those values.

- [ ] **Step 3: Update agreement and paid snapshots**

Set:

```csharp
public const int AgreementSnapshotSchemaVersion = 9;
public const int AgreementCoreSchemaVersion = 7;
```

Calculate:

```csharp
BuyerTotalSatang = checked(
    PriceSatang +
    ShippingFeeSatang +
    ParcelInsuranceFeeSatang +
    BuyerProtectionFeeSatang);
```

Include shipping fee, insurance fee/code, declared value, service, expiry, and
seller net in the immutable agreement core. Do not add provider lifecycle
timestamps or full addresses to the shared core.

- [ ] **Step 4: Keep SHIPPOP services disabled without certified insurance**

Add `ShippopServiceProfile` configuration with `Enabled`, `HandoffMode`,
`InsuranceCode`, `MaximumDeclaredValueSatang`, and
`CertificationReference`. `GetQuotesAsync` omits a service unless all values
are present, handoff is `DropOff`, the item value is covered, and `Enabled` is
true. Defaults for all four service codes are disabled.

- [ ] **Step 5: Run money, offer, and provider tests**

Run:

```bash
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj \
  --filter FullyQualifiedName~ShippingMoneyTests
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter "FullyQualifiedName~BuyerOfferFlowTests|FullyQualifiedName~ShippopShippingProviderTests"
```

Expected: PASS with no floating-point money operations.

- [ ] **Step 6: Commit**

```bash
git add src/Toklong.Application/Abstractions/IShippingQuoteProvider.cs \
  src/Toklong.Domain/Transactions/SaleTransaction.cs \
  src/Toklong.Application/Transactions/TransactionView.cs \
  src/Toklong.Infrastructure/Services/DevelopmentShippingQuoteProvider.cs \
  src/Toklong.Infrastructure/Services/ShippopShippingProvider.cs \
  tests/Toklong.Domain.Tests/Transactions/ShippingMoneyTests.cs \
  tests/Toklong.Application.Tests/Shipping/ShippopShippingProviderTests.cs \
  tests/Toklong.Application.Tests/Offers/BuyerOfferFlowTests.cs
git commit -m "feat: lock insured shipping amounts"
```

### Task 3: Add managed shipments and durable operations

**Files:**
- Create: `src/Toklong.Domain/Transactions/ManagedShipment.cs`
- Create: `src/Toklong.Domain/Transactions/ShippingOperation.cs`
- Create: `src/Toklong.Domain/Transactions/ProviderShippingAdjustment.cs`
- Create: `src/Toklong.Domain/Transactions/ShippingInsuranceCase.cs`
- Create: `src/Toklong.Application/Abstractions/IShippingOperationRepository.cs`
- Modify: `src/Toklong.Domain/Transactions/SaleTransaction.cs`
- Test: `tests/Toklong.Domain.Tests/Transactions/ShippingOperationTests.cs`

**Interfaces:**
- Produces: `ManagedShipment.CreateOutbound(...)`,
  `ManagedShipment.CreateReturn(...)`.
- Produces: `ShippingOperation.Queue(...)`, `Claim(...)`,
  `ScheduleRetry(...)`, `MarkOutcomeUnknown(...)`, `Succeed(...)`, and
  `SendToReview(...)`.

- [ ] **Step 1: Write lifecycle tests**

Cover one outbound per transaction, at most one active return, globally unique
idempotency keys, live-lease claim rejection, expired-lease reclaim, and the
rule that `OutcomeUnknown` cannot be scheduled for retry without an explicit
`providerReplayProvenSafe` flag.

- [ ] **Step 2: Implement exact enums**

```csharp
public enum ShipmentDirection { Outbound, Return }
public enum ManagedShipmentStatus
{
    PendingBooking, Reserved, Confirmed, CarrierAccepted, InTransit,
    Delivered, Cancelled, TrackingUnverified, CarrierException
}
public enum ShippingOperationType
{
    BookOutbound, ConfirmOutbound, CancelOutbound,
    BookReturn, ConfirmReturn, CancelReturn
}
public enum ShippingOperationStatus
{
    Pending, Processing, RetryScheduled, OutcomeUnknown, Succeeded, NeedsReview
}
```

- [ ] **Step 3: Implement operation invariants**

`Queue` requires non-empty transaction/shipment IDs, a normalized idempotency
key of at most 160 characters, a 64-character lowercase SHA-256 request
fingerprint, and `nextAttemptAt`. `Claim` increments attempts and sets a
five-minute default lease. All result methods require the matching lease owner
and clear the lease. Only a sanitized error code of at most 100 characters is
retained.

- [ ] **Step 4: Add append-only operational record invariants**

`ProviderShippingAdjustment.Create(...)` requires a unique provider reference,
positive integer-satang amount, ISO `THB`, provider occurrence time, and
authorized CRM case reference. `ShippingInsuranceCase.Open(...)` requires a
provider case reference, reason, declared/claimed integer-satang values, and
CRM reference. Its only mutation is authorized `Resolve(...)`; resolution
cannot call a transaction payout/refund transition.

- [ ] **Step 5: Attach shipment children to the aggregate**

Expose read-only `ManagedShipments`, `ShippingOperations`,
`ProviderShippingAdjustments`, and `ShippingInsuranceCases`. Creation methods
append audit events with deterministic idempotency keys. No method accepts or
stores an API key.

- [ ] **Step 6: Run domain tests and commit**

Run:

```bash
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj \
  --filter FullyQualifiedName~ShippingOperationTests
```

Then commit only Task 3 files with:

```bash
git commit -m "feat: model durable shipping operations"
```

### Task 4: Persist and lease operations atomically

**Files:**
- Modify: `src/Toklong.Infrastructure/Persistence/ToklongDbContext.cs`
- Create: `src/Toklong.Infrastructure/Persistence/ShippingOperationRepository.cs`
- Modify: `src/Toklong.Infrastructure/Persistence/TransactionRepository.cs`
- Modify: `src/Toklong.Infrastructure/DependencyInjection.cs`
- Create: `src/Toklong.Infrastructure/Persistence/Migrations/20260729190000_ShippopProductionShipping.cs`
- Create: `src/Toklong.Infrastructure/Persistence/Migrations/20260729190000_ShippopProductionShipping.Designer.cs`
- Modify: `src/Toklong.Infrastructure/Persistence/Migrations/ToklongDbContextModelSnapshot.cs`
- Test: `tests/Toklong.Application.Tests/Shipping/ShippingOperationPersistenceTests.cs`

**Interfaces:**
- Implements: `IShippingOperationRepository`.
- Produces: `ClaimDueAsync(string workerId, DateTimeOffset now,
  TimeSpan leaseDuration, CancellationToken)` returning at most one claimed
  operation with its transaction and shipment.

- [ ] **Step 1: Write SQLite concurrency and atomicity tests**

Use two `ToklongDbContext` instances against one SQLite database. Assert:

- the transaction, shipment, and operation commit together;
- duplicate idempotency key violates the unique index;
- two concurrent claims return the operation to only one Worker;
- an expired lease can be reclaimed;
- a live lease cannot be reclaimed.

- [ ] **Step 2: Map tables and constraints**

Create `managed_shipments`, `shipping_operations`,
`provider_shipping_adjustments`, and `shipping_insurance_cases`. Add:

```text
UNIQUE managed_shipments(transaction_id, direction)
UNIQUE shipping_operations(idempotency_key)
INDEX shipping_operations(status, next_attempt_at)
INDEX shipping_operations(lease_expires_at)
UNIQUE provider_shipping_adjustments(provider_reference)
UNIQUE shipping_insurance_cases(provider_case_reference)
```

Use string enum conversions, integer `bigint` money, timestamp-with-time-zone,
and concurrency tokens.

- [ ] **Step 3: Implement PostgreSQL-safe claim**

Within one transaction, select one due row ordered by
`next_attempt_at, created_at`, require no live lease, call `Claim`, and save.
On concurrency conflict, rollback and return `null`; do not process the same
row in memory.

- [ ] **Step 4: Generate and inspect the migration**

Run:

```bash
dotnet ef migrations add ShippopProductionShipping \
  --project src/Toklong.Infrastructure/Toklong.Infrastructure.csproj \
  --startup-project src/Toklong.Api/Toklong.Api.csproj \
  --output-dir Persistence/Migrations
dotnet ef migrations script \
  --project src/Toklong.Infrastructure/Toklong.Infrastructure.csproj \
  --startup-project src/Toklong.Api/Toklong.Api.csproj
```

Expected: four new tables, exact unique indexes, no dropped paid-snapshot
columns, and no secret-bearing column.

- [ ] **Step 5: Run persistence tests and commit**

Run:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter FullyQualifiedName~ShippingOperationPersistenceTests
```

Commit with:

```bash
git commit -m "feat: persist and lease shipping operations"
```

### Task 5: Queue seller acceptance instead of calling booking inline

**Files:**
- Modify: `src/Toklong.Application/Features/Offers/RespondToBuyerOffer/RespondToBuyerOffer.cs`
- Modify: `src/Toklong.Domain/Transactions/SaleTransaction.cs`
- Modify: `src/Toklong.Application/Transactions/TransactionView.cs`
- Modify: `src/Toklong.Api/Api/MobileApi.cs`
- Modify: `src/Toklong.Mobile/Core/AppTransaction.cs`
- Modify: `src/Toklong.Mobile/ViewModels/SellerOfferViewModel.cs`
- Test: `tests/Toklong.Application.Tests/Offers/BuyerOfferFlowTests.cs`
- Test: `tests/Toklong.Api.Tests/Api/MobileSellerOfferApiTests.cs`

**Interfaces:**
- Produces: `SaleTransaction.BeginManagedSellerAcceptance(...)`.
- Produces: `TransactionView.ShippingOperationStatus`.
- Consumes: `ManagedShipment.CreateOutbound` and `ShippingOperation.Queue`.

- [ ] **Step 1: Write failing atomic-queue tests**

Assert that pressing accept:

- validates actor, payout, quote, insurance, origin, and parcel;
- creates one outbound shipment and one `BookOutbound`;
- does not call `IShipmentProvider.ReserveAsync`;
- remains unavailable for buyer checkout until booking succeeds;
- repeated requests return the same pending operation;
- failure to save creates neither acceptance nor provider call.

- [ ] **Step 2: Split acceptance into begin and complete**

`BeginManagedSellerAcceptance` stores the validated seller/payout and immutable
shipment intent but does not create agreement acceptance evidence, transition
to `SellerAcceptedAwaitingPayment`, or set the one-hour deadline.

`CompleteManagedSellerAcceptance` receives the matching reservation, validates
provider/carrier/service/shipping fee/insurance/declared value, then creates
seller acceptance evidence, transitions, and sets:

```csharp
SellerAcceptedAt = completedAt;
BuyerPaymentDeadlineAt = completedAt.AddHours(BuyerPaymentWindowHours);
```

- [ ] **Step 3: Queue deterministic operation**

Use:

```text
idempotency_key = book-outbound:{transactionId:N}:{quoteFingerprint}
request_fingerprint = SHA256(managed shipment immutable request)
```

Return the current `TransactionView` with operation status `Pending`. The API
returns HTTP 202 for a pending booking and HTTP 200 when an idempotent replay
finds a succeeded operation.

- [ ] **Step 4: Present pending status without enabling payment**

Use seller copy `กำลังเตรียมรายการจัดส่ง` and supporting copy
`ระบบกำลังยืนยันบริการขนส่งที่เลือก`. Disable the accept button while the
same operation is pending. Buyer routes remain unavailable until state becomes
`SellerAcceptedAwaitingPayment`.

- [ ] **Step 5: Run offer/API/mobile tests and commit**

Run:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter FullyQualifiedName~BuyerOfferFlowTests
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj \
  --filter FullyQualifiedName~MobileSellerOfferApiTests
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter FullyQualifiedName~ManagedShipping
```

Commit with:

```bash
git commit -m "feat: queue managed seller acceptance"
```

### Task 6: Execute booking, confirmation, and cancellation safely

**Files:**
- Create: `src/Toklong.Application/Features/Shipping/ProcessShippingOperations/ProcessShippingOperations.cs`
- Modify: `src/Toklong.Application/Abstractions/IShippingQuoteProvider.cs`
- Modify: `src/Toklong.Infrastructure/Services/ShippopShippingProvider.cs`
- Modify: `src/Toklong.Infrastructure/Services/DevelopmentShippingQuoteProvider.cs`
- Modify: `src/Toklong.Worker/ShippingOperationsWorker.cs`
- Modify: `src/Toklong.Application/Features/Transactions/EvaluateDueExpirations/EvaluateDueExpirations.cs`
- Test: `tests/Toklong.Application.Tests/Shipping/DurableShippingOperationProcessingTests.cs`
- Test: `tests/Toklong.Application.Tests/Transactions/OfferExpirationTests.cs`

**Interfaces:**
- Produces: `ProcessNextShippingOperationCommand(string WorkerId)`.
- Produces: `ShipmentMutationException` with
  `ShipmentMutationOutcome.DefiniteFailure` or `OutcomeUnknown`.
- Consumes: operation repository leases and provider adapter.

- [ ] **Step 1: Write crash/timeout/retry tests**

Test:

- successful booking completes seller acceptance exactly once;
- timeout after request send records `OutcomeUnknown`;
- `OutcomeUnknown` is not replayed;
- definite pre-send network failure schedules exponential retry with jitter;
- carrier/service/money mismatch goes to `NeedsReview`;
- provider result and operation success commit together;
- payment-confirmed transaction queues exactly one `ConfirmOutbound`;
- unpaid expiry immediately sets `Expired/BuyerDidNotPay` and queues
  `CancelOutbound` without extending the deadline.

- [ ] **Step 2: Make mutation outcome explicit**

Provider mutations return validated results. Failures use:

```csharp
public enum ShipmentMutationOutcome
{
    DefiniteFailure,
    OutcomeUnknown
}

public sealed class ShipmentMutationException(
    ShipmentMutationOutcome outcome,
    string sanitizedCode) : Exception
{
    public ShipmentMutationOutcome Outcome { get; } = outcome;
    public string SanitizedCode { get; } = sanitizedCode;
}
```

No raw provider body, address, phone, API key, or label HTML enters the
exception.

- [ ] **Step 3: Implement the operation processor**

Claim one operation, load transaction and shipment, recompute and compare
`RequestFingerprint`, call the matching provider method outside the claim
transaction, reload both rows, verify lease ownership, apply the matching
domain result, save audit/analytics plus operation completion atomically.

For booking `OutcomeUnknown`, attempt only the certified lookup by TOKLONG
reference. If provider lookup is unsupported or inconclusive, mark
`NeedsReview` and leave the service disabled for production.

- [ ] **Step 4: Replace fixed 15-second loop**

Bind:

```json
{
  "ShippingWorker": {
    "OperationIdleSeconds": 5,
    "TrackingIntervalSeconds": 120,
    "TrackingJitterSeconds": 30,
    "LeaseSeconds": 300,
    "MaximumAttempts": 8
  }
}
```

Operation work may run promptly; tracking follows its own configurable jittered
schedule. Mobile refresh must not trigger SHIPPOP.

- [ ] **Step 5: Run operation and expiration tests and commit**

Run:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter "FullyQualifiedName~DurableShippingOperationProcessingTests|FullyQualifiedName~OfferExpirationTests|FullyQualifiedName~PaymentDeadlineTests"
```

Commit with:

```bash
git commit -m "feat: process SHIPPOP mutations durably"
```

### Task 7: Fail carrier exceptions closed and retain adjustments

**Files:**
- Modify: `src/Toklong.Domain/Transactions/ProviderShippingAdjustment.cs`
- Modify: `src/Toklong.Domain/Transactions/ShippingInsuranceCase.cs`
- Create: `src/Toklong.Application/Features/Shipping/ManageShippingExceptions/ManageShippingExceptions.cs`
- Modify: `src/Toklong.Domain/Transactions/SaleTransaction.cs`
- Modify: `src/Toklong.Domain/Transactions/TransactionState.cs`
- Modify: `src/Toklong.Domain/Transactions/TransactionTransitionService.cs`
- Modify: `src/Toklong.Application/Features/Payouts/EvaluateDuePayouts/EvaluateDuePayouts.cs`
- Modify: `src/Toklong.Application/Features/Refunds/ProcessRefunds/ProcessRefunds.cs`
- Create: `src/Toklong.Crm/Shipping/CrmShippingOperations.cs`
- Create: `src/Toklong.Crm/Components/Pages/ShippingCaseDetail.razor`
- Test: `tests/Toklong.Domain.Tests/Transactions/CarrierExceptionTests.cs`
- Test: `tests/Toklong.Application.Tests/Shipping/ShippingExceptionGateTests.cs`
- Test: `tests/Toklong.Crm.Tests/Shipping/CrmShippingAuthorizationTests.cs`

**Interfaces:**
- Produces: `TransactionState.CarrierException`.
- Produces: `OpenCarrierExceptionCommand`, `RecordShippingAdjustmentCommand`,
  `OpenInsuranceCaseCommand`, `ResolveInsuranceCaseCommand`.

- [ ] **Step 1: Write payout/refund block tests**

For missing delivery time, `problem`, `invalid`, return status, unknown status,
tracking mismatch, surcharge, or open insurance case, assert:

```csharp
Assert.False(transaction.IsPayoutEligible);
Assert.False(transaction.IsAutomaticRefundEligible);
Assert.Null(transaction.DeliveredAt);
```

Replay the same evidence and assert one case and one audit event.

- [ ] **Step 2: Add allow-listed review transitions**

Allow `TrackingSubmitted`, `TrackingUnverified`, `InTransit`, and
`RefundPending` to enter `CarrierException` only through CarrierProvider,
Reconciliation, or System. Only an authorized reconciliation command with
actor, reason, case reference, and idempotency key may leave it.

- [ ] **Step 3: Persist append-only adjustments and cases**

Adjustment amount must be positive integer satang and must not call any
`SaleTransaction` money setter. Insurance resolution records provider result
but does not choose refund or payout.

- [ ] **Step 4: Add application gates**

Payout queries exclude any transaction with a non-resolved shipping or
insurance case. Refund processing excludes return-required or carrier-review
transactions until an authorized resolution has set the exact next path.

- [ ] **Step 5: Add authorized CRM review**

`CrmShippingOperations` resolves the authenticated workforce subject and
requires the existing shipping-operations role plus step-up authorization for
resolution. The page shows sanitized carrier evidence, immutable agreement
references, adjustment/insurance status, and audit history. Every command
requires a non-empty reason and idempotency key. The page never renders API
keys, raw provider payloads, full addresses, or ordinary account passwords.

- [ ] **Step 6: Run state, payout, refund, replay, and CRM authorization tests and commit**

Run:

```bash
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj \
  --filter "FullyQualifiedName~CarrierExceptionTests|FullyQualifiedName~Transition"
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter "FullyQualifiedName~ShippingExceptionGateTests|FullyQualifiedName~Payout|FullyQualifiedName~Refund"
dotnet test tests/Toklong.Crm.Tests/Toklong.Crm.Tests.csproj \
  --filter FullyQualifiedName~CrmShippingAuthorizationTests
```

Commit with:

```bash
git commit -m "feat: block money flows on shipping exceptions"
```

### Task 8: Add provider-managed return and refund gate

**Files:**
- Modify: `src/Toklong.Application/Abstractions/IShippingQuoteProvider.cs`
- Modify: `src/Toklong.Domain/Transactions/ManagedShipment.cs`
- Modify: `src/Toklong.Domain/Transactions/SaleTransaction.cs`
- Modify: `src/Toklong.Application/Features/Shipping/ManageShippingExceptions/ManageShippingExceptions.cs`
- Modify: `src/Toklong.Application/Features/Shipping/ProcessShippingOperations/ProcessShippingOperations.cs`
- Modify: `src/Toklong.Application/Features/Refunds/ProcessRefunds/ProcessRefunds.cs`
- Modify: `src/Toklong.Infrastructure/Services/ShippopShippingProvider.cs`
- Test: `tests/Toklong.Application.Tests/Shipping/ManagedReturnTests.cs`

**Interfaces:**
- Produces: `AuthorizeManagedReturnCommand`.
- Produces: `ReturnShipmentRequest` with outbound destination as return origin
  and outbound origin as return destination.
- Consumes: `BookReturn`, `ConfirmReturn`, `CancelReturn`.

- [ ] **Step 1: Write return separation and refund-gate tests**

Assert return purchase/tracking references differ from outbound, TOKLONG return
cost is an operational adjustment, outbound paid snapshot is byte-identical,
and refund cannot start before trusted return delivery or explicit authorized
manual resolution.

- [ ] **Step 2: Authorize return with CRM identity**

Require transaction in `ResolutionPending`, an authorized actor, case
reference, reason, idempotency key, parcel snapshot, and certified return
service. Create a return shipment plus `BookReturn` atomically. Never reuse an
outbound provider reference.

- [ ] **Step 3: Process return tracking**

Normalize the same trusted statuses against the return shipment. Trusted return
delivery records `ReturnDeliveredAt`; missing timestamp or exception enters
review. It must not set the outbound `DeliveredAt` or inspection window.

- [ ] **Step 4: Gate refund**

`ProcessRefunds` may prepare the provider refund only when:

```text
return not required
OR trusted return delivery exists
OR authorized manual return resolution exists
```

Provider-confirmed Stripe refund completion remains mandatory.

- [ ] **Step 5: Run return/refund tests and commit**

Run:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter "FullyQualifiedName~ManagedReturnTests|FullyQualifiedName~Refund"
```

Commit with:

```bash
git commit -m "feat: manage returns before refund"
```

### Task 9: Deliver role-safe API and four-step mobile shipping UI

**Files:**
- Modify: `src/Toklong.Application/Transactions/TransactionView.cs`
- Modify: `src/Toklong.Api/Api/MobileApi.cs`
- Modify: `src/Toklong.Mobile/Core/AppTransaction.cs`
- Create: `src/Toklong.Mobile/Controls/ShippingProgressView.xaml`
- Create: `src/Toklong.Mobile/Controls/ShippingProgressView.xaml.cs`
- Modify: `src/Toklong.Mobile/Pages/TransactionDetailPage.xaml`
- Modify: `src/Toklong.Mobile/ViewModels/TransactionDetailViewModel.cs`
- Test: `tests/Toklong.Api.Tests/Api/MobileTransactionShippingPrivacyTests.cs`
- Test: `tests/Toklong.Mobile.Core.Tests/ShippingProgressPresentationTests.cs`
- Test: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`

**Interfaces:**
- Produces: `ShippingMilestone` values `Preparing`, `CarrierAccepted`,
  `InTransit`, `Delivered`.
- Produces: carrier journey entries with normalized description, optional
  location, and exact timestamp.

- [ ] **Step 1: Write API privacy tests**

Buyer response contains item, shipping, insurance, Buyer Protection, and total.
Seller response contains item, shipping, insurance, insured value, and expected
net, but serializes neither Buyer Protection amount nor buyer total. Neither
role receives provider keys, raw provider payload, private counterparty address,
or internal operation errors.

- [ ] **Step 2: Implement milestone presenter**

Map:

```text
confirmed label/no scan → เตรียมจัดส่ง
first trusted scan      → ขนส่งรับพัสดุแล้ว
verified in transit     → กำลังจัดส่ง
trusted delivery        → ส่งถึงแล้ว
```

All exception states use `การจัดส่งต้องตรวจสอบ`; the primary action is
`ดูรายละเอียด`.

- [ ] **Step 3: Build reusable accessible progress control**

Use four equal columns, app-color border and active fill, centered labels under
icons, dynamic type without clipping, semantic descriptions such as
`ขั้นที่ 2 จาก 4 ขนส่งรับพัสดุแล้ว`, and minimum 44×44-point disclosure target.
Do not replace the main three-stage transaction progress.

- [ ] **Step 4: Add journey disclosure**

The card initially shows milestone, service, tracking, and exact latest trusted
time. `รายละเอียดการเดินทาง` expands normalized newest-first events. Do not
show raw SHIPPOP statuses or reconciliation jargon.

- [ ] **Step 5: Run API and mobile tests and commit**

Run:

```bash
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj \
  --filter FullyQualifiedName~MobileTransactionShippingPrivacyTests
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~ShippingProgressPresentationTests|FullyQualifiedName~UiLayoutConsistencyTests"
```

Commit with:

```bash
git commit -m "feat: show managed shipping progress"
```

### Task 10: Add operational controls and certification harness

**Files:**
- Modify: `src/Toklong.Infrastructure/Services/ShippopShippingProvider.cs`
- Modify: `src/Toklong.Infrastructure/ProductionConfigurationValidator.cs`
- Modify: `src/Toklong.Infrastructure/DependencyInjection.cs`
- Modify: `src/Toklong.Worker/ShippingOperationsWorker.cs`
- Modify: `src/Toklong.Api/appsettings.json`
- Modify: `src/Toklong.Worker/appsettings.json`
- Create: `tests/Toklong.Shippop.Certification/Toklong.Shippop.Certification.csproj`
- Create: `tests/Toklong.Shippop.Certification/ShippopServiceCertificationTests.cs`
- Create: `scripts/shippop-certify.sh`
- Create: `docs/SHIPPOP_CERTIFICATION_RUNBOOK.md`
- Test: `tests/Toklong.Application.Tests/Shipping/ShippopConfigurationTests.cs`

**Interfaces:**
- Produces: `ShippopServiceProfile`.
- Produces: `ShippingWorkerOptions`.
- Produces: per-capability kill switches.

- [ ] **Step 1: Write fail-closed configuration tests**

Production startup must fail if SHIPPOP is selected with HTTP, missing secrets,
enabled uncertified service, pickup handoff, coverage below supported maximum,
missing certification reference, or operation lookup disabled while booking is
enabled.

- [ ] **Step 2: Add kill switches and metrics**

Per service expose disabled-by-default switches for quote, outbound booking,
confirmation, return, and insurance. Emit counters/gauges for pending age,
lease expiry, outcome unknown, retry, confirmation lag, tracking lag,
cancellation backlog, missing delivery time, surcharge, and open cases.
Metric labels contain service code and sanitized error code only.

- [ ] **Step 3: Build secret-safe certification tests**

Read `SHIPPOP_BASE_URL`, `SHIPPOP_API_KEY`, `SHIPPOP_ACCOUNT_EMAIL`, and a
synthetic address JSON path from environment. Tests skip unless
`SHIPPOP_CERTIFY=1`. Output only service code, pass/fail contract fields,
sanitized test reference, and timestamps. Never print request bodies, contacts,
addresses, keys, or raw responses.

- [ ] **Step 4: Write the runbook**

For each `EMST`, `FLE`, `KRYX`, and `KRYS`, record quote, unconfirmed booking,
lookup/idempotency, confirm, 4×6 label, first scan, delivery/POD time, cancel
before scan, insurance code/value/unit/premium, surcharge, return, rate limit,
reviewer, and date. A failed or unknown cell keeps that capability disabled.

- [ ] **Step 5: Run configuration and full regression suites**

Run:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter FullyQualifiedName~ShippopConfigurationTests
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj
dotnet test tests/Toklong.Crm.Tests/Toklong.Crm.Tests.csproj
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj
dotnet build Toklong.slnx
```

Expected: all tests and build pass. Do not run live certification until rotated
credentials and synthetic test data are supplied through the environment.

- [ ] **Step 6: Commit**

```bash
git add src/Toklong.Infrastructure src/Toklong.Worker \
  src/Toklong.Api/appsettings.json tests/Toklong.Shippop.Certification \
  scripts/shippop-certify.sh docs/SHIPPOP_CERTIFICATION_RUNBOOK.md \
  tests/Toklong.Application.Tests/Shipping/ShippopConfigurationTests.cs
git commit -m "chore: gate and certify SHIPPOP services"
```

## Completion gate

Before calling the production flow complete:

1. Run `git diff --check`.
2. Confirm no old or rotated provider credential appears in Git history added
   by this work or in current tracked files.
3. Run all test projects and `dotnet build Toklong.slnx`.
4. Inspect the generated migration SQL.
5. Verify seller API/UI never contains Buyer Protection fee or buyer total.
6. Verify buyer API/UI shows shipping and parcel insurance separately.
7. Verify every money-changing outcome still requires its provider-confirmed
   event or authorized reconciliation.
8. Verify `complete` without trusted delivery time cannot set any delivery or
   payout deadline.
9. Verify every open dispute/carrier/insurance/return exception blocks payout.
10. Keep all SHIPPOP capabilities disabled until account-specific
    certification passes with rotated secrets.
