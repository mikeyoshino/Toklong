# SHIPPOP Counter QR Runtime Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the approved seller Counter QR experience end to end for the deterministic Development provider, while keeping every SHIPPOP service fail-closed until an official account-specific Counter QR contract is observed and certified.

**Architecture:** A transaction-scoped, encrypted `ShipmentCounterQrResource` belongs to the confirmed outbound managed shipment and carries only normalized Pending/Ready/Error/Unavailable state. A separate read-only Worker claims due resources and calls a provider-neutral `GetCounterQrAsync` boundary; it never replays booking or confirmation. The seller-only API returns status in the transaction projection, delivers ready PNG bytes with `no-store`, and accepts an idempotent retry. MAUI renders Pending/Ready/Error cards, opens a dedicated full-screen QR page, and downloads the original label through the native share/save sheet without any HTML preview.

**Tech Stack:** .NET 10, C# 14, EF Core 10/PostgreSQL, ASP.NET Core Minimal APIs, MediatR, ASP.NET Core Data Protection, QRCoder 1.8.0 for the deterministic Development provider, .NET MAUI, xUnit 2.9.

## Global Constraints

- Never derive a Counter QR from tracking, purchase, label HTML, or another inferred shipment value.
- Never parse a speculative SHIPPOP field or call a speculative endpoint.
- Checked-in SHIPPOP service flags stay disabled; each service needs a non-empty reviewed Counter QR certification reference.
- Counter QR access requires provider-confirmed payment, the exact confirmed outbound shipment, and the authenticated transaction seller.
- Counter QR readiness is a shipment resource, not a `TransactionState`, and cannot start a carrier scan, delivery window, refund, payout, or settlement event.
- Artifact bytes/text and provider URLs never enter logs, analytics, notifications, audit metadata, or ordinary transaction JSON.
- Stored artifact content is protected at rest; consumer responses use `Cache-Control: no-store`.
- Label behavior is seller download-only; no transaction thumbnail, label WebView, or outbound full-screen label viewer remains.
- All money remains integer satang plus ISO currency and all existing payment, dispute, delivery, and payout invariants remain unchanged.
- Mobile controls remain at least 44×44 points, preserve the approved seller colors/fonts, support Thai Dynamic Type, and never read the QR payload aloud.

---

### Task 1: Observe SHIPPOP booking and confirmation response shapes safely

**Files:**
- Create: `tests/Toklong.Shippop.Certification/CounterQrResponseShape.cs`
- Create: `tests/Toklong.Shippop.Certification/CounterQrResponseShapeTests.cs`
- Create: `tests/Toklong.Shippop.Certification/CounterQrObservationHandler.cs`
- Create: `tests/Toklong.Shippop.Certification/CounterQrObservationHandlerTests.cs`
- Create: `tests/Toklong.Shippop.Certification/CounterQrCertificationContext.cs`
- Create: `tests/Toklong.Shippop.Certification/CounterQrEvidenceReport.cs`
- Create: `tests/Toklong.Shippop.Certification/CounterQrCertificationTests.cs`
- Modify: `scripts/shippop-certify.sh`
- Modify: `docs/SHIPPOP_CERTIFICATION_RUNBOOK.md`

**Interfaces:**
- Produces: `CounterQrResponseShapeParser.Parse(string, ReadOnlySpan<byte>)` with value-free field paths and JSON kinds.
- Produces: `CounterQrObservationHandler` that observes only `booking/` and `confirm/` while preserving the original response.
- Produces: `./scripts/shippop-certify.sh counter-qr-observe` with explicit mutation opt-in and evidence outside the repository.

- [ ] **Step 1: Execute Tasks 1–4 of `2026-08-02-shippop-counter-qr-contract-observation.md` exactly in red-green order.**

- [ ] **Step 2: Run the offline certification project.**

Run:

```bash
dotnet test tests/Toklong.Shippop.Certification/Toklong.Shippop.Certification.csproj
bash -n scripts/shippop-certify.sh
```

Expected: ordinary tests pass and live facts skip without `SHIPPOP_CERTIFY=1`.

- [ ] **Step 3: Record the provider checkpoint without guessing.**

If the required environment secrets and approved synthetic address are absent, record that the live observation is blocked and continue only with the provider-neutral runtime plus Development provider. If they are present, run one service sequentially and stop immediately if cleanup is not `cancelled`.

