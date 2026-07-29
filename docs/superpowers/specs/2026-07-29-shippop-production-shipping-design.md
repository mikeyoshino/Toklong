# SHIPPOP Production Shipping Design

**Date:** 2026-07-29
**Status:** Approved for implementation
**Scope:** Production-grade outbound and return shipping through SHIPPOP for
physical TOKLONG transactions

## 1. Outcome

TOKLONG will replace direct, synchronous mutation calls to SHIPPOP with a
durable shipping orchestrator. The orchestrator must safely continue after
process crashes, timeouts, duplicate commands, delayed provider responses, and
database failures without creating duplicate bookings or treating uncertain
carrier data as payment-release evidence.

The production flow is:

```text
Seller selects a certified drop-off service and insured quote
  → TOKLONG creates one unconfirmed outbound booking
  → Seller accepts the immutable agreement
  → Buyer obtains provider-confirmed payment within one hour
  → Worker confirms the exact stored SHIPPOP purchase
  → Seller receives the provider tracking number and 4×6 label
  → Carrier first scan proves seller handoff
  → Carrier progress is reconciled
  → complete plus a trusted delivery timestamp starts the 72-hour window
  → no dispute/hold/refund makes payout eligible
```

Any uncertain mutation result, mismatched carrier/tracking data, missing trusted
delivery timestamp, provider problem/invalid/return status, unexpected
surcharge, or insurance case fails closed and cannot release payout.

## 2. Approved product decisions

1. The target is production readiness, not only a Dev demo.
2. Use a durable shipping orchestrator inside the existing application and
   Worker deployment. Do not add a message broker or separate shipping service
   for the MVP.
3. Support drop-off services only. Do not claim pickup behavior.
4. Prepare support for `EMST`, `FLE`, `KRYX`, and `KRYS`, but enable each code
   only after account-specific provider certification proves drop-off,
   insurance, tracking, label, cancellation, and rate-limit behavior.
5. A SHIPPOP `complete` value without a trusted carrier delivery timestamp does
   not start the 72-hour physical inspection window. It enters
   `TrackingUnverified` and manual review.
6. Consumer tracking uses four shipping milestones:
   `เตรียมจัดส่ง`, `ขนส่งรับพัสดุแล้ว`, `กำลังจัดส่ง`, and `ส่งถึงแล้ว`.
   Detailed carrier events are available under `รายละเอียดการเดินทาง`.
7. A provider mutation timeout is not an ordinary failure. It becomes
   `OutcomeUnknown`; TOKLONG reconciles the original operation before any
   retry.
8. All four service codes remain disabled until individually certified.
9. `problem`, `invalid`, `return`, and related exception states block automatic
   payout and automatic refund and create a carrier-exception review.
10. Every enabled service must provide insurance for the full approved item
    value. A service without approved coverage remains disabled.
11. An unpaid accepted offer closes at the exact one-hour deadline. Booking
    cleanup continues in the background and cannot keep the consumer offer
    open.
12. Existing credentials disclosed during design are considered compromised.
    They will not be used. A rotated Dev API key and password must be supplied
    outside Git through server-side secret configuration.
13. `ค่าจัดส่ง` and `ค่าประกันพัสดุ` are separate buyer-visible amounts.
14. Insurance claims are managed by authorized CRM operations. AI and ordinary
    consumer actions cannot resolve a claim.
15. An approved return uses a separate provider-managed return shipment.
    Refund starts only after trusted return delivery or an authorized manual
    resolution.
16. TOKLONG advances the return-shipping cost and records responsibility for
    later operations/accounting handling.
17. TOKLONG absorbs post-payment carrier surcharge from an operational reserve.
    A surcharge never mutates the paid buyer total or seller net.

## 3. Architecture

The architecture has four boundaries:

```text
API and domain command
  → committed ShippingOperation
  → ShippingOperationsWorker
  ⇄ SHIPPOP HTTPS API
```

The API/domain command and new operation are committed in one database
transaction. The Worker calls SHIPPOP outside that transaction. It then opens a
new database transaction, validates the provider result against the immutable
transaction and shipment snapshot, applies the allow-listed domain transition,
writes audit/analytics records, and completes the operation atomically.

The mobile application never calls SHIPPOP. It reads normalized TOKLONG state.

### 3.1 Durable mutations

The following provider-changing actions require durable operations:

- `BookOutbound`
- `ConfirmOutbound`
- `CancelOutbound`
- `BookReturn`
- `ConfirmReturn`
- `CancelReturn`

