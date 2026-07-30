# Synchronous SHIPPOP Booking and Scalable Confirmation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the outbound SHIPPOP reservation into the buyer's payment request, return Stripe only after a validated reservation is committed, and retain durable post-payment confirmation without RabbitMQ.

**Architecture:** Stateless API instances coordinate a synchronous `force_confirm=0` reservation through a PostgreSQL-backed `BookingAttempt`. The verified Stripe webhook atomically queues the existing `ConfirmOutbound` operation, while the existing background deployment drains prioritized durable work with leases. Mobile checkout submits the protection election once and lets the payment request own reservation progress and retry.

**Tech Stack:** .NET 10, C# 14, ASP.NET Core Minimal APIs, MediatR, EF Core 10, PostgreSQL/Npgsql, Stripe PaymentSheet, .NET MAUI, xUnit

## Global Constraints

- The mobile client never calls SHIPPOP or Stripe server APIs directly.
- Use SHIPPOP `POST /booking/` with `force_confirm=0`; never use `force_confirm=1` during checkout.
- Never create or return a Stripe PaymentIntent until the matching booking result is validated and committed.
- Never infer payment from a client callback; require the verified Stripe webhook or authorized reconciliation.
- The seller cannot fulfill and payout cannot proceed until the paid booking is provider-confirmed.
- A timed-out booking is `TimedOut` and is never automatically replayed.
- Use integer satang plus ISO currency; never use floating-point money.
- Do not add RabbitMQ, Kafka, an unbounded in-memory queue, or a separate pre-payment booking service.
- Preserve immutable paid snapshots, transition-service authorization, immutable audit events, tracking idempotency, and dispute payout blocks.
- Keep every SHIPPOP Production service flag disabled until the certification gates in the approved design have evidence.
- Target a 1,000-request checkout burst and p95 API latency at or below three seconds; report provider latency separately.

---

## File Structure

### New files

- `src/Toklong.Domain/Transactions/BookingAttempt.cs` — lifecycle and immutable identity of one direct booking attempt.
- `src/Toklong.Application/Abstractions/IBookingAttemptRepository.cs` — atomic acquire/result persistence contract.
- `src/Toklong.Application/Features/Checkout/BookShipmentForPayment/BookShipmentForPayment.cs` — direct booking orchestration and safe result categories.
- `src/Toklong.Infrastructure/Persistence/BookingAttemptRepository.cs` — PostgreSQL/EF coordination for concurrent API instances.
- `src/Toklong.Application/Abstractions/IDirectBookingAdmission.cs` —
  checkout-only bulkhead and circuit-breaker contract.
- `src/Toklong.Infrastructure/Services/DirectBookingAdmission.cs` — bounded
  concurrency and short provider-failure circuit.
- `src/Toklong.Application/Features/Checkout/BookShipmentForPayment/DirectBookingMetrics.cs` —
  low-cardinality booking measurements.
- `src/Toklong.Infrastructure/Persistence/Migrations/20260730220000_SynchronousCheckoutBooking.cs` — booking-attempt table and indexes.
- `src/Toklong.Infrastructure/Persistence/Migrations/20260730220000_SynchronousCheckoutBooking.Designer.cs` — generated EF model metadata.
- `tests/Toklong.Domain.Tests/Transactions/BookingAttemptTests.cs` — lifecycle and validation tests.
- `tests/Toklong.Application.Tests/Checkout/DirectCheckoutBookingTests.cs` — direct booking, timeout, mismatch, crash-boundary, and idempotency tests.
- `tests/Toklong.Application.Tests/Persistence/BookingAttemptPersistenceTests.cs` — relational uniqueness and concurrent claim tests.
- `tests/Toklong.Application.Tests/Shipping/ShippingWorkerThroughputTests.cs` — drain, priority, and lease behavior.
- `tests/Toklong.Api.Tests/Api/MobileDirectBookingApiTests.cs` — HTTP status and no-Stripe-on-failure tests.
- `tests/Toklong.Mobile.Core.Tests/DirectBookingCheckoutViewModelTests.cs` — mobile busy, retry, and close-dialog behavior.
- `tests/Toklong.LoadTests/Toklong.LoadTests.csproj` — executable checkout load-test project.
- `tests/Toklong.LoadTests/Program.cs` — 1,000-request test and percentile/error report.
- `tests/Toklong.LoadTests/LoadTestFactory.cs` — in-process API host,
  PostgreSQL seed, and deterministic authentication.
- `tests/Toklong.LoadTests/LoadShipmentProvider.cs` — bounded-latency,
  thread-safe provider double with duplicate-call counters.

### Existing files changed

- `src/Toklong.Domain/Transactions/SaleTransaction.cs` — queue an outbound shipment intent without `BookOutbound`, record the direct reservation, and audit both actions.
- `src/Toklong.Application/Abstractions/IShippingQuoteProvider.cs` — carry the opaque booking-attempt reference to SHIPPOP.
- `src/Toklong.Application/Features/Checkout/ChooseParcelProtection/ChooseParcelProtection.cs` — persist the election and shipment intent without calling or queuing SHIPPOP.
- `src/Toklong.Application/Features/Checkout/PreparePaymentSheet/PreparePaymentSheet.cs` — invoke direct booking before Stripe.
- `src/Toklong.Application/Features/Shipping/ManagedShippingOperationQueue.cs` — keep confirmation/cancellation/return operations, remove new outbound booking creation from checkout.
- `src/Toklong.Application/Features/Shipping/ProcessShippingOperations/ProcessShippingOperations.cs` — retain legacy outbound rollback and returns; expose operation priority and batch processing.
- `src/Toklong.Infrastructure/Persistence/ToklongDbContext.cs` — map `BookingAttempt`.
- `src/Toklong.Infrastructure/DependencyInjection.cs` — register the repository and configure bounded SHIPPOP HTTP behavior.
- `src/Toklong.Infrastructure/Services/ShippopShippingProvider.cs` — use the attempt reference and preserve strict response validation.
- `src/Toklong.Infrastructure/ProductionConfigurationValidator.cs` — enforce direct-booking certification and HTTPS gates.
- `src/Toklong.Api/Api/MobileApi.cs` — require a checkout idempotency key and map safe direct-booking errors.
- `src/Toklong.Api/appsettings.json` and `src/Toklong.Worker/appsettings.json` — disabled direct-booking defaults and bounded runner settings.
- `src/Toklong.Mobile/Core/ITransactionService.cs` — model retryable payment preparation outcomes.
- `src/Toklong.Mobile/Services/StripePaymentSheetService.cs` — submit the stable checkout idempotency key and surface safe API failures.
- `src/Toklong.Mobile/ViewModels/TransactionDetailViewModel.cs` — remove booking polling and expose explicit retry.
- `src/Toklong.Mobile/Pages/TransactionDetailPage.xaml` — bind the protection
  dismissal action without disabling checkout.
- `src/Toklong.Worker/ShippingOperationsWorker.cs` — drain batches immediately and isolate confirmation from tracking cadence.
- `docs/01_USER_FLOWS_AND_STATE_MACHINE.md`
- `docs/02_UI_UX_AND_CONTENT_SPEC.md`
- `docs/03_BACKEND_TRANSACTION_RECORD.md`
- `docs/04_PAYMENT_SHIPPING_AND_DISPUTE_RULES.md`
- `docs/05_ACCEPTANCE_TESTS.md`
- `docs/06_OPEN_DECISIONS.md`
- `docs/08_SHIPPOP_PRODUCTION_FLOW.md`

---

### Task 1: BookingAttempt Domain Lifecycle

**Files:**
- Create: `src/Toklong.Domain/Transactions/BookingAttempt.cs`
- Create: `tests/Toklong.Domain.Tests/Transactions/BookingAttemptTests.cs`

**Interfaces:**
- Produces: `BookingAttempt.Create(Guid transactionId, Guid managedShipmentId, Guid buyerId, string idempotencyKey, string requestFingerprint, int attemptNumber, DateTimeOffset now)`
- Produces: `BookingAttempt.Claim(DateTimeOffset now)`, `Succeed(BookingAttemptSuccess, DateTimeOffset)`, `Fail(string, DateTimeOffset)`, and `TimeOut(string, DateTimeOffset)`
- Produces: `BookingAttemptStatus` and `BookingAttemptSuccess`

- [ ] **Step 1: Write lifecycle tests**