- [ ] **Step 4: Commit the certification slice.**

```bash
git add tests/Toklong.Shippop.Certification scripts/shippop-certify.sh docs/SHIPPOP_CERTIFICATION_RUNBOOK.md docs/06_OPEN_DECISIONS.md
git commit -m "test: observe SHIPPOP counter QR contract"
```

---

### Task 2: Add the transaction-scoped Counter QR resource

**Files:**
- Create: `src/Toklong.Domain/Transactions/ShipmentCounterQrResource.cs`
- Modify: `src/Toklong.Domain/Transactions/ManagedShipment.cs`
- Modify: `src/Toklong.Domain/Transactions/SaleTransaction.cs`
- Test: `tests/Toklong.Domain.Tests/Transactions/ShipmentCounterQrResourceTests.cs`

**Interfaces:**
- Produces: `CounterQrResourceStatus` = `Pending | Ready | RetryableError | Unavailable`.
- Produces: `CounterQrRepresentation` = `ProviderPng | ProviderCounterPayload`.
- Produces: `ShipmentCounterQrResource.Queue(Guid managedShipmentId, DateTimeOffset now)`.
- Produces: `Claim`, `RecordReady`, `RecordRetryableError`, `RecordUnavailable`, and `RequestRetry` methods with bounded values and optimistic `Version`.
- Produces: one optional `ManagedShipment.CounterQrResource`, created only for a confirmed outbound shipment.
- Produces: sanitized transaction audit events for queued, ready, rotated, retryable-error, and unavailable outcomes; metadata contains service code and safe reason only.

- [ ] **Step 1: Write failing domain tests.**

Cover these observable behaviors:

```csharp
[Fact] public void Confirmed_outbound_can_queue_one_pending_counter_qr();
[Fact] public void Resource_rejects_ready_without_protected_artifact_and_sha256();
[Fact] public void Retryable_failure_schedules_only_the_resource_read();
[Fact] public void Manual_retry_is_idempotent_and_does_not_change_shipment_status();
[Fact] public void Return_or_unconfirmed_shipment_cannot_queue_counter_qr();
```

- [ ] **Step 2: Run the tests and verify the missing-type failures.**

```bash
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj --filter FullyQualifiedName~ShipmentCounterQrResourceTests
```

- [ ] **Step 3: Implement the minimal resource and aggregate methods.**

The ready method accepts only protected bytes and metadata:

```csharp
public void RecordReady(
    CounterQrRepresentation representation,
    byte[] protectedArtifact,
    string protectionVersion,
    string artifactSha256,
    string providerResourceDigest,
    DateTimeOffset? providerExpiresAt,
    DateTimeOffset fetchedAt,
    string workerId);
```

No raw provider URL or unprotected payload is a domain property.

- [ ] **Step 4: Run the domain tests and commit.**

```bash
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj --filter FullyQualifiedName~ShipmentCounterQrResourceTests
git add src/Toklong.Domain/Transactions tests/Toklong.Domain.Tests/Transactions/ShipmentCounterQrResourceTests.cs
git commit -m "feat: add shipment counter QR resource"
```

---

### Task 3: Persist and protect Counter QR artifacts

**Files:**
- Create: `src/Toklong.Application/Abstractions/ICounterQrArtifactProtector.cs`
- Create: `src/Toklong.Infrastructure/Security/CounterQrArtifactProtector.cs`
- Create: `src/Toklong.Application/Abstractions/ICounterQrResourceRepository.cs`
- Create: `src/Toklong.Infrastructure/Persistence/CounterQrResourceRepository.cs`
- Modify: `src/Toklong.Infrastructure/Persistence/ToklongDbContext.cs`
- Modify: `src/Toklong.Infrastructure/Persistence/TransactionRepository.cs`
- Modify: `src/Toklong.Infrastructure/DependencyInjection.cs`
- Modify: `src/Toklong.Api/Program.cs`
- Modify: `src/Toklong.Worker/Program.cs`
- Create: `src/Toklong.Infrastructure/Persistence/Migrations/20260802193000_ShipmentCounterQrResource.cs`
- Modify: `src/Toklong.Infrastructure/Persistence/Migrations/ToklongDbContextModelSnapshot.cs`
- Test: `tests/Toklong.Application.Tests/Shipping/CounterQrPersistenceTests.cs`