The following are safe reads and do not create a durable mutation row for every
request:

- price lookup;
- label retrieval;
- tracking reconciliation.

Safe reads still require bounded retry, rate-limit handling, response-size
limits, provider validation, logging redaction, and metrics.

### 3.2 Operation lifecycle

```text
Pending
  → Processing
  → Succeeded

Processing
  → RetryScheduled       only when retry safety is proven
  → OutcomeUnknown       request may have reached SHIPPOP
  → NeedsReview          result cannot be proven safely
```

`OutcomeUnknown` never returns to `Pending` merely because a timer elapsed.
The Worker must first find the result of the original request or obtain a
provider guarantee that replay is idempotent.

Operations use a processing lease. A second Worker cannot process a live lease.
An expired lease can be reclaimed. Every claim and result update uses optimistic
concurrency.

### 3.3 Fail-closed provider gates

Production activation requires written or testable answers for:

- how to find a booking by TOKLONG reference such as `meta.ref_no_1`, or an
  explicit SHIPPOP booking-idempotency guarantee;
- whether repeated `confirm` and `cancel` calls are idempotent;
- how to cancel an unconfirmed booking that has no courier tracking code;
- the natural expiry behavior of an unconfirmed booking;
- rate limits and `Retry-After` behavior;
- trusted delivery status/timestamp semantics per enabled service.

If SHIPPOP does not provide a safe reconciliation mechanism for an uncertain
booking, that service cannot be enabled in production.

## 4. Data model

### 4.1 Managed shipment

`ManagedShipment` is a transaction child entity. A transaction has exactly one
outbound shipment for a paid physical agreement and at most one active return
shipment.

```text
id
transaction_id
direction = outbound | return
provider
status
origin_private_snapshot_reference
destination_private_snapshot_reference
parcel_name
weight_grams
width_centimeters
length_centimeters
height_centimeters
carrier_code
service_code
service_name
handoff_mode = drop_off
base_shipping_fee_satang
insurance_fee_satang
declared_value_satang
insurance_code
quote_reference
quote_expires_at
purchase_reference
provider_tracking_code
courier_tracking_code
reserved_at
confirmed_at
cancelled_at
first_carrier_scan_at
in_transit_at
delivered_at
last_provider_status
last_reconciled_at
created_at
version
```

The entity contains an immutable accepted shipment snapshot plus provider
lifecycle fields. Outbound and return provider references cannot be reused or
silently exchanged.

New physical agreements use schema version 9 for both the agreement-core and
paid-product documents because the current aggregate has one shared schema
number. They add the parcel-insurance fee, insurance code, declared value, and
managed-shipment reference. Version 8 remains readable without inventing
coverage that did not exist.

### 4.2 Shipping operation

```text
id
transaction_id
managed_shipment_id
operation_type
status
idempotency_key
request_fingerprint
provider_purchase_reference
provider_tracking_reference
attempt_count
next_attempt_at
lease_owner
lease_expires_at
last_sanitized_error_code
created_at
started_at
completed_at
version
```

The idempotency key is unique. The operation does not store an API key or a
duplicate raw address payload. It reconstructs the request from the immutable
private fulfillment snapshot and verifies it using `request_fingerprint`.

### 4.3 Provider shipping adjustment

Unexpected provider charges are append-only:

```text
id
transaction_id
managed_shipment_id
provider_reference
adjustment_type
amount_satang
currency
provider_occurred_at
observed_at
crm_case_reference
created_at
```

They are TOKLONG operational costs. They do not change buyer-paid or
seller-payable values.

### 4.4 Insurance case

```text
id
transaction_id
managed_shipment_id
provider_case_reference
claim_reason
declared_value_satang
claimed_amount_satang
approved_amount_satang
status
opened_at
resolved_at
crm_case_reference
```

Only authorized CRM commands can open or resolve the operational case. Provider
claim completion does not by itself choose a buyer refund or seller payout
outcome.

## 5. Money and insurance

All monetary values remain integer satang with ISO currency `THB`.

```text
buyer_total_satang =
  item_price_satang
  + shipping_fee_satang
  + parcel_insurance_fee_satang
  + buyer_protection_fee_satang
```

For the current fee policy:

```text
seller_expected_net_satang = item_price_satang
```

Shipping and insurance are buyer-funded pass-through amounts and are not seller
proceeds. They are shown as separate rows before buyer acceptance and payment.

The signed quote fingerprint includes origin, destination, parcel measurements,
service code, insurance code, declared value, shipping fee, insurance fee, and
expiry. Changing any value invalidates the quote.