```csharp
[Fact]
public void Attempt_moves_from_created_to_calling_to_succeeded()
{
    var attempt = BookingAttempt.Create(
        TransactionId, ShipmentId, BuyerId, "checkout-001",
        new string('a', 64), 1, Now);

    attempt.Claim(Now.AddSeconds(1));
    attempt.Succeed(new BookingAttemptSuccess(
        "purchase-1", "provider-track-1", "courier-track-1",
        5_200, 600, 100_000, "THB", new string('b', 64)),
        Now.AddSeconds(2));

    Assert.Equal(BookingAttemptStatus.Succeeded, attempt.Status);
    Assert.Equal($"checkout:{attempt.Id:N}", attempt.ProviderReference);
    Assert.Equal("purchase-1", attempt.ProviderPurchaseId);
}

[Fact]
public void Timed_out_attempt_cannot_be_claimed_or_succeeded()
{
    var attempt = NewAttempt();
    attempt.Claim(Now);
    attempt.TimeOut("shippop-timeout", Now.AddSeconds(2));

    Assert.Throws<DomainException>(() => attempt.Claim(Now.AddSeconds(3)));
    Assert.Throws<DomainException>(() => attempt.Succeed(Success(), Now.AddSeconds(3)));
}

[Theory]
[InlineData("")]
[InlineData("not-a-sha256")]
public void Fingerprint_must_be_lowercase_sha256(string fingerprint) =>
    Assert.Throws<DomainException>(() => BookingAttempt.Create(
        TransactionId, ShipmentId, BuyerId, "checkout-001",
        fingerprint, 1, Now));

[Theory]
[InlineData(0)]
[InlineData(4)]
public void Attempt_number_is_limited_to_three(int attemptNumber) =>
    Assert.Throws<DomainException>(() => BookingAttempt.Create(
        TransactionId, ShipmentId, BuyerId, "checkout-001",
        new string('a', 64), attemptNumber, Now));
```

- [ ] **Step 2: Run the focused tests and verify the red state**

Run:

```bash
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj \
  --filter FullyQualifiedName~BookingAttemptTests
```

Expected: compilation fails because `BookingAttempt` and its related types do
not exist.

- [ ] **Step 3: Implement the domain entity**

```csharp
public enum BookingAttemptStatus
{
    Created,
    CallingProvider,
    Succeeded,
    Failed,
    TimedOut
}

public sealed record BookingAttemptSuccess(
    string ProviderPurchaseId,
    string ProviderTrackingCode,
    string? CourierTrackingCode,
    long ShippingFeeSatang,
    long ProtectionFeeSatang,
    long CoverageLimitSatang,
    string Currency,
    string ProviderResponseFingerprint);

public sealed class BookingAttempt
{
    private BookingAttempt() { }

    public Guid Id { get; private set; }
    public Guid TransactionId { get; private set; }
    public Guid ManagedShipmentId { get; private set; }
    public Guid BuyerId { get; private set; }
    public string IdempotencyKey { get; private set; } = "";
    public string RequestFingerprint { get; private set; } = "";
    public string ProviderReference { get; private set; } = "";
    public BookingAttemptStatus Status { get; private set; }
    public int AttemptNumber { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? ProviderPurchaseId { get; private set; }
    public string? ProviderTrackingCode { get; private set; }
    public string? CourierTrackingCode { get; private set; }
    public long? QuotedShippingFeeSatang { get; private set; }
    public long? QuotedProtectionFeeSatang { get; private set; }
    public long? QuotedCoverageLimitSatang { get; private set; }
    public string? Currency { get; private set; }
    public string? ProviderResponseFingerprint { get; private set; }
    public string? FailureCategory { get; private set; }
    public string? SafeFailureCode { get; private set; }
    public long Version { get; private set; }
}
```

Implement `Create`, `Claim`, `Succeed`, `Fail`, and `TimeOut` with:

- non-empty IDs;
- a 1–160 character idempotency key;
- exactly 64 lowercase hexadecimal fingerprint characters;
- non-negative satang values;
- three-letter uppercase currency;
- legal transitions only; and
- `ProviderReference = $"checkout:{Id:N}"`.

- [ ] **Step 4: Run the domain tests**

Run:

```bash
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj \
  --filter FullyQualifiedName~BookingAttemptTests
```

Expected: all `BookingAttemptTests` pass.

- [ ] **Step 5: Commit the lifecycle**

```bash
git add src/Toklong.Domain/Transactions/BookingAttempt.cs \
  tests/Toklong.Domain.Tests/Transactions/BookingAttemptTests.cs
git commit -m "feat: add checkout booking attempt lifecycle"
```

---

### Task 2: Atomic BookingAttempt Persistence

**Files:**
- Create: `src/Toklong.Application/Abstractions/IBookingAttemptRepository.cs`
- Create: `src/Toklong.Infrastructure/Persistence/BookingAttemptRepository.cs`
- Modify: `src/Toklong.Infrastructure/Persistence/ToklongDbContext.cs`
- Modify: `src/Toklong.Infrastructure/DependencyInjection.cs`
- Create: `src/Toklong.Infrastructure/Persistence/Migrations/20260730220000_SynchronousCheckoutBooking.cs`
- Create: `src/Toklong.Infrastructure/Persistence/Migrations/20260730220000_SynchronousCheckoutBooking.Designer.cs`
- Modify: `src/Toklong.Infrastructure/Persistence/Migrations/ToklongDbContextModelSnapshot.cs`
- Create: `tests/Toklong.Application.Tests/Persistence/BookingAttemptPersistenceTests.cs`

**Interfaces:**
- Consumes: `BookingAttempt` from Task 1.
- Produces: `AcquireBookingAttempt`, `AcquireBookingAttemptResult`, and `BookingAttemptAcquireState`.
- Produces: `IBookingAttemptRepository.AcquireAsync` and `GetAsync`. Result
  transitions remain tracked domain mutations and are committed through
  `IUnitOfWork`.

- [ ] **Step 1: Write relational concurrency and model tests**

```csharp
[Fact]
public async Task Concurrent_acquire_has_one_provider_caller()
{
    await using var database = await RelationalDatabase.CreateAsync();
    var command = Seed();
    await using var first = database.CreateContext();
    await using var second = database.CreateContext();

    var results = await Task.WhenAll(
        new BookingAttemptRepository(first).AcquireAsync(command, default),
        new BookingAttemptRepository(second).AcquireAsync(command, default));

    Assert.Single(results, x => x.State == BookingAttemptAcquireState.Acquired);
    Assert.Single(results, x => x.State == BookingAttemptAcquireState.InProgress);
    Assert.Equal(results[0].Attempt.Id, results[1].Attempt.Id);
}

[Fact]
public void Model_has_unique_transaction_idempotency_and_provider_reference()
{
    using var context = RelationalDatabase.CreateModelContext();
    var entity = context.Model.FindEntityType(typeof(BookingAttempt))!;

    Assert.Contains(entity.GetIndexes(), x => x.IsUnique &&
        x.Properties.Select(p => p.Name).SequenceEqual([
            nameof(BookingAttempt.TransactionId),
            nameof(BookingAttempt.IdempotencyKey)]));
    Assert.Contains(entity.GetIndexes(), x => x.IsUnique &&
        x.Properties.Single().Name == nameof(BookingAttempt.ProviderReference));
    Assert.Contains(entity.GetIndexes(), x => x.IsUnique &&
        x.Properties.Select(p => p.Name).SequenceEqual([
            nameof(BookingAttempt.TransactionId),
            nameof(BookingAttempt.AttemptNumber)]));
    Assert.True(entity.FindProperty(nameof(BookingAttempt.Version))!.IsConcurrencyToken);
}
```

- [ ] **Step 2: Run persistence tests and verify the red state**

Run:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter FullyQualifiedName~BookingAttemptPersistenceTests
```

Expected: compilation fails because the repository contract and mapping do not
exist.

- [ ] **Step 3: Define the repository contract**

```csharp
public enum BookingAttemptAcquireState
{
    Acquired,
    InProgress,
    Succeeded,
    Failed,
    TimedOut,
    RetryLimitReached,
    FingerprintConflict
}

public sealed record AcquireBookingAttempt(
    Guid TransactionId,
    Guid ManagedShipmentId,
    Guid BuyerId,
    string IdempotencyKey,
    string RequestFingerprint,
    DateTimeOffset Now);

public sealed record AcquireBookingAttemptResult(
    BookingAttempt Attempt,
    BookingAttemptAcquireState State);