**Interfaces:**
- Produces: `CounterQrArtifact(byte[] Content, string ContentType)` and `ProtectedCounterQrArtifact(byte[] Ciphertext, string ProtectionVersion, string Sha256)`.
- Produces: `ICounterQrArtifactProtector.Protect` and `Unprotect` using purpose `Toklong.ShipmentCounterQr.v1`.
- Produces: `ICounterQrResourceRepository.ClaimDueAsync(workerId, now, leaseDuration, cancellationToken)` using row locking/skip-locked on PostgreSQL.

- [ ] **Step 1: Write failing encryption and persistence tests.**

Assert that plaintext is not present in stored ciphertext, an isolated API/Worker Counter QR protector using the same key directory can decrypt it, ready metadata round-trips, and two workers cannot claim one live lease.

- [ ] **Step 2: Verify the failures.**

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj --filter FullyQualifiedName~CounterQrPersistenceTests
```

- [ ] **Step 3: Implement Data Protection and EF mapping.**

`CounterQrArtifactProtector` creates an isolated provider with `DataProtectionProvider.Create(...)`, application name `Toklong.CounterQr`, and the configured persistent `DataProtection:KeysPath`. It must not replace or rename the API's existing `Toklong.MobileApi` provider because that provider protects mobile sessions. API and Worker resolve the same isolated key directory, and production startup continues to require persistent protected keys. Map one resource per managed shipment, bounded string/byte lengths, concurrency token `Version`, and cascade deletion with the shipment aggregate.

- [ ] **Step 4: Generate and inspect the EF migration.**

```bash
dotnet ef migrations add ShipmentCounterQrResource --project src/Toklong.Infrastructure --startup-project src/Toklong.Api
```

Reject any migration that mutates paid transaction fields or backfills a fabricated QR state.

- [ ] **Step 5: Run tests and commit.**

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj --filter FullyQualifiedName~CounterQrPersistenceTests
git add src/Toklong.Application/Abstractions src/Toklong.Infrastructure src/Toklong.Api/Program.cs src/Toklong.Worker/Program.cs tests/Toklong.Application.Tests/Shipping/CounterQrPersistenceTests.cs
git commit -m "feat: persist encrypted counter QR resources"
```

---

### Task 4: Add provider capability gates and a read-only provider boundary

**Files:**
- Modify: `src/Toklong.Application/Abstractions/IShippingQuoteProvider.cs`
- Modify: `src/Toklong.Infrastructure/Services/DevelopmentShippingQuoteProvider.cs`
- Modify: `src/Toklong.Infrastructure/Services/ShippopShippingProvider.cs`
- Modify: `src/Toklong.Infrastructure/ProductionConfigurationValidator.cs`
- Modify: `src/Toklong.Infrastructure/Toklong.Infrastructure.csproj`
- Modify: `src/Toklong.Api/appsettings.json`
- Modify: `src/Toklong.Worker/appsettings.json`
- Test: `tests/Toklong.Application.Tests/Shipping/ShippopShippingProviderTests.cs`
- Test: `tests/Toklong.Application.Tests/Shipping/CounterQrProviderContractTests.cs`

**Interfaces:**
- Produces: `CounterQrRequest` containing the locked purchase/carrier/service/tracking references.
- Produces: `CounterQrReadResult` containing status, representation, bounded artifact, provider reference digest, authoritative expiry, fetched time, and sanitized error code.
- Adds: `IShipmentProvider.GetCounterQrAsync(CounterQrRequest, CancellationToken)`.
- Adds: fail-closed `CounterQrEnabled` and `CounterQrCertificationReference` to each `ShippopServiceProfile`.

- [ ] **Step 1: Write failing provider contract tests.**

Cover: development returns a valid bounded PNG; its payload is not tracking/purchase/label-derived; SHIPPOP quotes exclude services without both Counter QR flag and reviewed reference; checked-in `EMST`, `FLE`, `KRYX`, and `KRYS` stay disabled; SHIPPOP read returns `Unavailable` until an official contract parser exists.

- [ ] **Step 2: Verify the failures.**

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj --filter "FullyQualifiedName~CounterQrProviderContractTests|FullyQualifiedName~ShippopShippingProviderTests"
```

- [ ] **Step 3: Implement the normalized boundary and Development PNG.**

Use QRCoder only inside `DevelopmentShippingQuoteProvider`:

```csharp
var png = PngByteQRCodeHelper.GetQRCode(
    $"TOKLONG-DEVELOPMENT-COUNTER:{request.ManagedShipmentId:N}",
    QRCodeGenerator.ECCLevel.Q,
    12);