The booking response must match the selected carrier/service and all disclosed
charges. A mismatch rejects seller acceptance and requires a new quote.

Provider certification must establish:

- the insurance code for each service;
- coverage limits and excluded item categories;
- the unit and rounding rule for `declared_value`;
- where the insurance premium appears in quote and booking responses;
- post-booking surcharge types;
- claim process and SLA.

No rounding or coverage assumption may be invented in application code.

## 6. Outbound state flow

```text
Quote selected and certified
  → BookOutbound operation
  → unconfirmed SHIPPOP booking (`force_confirm=0`)
  → seller acceptance and one-hour buyer payment deadline
```

If provider-confirmed payment occurs on time:

```text
PaidAwaitingShipment
  → ConfirmOutbound operation
  → TrackingSubmitted / เตรียมจัดส่ง
  → first trusted carrier scan / ขนส่งรับพัสดุแล้ว
  → shipping / กำลังจัดส่ง
  → complete + trusted delivery timestamp / ส่งถึงแล้ว
  → 72-hour inspection window
```

The label and tracking number alone do not prove handoff. The first trusted
carrier scan is the seller-handoff boundary.

If the buyer does not pay:

```text
buyer payment deadline reached
  → Expired / BuyerDidNotPay
  → notify both parties that no payment was collected
  → enqueue CancelOutbound cleanup
```

Cleanup retry happens in the background and cannot reactivate or extend the
offer.

## 7. Tracking truth and consumer presentation

The main transaction progress remains the existing role-specific three-stage
TOKLONG progress. A separate shipping card contains four milestones:

1. `เตรียมจัดส่ง` — provider booking confirmed and label available; no carrier
   scan yet.
2. `ขนส่งรับพัสดุแล้ว` — first trusted matching carrier scan.
3. `กำลังจัดส่ง` — verified in-transit events.
4. `ส่งถึงแล้ว` — SHIPPOP complete plus a trusted delivery timestamp.

`รายละเอียดการเดินทาง` shows normalized carrier description, location, and
exact local time. Raw `order_status`, provider identifiers, internal event
names, and technical reconciliation text are not consumer copy.

`complete` without a trusted delivery timestamp enters
`TrackingUnverified`. TOKLONG must not use poll-observation time as delivery
time.

Problem copy is `การจัดส่งต้องตรวจสอบ`. It states that payout is paused and
offers one primary action, `ดูรายละเอียด`.

## 8. Carrier exceptions

These conditions block ordinary release:

- missing trusted delivery timestamp;
- mismatched provider tracking or courier tracking;
- carrier/service mismatch;
- suspicious prior delivery or reused tracking;
- `invalid`, `problem`, `return`, or any return-exception state;
- unexpected material surcharge;
- lost/damaged claim;
- unrecognized provider status.

Processing is:

```text
normalize and retain provider evidence
  → move to TrackingUnverified or CarrierException
  → block payout and automatic refund
  → create CRM case
  → notify both parties in plain language
  → require authorized resolution
```

AI may summarize the case but cannot choose refund, payout, or insurance
allocation.

## 9. Return shipping

An authorized dispute resolution can require a return. TOKLONG creates a new
`ManagedShipment` with `Direction=Return`; it never reverses the outbound
shipment fields.

```text
authorized return decision
  → BookReturn
  → ConfirmReturn
  → buyer receives return label
  → trusted first return scan
  → trusted return delivery to seller
  → authorized refund instruction
  → provider-confirmed refund completion
```

TOKLONG advances the return cost. The cost is an operational accounting record,
not a mutation of the original paid agreement. Missing return delivery evidence
blocks automatic refund and enters manual review.

## 10. Reliability and provider access

- HTTPS is mandatory, including SHIPPOP Dev.
- Rotated credentials are loaded only from server secrets.
- Mobile clients, public APIs, logs, analytics, and Git never receive provider
  credentials.
- Read retries use exponential backoff with jitter and respect `Retry-After`.
- Mutation retries occur only after proven-safe reconciliation.
- Tracking polling interval and concurrency are configurable from certified
  rate limits; mobile refresh never causes a direct provider call.
- Poll scheduling uses jitter to avoid synchronized bursts.
- A provider outage preserves the last trusted state.
- The documented unsigned SHIPPOP callback remains disabled.
- Response bodies have strict size/depth limits and are normalized before
  persistence.
- Logs redact keys, phones, addresses, labels, and raw provider payloads.