public interface IBookingAttemptRepository
{
    Task<AcquireBookingAttemptResult> AcquireAsync(
        AcquireBookingAttempt request,
        CancellationToken cancellationToken);
    Task<BookingAttempt?> GetAsync(
        Guid transactionId,
        string idempotencyKey,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Map and implement atomic acquisition**

Add `DbSet<BookingAttempt> BookingAttempts` and map:

```csharp
attempt.ToTable("booking_attempts");
attempt.HasKey(x => x.Id);
attempt.Property(x => x.Id).ValueGeneratedNever();
attempt.HasIndex(x => new { x.TransactionId, x.IdempotencyKey }).IsUnique();
attempt.HasIndex(x => x.ProviderReference).IsUnique();
attempt.HasIndex(x => new { x.TransactionId, x.AttemptNumber }).IsUnique();
attempt.HasIndex(x => x.TransactionId).IsUnique()
    .HasFilter("\"Status\" IN ('Created', 'CallingProvider')");
attempt.HasIndex(x => new { x.TransactionId, x.Status, x.CreatedAt });
attempt.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
attempt.Property(x => x.IdempotencyKey).HasMaxLength(160);
attempt.Property(x => x.RequestFingerprint).HasMaxLength(64);
attempt.Property(x => x.ProviderReference).HasMaxLength(80);
attempt.Property(x => x.ProviderPurchaseId).HasMaxLength(160);
attempt.Property(x => x.ProviderTrackingCode).HasMaxLength(120);
attempt.Property(x => x.CourierTrackingCode).HasMaxLength(120);
attempt.Property(x => x.Currency).HasMaxLength(3);
attempt.Property(x => x.ProviderResponseFingerprint).HasMaxLength(64);
attempt.Property(x => x.FailureCategory).HasMaxLength(40);
attempt.Property(x => x.SafeFailureCode).HasMaxLength(100);
attempt.Property(x => x.Version).IsConcurrencyToken();
attempt.HasOne<ManagedShipment>().WithMany()
    .HasForeignKey(x => x.ManagedShipmentId).OnDelete(DeleteBehavior.Restrict);
```

`AcquireAsync` must use a short transaction. Insert the new attempt, catch the
unique-key race, reload the existing row, reject a different fingerprint, and
claim only `Created`. Never hold this transaction across an HTTP request.

When an existing `CallingProvider` has `StartedAt` older than the configured
three-second total request budget, atomically call
`TimeOut("checkout-process-interrupted", now)`, save it, and return `TimedOut`.
This closes a process-crash window without replaying the provider mutation.
Only a later explicit buyer action with a new idempotency key may create a new
attempt.

For a new idempotency key, calculate `AttemptNumber` from provider-calling
attempts for the same transaction created within the previous ten minutes.
Reject number four as `RetryLimitReached`. The unique
`(TransactionId, AttemptNumber)` index and partial unique active-attempt index
arbitrate different keys arriving on different API instances. If the partial
index wins elsewhere, return that active attempt as `InProgress` without a
provider call.

- [ ] **Step 5: Generate and inspect the migration**

Run:

```bash
dotnet ef migrations add SynchronousCheckoutBooking \
  --project src/Toklong.Infrastructure/Toklong.Infrastructure.csproj \
  --startup-project src/Toklong.Api/Toklong.Api.csproj
```

Expected: one migration creates `booking_attempts`, its foreign key, five
indexes, and updates the model snapshot. Normalize the generated migration ID
to `20260730220000_SynchronousCheckoutBooking` in both filenames and the
designer's `[Migration]` attribute; keep the generated class name
`SynchronousCheckoutBooking`.

- [ ] **Step 6: Register and run persistence tests**

Register:

```csharp
services.AddScoped<IBookingAttemptRepository, BookingAttemptRepository>();
```

Run:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter FullyQualifiedName~BookingAttemptPersistenceTests
```

Expected: all booking-attempt persistence tests pass, including the two-context
claim.

- [ ] **Step 7: Commit persistence**

```bash
git add src/Toklong.Application/Abstractions/IBookingAttemptRepository.cs \
  src/Toklong.Infrastructure/Persistence/BookingAttemptRepository.cs \
  src/Toklong.Infrastructure/Persistence/ToklongDbContext.cs \
  src/Toklong.Infrastructure/Persistence/Migrations \
  src/Toklong.Infrastructure/DependencyInjection.cs \
  tests/Toklong.Application.Tests/Persistence/BookingAttemptPersistenceTests.cs
git commit -m "feat: persist atomic checkout booking attempts"
```

---

### Task 3: Persist Protection Choice Without Queuing SHIPPOP

**Files:**
- Modify: `src/Toklong.Domain/Transactions/SaleTransaction.cs`
- Modify: `src/Toklong.Application/Features/Checkout/ChooseParcelProtection/ChooseParcelProtection.cs`
- Modify: `src/Toklong.Application/Features/Checkout/GetParcelProtection/GetParcelProtection.cs`
- Modify: `tests/Toklong.Domain.Tests/Transactions/ShippingMoneyTests.cs`
- Modify: `tests/Toklong.Application.Tests/Checkout/ParcelProtectionCheckoutTests.cs`
- Modify: `tests/Toklong.Application.Tests/Checkout/ParcelProtectionChangeTests.cs`
- Modify: `tests/Toklong.Api.Tests/Api/MobileParcelProtectionApiTests.cs`

**Interfaces:**
- Produces: `SaleTransaction.QueueBuyerCheckoutShipmentIntent(ManagedShipment shipment, Guid buyerId, string idempotencyKey, DateTimeOffset now)`.
- Produces: `SaleTransaction.QueueReplacementOutboundShipmentIntent(ManagedShipment shipment, Guid changeRequestId, DateTimeOffset now)`.
- Changes: `ChooseParcelProtectionResult.BookingStatus` returns `selection_saved`, `cancelling_shipping`, or `reconfirmation_required`; it no longer returns `preparing_shipping` for a new election.
- Preserves: cancellation-before-rebooking when an earlier provider reservation already exists.

- [ ] **Step 1: Replace queue-expectation tests with intent tests**

```csharp
[Fact]
public async Task Election_creates_shipment_intent_without_book_operation()
{
    var result = await handler.Handle(Command(addProtection: true), default);

    Assert.Equal("selection_saved", result.BookingStatus);
    Assert.Single(transaction.ManagedShipments);
    Assert.DoesNotContain(transaction.ShippingOperations,
        x => x.OperationType == ShippingOperationType.BookOutbound);
    Assert.Contains(transaction.AuditEvents,
        x => x.EventType == "parcel_protection.booking_intent_created");
}

[Fact]
public async Task Repeated_election_key_reuses_the_same_shipment_intent()
{
    await handler.Handle(Command(idempotencyKey: "choice-001"), default);
    await handler.Handle(Command(idempotencyKey: "choice-001"), default);

    Assert.Single(transaction.ManagedShipments);
}
```

- [ ] **Step 2: Run checkout tests and verify the red state**

Run:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter "FullyQualifiedName~ParcelProtectionCheckoutTests|FullyQualifiedName~ParcelProtectionChangeTests"
```

Expected: tests fail because the handler still queues `BookOutbound`.

- [ ] **Step 3: Add the aggregate intent method**

```csharp
public void QueueBuyerCheckoutShipmentIntent(
    ManagedShipment shipment,
    Guid buyerId,
    string idempotencyKey,
    DateTimeOffset now)
{
    if (State != TransactionState.SellerAcceptedAwaitingPayment ||
        BuyerId != buyerId ||
        shipment.TransactionId != Id ||
        shipment.Direction != ShipmentDirection.Outbound)
        throw new DomainException("ข้อมูลรายการจัดส่งไม่ถูกต้อง");

    EnsureBuyerPaymentWindowOpen(now);
    var key = $"parcel-protection-booking:{Id:N}:{idempotencyKey}";
    if (_auditEvents.Any(x => x.IdempotencyKey == key))
        return;

    _managedShipments.Add(shipment);
    RecordParcelProtectionBookingIntent(shipment, buyerId, idempotencyKey, now);
}
```

Keep the current aggregate checks that only one non-cancelled outbound shipment
is active and that the shipment draft matches the election.

- [ ] **Step 4: Remove the new-booking queue path**

In `QueueBookingAsync`, create the shipment and call
`QueueBuyerCheckoutShipmentIntent`; do not create `ShippingOperation.Queue`.
Return:

```csharp
return new ChooseParcelProtectionResult(
    TransactionView.From(transaction),
    "selection_saved");
```

Retain the existing durable `CancelOutbound` path when a previous provider
reservation must be cancelled before changing protection.

Add `QueueReplacementOutboundShipmentIntent` for the cancellation worker. It
must validate the `AwaitingRebooking` change, add the replacement shipment, and
audit `parcel_protection.rebooking_intent_created` without accepting a
`ShippingOperation`.

- [ ] **Step 5: Run domain, application, and API tests**

Run:

```bash
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj \
  --filter FullyQualifiedName~ShippingMoneyTests
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter "FullyQualifiedName~ParcelProtectionCheckoutTests|FullyQualifiedName~ParcelProtectionChangeTests"
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj \
  --filter FullyQualifiedName~MobileParcelProtectionApiTests
```

Expected: all selected tests pass and no assertion expects a new
`BookOutbound`.

- [ ] **Step 6: Commit election persistence**

```bash
git add src/Toklong.Domain/Transactions/SaleTransaction.cs \
  src/Toklong.Application/Features/Checkout/ChooseParcelProtection \
  src/Toklong.Application/Features/Checkout/GetParcelProtection \
  tests/Toklong.Domain.Tests/Transactions/ShippingMoneyTests.cs \
  tests/Toklong.Application.Tests/Checkout \
  tests/Toklong.Api.Tests/Api/MobileParcelProtectionApiTests.cs
git commit -m "refactor: defer outbound booking until payment"
```

---

### Task 4: Direct Booking Orchestrator and Provider Correlation

**Files:**
- Create: `src/Toklong.Application/Features/Checkout/BookShipmentForPayment/BookShipmentForPayment.cs`
- Modify: `src/Toklong.Application/Abstractions/IShippingQuoteProvider.cs`
- Modify: `src/Toklong.Infrastructure/Services/ShippopShippingProvider.cs`
- Modify: `src/Toklong.Infrastructure/DependencyInjection.cs`
- Create: `tests/Toklong.Application.Tests/Checkout/DirectCheckoutBookingTests.cs`
- Modify: `tests/Toklong.Application.Tests/Shipping/ShippopShippingProviderTests.cs`

**Interfaces:**
- Consumes: `IBookingAttemptRepository`, `IShipmentProvider`, `SaleTransaction.CurrentOutboundShipment`.
- Changes: `ShipmentReservationRequest` adds required `string OperationReference`.
- Produces: `BookShipmentForPaymentCommand`, `DirectBookingResult`, and `DirectBookingState`.

- [ ] **Step 1: Write provider-reference and orchestration tests**

```csharp
[Fact]
public async Task Direct_booking_uses_attempt_reference_and_persists_success()
{
    var result = await handler.Handle(new BookShipmentForPaymentCommand(
        transaction.Id, buyer.Id, "checkout-001"), default);

    Assert.Equal(DirectBookingState.Ready, result.State);
    Assert.Equal(attempt.ProviderReference, provider.LastRequest!.OperationReference);
    Assert.Equal(BookingAttemptStatus.Succeeded, attempts.Stored.Status);
    Assert.True(transaction.ParcelProtectionBookingReady);
}

[Fact]
public async Task Provider_timeout_marks_attempt_and_does_not_replay()
{
    provider.Exception = new ShipmentMutationException(
        ShipmentMutationOutcome.OutcomeUnknown, "shippop-timeout");

    var result = await handler.Handle(Command("checkout-timeout"), default);
    var repeated = await handler.Handle(Command("checkout-timeout"), default);

    Assert.Equal(DirectBookingState.TimedOut, result.State);
    Assert.Equal(DirectBookingState.TimedOut, repeated.State);
    Assert.Equal(1, provider.ReserveCalls);
}

[Fact]
public async Task Price_mismatch_fails_before_booking_is_applied()
{
    provider.Reservation = Reservation(feeSatang: expectedFee + 1);

    var result = await handler.Handle(Command("checkout-mismatch"), default);

    Assert.Equal(DirectBookingState.ReconfirmationRequired, result.State);
    Assert.False(transaction.ParcelProtectionBookingReady);
}
```

- [ ] **Step 2: Run focused tests and verify the red state**

Run:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter "FullyQualifiedName~DirectCheckoutBookingTests|FullyQualifiedName~ShippopShippingProviderTests"
```

Expected: compilation fails because the command and required operation
reference do not exist.

- [ ] **Step 3: Make provider correlation explicit**

Change the request to:

```csharp
public sealed record ShipmentReservationRequest(
    Guid TransactionId,
    ShippingQuoteRequest Shipment,
    ShippingQuoteOption Quote,
    Guid ManagedShipmentId,
    bool IsReturn,
    string OperationReference);
```

In `ShippopShippingProvider.ReserveMutationAsync`, validate
`OperationReference` as 1–80 safe opaque characters and send:

```csharp
ShipmentPayload(
    request.Shipment,
    request.Quote.ServiceCode,
    request.OperationReference,
    showAll: false)
```

Update the legacy worker call to pass `operation.Id.ToString("N")`. Update
return booking calls the same way. Assert that `ref_no_1` equals the direct
attempt reference in provider tests.

- [ ] **Step 4: Implement the direct booking result contract**

```csharp
public enum DirectBookingState
{
    Ready,
    InProgress,
    Failed,
    TimedOut,
    RetryLimitReached,
    ReconfirmationRequired
}

public sealed record DirectBookingResult(
    DirectBookingState State,
    Guid AttemptId,
    string? SafeCode);

public sealed record BookShipmentForPaymentCommand(
    Guid TransactionId,
    Guid BuyerId,
    string IdempotencyKey) : IRequest<DirectBookingResult>;
```

The handler must:

1. authorize the buyer and unpaid state;
2. require the recorded protection election and current shipment intent;
3. compute `ManagedShippingOperationQueue.BookingFingerprint(shipment)`;
4. call `AcquireAsync`;
5. return the stored state without a provider call unless acquisition is
   `Acquired`;
6. call `ReserveAsync` with the attempt reference outside a database
   transaction;
7. compare provider, carrier, service, shipping fee, protection fee, coverage,
   and insurance code with the shipment;
8. build the response fingerprint from the normalized reservation fields and
   the transaction ISO currency;
9. call `attempt.Succeed` and
   `transaction.CompleteBuyerCheckoutShipmentBooking`;
10. save the attempt, managed shipment, transaction, and audit event in one
    `IUnitOfWork.SaveChangesAsync` transaction; and
11. map definite failure to `Failed`, ambiguous timeout to `TimedOut`, and
    changed price/coverage to `ReconfirmationRequired`. Map the repository's
    fourth-attempt rejection to `RetryLimitReached` without a provider call.

If `AcquireAsync` returns `Succeeded` but the aggregate reservation is absent
because an earlier process ended after committing an attempt created by an
older compatible release, rebuild `BookingAttemptSuccess` from the stored
normalized fields, apply `CompleteBuyerCheckoutShipmentBooking`, and commit
without calling SHIPPOP. With the new implementation, success and aggregate
reservation commit atomically, so this branch is a migration repair path.

- [ ] **Step 5: Add a strict 2.2-second provider budget**

Create a linked cancellation token:

```csharp
using var providerBudget =
    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
providerBudget.CancelAfter(TimeSpan.FromMilliseconds(2_200));
```

Distinguish caller cancellation from provider-budget cancellation. Caller
cancellation propagates. Provider-budget cancellation marks the attempt
`TimedOut` with `shippop-timeout`.

- [ ] **Step 6: Run direct booking and provider tests**

Run:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter "FullyQualifiedName~DirectCheckoutBookingTests|FullyQualifiedName~ShippopShippingProviderTests"
```

Expected: all selected tests pass; repeated timed-out keys produce one provider
call.

- [ ] **Step 7: Commit the direct orchestrator**

```bash
git add src/Toklong.Application/Features/Checkout/BookShipmentForPayment \
  src/Toklong.Application/Abstractions/IShippingQuoteProvider.cs \
  src/Toklong.Infrastructure/Services/ShippopShippingProvider.cs \
  src/Toklong.Infrastructure/DependencyInjection.cs \
  tests/Toklong.Application.Tests/Checkout/DirectCheckoutBookingTests.cs \
  tests/Toklong.Application.Tests/Shipping/ShippopShippingProviderTests.cs
git commit -m "feat: reserve SHIPPOP shipment during checkout"
```

---

### Task 5: Gate Stripe Behind the Committed Booking

**Files:**
- Modify: `src/Toklong.Application/Features/Checkout/PreparePaymentSheet/PreparePaymentSheet.cs`
- Modify: `src/Toklong.Api/Api/MobileApi.cs`
- Modify: `src/Toklong.Application/Features/ExternalEvents/ProcessExternalEvent.cs`
- Modify: `tests/Toklong.Application.Tests/Payments/PaymentDeadlineTests.cs`
- Modify: `tests/Toklong.Application.Tests/Payments/ExternalProviderBoundaryTests.cs`
- Create: `tests/Toklong.Api.Tests/Api/MobileDirectBookingApiTests.cs`

**Interfaces:**
- Consumes: `BookShipmentForPaymentCommand`.
- Changes: `PreparePaymentSheetCommand` adds `string IdempotencyKey`.
- Produces: safe HTTP `409`, `429`, and `503` responses with stable error codes.
- Preserves: verified webhook queues exactly one `ConfirmOutbound` in the same unit of work as payment confirmation.

- [ ] **Step 1: Write payment boundary tests**

```csharp
[Fact]
public async Task Physical_payment_books_before_payment_provider()
{
    var result = await handler.Handle(Command("checkout-001"), default);

    Assert.Equal(["booking", "stripe"], callOrder);
    Assert.Equal(1, paymentIntents.CallCount);
    Assert.True(transaction.ParcelProtectionBookingReady);
}

[Theory]
[InlineData(DirectBookingState.InProgress)]
[InlineData(DirectBookingState.Failed)]
[InlineData(DirectBookingState.TimedOut)]
[InlineData(DirectBookingState.RetryLimitReached)]
[InlineData(DirectBookingState.ReconfirmationRequired)]
public async Task Non_ready_booking_never_calls_stripe(DirectBookingState state)
{
    booking.Result = Result(state);

    await Assert.ThrowsAsync<CheckoutBookingException>(
        () => handler.Handle(Command("checkout-001"), default));

    Assert.Equal(0, paymentIntents.CallCount);
}

[Fact]
public async Task Replayed_stripe_webhook_queues_one_confirmation()
{
    await handler.Handle(Webhook("evt-1"), default);
    await handler.Handle(Webhook("evt-1"), default);

    Assert.Single(transaction.ShippingOperations,
        x => x.OperationType == ShippingOperationType.ConfirmOutbound);
}
```

- [ ] **Step 2: Run payment tests and verify the red state**

Run:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter "FullyQualifiedName~PaymentDeadlineTests|FullyQualifiedName~ExternalProviderBoundaryTests"
```

Expected: tests fail because payment currently only checks
`ParcelProtectionBookingReady` and cannot initiate direct booking.

- [ ] **Step 3: Invoke booking before Stripe**

Inject `ISender` is not allowed inside the handler. Inject a focused service
implemented by the Task 4 handler logic:

```csharp
public interface IDirectCheckoutBooking
{
    Task<DirectBookingResult> BookAsync(
        SaleTransaction transaction,
        Guid buyerId,
        string idempotencyKey,
        CancellationToken cancellationToken);
}
```

Have `BookShipmentForPaymentHandler` implement this interface and register it
scoped. In `PreparePaymentSheetHandler`, call it for physical transactions
before `paymentIntents.PrepareAsync`. The service receives the already tracked
transaction; a `Ready` result means that same aggregate instance contains the
committed reservation.

- [ ] **Step 4: Add typed safe errors and HTTP mapping**

```csharp
public sealed class CheckoutBookingException(
    DirectBookingState state,
    string safeCode,
    string message) : InvalidOperationException(message)
{
    public DirectBookingState State { get; } = state;
    public string SafeCode { get; } = safeCode;
}
```

Map:

- `InProgress` → HTTP 409, code `shipping_preparing`;
- `ReconfirmationRequired` → HTTP 409, code
  `shipping_reconfirmation_required`;
- local bulkhead or provider `429` → HTTP 429 with bounded `Retry-After`;
- `RetryLimitReached` → HTTP 429, code `shipping_retry_limit`, with
  `Retry-After` equal to the bounded remainder of the ten-minute window;
- `TimedOut` or provider unavailable → HTTP 503, code
  `shipping_retry_required`.

Return Problem Details extension `code`; never expose raw SHIPPOP codes.

- [ ] **Step 5: Require the HTTP idempotency key**

Read `Idempotency-Key` with the existing safe-key validator and pass it to:

```csharp
new PreparePaymentSheetCommand(
    transactionId,
    buyerId,
    request.AcceptedTerms,
    RequiredCheckoutIdempotencyKey(httpRequest))
```

Digital checkout uses the key for Stripe preparation idempotency but skips
SHIPPOP.

- [ ] **Step 6: Run application and API tests**

Run:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter "FullyQualifiedName~PaymentDeadlineTests|FullyQualifiedName~ExternalProviderBoundaryTests|FullyQualifiedName~DirectCheckoutBookingTests"
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj \
  --filter FullyQualifiedName~MobileDirectBookingApiTests
```

Expected: all selected tests pass, and every failure path asserts zero Stripe
calls.

- [ ] **Step 7: Commit the payment gate**

```bash
git add src/Toklong.Application/Features/Checkout/PreparePaymentSheet \
  src/Toklong.Application/Features/Checkout/BookShipmentForPayment \
  src/Toklong.Application/Features/ExternalEvents/ProcessExternalEvent.cs \
  src/Toklong.Api/Api/MobileApi.cs \
  tests/Toklong.Application.Tests/Payments \
  tests/Toklong.Application.Tests/Checkout/DirectCheckoutBookingTests.cs \
  tests/Toklong.Api.Tests/Api/MobileDirectBookingApiTests.cs
git commit -m "feat: gate Stripe behind committed shipping booking"
```

---

### Task 6: Mobile Checkout Without Worker Polling

**Files:**
- Modify: `src/Toklong.Mobile/Core/ITransactionService.cs`
- Modify: `src/Toklong.Mobile/Services/StripePaymentSheetService.cs`
- Modify: `src/Toklong.Mobile/ViewModels/TransactionDetailViewModel.cs`
- Modify: `src/Toklong.Mobile/Pages/TransactionDetailPage.xaml`
- Modify: `src/Toklong.Mobile/Core/ParcelProtectionCheckoutPresentation.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/TransactionDetailParcelProtectionViewModelTests.cs`
- Create: `tests/Toklong.Mobile.Core.Tests/DirectBookingCheckoutViewModelTests.cs`

**Interfaces:**
- Produces: `PaymentPreparationException(string Code, bool CanRetry, string ConsumerMessage)`.
- Changes: `IStripePaymentSheetService.PresentAsync(Guid transactionId, string idempotencyKey, CancellationToken)`.
- Produces: `DismissParcelProtectionCommand`, which hides the current choice
  without recording an election and leaves the primary action enabled.
- Removes: `WaitForParcelProtectionBookingAsync`.

- [ ] **Step 1: Write mobile behavior tests**

```csharp
[Fact]
public async Task Choosing_protection_opens_payment_without_booking_poll()
{
    await LoadAndOpenChoiceAsync(viewModel);
    await ExecuteAsync(viewModel.AcceptParcelProtectionCommand);

    Assert.Equal(1, service.ChooseCalls);
    Assert.Equal(1, sheet.Calls);
    Assert.Equal(0, service.BookingPollCalls);
}

[Fact]
public async Task Timeout_enables_retry_with_a_new_attempt_key()
{
    sheet.Failures.Enqueue(new PaymentPreparationException(
        "shipping_retry_required", true,
        "เตรียมการจัดส่งไม่สำเร็จ ยังไม่มีการชำระเงิน กรุณาลองอีกครั้ง"));

    await ExecuteAsync(viewModel.PrimaryActionCommand);
    var firstKey = sheet.Keys.Single();
    await ExecuteAsync(viewModel.PrimaryActionCommand);

    Assert.Equal(2, sheet.Calls);
    Assert.NotEqual(firstKey, sheet.Keys.Last());
}

[Fact]
public async Task Closing_protection_choice_does_not_disable_payment()
{
    await LoadAndOpenChoiceAsync(viewModel);
    await ExecuteAsync(viewModel.DismissParcelProtectionCommand);
    await ExecuteAsync(viewModel.PrimaryActionCommand);

    Assert.True(viewModel.IsParcelProtectionChoiceVisible);
    Assert.True(viewModel.PrimaryActionCommand.CanExecute(null));
}
```

- [ ] **Step 2: Run mobile tests and verify the red state**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~DirectBookingCheckoutViewModelTests|FullyQualifiedName~TransactionDetailParcelProtectionViewModelTests"
```

Expected: compilation or assertions fail because the view model still polls up
to eight times.

- [ ] **Step 3: Submit a stable key for one button attempt**

Change the service signature:

```csharp
Task<PaymentSheetOutcome> PresentAsync(
    Guid transactionId,
    string idempotencyKey,
    CancellationToken cancellationToken = default);
```

Attach:

```csharp
request.Headers.Add("Idempotency-Key", idempotencyKey);
```

Keep the same key while one request is active. Generate a new key only after a
typed retryable failure or a changed protection election:

```csharp
$"mobile:{transactionId:N}:checkout:{Guid.NewGuid():N}"
```

- [ ] **Step 4: Remove booking polling**

After `ChooseParcelProtectionAsync` returns `selection_saved`, hide the choice
and call `PresentPaymentSheetAsync` immediately. Delete
`WaitForParcelProtectionBookingAsync` and the `Task.Delay(750ms)` loop.

Use exact copy:

```text
กำลังเตรียมการจัดส่ง…
```

For `shipping_retry_required`:

```text
เตรียมการจัดส่งไม่สำเร็จ
ยังไม่มีการชำระเงิน กรุณาลองอีกครั้ง
```

Keep `IsBusy = true` for the whole request and restore it in `finally`, so a
closed dialog or cancelled PaymentSheet never permanently disables payment.
Bind the dialog close action in `TransactionDetailPage.xaml` to:

```csharp
public ICommand DismissParcelProtectionCommand => new Command(() =>
{
    IsParcelProtectionChoiceVisible = false;
    Message = "";
});
```

The next primary action reloads server state and reopens the choice when no
election was recorded.

- [ ] **Step 5: Parse safe Problem Details**

In `StripePaymentSheetService`, parse `extensions.code` and throw:

```csharp
throw new PaymentPreparationException(
    problem.Code,
    problem.Code is "shipping_retry_required" or "shipping_preparing",
    problem.Detail ?? "เปิดหน้าจ่ายเงินไม่ได้");
```

Do not display provider names or internal error strings.

- [ ] **Step 6: Run all mobile core tests**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj
```

Expected: the complete mobile core suite passes and no test expects the
eight-iteration booking poll.

- [ ] **Step 7: Commit the mobile flow**

```bash
git add src/Toklong.Mobile/Core \
  src/Toklong.Mobile/Services/StripePaymentSheetService.cs \
  src/Toklong.Mobile/ViewModels/TransactionDetailViewModel.cs \
  src/Toklong.Mobile/Pages/TransactionDetailPage.xaml \
  tests/Toklong.Mobile.Core.Tests
git commit -m "feat: retry direct shipping booking from checkout"
```

---

### Task 7: Drain and Prioritize Durable Post-Payment Work

**Files:**
- Modify: `src/Toklong.Application/Abstractions/IShippingOperationRepository.cs`
- Modify: `src/Toklong.Infrastructure/Persistence/ShippingOperationRepository.cs`
- Modify: `src/Toklong.Application/Features/Shipping/ProcessShippingOperations/ProcessShippingOperations.cs`
- Modify: `src/Toklong.Worker/ShippingOperationsWorker.cs`
- Modify: `src/Toklong.Api/appsettings.json`
- Modify: `src/Toklong.Worker/appsettings.json`
- Create: `tests/Toklong.Application.Tests/Shipping/ShippingWorkerThroughputTests.cs`
- Modify: `tests/Toklong.Application.Tests/Shipping/ShippingOperationPersistenceTests.cs`

**Interfaces:**
- Changes: `ClaimDueAsync` adds `IReadOnlySet<ShippingOperationType> allowedTypes`.
- Produces: `ProcessShippingOperationBatchCommand(string WorkerId, IReadOnlySet<ShippingOperationType> AllowedTypes, int BatchSize, int LeaseSeconds, int MaximumAttempts)`.
- Changes: successful cancellation for a protection change creates a
  replacement shipment intent through
  `QueueReplacementOutboundShipmentIntent`; it does not queue a new
  `BookOutbound`.
- Preserves: leases, `FOR UPDATE SKIP LOCKED`, unknown-outcome reconciliation, and operation idempotency.

- [ ] **Step 1: Write priority and drain tests**

```csharp
[Fact]
public async Task Confirm_queue_is_claimed_before_tracking_or_cancel_backlog()
{
    await SeedAsync(cancelCount: 50, confirmCount: 1);

    var claimed = await repository.ClaimDueAsync(
        "worker-a", Now, TimeSpan.FromMinutes(5),
        new HashSet<ShippingOperationType> {
            ShippingOperationType.ConfirmOutbound,
            ShippingOperationType.ConfirmReturn
        }, default);

    Assert.Equal(ShippingOperationType.ConfirmOutbound, claimed!.OperationType);
}

[Fact]
public async Task Batch_drains_all_available_confirmations_without_idle_delay()
{
    await SeedAsync(confirmCount: 20);

    var count = await handler.Handle(new ProcessShippingOperationBatchCommand(
        "worker-a", ConfirmationTypes, 20, 300, 8), default);

    Assert.Equal(20, count);
}
```

- [ ] **Step 2: Run worker tests and verify the red state**

Run:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter "FullyQualifiedName~ShippingWorkerThroughputTests|FullyQualifiedName~ShippingOperationPersistenceTests"
```

Expected: compilation fails because claim filtering and batch processing do not
exist.

- [ ] **Step 3: Add operation filtering to the lease query**

For Npgsql, pass the allowed enum names as a bounded array and add:

```sql
AND "OperationType" = ANY ({allowedTypeNames})
ORDER BY "NextAttemptAt", "CreatedAt"
FOR UPDATE SKIP LOCKED
LIMIT 1
```

For SQLite tests, use the equivalent LINQ predicate before the existing
single-row claim. Reject an empty allowed set.

- [ ] **Step 4: Add bounded batch draining**

```csharp
public async Task<int> Handle(
    ProcessShippingOperationBatchCommand request,
    CancellationToken cancellationToken)
{
    var processed = 0;
    for (; processed < Math.Clamp(request.BatchSize, 1, 100); processed++)
    {
        var hadWork = await singleOperation.Handle(
            new ProcessNextShippingOperationCommand(
                request.WorkerId,
                request.LeaseSeconds,
                request.MaximumAttempts,
                request.AllowedTypes),
            cancellationToken);
        if (!hadWork)
            break;
    }
    return processed;
}
```

Use separate scopes per operation so EF tracking state does not grow across a
batch.

- [ ] **Step 5: Stop cancellation from re-queuing checkout booking**

Replace:

```csharp
var booking = ShippingOperation.Queue(
    transaction.Id, replacement.Id,
    ShippingOperationType.BookOutbound,
    $"book-outbound-change:{change.Id:N}",
    ManagedShippingOperationQueue.BookingFingerprint(replacement),
    clock.UtcNow);
transaction.QueueReplacementOutboundShipment(
    replacement, booking, clock.UtcNow);
```

with:

```csharp
transaction.QueueReplacementOutboundShipmentIntent(
    replacement,
    change.Id,
    clock.UtcNow);
```

The buyer's next payment action owns direct reservation. Legacy
`BookOutbound` remains processable only for rows committed before rollout.

- [ ] **Step 6: Split confirmation drain from tracking cadence**

Configure:

```json
"ShippingWorker": {
  "OperationIdleSeconds": 1,
  "ConfirmationBatchSize": 50,
  "OtherMutationBatchSize": 20,
  "TrackingIntervalSeconds": 120,
  "TrackingJitterSeconds": 30,
  "LeaseSeconds": 300,
  "MaximumAttempts": 8
}
```

Each loop drains confirmation types first, then a bounded set of
cancel/return/legacy booking operations. Tracking reconciliation remains on
its independent interval. If either mutation batch is full, loop again
immediately; only idle loops await the timer.

- [ ] **Step 7: Run shipping tests**

Run:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter "FullyQualifiedName~ShippingWorkerThroughputTests|FullyQualifiedName~ShippingOperationPersistenceTests|FullyQualifiedName~DurableShippingOperationProcessingTests"
```

Expected: all selected tests pass, multi-context claims remain exclusive, and
confirmation is not starved.

- [ ] **Step 8: Commit worker throughput**

```bash
git add src/Toklong.Application/Abstractions/IShippingOperationRepository.cs \
  src/Toklong.Application/Features/Shipping/ProcessShippingOperations \
  src/Toklong.Infrastructure/Persistence/ShippingOperationRepository.cs \
  src/Toklong.Worker/ShippingOperationsWorker.cs \
  src/Toklong.Api/appsettings.json src/Toklong.Worker/appsettings.json \
  tests/Toklong.Application.Tests/Shipping
git commit -m "perf: prioritize paid shipping confirmations"
```

---

### Task 8: Backpressure, Feature Gates, and Metrics

**Files:**
- Create: `src/Toklong.Application/Abstractions/IDirectBookingAdmission.cs`
- Create: `src/Toklong.Infrastructure/Services/DirectBookingAdmission.cs`
- Create: `src/Toklong.Application/Features/Checkout/BookShipmentForPayment/DirectBookingMetrics.cs`
- Modify: `src/Toklong.Infrastructure/Services/ShippopShippingProvider.cs`
- Modify: `src/Toklong.Infrastructure/DependencyInjection.cs`
- Modify: `src/Toklong.Infrastructure/ProductionConfigurationValidator.cs`
- Modify: `src/Toklong.Api/appsettings.json`
- Modify: `src/Toklong.Api/appsettings.Development.json`
- Modify: `tests/Toklong.Application.Tests/Security/ProductionConfigurationValidatorTests.cs`
- Modify: `tests/Toklong.Application.Tests/Shipping/ShippopShippingProviderTests.cs`
- Modify: `src/Toklong.Application/Features/Shipping/ProcessShippingOperations/ProcessShippingOperations.cs`
- Modify: `src/Toklong.Application/Features/Checkout/BookShipmentForPayment/BookShipmentForPayment.cs`

**Interfaces:**
- Produces configuration keys `Shippop:DirectBookingEnabled`,
  `Shippop:DirectBookingTimeoutMilliseconds`,
  `Shippop:DirectBookingMaximumConcurrency`, and
  `Shippop:DirectBookingCertificationReference`.
- Produces counters/histograms for direct booking duration, results, bulkhead
  rejection, `429`, timeout, and confirmation queue age.
- Produces: `IDirectBookingAdmission.TryEnter(out IDisposable lease)`,
  `RecordProviderSuccess()`, and `RecordProviderFailure(DateTimeOffset now)`.

- [ ] **Step 1: Write configuration gate tests**

```csharp
[Fact]
public void Production_rejects_direct_booking_without_certification()
{
    var values = ValidProductionConfiguration();
    values["Shippop:DirectBookingEnabled"] = "true";
    values["Shippop:DirectBookingCertificationReference"] = "";

    var error = Assert.Throws<InvalidOperationException>(
        () => ProductionConfigurationValidator.Validate(Configuration(values)));

    Assert.Contains("DirectBookingCertificationReference", error.Message);
}

[Theory]
[InlineData(0)]
[InlineData(2201)]
public void Production_rejects_invalid_direct_booking_timeout(int milliseconds)
{
    var values = ValidDirectBookingConfiguration();
    values["Shippop:DirectBookingTimeoutMilliseconds"] = milliseconds.ToString();

    Assert.Throws<InvalidOperationException>(
        () => ProductionConfigurationValidator.Validate(Configuration(values)));
}
```

- [ ] **Step 2: Run configuration tests and verify the red state**

Run:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter "FullyQualifiedName~ProductionConfigurationValidatorTests|FullyQualifiedName~ShippopShippingProviderTests"
```

Expected: tests fail because direct-booking options and gates do not exist.

- [ ] **Step 3: Add disabled defaults and validation**

Use:

```json
"DirectBookingEnabled": false,
"DirectBookingTimeoutMilliseconds": 2200,
"DirectBookingMaximumConcurrency": 32,
"DirectBookingCertificationReference": ""
```

Outside Development, enabling direct booking requires:

- SHIPPOP provider selected;
- HTTPS base URL;
- `BookOutboundEnabled`, `ConfirmEnabled`, and
  `OperationLookupEnabled` for every enabled direct-booking service;
- non-empty direct-booking certification reference;
- timeout from 500 through 2,200 milliseconds; and
- concurrency from 1 through 256.

- [ ] **Step 4: Add bounded admission and a short circuit**

`DirectBookingAdmission` owns one `SemaphoreSlim` sized from
`DirectBookingMaximumConcurrency`. `TryEnter` uses zero queue wait:

```csharp
if (!admission.TryEnter(out var lease))
    throw new RequestCooldownException(
        "ระบบกำลังมีผู้ใช้งานจำนวนมาก กรุณาลองอีกครั้ง",
        TimeSpan.FromSeconds(2));
using (lease)
{
    return await provider.ReserveAsync(request, cancellationToken);
}
```

The admission controller opens its circuit for ten seconds after five
consecutive provider timeouts, `429` responses, or `5xx` failures. A successful
provider response resets the count:

```csharp
admission.RecordProviderSuccess();
// or
admission.RecordProviderFailure(clock.UtcNow);
```

When the circuit is open, `TryEnter` returns false without consuming a permit.
Register one singleton admission controller:

```csharp
services.AddSingleton<IDirectBookingAdmission, DirectBookingAdmission>();
services.AddSingleton<DirectBookingMetrics>();
```

Inject it only into the direct checkout orchestrator:

```csharp
try
{
    using var lease = admission.TryEnter(out var acquired)
        ? acquired
        : throw new RequestCooldownException(
            "ระบบกำลังมีผู้ใช้งานจำนวนมาก กรุณาลองอีกครั้ง",
            TimeSpan.FromSeconds(2));
    var result = await provider.ReserveAsync(request, cancellationToken);
    admission.RecordProviderSuccess();
    return result;
}
catch (ShipmentMutationException)
{
    admission.RecordProviderFailure(clock.UtcNow);
    throw;
}
```

Apply it only to direct checkout booking. Worker confirmation has its own
bounded concurrency and cannot consume these permits.

- [ ] **Step 5: Bound HTTP connections and record metrics**

Configure the SHIPPOP primary handler:

```csharp
new SocketsHttpHandler
{
    MaxConnectionsPerServer = options.DirectBookingMaximumConcurrency,
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    ConnectTimeout = TimeSpan.FromMilliseconds(500)
}
```

Record:

```text
toklong.shipping.booking.duration
toklong.shipping.booking.result
toklong.shipping.booking.bulkhead_rejected
toklong.shipping.booking.provider_429
toklong.shipping.booking.timeout
toklong.shipping.confirm.queue_age
```

Metric tags are `service_code`, `result`, and `environment`; never transaction
IDs, phone numbers, addresses, or provider response bodies.

- [ ] **Step 6: Run security and provider tests**

Run:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter "FullyQualifiedName~ProductionConfigurationValidatorTests|FullyQualifiedName~ShippopShippingProviderTests|FullyQualifiedName~DirectCheckoutBookingTests"
```

Expected: all selected tests pass, HTTP Production remains HTTPS-only, and the
default direct-booking flag is false.

- [ ] **Step 7: Commit gates and telemetry**

```bash
git add src/Toklong.Infrastructure \
  src/Toklong.Application/Abstractions/IDirectBookingAdmission.cs \
  src/Toklong.Application/Features/Checkout/BookShipmentForPayment \
  src/Toklong.Application/Features/Shipping/ProcessShippingOperations/ProcessShippingOperations.cs \
  src/Toklong.Api/appsettings.json \
  src/Toklong.Api/appsettings.Development.json \
  tests/Toklong.Application.Tests/Security \
  tests/Toklong.Application.Tests/Shipping \
  tests/Toklong.Application.Tests/Checkout/DirectCheckoutBookingTests.cs
git commit -m "feat: bound and observe direct shipping booking"
```

---

### Task 9: Integration, Load Test, Canonical Docs, and Final Verification

**Files:**
- Create: `tests/Toklong.LoadTests/Toklong.LoadTests.csproj`
- Create: `tests/Toklong.LoadTests/Program.cs`
- Create: `tests/Toklong.LoadTests/LoadTestFactory.cs`
- Create: `tests/Toklong.LoadTests/LoadShipmentProvider.cs`
- Modify: `Toklong.slnx`
- Modify: `tests/Toklong.Api.Tests/Api/MobileDirectBookingApiTests.cs`
- Modify: `tests/Toklong.Application.Tests/Payments/ExternalProviderBoundaryTests.cs`
- Modify: `docs/01_USER_FLOWS_AND_STATE_MACHINE.md`
- Modify: `docs/02_UI_UX_AND_CONTENT_SPEC.md`
- Modify: `docs/03_BACKEND_TRANSACTION_RECORD.md`
- Modify: `docs/04_PAYMENT_SHIPPING_AND_DISPUTE_RULES.md`
- Modify: `docs/05_ACCEPTANCE_TESTS.md`
- Modify: `docs/06_OPEN_DECISIONS.md`
- Modify: `docs/08_SHIPPOP_PRODUCTION_FLOW.md`

**Interfaces:**
- Consumes: all tasks.
- Produces: a load-test executable accepting
  `TOKLONG_LOAD_POSTGRES_CONNECTION`, `TOKLONG_LOAD_REQUESTS`, and
  `TOKLONG_LOAD_PROVIDER_DELAY_MS`.
- Produces: a report containing request count, success count, safe rejection
  count, p50, p95, p99, maximum latency, and connection errors.

- [ ] **Step 1: Add end-to-end API tests**

```csharp
[Fact]
public async Task Checkout_reservation_then_verified_webhook_then_confirm_is_ordered()
{
    var payment = await client.PostPaymentSheetAsync(
        transaction.Id, "checkout-e2e-001");
    Assert.Equal(HttpStatusCode.OK, payment.StatusCode);
    Assert.Equal(1, shippop.BookingCalls);
    Assert.Equal(0, shippop.ConfirmCalls);

    await stripe.SendSignedSucceededWebhookAsync(transaction.Id, "evt-e2e-001");
    Assert.Single(await database.ConfirmOutboundOperations(transaction.Id));

    await shipping.ProcessConfirmBatchAsync();
    Assert.Equal(1, shippop.ConfirmCalls);
    Assert.True((await database.Load(transaction.Id)).ShippingConfirmedAt.HasValue);
}

[Fact]
public async Task Dispute_and_unconfirmed_shipping_both_block_payout()
{
    await SeedPaidUnconfirmedShipmentAsync();
    await OpenDisputeAsync();

    await payout.ProcessAsync();

    Assert.Equal(0, payout.ProviderCalls);
}
```

- [ ] **Step 2: Run the end-to-end tests**

Run:

```bash
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj \
  --filter FullyQualifiedName~MobileDirectBookingApiTests
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter FullyQualifiedName~ExternalProviderBoundaryTests
```

Expected: all selected end-to-end boundary tests pass.

- [ ] **Step 3: Add the load-test executable**

`LoadTestFactory` starts the real ASP.NET Core pipeline in the `Test`
environment, replaces `IShipmentProvider` with `LoadShipmentProvider`, replaces
`IPaymentIntentProvider` with a deterministic in-memory provider, and uses the
PostgreSQL connection from `TOKLONG_LOAD_POSTGRES_CONNECTION`. Refuse to start
unless the database name ends in `_load`; then migrate and seed exactly
`TOKLONG_LOAD_REQUESTS` accepted physical transactions, buyers, mobile
sessions, protection elections, and shipment intents.

`LoadShipmentProvider` waits exactly `TOKLONG_LOAD_PROVIDER_DELAY_MS`, returns
one valid unique reservation per operation reference, and records call counts
in a `ConcurrentDictionary<string, int>`.

Use `Parallel.ForEachAsync` over the seeded transactions. Authenticate each
request through the deterministic mobile test handler and use a distinct
idempotency key:

```csharp
var started = Stopwatch.GetTimestamp();
using var request = new HttpRequestMessage(
    HttpMethod.Post,
    $"api/mobile/transactions/{transaction.Id}/payment-sheet")
{
    Content = JsonContent.Create(new { AcceptedTerms = true })
};
request.Headers.Authorization =
    new AuthenticationHeaderValue("Bearer", transaction.BuyerId.ToString("N"));
request.Headers.Add(
    "Idempotency-Key",
    $"load:{transaction.Id:N}:checkout");
using var response = await client.SendAsync(request, cancellationToken);
latencies.Add(Stopwatch.GetElapsedTime(started));
results.Add((int)response.StatusCode);
```

Sort completed latencies and calculate percentile index as:

```csharp
static TimeSpan Percentile(TimeSpan[] sorted, double percentile) =>
    sorted[(int)Math.Ceiling(percentile * sorted.Length) - 1];
```

Exit non-zero when:

- completed request count differs from requested count;
- unexpected status codes exceed 1%;
- connection errors exceed 0.1%; or
- p95 exceeds three seconds; or
- any operation reference has a provider call count other than one.

The real-provider run must respect the provider-approved concurrency and rate
limit. The 1,000-request unrestricted run uses the deterministic provider
stub unless SHIPPOP grants equivalent Dev capacity.

- [ ] **Step 4: Add the load project and run a deterministic 1,000-request test**

Run:

```bash
dotnet sln Toklong.slnx add tests/Toklong.LoadTests/Toklong.LoadTests.csproj
TOKLONG_LOAD_POSTGRES_CONNECTION='Host=localhost;Port=5432;Database=toklong_load;Username=toklong;Password=toklong_dev' \
TOKLONG_LOAD_REQUESTS=1000 \
TOKLONG_LOAD_PROVIDER_DELAY_MS=100 \
dotnet run --project tests/Toklong.LoadTests/Toklong.LoadTests.csproj \
  --configuration Release
```

Expected: exit 0, 1,000 completed requests, no duplicate booking per
transaction/idempotency key, p95 at or below three seconds, and database/HTTP
pool use below configured ceilings.

- [ ] **Step 5: Update canonical documentation**

Document this exact sequence:

```text
Buyer election saved
  → payment request creates one unconfirmed booking
  → validated booking committed
  → Stripe PaymentSheet returned
  → signed Stripe webhook confirms payment
  → ConfirmOutbound committed
  → runner confirms SHIPPOP
  → seller fulfillment exposed
```

Remove statements that new checkout `BookOutbound` always waits on the worker.
Keep the legacy operation documented only as a disabled rollback path. Record
the unresolved SHIPPOP expiry, lookup/idempotency, charge/protection activation,
rate-limit, and latency questions in `docs/06_OPEN_DECISIONS.md`. Do not mark
them resolved without provider evidence.

- [ ] **Step 6: Run the full required verification**

Run:

```bash
dotnet build Toklong.slnx --configuration Release
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj
dotnet test tests/Toklong.Crm.Tests/Toklong.Crm.Tests.csproj
dotnet test tests/Toklong.Shippop.Certification/Toklong.Shippop.Certification.csproj
git diff --check
```

Expected: build exit 0, every test project reports zero failures, certification
tests skip only capabilities that remain explicitly disabled, and
`git diff --check` prints nothing.

- [ ] **Step 7: Verify the security and domain checklist**

Run:

```bash
rg -n \"dv2f|Lovr|khonklan\\.support|api_key\\s*[:=]\\s*[^\\\"]\" \
  src tests docs config deploy
rg -n \"force_confirm\\s*=\\s*1|force_confirm\\\":1\" src tests
rg -n \"BookOutbound\" \
  src/Toklong.Application/Features/Checkout \
  src/Toklong.Mobile
```

Expected:

- no disclosed API key, password, or account email appears;
- no production path contains `force_confirm=1`;
- checkout/mobile code does not queue `BookOutbound`; and
- remaining `BookOutbound` references are the disabled rollback/return-safe
  orchestration and explicit tests.

- [ ] **Step 8: Commit integration and documentation**

```bash
git add Toklong.slnx tests/Toklong.LoadTests \
  tests/Toklong.Api.Tests tests/Toklong.Application.Tests \
  docs/01_USER_FLOWS_AND_STATE_MACHINE.md \
  docs/02_UI_UX_AND_CONTENT_SPEC.md \
  docs/03_BACKEND_TRANSACTION_RECORD.md \
  docs/04_PAYMENT_SHIPPING_AND_DISPUTE_RULES.md \
  docs/05_ACCEPTANCE_TESTS.md \
  docs/06_OPEN_DECISIONS.md \
  docs/08_SHIPPOP_PRODUCTION_FLOW.md
git commit -m "test: verify scalable synchronous shipping checkout"
```

---

## Rollout and Rollback

1. Deploy the schema with `Shippop:DirectBookingEnabled=false`.
2. Deploy code with the existing worker booking path available as rollback.
3. Exercise one certified service in SHIPPOP Dev with direct booking enabled.
4. Verify timeout cleanup, provider-reference lookup, no-charge unconfirmed
   expiry, repeated confirmation semantics, and safe cancellation.
5. Run the deterministic 1,000-request test.
6. Run the real-provider test only at SHIPPOP-approved concurrency.
7. Enable direct booking for one service/environment.
8. Monitor booking p95/p99, timeout, `429`, bulkhead rejection, unknown outcome,
   and oldest paid confirmation.
9. Roll back by disabling the direct-booking flag; do not delete
   `BookingAttempt` history or replay timed-out attempts.

Production activation remains blocked if SHIPPOP cannot prove lookup or
reconciliation by TOKLONG reference, unconfirmed booking expiry/no-charge
semantics, repeated confirm/cancel safety, and sufficient capacity.