```

This makes the Development provider itself the issuer. Production SHIPPOP never renders tracking or purchase values and returns the stable unavailable code `counter-qr-contract-not-certified` until Task 1 plus provider documentation proves an exact read contract.

- [ ] **Step 4: Run tests and commit.**

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj --filter "FullyQualifiedName~CounterQrProviderContractTests|FullyQualifiedName~ShippopShippingProviderTests"
git add src/Toklong.Application/Abstractions/IShippingQuoteProvider.cs src/Toklong.Infrastructure tests/Toklong.Application.Tests/Shipping
git commit -m "feat: add certified counter QR provider boundary"
```

---

### Task 5: Retrieve and retry the resource without shipment mutations

**Files:**
- Create: `src/Toklong.Application/Features/Shipping/ProcessCounterQrResources/ProcessCounterQrResources.cs`
- Create: `src/Toklong.Application/Features/Shipping/RetryCounterQr/RetryCounterQr.cs`
- Modify: `src/Toklong.Application/Features/Shipping/ProcessShippingOperations/ProcessShippingOperations.cs`
- Modify: `src/Toklong.Worker/ShippingOperationsWorker.cs`
- Test: `tests/Toklong.Application.Tests/Shipping/CounterQrResourceProcessingTests.cs`

**Interfaces:**
- Produces: `ProcessNextCounterQrResourceCommand` and bounded batch command.
- Produces: `RetryCounterQrCommand(Guid TransactionId, Guid SellerId)`.
- Confirmation of an outbound Development/certified service queues one pending resource after the existing tracking transition succeeds.

- [ ] **Step 1: Write failing orchestration tests.**

Assert no resource exists before provider-confirmed payment; successful outbound confirmation queues exactly one resource; repeated confirmation is idempotent; the read Worker stores one encrypted artifact; retryable errors back off; manual retry changes only the resource; buyer/unrelated seller retry is forbidden; QR work never calls reserve/confirm/cancel and never changes `TransactionState`; and audit metadata contains no artifact, URL, purchase, tracking, phone, or address value.

- [ ] **Step 2: Verify failures.**

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj --filter FullyQualifiedName~CounterQrResourceProcessingTests
```

- [ ] **Step 3: Implement read-only processing and bounded backoff.**

Use the resource lease, a maximum of eight automatic attempts, `5 * 2^n` seconds capped at 300 seconds plus deterministic jitter, and sanitized error codes. `Unavailable` is terminal; `RetryableError` remains manually retryable. Do not add `RetrieveCounterQr` to `ShippingOperationType`.

- [ ] **Step 4: Run tests and commit.**

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj --filter FullyQualifiedName~CounterQrResourceProcessingTests
git add src/Toklong.Application/Features/Shipping src/Toklong.Worker/ShippingOperationsWorker.cs tests/Toklong.Application.Tests/Shipping/CounterQrResourceProcessingTests.cs
git commit -m "feat: retrieve counter QR without shipment mutations"
```

---

### Task 6: Add seller-only status, image, retry, and label-download APIs

**Files:**
- Create: `src/Toklong.Application/Features/Shipping/GetCounterQr/GetCounterQr.cs`
- Modify: `src/Toklong.Application/Transactions/TransactionView.cs`
- Modify: `src/Toklong.Api/Api/MobileApi.cs`
- Modify: `src/Toklong.Api/Program.cs`
- Test: `tests/Toklong.Application.Tests/Shipping/CounterQrAccessTests.cs`
- Test: `tests/Toklong.Api.Tests/Api/MobileCounterQrApiTests.cs`

**Interfaces:**
- Adds to seller projection: `CounterQrStatus`, `CounterQrExpiresAt`, and `CounterQrLastErrorCode`; no artifact or provider reference.
- Produces: `GET /api/mobile/transactions/{id}/counter-qr` returning ready `image/png` only.
- Produces: `POST /api/mobile/transactions/{id}/counter-qr/retry` returning `202 Accepted` for the idempotent resource retry.
- Preserves: `GET /shipping-label` as seller-only attachment download.

- [ ] **Step 1: Write failing application/API tests.**