## 11. Operations and controls

Kill switches exist per service code and capability:

- quote;
- outbound book;
- outbound confirm;
- return;
- insurance.

Metrics and alerts cover:

- pending operation count and age;
- processing lease expiry;
- outcome-unknown count and age;
- retry count and oldest retry;
- paid shipment awaiting confirmation;
- tracking polling lag;
- cancellation backlog;
- delivery without trusted timestamp;
- surcharge count/value;
- carrier and insurance cases;
- service-specific error and latency rate.

Manual retry or resolution requires an authorized role, reason, correlation
reference, and immutable audit event.

## 12. Audit and analytics

Audit events include:

- shipping operation queued, claimed, outcome unknown, reconciled, completed,
  and sent to review;
- outbound/return booking reserved, confirmed, and cancelled;
- trusted first scan and trusted delivery;
- carrier exception created;
- surcharge recorded;
- insurance case opened/resolved;
- return authorized and return delivered.

Analytics events measure user-visible progression and operational latency but
must not contain full addresses, phone numbers, tracking numbers, provider
payloads, or credentials.

## 13. Testing

Automated coverage must include:

- operation/domain atomic creation;
- unique idempotency key and concurrent claim exclusion;
- expired lease recovery;
- outcome-unknown booking does not replay blindly;
- confirm requires provider-confirmed payment;
- unpaid expiry closes the offer and queues cleanup;
- integer-satang shipping/insurance arithmetic;
- quote/booking carrier, service, fee, and insurance mismatch rejection;
- complete without trusted timestamp cannot create `delivered_at`;
- carrier/tracking mismatch blocks release;
- replayed provider events are idempotent;
- surcharge cannot mutate the paid snapshot or seller net;
- every exception blocks payout and automatic refund;
- outbound and return references remain separate;
- refund waits for trusted return delivery or authorized manual resolution;
- label and shipment authorization;
- logging/serialization secret and personal-data redaction;
- accessible four-step shipping progress and carrier-event disclosure.

Changes to state transitions, authorization, tracking, dispute blocking,
payment release, and carrier idempotency require their existing full regression
suites.

## 14. SHIPPOP Dev certification

Certification uses a rotated Dev key in local server secrets and synthetic test
contacts/addresses. No credential is committed or placed in a mobile build.

Each of `EMST`, `FLE`, `KRYX`, and `KRYS` is tested independently:

```text
pricelist
  → booking force_confirm=0
  → verify wait/unconfirmed behavior
  → confirm
  → retrieve 4×6 label
  → tracking normalization
  → cancel before scan
  → insurance and declared value
  → surcharge response behavior
  → return booking
```

The certification record stores no API key. It records the service, account,
Dev environment, test references, observed request/response contract,
drop-off behavior, insurance, cancellation, duplicate behavior, rate limit,
label format, tracking/POD behavior, result, reviewer, and date.

Production enablement requires:

1. automated tests pass;
2. account-specific Dev certification passes;
3. provider questions are answered;
4. monitoring and alerts are active;
5. kill switch is tested;
6. production credentials and commercial approval exist;
7. no service remains enabled by default merely because code supports it.

## 15. Documentation updates

The implementation must keep these canonical documents aligned:

- `docs/01_USER_FLOWS_AND_STATE_MACHINE.md`
- `docs/02_UI_UX_AND_CONTENT_SPEC.md`
- `docs/03_BACKEND_TRANSACTION_RECORD.md`
- `docs/04_PAYMENT_SHIPPING_AND_DISPUTE_RULES.md`
- `docs/05_ACCEPTANCE_TESTS.md`
- `docs/06_OPEN_DECISIONS.md`

New provider facts discovered during certification update the applicable
canonical rule or remain an explicit launch blocker. They must not live only in
test notes, code comments, or chat.

## 16. Implementation slices

The design is one subsystem but implementation is divided into safe vertical
slices:

1. Durable outbound operation infrastructure and migration.
2. Retry-safe booking, confirmation, and cancellation.
3. Strict tracking reconciliation and four-stage consumer presentation.
4. Unpaid-booking cleanup, surcharge recording, metrics, and kill switches.
5. Insurance quote/booking fields and CRM carrier/insurance cases.
6. Provider-managed return shipment and refund gate.
7. SHIPPOP Dev certification tooling and evidence.

Each slice must preserve existing buyer-first authorization, immutable paid
snapshot, dispute payout block, provider-confirmed money states, and the rule
that only trusted carrier delivery starts the physical 72-hour window.
