# SHIPPOP Counter QR Seller Flow Design

**Date:** 2026-08-02

**Status:** Approved in brainstorming; awaiting written-spec review

**Scope:** Provider-managed physical shipments only

## 1. Objective

After provider-confirmed buyer payment and successful confirmation of the
reserved SHIPPOP shipment, make the official counter QR the seller's primary
drop-off tool. The seller sees a large, easy-to-scan QR in TOKLONG and may
download the original 4×6 provider label as a fallback. TOKLONG does not show a
label preview or open provider label HTML inside an in-app WebView.

This is a fulfillment-resource change, not a new transaction or payout state.
It does not weaken payment truth, immutable paid snapshots, trusted carrier
tracking, dispute blocking, or payout-release rules.

## 2. Product decisions

The approved decisions are:

1. Use only an official SHIPPOP counter-QR artifact or a SHIPPOP-issued payload
   explicitly certified for counter use.
2. Never create a counter QR from the courier tracking number, purchase ID,
   label barcode, or another inferred value.
3. Do not extract a QR from provider label HTML.
4. Require independent, account-specific certification for each service code.
   A service without certified counter-QR support is absent from seller quotes.
5. Show the QR only to the authenticated seller after provider-confirmed
   payment and successful outbound-shipment confirmation.
6. Keep the label available through `ดาวน์โหลดใบปะหน้า`; never preview it in
   the transaction page or a dedicated in-app viewer.
7. If QR retrieval fails after a certified shipment has been confirmed, retry
   only the read-oriented QR retrieval. Never replay booking or confirmation.
8. Show an expiry only when SHIPPOP supplies an authoritative expiry for the
   exact artifact. Do not invent a countdown or expiry.
9. A QR, label, tracking allocation, or seller screen view does not prove a
   carrier scan, delivery, or payout eligibility.

## 3. Non-goals

- Do not add pickup, on-demand collection, branch discovery, locker delivery,
  or a generic barcode feature.
- Do not enable a new SHIPPOP service or production capability merely because
  code support exists.
- Do not call SHIPPOP directly from the mobile app.
- Do not expose the counter QR to the buyer, another seller, a public link, an
  OS notification, analytics, support notes, or ordinary logs.
- Do not make a client callback, QR display, label download, or seller action
  authoritative for payment, carrier custody, delivery, refund, or payout.
- Do not change the paid snapshot or the existing physical transaction-state
  sequence.
- Do not add manual carrier or tracking replacement to a provider-managed
  shipment.

## 4. Seller experience

The physical seller transaction retains the current purple seller treatment,
exact ship-by date and time, selected carrier/service, provider-issued tracking
number, shipping progress, and payout condition. The fulfillment card replaces
the compact 4×6 label preview with the counter-QR presentation below.

### 4.1 Pending

Shown after the reserved shipment is confirmed but before an artifact is
available:

- title `กำลังเตรียม QR เคาน์เตอร์`;
- short copy explaining that TOKLONG is obtaining it from the selected
  delivery service;
- a bounded loading treatment and automatic refresh only while the page is
  visible;
- `ดาวน์โหลดใบปะหน้า` as soon as the provider label is available; and
- no empty QR frame, generated placeholder, fake countdown, or instruction to
  ship before the resource is ready.

The transaction page refreshes server state. The mobile app does not poll
SHIPPOP and stops its refresh loop when the page is hidden.

### 4.2 Ready

The counter QR is the largest element in the fulfillment card. The screen
shows:

- title `QR สำหรับส่งที่เคาน์เตอร์`;
- the official QR with sufficient quiet zone and contrast;
- the selected carrier/service and provider-issued tracking number;
- the exact ship-by date and time;
- `แสดงเต็มหน้าจอ` as the primary QR action;
- `ดาวน์โหลดใบปะหน้า` as a secondary action; and
- the provider expiry only when it is authoritative.

Full-screen mode keeps the device awake while visible, preserves the QR quiet
zone, and provides an obvious close/Back action. It does not silently change
system brightness. Screen-reader text announces that a counter QR is ready but
does not read the encoded value aloud.

Consumer instructions must be scoped to the certified service. The app must
not claim that every branch, carrier, or counter accepts screen scanning.

### 4.3 Retrieval error

For a retryable or terminal retrieval failure after shipment confirmation, the
card shows:

- title `ยังโหลด QR ไม่สำเร็จ`;
- a plain-language, sanitized explanation;
- primary action `ลองโหลด QR อีกครั้ง`; and
- `ดาวน์โหลดใบปะหน้า` whenever the label is available.