Test seller Ready success, Pending conflict, buyer/unrelated seller/pre-payment/unconfirmed shipment forbidden, no-store/nosniff headers, no artifact in transaction JSON, bounded artifact response size, retry idempotency, retry/read rate limiting, and label attachment availability independent of QR failure.

- [ ] **Step 2: Verify failures.**

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj --filter FullyQualifiedName~CounterQrAccessTests
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj --filter FullyQualifiedName~MobileCounterQrApiTests
```

- [ ] **Step 3: Implement authorization and endpoints.**

For the PNG response set:

```text
Cache-Control: no-store
Pragma: no-cache
X-Content-Type-Options: nosniff
Content-Security-Policy: default-src 'none'; sandbox
```

Do not put the encoded value, digest, purchase reference, tracking number, or provider URL in the response headers.

- [ ] **Step 4: Run tests and commit.**

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj --filter FullyQualifiedName~CounterQrAccessTests
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj --filter FullyQualifiedName~MobileCounterQrApiTests
git add src/Toklong.Application src/Toklong.Api/Api/MobileApi.cs tests/Toklong.Application.Tests/Shipping/CounterQrAccessTests.cs tests/Toklong.Api.Tests/Api/MobileCounterQrApiTests.cs
git commit -m "feat: expose seller counter QR safely"
```

---

### Task 7: Replace outbound label preview with the mobile Counter QR experience

**Files:**
- Modify: `src/Toklong.Mobile/Core/AppTransaction.cs`
- Modify: `src/Toklong.Mobile/Core/ITransactionService.cs`
- Modify: `src/Toklong.Mobile/Core/IMobileAnalytics.cs`
- Modify: `src/Toklong.Mobile/Services/ApiTransactionService.cs`
- Modify: `src/Toklong.Mobile/ViewModels/TransactionDetailViewModel.cs`
- Modify: `src/Toklong.Mobile/Pages/TransactionDetailPage.xaml`
- Create: `src/Toklong.Mobile/ViewModels/CounterQrViewModel.cs`
- Create: `src/Toklong.Mobile/Pages/CounterQrPage.xaml`
- Create: `src/Toklong.Mobile/Pages/CounterQrPage.xaml.cs`
- Modify: `src/Toklong.Mobile/AppShell.xaml.cs`
- Modify: `src/Toklong.Mobile/MauiProgram.cs`
- Test: `tests/Toklong.Mobile.Core.Tests/CounterQrPresentationTests.cs`
- Test: `tests/Toklong.Mobile.Core.Tests/CounterQrViewModelTests.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`

**Interfaces:**
- Produces: `CounterQrImageFile(byte[] Content)` plus `DownloadCounterQrAsync` and `RetryCounterQrAsync`.
- Produces: Pending, Ready, RetryableError, and Unavailable presentation properties.
- Produces: full-screen `CounterQrPage?TransactionId={id}` with `KeepScreenOn` restored when hidden.
- Changes: outbound `DownloadShippingLabelCommand` saves a bounded temporary HTML file and opens the native share/save/print sheet directly.
- Produces: coarse `counter_qr_ready_viewed`, `counter_qr_fullscreen`, `counter_qr_retry_requested`, and `shipping_label_download_requested` analytics with no artifact or shipment reference fields.

- [ ] **Step 1: Write failing presentation, ViewModel, and XAML tests.**

Assert that the screenshot state (`PaidAwaitingShipment`, managed seller) visibly shows `กำลังเตรียม QR เคาน์เตอร์`; Ready shows a large white quiet-zone image, carrier/tracking/ship-by, `แสดงเต็มหน้าจอ`, and `ดาวน์โหลดใบปะหน้า`; Error shows `ลองโหลด QR อีกครั้ง`; expiry is absent when null; buyer never sees the card; all controls meet 44 points; analytics contain only coarse event names; and outbound UI contains no label thumbnail, WebView, `เปิดใบปะหน้า`, or `แตะเพื่อดูใบปะหน้าเต็มจอ`.