Retry requests only the official QR resource for the already-confirmed
shipment. It cannot enqueue or invoke booking, outbound confirmation, payment,
tracking submission, or another state transition.

If SHIPPOP reports that a supposedly certified service cannot supply its QR,
the current seller keeps the printable-label fallback and sees support-safe
guidance. Operations is alerted and the service's counter-QR capability is
disabled for new quotes until recertified.

## 5. Transaction and provider flow

```text
Seller selects one certified service while preparing the sale
  → Seller confirms readiness
  → Buyer reviews and pays
  → Verified payment-provider event confirms payment
  → Existing Worker confirms the exact reserved SHIPPOP shipment
  → Provider-issued tracking and downloadable label become available
  → Worker reads the official counter-QR resource
  → Seller-only mobile API returns Pending, Ready, or Error
  → Carrier scan remains the first authoritative seller-handoff event
```

The existing domain states remain authoritative:

```text
PAYMENT_PENDING
  → PAID_AWAITING_SHIPMENT
  → TRACKING_SUBMITTED
  → IN_TRANSIT
  → DELIVERED_DISPUTE_WINDOW
```

Counter-QR readiness does not add or substitute a `TransactionState`. It is a
resource status on the managed outbound shipment. It creates no inspection
window, dispute deadline, refund, payout eligibility, or settlement event.

## 6. Capability gate and certification

Every service profile gains a fail-closed counter-QR capability and a non-empty
reviewed certification reference. A service is returned by the quote boundary
only when every normal production prerequisite passes and its official counter
QR is certified.

For the current allow-list, `EMST`, `FLE`, `KRYX`, and `KRYS` are assessed
separately. Success for one code never enables another. Checked-in production
flags remain off.

The existing SHIPPOP Sandbox capability exercise is extended to record, for
each service:

| Capability | Passing evidence |
|---|---|
| Official source | Exact authenticated endpoint or response field is documented and observed |
| Timing | Artifact is available after the exact confirmed booking without a second mutation |
| Representation | Provider image or provider-designated counter payload has a bounded, known format |
| Counter purpose | SHIPPOP/carrier evidence identifies it specifically for counter handoff |
| Repeated read | Re-fetch is read-only and safe; it does not create or confirm another shipment |
| Expiry | Expiry/rotation semantics are authoritative, or explicitly absent |
| Service match | Purchase, carrier, service, and tracking references match the locked shipment |
| Failure contract | Missing, pending, expired, invalid, and provider-error outcomes are distinguishable |
| Label fallback | The original provider label remains independently downloadable |
| Privacy | Evidence can be sanitized without storing raw QR, address, phone, credential, or label content |

`pass`, `fail`, `blocked`, and `not_observed` are distinct. Only `pass` may
enable the service. Sandbox observation alone does not prove that a production
branch can scan a test artifact; provider confirmation and a controlled
carrier/counter exercise are required where the Sandbox cannot establish that
fact.

The application adapter is implemented only after the exercise establishes
the exact SHIPPOP contract. No speculative field name or endpoint is added to
production parsing.

## 7. Provider-neutral backend contract

The shipping boundary exposes a read-oriented counter-QR result for an existing
confirmed managed shipment. The normalized result contains only:

```text
status = pending | ready | retryable_error | unavailable
representation = provider_png | provider_counter_payload
encrypted_artifact
artifact_sha256
provider_resource_reference_or_digest
provider_expires_at_or_null
fetched_at
last_sanitized_error_code_or_null
```

The exact representation is enabled only after certification:

- `provider_png` is a bounded image returned by SHIPPOP or fetched server-side
  from an authenticated SHIPPOP resource. The mobile app never receives a
  reusable provider URL.
- `provider_counter_payload` is allowed only when SHIPPOP explicitly defines
  the value as the payload for its counter QR. TOKLONG may render that exact
  provider-issued payload, but cannot derive it from tracking, purchase, or
  label data.

HTML, SVG with active content, arbitrary remote URLs, unknown formats,
oversized data, malformed images, and tracking-derived values fail closed. Raw
SHIPPOP responses are neither stored nor returned.

The artifact is transaction-scoped, encrypted at rest, and associated with the
outbound managed shipment rather than the immutable paid product snapshot. A
hash supports change detection without logging or exposing the value. When
SHIPPOP rotates an artifact, TOKLONG appends resource history or audit evidence
and serves only the latest valid artifact; historical secret content is not
placed in consumer-visible audit data.

## 8. Worker orchestration and retry safety

Successful completion of the existing durable `ConfirmOutbound` operation
sets the QR resource status to `pending` and queues one idempotent read task.
The task either consumes a certified field captured from the confirmation
response or invokes the certified read endpoint. The selected mechanism is
fixed per service certification.

Read attempts use bounded exponential backoff, a processing lease, and a stable
shipment-scoped key. A timeout may retry only this read because it has no
provider mutation. It must not reuse the mutation operation path or call:

- booking;
- confirm;
- cancel;
- return booking;
- payment preparation; or
- transaction transition commands.

Manual `ลองโหลด QR อีกครั้ง` releases or schedules the same read task when it
is eligible. Concurrent taps, Worker retries, app restarts, and page refreshes
produce one current resource and no duplicate provider mutations.

## 9. Seller-only API and authorization

The mobile API returns normalized resource state and never exposes raw SHIPPOP
JSON. Access requires all of the following:

- a valid authenticated mobile session;
- the authenticated user is the transaction seller;
- physical, provider-managed outbound shipment;
- provider-confirmed payment remains valid;
- successful confirmation of the exact managed shipment; and
- no shipment/reference mismatch or access hold.

Buyer, another seller, public-link, expired-session, pre-payment, unconfirmed-
shipment, and mismatched-reference requests are forbidden without revealing
artifact data.

QR responses use `Cache-Control: no-store` and an appropriate no-cache privacy
policy. They are not placed in shared caches, push notifications, error text,
analytics payloads, or support-visible logs. Mobile state is cleared on sign
out, account switch, or loss of authorization.

## 10. Label download

The existing seller-only label authorization remains, but the mobile behavior
changes from preview/viewer to download-only:

- the action is labelled `ดาวน์โหลดใบปะหน้า`;
- the response uses attachment disposition and a safe filename;
- the original provider file is not transformed into a QR source;
- the app stores it only in bounded temporary/app-owned download storage and
  offers the native save/share/print path supported by the device;
- provider HTML is never opened in an application WebView; and
- temporary files follow cleanup and sign-out rules.

Label access remains unavailable before provider-confirmed payment and shipment
confirmation. Downloading it does not change shipment or transaction state.

## 11. Security, privacy, audit, and analytics

- Encrypt counter artifacts in transit and at rest.
- Validate representation, decoded size, dimensions, payload length, and
  content before persistence or response.
- Never log artifact bytes/text, provider URLs, raw responses, API keys,
  addresses, phones, or label contents.
- Keep provider credentials server-side.
- Rate-limit seller reads and manual retry without preventing normal page
  refresh.
- Record seller authorization checks and suspicious cross-account access using
  sanitized security events.
- Append sanitized audit events when the artifact becomes ready, is rotated,
  becomes unavailable, or reaches a terminal provider error. Metadata contains
  service and safe reason codes, not the QR value.
- Limit mobile analytics to coarse events such as QR ready/viewed/full-screen,
  retry requested, and label download requested. Do not include the QR,
  tracking number, purchase reference, provider URL, address, or phone.
- Apply the managed-shipment/transaction retention schedule. Artifact content
  is removed with the applicable transaction aggregate and any temporary mobile
  copy is cleaned independently.

## 12. Error behavior

| Condition | Seller experience | System behavior |
|---|---|---|
| QR not ready yet | Pending card; label download when ready | Bounded read retries only |
| Temporary SHIPPOP/read failure | Retry card; label fallback | Retry resource read with backoff |
| Unknown or unsafe representation | Error card; label fallback | Reject, audit sanitized reason, alert operations |
| Artifact expired with certified refresh behavior | Pending/error until refreshed | Fetch replacement through the certified read path |
| Artifact expired without certified refresh behavior | Error and label fallback | No guessed regeneration; operations review |
| Certified capability disappears | Error and label fallback | Disable service for new quotes and alert operations |
| Authorization/payment/reference mismatch | No artifact disclosure | Forbid and record sanitized security evidence |
| Label unavailable but QR ready | QR remains usable | Retry label independently; no QR regression |

No error path generates a counter QR locally from another shipment value or
marks the parcel handed over.

## 13. Test requirements

### 13.1 Capability and parser

- Each service remains absent from quotes without a passing counter-QR
  certification reference.
- Certified provider image and provider counter-payload fixtures normalize to
  the expected representation.
- Missing, unknown, ambiguous, malformed, active-content, oversized, and
  reference-mismatched results fail closed.