- [ ] **Step 2: Verify the failures.**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter "FullyQualifiedName~CounterQr|FullyQualifiedName~UiLayoutConsistencyTests"
```

- [ ] **Step 3: Implement mobile DTO/service/ViewModels and XAML.**

The detail page keeps its existing five-second visible-only transaction refresh. Fetch image bytes only for Ready, clear them when state/account changes, page hides, or authorization is lost, render them through an `ImageSource` whose accessibility description says `QR สำหรับส่งที่เคาน์เตอร์พร้อมใช้งาน` without reading payload text, and keep a minimum four-module quiet-zone through white card padding. Full-screen mode restores `KeepScreenOn`, never changes brightness, and remains dismissible with the shared Back affordance.

- [ ] **Step 4: Implement direct label download and temporary cleanup.**

Reuse the existing safe filename/path and native `ShareFile` behavior. The outbound action never navigates to `ShippingLabelPage`; return-label behavior may retain its existing buyer flow. Delete the temporary outbound file after the share sheet completes and clear it on error.

- [ ] **Step 5: Run tests and commit.**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter "FullyQualifiedName~CounterQr|FullyQualifiedName~UiLayoutConsistencyTests"
git add src/Toklong.Mobile tests/Toklong.Mobile.Core.Tests
git commit -m "feat: add seller counter QR mobile flow"
```

---

### Task 8: Align product documentation and verify every invariant

**Files:**
- Modify: `docs/01_USER_FLOWS_AND_STATE_MACHINE.md`
- Modify: `docs/02_UI_UX_AND_CONTENT_SPEC.md`
- Modify: `docs/03_BACKEND_TRANSACTION_RECORD.md`
- Modify: `docs/04_PAYMENT_SHIPPING_AND_DISPUTE_RULES.md`
- Modify: `docs/05_ACCEPTANCE_TESTS.md`
- Modify: `docs/06_OPEN_DECISIONS.md`
- Modify: `docs/08_IMPLEMENTATION.md`
- Modify: `docs/10_MOBILE_APP_SPEC.md`

**Interfaces:**
- Replaces outbound label preview/viewer requirements with official Counter QR Pending/Ready/Error and download-only label behavior.
- Retains the provider-contract blocker and disabled SHIPPOP service flags.

- [ ] **Step 1: Update source-of-truth copy without weakening shipping truth.**

State explicitly that a QR view/download never proves carrier custody; the first trusted scan remains the seller-handoff boundary. Keep the exact ship-by deadline, 72-hour trusted-delivery window, dispute blocking, and provider-confirmed payment/payout rules unchanged.

- [ ] **Step 2: Run focused and full verification.**

```bash
bash -n scripts/shippop-certify.sh
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj --no-restore
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj --no-restore
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj --no-restore
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore
dotnet test tests/Toklong.Crm.Tests/Toklong.Crm.Tests.csproj --no-restore
dotnet test tests/Toklong.Shippop.Certification/Toklong.Shippop.Certification.csproj --no-restore
git diff --check
```

Expected: zero failures; only documented environment-dependent certification skips remain.

- [ ] **Step 3: Verify absence and security invariants.**

```bash
rg -n "เปิดใบปะหน้า|แตะเพื่อดูใบปะหน้าเต็มจอ|ManagedShippingLabelCard|ShippingLabelWebView" src/Toklong.Mobile docs/01_USER_FLOWS_AND_STATE_MACHINE.md docs/02_UI_UX_AND_CONTENT_SPEC.md docs/04_PAYMENT_SHIPPING_AND_DISPUTE_RULES.md docs/05_ACCEPTANCE_TESTS.md
rg -n "CounterQrEnabled|CounterQrCertificationReference" src/Toklong.Api/appsettings.json src/Toklong.Worker/appsettings.json
git status --short
```

Expected: no outbound preview/viewer consumer copy remains; every checked-in SHIPPOP Counter QR flag is false and every activation stays evidence-gated.

- [ ] **Step 4: Commit documentation and final verification fixes.**

```bash
git add docs
git commit -m "docs: align counter QR fulfillment flow"
```

## Provider checkpoint and honest completion boundary

The code slice is complete when Development can exercise the full mobile flow and production SHIPPOP remains fail-closed. A real SHIPPOP Sandbox Ready QR additionally requires all of the following external evidence:

1. exact authenticated endpoint or confirmation field;
2. explicit statement that it is for counter handoff;
3. PNG or provider-counter-payload representation;
4. authoritative expiry/rotation semantics;
5. safe read-only repeated retrieval after confirmation; and
6. controlled account/service counter-acceptance evidence.

Without that contract, the app must show the safe Pending/Error/label fallback and must never synthesize a SHIPPOP QR.