- Tracking number, purchase ID, courier barcode, and label HTML are never
  accepted as implicit counter-QR sources.
- Provider expiry is preserved exactly; absent expiry remains absent.

### 13.2 Worker and integration

- No QR task or artifact exists before provider-confirmed payment.
- Verified payment queues the existing outbound confirmation, not QR readiness
  directly.
- Successful shipment confirmation queues one idempotent QR read.
- Repeated payment events, confirmation completion, QR reads, retries, and app
  refreshes do not duplicate booking, confirmation, tracking, audit, or
  artifact records.
- A QR timeout retries only the read task.
- QR failure leaves the label independently downloadable.
- Counter-QR readiness does not start a carrier scan, delivery window, refund,
  payout, or another transaction transition.
- An open dispute still blocks payout in every applicable path.

### 13.3 Authorization and security

- Only the authenticated transaction seller can read the resource or download
  the label.
- Buyer, unrelated user, pre-payment, unconfirmed shipment, expired session,
  and mismatched shipment requests are forbidden.
- QR responses are `no-store`; logs, analytics, notifications, and audit
  metadata contain no artifact or reusable provider URL.
- Resource size/rate limits and sign-out/account-switch clearing are tested.

### 13.4 Mobile UI and accessibility

- Pending, ready, and retrieval-error cards match the approved mobile-first
  hierarchy.
- Ready uses a large QR, sufficient quiet zone, strong contrast, and one
  full-screen action.
- Full-screen mode remains dismissible, supports large text, keeps the device
  awake, and does not expose the payload as spoken text.
- Exact ship-by date/time remains visible and authoritative provider expiry is
  shown only when present.
- No label thumbnail, HTML preview, WebView, `เปิดใบปะหน้า`, or
  `แตะเพื่อดูใบปะหน้าเต็มจอ` remains in this seller flow.
- `ดาวน์โหลดใบปะหน้า` uses the native file/save/share path.
- Interactive controls have at least 44×44 point targets, correct focus order,
  semantic labels, and acceptable contrast on supported iOS and Android sizes.

## 14. Required documentation alignment during implementation

The implementation slice must update the existing source-of-truth documents
and acceptance tests that currently require an in-app label preview/viewer:

- `docs/01_USER_FLOWS_AND_STATE_MACHINE.md` seller physical fulfillment;
- `docs/02_UI_UX_AND_CONTENT_SPEC.md` Scene 4 and seller shipment UI;
- `docs/03_BACKEND_TRANSACTION_RECORD.md` shipment resource and seller
  permissions;
- `docs/04_PAYMENT_SHIPPING_AND_DISPUTE_RULES.md` shipment rule 6;
- `docs/05_ACCEPTANCE_TESTS.md` B3 and C1;
- `docs/06_OPEN_DECISIONS.md` counter-QR certification blockers; and
- the SHIPPOP Sandbox capability exercise design and its resulting plan.

The updated documents must preserve the original-provider label download,
while removing label preview/full-screen HTML behavior and adding the certified
counter-QR capability gate.

## 15. Assumptions and provider blockers

- The SHIPPOP Dev/Sandbox account is permitted to exercise the relevant
  shipping services without real customer data.
- The exact official counter-QR endpoint/field, representation, expiry,
  rotation, and repeated-read behavior are not established by the currently
  reviewed public documentation.
- Implementation of provider parsing is blocked until real sanitized Sandbox
  evidence or SHIPPOP's account-specific documentation establishes that
  contract.
- A Sandbox artifact is not presented as proof of real-counter acceptance when
  the carrier cannot scan it outside Production. Provider/carrier certification
  must close that gap before a service is enabled.
- If SHIPPOP provides no official counter QR for a service, that service is not
  offered under this product design; the application does not substitute a
  tracking-derived QR.

## 16. Rollout

1. Extend the isolated Sandbox certification runner and gather sanitized
   evidence per service.
2. Review the exact provider contract and counter-acceptance evidence.
3. Implement the provider-neutral resource, seller-only API, Worker read task,
   label download-only behavior, and mobile states behind disabled capability
   flags.
4. Run parser, authorization, idempotency, state-transition, mobile
   accessibility, and regression tests.
5. Enable one service only after its complete certification reference is
   approved. Monitor sanitized success/error rates and disable the service for
   new quotes if the provider contract drifts.

Existing paid shipments remain immutable. Capability rollback affects new
service selection and QR retrieval policy; it does not mutate their payment,
agreement, tracking, delivery, dispute, refund, or payout evidence.
