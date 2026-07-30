# Synchronous SHIPPOP Booking and Scalable Confirmation Design

**Date:** 2026-07-30
**Status:** Approved design; awaiting written-spec review
**Scope:** Physical checkout booking, post-payment confirmation, and horizontal
scale without RabbitMQ

## 1. Outcome

TOKLONG will create the unconfirmed SHIPPOP booking in the buyer's payment
request instead of waiting for a separate booking worker. A successful,
validated booking is required before TOKLONG creates or returns a Stripe
PaymentSheet.

After a verified Stripe payment webhook, TOKLONG will durably enqueue
`ConfirmOutbound`. A bounded background job runner in the existing deployment
will confirm and reconcile the paid shipment. Tracking, cancellation, returns,
and deadline processing remain durable background work.

The target experience is:

```text
Buyer taps Pay
  → TOKLONG validates the final checkout snapshot
  → TOKLONG books with SHIPPOP using force_confirm=0
  → TOKLONG validates and stores the exact booking response
  → TOKLONG creates or reuses the matching Stripe PaymentIntent
  → PaymentSheet opens
  → verified Stripe webhook confirms payment
  → durable ConfirmOutbound job is committed
  → background runner confirms the stored SHIPPOP purchase
  → seller fulfillment becomes available only after confirmation succeeds
```

The design targets a burst of 1,000 buyer requests and p95 API latency of at
most three seconds. That latency target is conditional on certified SHIPPOP
capacity and response time; TOKLONG cannot compensate for a provider that
cannot sustain the required request rate.

## 2. Product and domain invariants

This design does not change the following rules:

- The mobile client never calls SHIPPOP or Stripe server APIs directly.
- The paid transaction snapshot is immutable.
- Money uses integer satang and an ISO currency code.
- A client callback, redirect, screenshot, or local state never proves payment.
- Only a verified Stripe webhook or authorized reconciliation job may confirm
  payment.
- The seller cannot fulfill until payment and the outbound booking confirmation
  are both provider-confirmed.
- A confirmation failure or uncertain outcome blocks fulfillment and payout.
- Tracking events are idempotently ingested and retained.
- Only trusted carrier delivery or buyer confirmation may satisfy the physical
  release condition.
- Any open dispute blocks payout.
- Every domain transition passes through the transition service and creates
  immutable audit and appropriate analytics events.
- Production SHIPPOP service flags remain disabled until the provider
  certification gates in this document pass.

## 3. Non-goals

- Do not add RabbitMQ, Kafka, or another message broker.
- Do not create a separately deployed booking worker for the pre-payment path.
- Do not confirm a SHIPPOP booking before verified payment.
- Do not use `force_confirm=1` during checkout.
- Do not automatically replay a timed-out booking when the original result
  might have succeeded.
- Do not invent SHIPPOP idempotency, expiration, insurance, or rate-limit
  guarantees that are absent from its documentation.
- Do not make tracking, cancellation, return, inspection deadlines, or payout
  depend on the buyer keeping the app open.
- Do not enable marketplace, multi-item, split-shipment, or multi-currency
  behavior.

## 4. Decision and alternatives

### 4.1 Chosen: direct booking plus durable post-payment jobs

The checkout API performs the unconfirmed booking synchronously. It returns a
PaymentSheet only after the response is validated and committed. Provider work
that must survive after payment uses PostgreSQL-backed durable jobs.

This gives the buyer a single immediate payment action, avoids broker
infrastructure, and preserves reliable post-payment processing.

### 4.2 Rejected: queue every booking before payment

Queuing `BookOutbound` through a worker is crash-resistant but makes an
ordinary checkout wait for polling and queue scheduling. It also creates the
stuck “กำลังเตรียมรายการจัดส่ง” experience that this decision replaces.

### 4.3 Rejected: book and confirm before payment

Using `force_confirm=1` could reduce one post-payment call, but it confirms a
shipment before TOKLONG has provider-confirmed payment. This creates cleanup,
cost, protection, and fulfillment risks and is not allowed.

## 5. Architecture

```text
Mobile app
  → Checkout API instance
      → PostgreSQL BookingAttempt and transaction snapshot
      → SHIPPOP /booking/ with force_confirm=0
      → PostgreSQL managed shipment and booking result
      → Stripe PaymentIntent

Stripe signed webhook
  → payment transition plus durable ConfirmOutbound job
      → PostgreSQL job runner in existing API/Worker deployment
          → SHIPPOP /confirm/
          → domain transition, audit, and analytics
```

API instances are stateless and may scale horizontally. PostgreSQL owns
coordination, uniqueness, leases, and durable state. The job runner may execute
in the existing API deployment or the existing Worker deployment; it is a
logical background component, not a new service requirement.

The direct SHIPPOP call must use asynchronous HTTP and must not hold a database
transaction or row lock while waiting on the provider.

## 6. BookingAttempt model

A `BookingAttempt` records the pre-payment network operation:

```text
id
transaction_id
buyer_id
idempotency_key
request_fingerprint
provider_reference
status
attempt_number
started_at
completed_at
provider_purchase_id
provider_tracking_code
provider_courier_tracking_code
quoted_shipping_fee_satang
quoted_protection_fee_satang
quoted_coverage_limit_satang
currency
provider_response_fingerprint
failure_category
safe_failure_code
created_at
updated_at
```

Allowed statuses:

```text
Created → CallingProvider → Succeeded
                          → Failed
                          → TimedOut
```

Rules:

- `(transaction_id, idempotency_key)` is unique.
- A request fingerprint covers the immutable agreement, address snapshots,
  parcel facts, service code, shipping charge, optional-protection election,
  all provider price inputs, and terms version.
- `provider_reference` is a TOKLONG-generated opaque reference supplied through
  SHIPPOP metadata such as `meta.ref_no_1`.
- Provider identifiers are unique where SHIPPOP semantics make them unique.
- Sensitive address and contact fields stay in the existing protected private
  snapshot. They are not duplicated into ordinary logs.
- Provider request and response bodies are redacted. Retained fingerprints and
  selected normalized fields are sufficient for audit and reconciliation.
- A successful attempt is usable only while its fingerprint still matches the
  current unpaid transaction and its quote remains valid.

## 7. Checkout sequence

The payment endpoint performs these steps:

1. Authenticate the buyer and authorize the transaction.
2. Load the accepted agreement and current private shipping snapshot.
3. Reject expired, paid, cancelled, disputed, or otherwise ineligible states.
4. Validate the final service, parcel data, fees, optional-protection election,
   exact buyer total, and terms version on the server.
5. Derive the request fingerprint and idempotency key.
6. Reuse a matching `Succeeded` booking or atomically create a
   `BookingAttempt` in `Created`.
7. Commit before making a provider call.
8. Atomically claim the attempt as `CallingProvider`.
9. Call SHIPPOP `POST /booking/` with `force_confirm=0`, the opaque TOKLONG
   reference, and a strict time budget.
10. Validate the response status, purchase ID, service, tracking references,
    price, protection tuple, currency, and transaction correlation.
11. In a short database transaction, store the successful result and update the
    managed shipment without changing the paid snapshot.
12. Create or reuse the Stripe PaymentIntent for the exact validated total.
13. Return the PaymentSheet data.

No Stripe PaymentIntent is created if booking validation fails, times out, or
returns an ambiguous result.

Concurrent requests with the same idempotency key do not both call SHIPPOP.
The first caller owns the attempt. Other callers receive a short
`preparing_shipping` response and poll the same attempt, or receive the already
stored result.

The endpoint has a total budget of three seconds. The initial recommended
SHIPPOP budget is 2.2 seconds, with the remaining time reserved for database,
Stripe, serialization, and network overhead. Production values must come from
load and provider certification rather than hard-coded assumptions.

## 8. Timeout and retry behavior before payment

A SHIPPOP timeout is ambiguous: SHIPPOP may have created the unconfirmed
booking even though TOKLONG did not receive its response.

Therefore:

- mark the attempt `TimedOut`;
- do not open Stripe;
- do not automatically replay it;
- tell the buyer that delivery preparation could not be completed and provide
  an explicit retry action;
- create a new attempt number and provider reference for an authorized retry;
- limit retries to three within a short checkout window; and
- correlate any later provider evidence with the exact attempt reference.

Only the latest matching `Succeeded` attempt may be attached to a new
PaymentIntent. An older or uncertain attempt must never be confirmed merely
because it shares a transaction ID.

This may leave an unconfirmed provider booking after an ambiguous timeout. That
is accepted for the sandbox design only if SHIPPOP confirms that unconfirmed
bookings expire safely and do not create a charge, active protection, or
fulfillment obligation. Without that confirmation, production direct booking
remains disabled.

## 9. Post-payment durable jobs

The verified Stripe webhook commits the payment transition and one unique
`ConfirmOutbound` job atomically. Replayed webhooks reuse the same transition
and job.

The job runner:

- continuously drains available jobs rather than processing one job and then
  sleeping for a fixed interval;
- claims jobs in bounded batches with `FOR UPDATE SKIP LOCKED`;
- uses processing leases so multiple instances can run safely;
- limits concurrency independently for confirm, cancel, return, tracking, and
  deadline work;
- honors provider throttling and `Retry-After`;
- retries only when safety is proven;
- reconciles an uncertain outcome before replaying a provider mutation; and
- writes the provider result, domain transition, audit event, and analytics
  event atomically.

`ConfirmOutbound` has higher priority than tracking refresh. A large tracking
backlog cannot starve newly paid confirmations.

Until confirmation is proven:

- the seller sees plain-language preparation status;
- no label or fulfillment action is exposed;
- shipment handoff cannot be asserted; and
- payout remains blocked.

Cancellation, return processing, tracking reconciliation, inspection deadlines,
payment reconciliation, and payout remain durable because the buyer cannot be
asked to retry them manually.

## 10. Capacity and backpressure

The design target is a burst of 1,000 checkout requests with API p95 at or
below three seconds.

If all 1,000 calls arrive in three seconds, SHIPPOP must support approximately
334 booking requests per second, or provide a certified batch/capacity
equivalent. The service cannot truthfully promise the latency target until this
is confirmed.

TOKLONG applies:

- horizontal API scaling;
- bounded SHIPPOP connection pools;
- asynchronous I/O;
- a per-instance and shared effective concurrency ceiling;
- a bulkhead so shipping cannot exhaust all API resources;
- a circuit breaker for sustained provider failure;
- fail-fast behavior when local capacity is exhausted;
- immediate bounded handling of provider `429`; and
- PostgreSQL pool limits tested against the total number of replicas.

The system prefers a clear retry response over accepting work it cannot finish
inside the budget. It does not create an unbounded in-memory queue.

Scaling calculations must include all replicas:

```text
total database connections
  = API replicas × API pool
  + job-runner replicas × runner pool
  + operational reserve
```

The total must remain under the database connection limit. The same aggregate
calculation applies to SHIPPOP concurrency and Stripe requests.

## 11. Failure matrix

| Failure | Consumer result | Server action |
| --- | --- | --- |
| Invalid address, parcel, service, or unsupported item | Explain the field to fix; no payment | Mark safe validation failure |
| Provider price/protection differs from the accepted values | Ask buyer to review the updated total | Persist mismatch evidence; do not create PaymentIntent |
| SHIPPOP `429` or local bulkhead full | Ask buyer to retry shortly | Fail fast; honor certified retry guidance |
| SHIPPOP timeout or ambiguous `5xx` before payment | Show retry action; no payment | Mark `TimedOut`; no automatic booking replay |
| Definite booking rejection | Explain that delivery could not be prepared | Mark `Failed` with safe code |
| Process crashes after provider success but before persistence | Do not open Stripe | Reconcile by provider reference or require review |
| Buyer closes or Stripe payment fails | No fulfillment | Leave booking unconfirmed; cleanup durably |
| Verified Stripe webhook repeats | No duplicate effect | Reuse payment transition and unique confirm job |
| Confirm call definitely fails | Show preparation delay | Retry only if provider semantics prove it safe |
| Confirm result is unknown | Show preparation delay | Reconcile; do not blindly replay |
| Stored booking does not match paid snapshot | No fulfillment | Block and open authorized review |

Consumer errors do not expose provider brands, raw codes, internal job names,
webhooks, or stack traces.

## 12. Consumer states and copy

While the synchronous request is active:

```text
กำลังเตรียมการจัดส่ง…
```

If it times out or reaches capacity:

```text
เตรียมการจัดส่งไม่สำเร็จ
ยังไม่มีการชำระเงิน กรุณาลองอีกครั้ง

[ ลองอีกครั้ง ]
```

After verified payment but before confirmation:

```text
ได้รับชำระเงินแล้ว
กำลังเตรียมข้อมูลจัดส่งให้ผู้ขาย
```

The payment button becomes busy and cannot trigger another call while the same
request is in flight. Closing a protection dialog does not permanently disable
payment. Returning to checkout reloads the server-owned attempt and payment
state.

## 13. Security, observability, and audit

- SHIPPOP credentials are server-only secrets and must be rotated if disclosed.
- No credential, personal address, phone number, raw provider body, or Stripe
  secret is written to Git or normal logs.
- Stripe webhook signatures and replay protection remain mandatory.
- SHIPPOP responses are treated as untrusted input and bounded before parsing.
- All outbound calls use the environment's certified transport configuration;
  production requires HTTPS.
- Metrics include booking latency, result category, timeout rate, price
  mismatch, bulkhead rejection, provider `429`, job age, confirm latency,
  unknown outcomes, and queue depth by operation.
- Traces correlate transaction ID, booking-attempt ID, provider reference,
  payment ID, shipping-operation ID, and deployment instance without recording
  personal data.
- Alerts cover p95/p99 latency, sustained timeout/error rate, oldest paid
  confirmation job, uncertain mutations, and database/HTTP pool saturation.

## 14. Acceptance and load tests

Implementation is not complete until the following pass:

1. A successful booking is stored before a matching PaymentIntent is returned.
2. Invalid or mismatched booking data never opens Stripe.
3. Two concurrent identical taps create one provider call and one successful
   `BookingAttempt`.
4. A provider timeout never triggers automatic replay and never opens Stripe.
5. A crash after provider success but before database persistence never opens
   Stripe or confirms an unproven booking.
6. Repeated verified Stripe webhooks create one payment transition and one
   `ConfirmOutbound` job.
7. A confirm failure or unknown result blocks seller fulfillment and payout.
8. Multiple job-runner instances cannot execute the same live lease.
9. Tracking backlog does not delay paid confirmation beyond its service target.
10. Client callbacks cannot mark payment, confirmation, delivery, or payout
    successful.
11. A 1,000-request burst respects the error budget, connection ceilings, and
    p95 three-second target in a certified test environment.
12. Provider `429`, latency, and outage tests shed load without exhausting API
    or database pools.
13. State-transition, authorization, audit, webhook replay, tracking
    idempotency, dispute-blocks-payout, and accessibility tests continue to
    pass.

The load-test report must separate TOKLONG processing latency from SHIPPOP and
Stripe latency. A mocked provider test is necessary for capacity regression but
does not certify real SHIPPOP capacity.

## 15. Migration from the current flow

The current implementation queues `BookOutbound` before payment and waits for
the worker. Migration is feature-flagged per environment and service code:

1. Add `BookingAttempt` persistence and direct booking behind a disabled flag.
2. Preserve current `BookOutbound` processing as the rollback path during
   sandbox validation.
3. Move checkout booking to the direct path only for a certified Dev service.
4. Keep `ConfirmOutbound`, cancellation, return, tracking, and deadlines on the
   durable runner.
5. Verify cleanup and reconciliation of old pending `BookOutbound` rows.
6. Load-test with mocked latency/failures, then SHIPPOP Dev within approved
   limits.
7. Update canonical product, flow, payment/shipping, backend-record, acceptance,
   and open-decision documents.
8. Enable Production only after every gate below has evidence.

This document supersedes the pre-payment `BookOutbound` worker decision in
`2026-07-29-shippop-production-shipping-design.md` only after the feature is
implemented, verified, and the canonical documents are updated. Until then,
the existing code and canonical documentation remain authoritative.

## 16. Provider certification gates

SHIPPOP must provide written documentation or reproducible account-specific
evidence for:

- booking lookup or reconciliation using the TOKLONG provider reference;
- whether booking creation is idempotent, and under which key;
- the lifetime and automatic-expiry behavior of an unconfirmed booking;
- whether an unconfirmed booking creates a charge, protection activation, or
  other obligation;
- safe handling of duplicate unconfirmed bookings;
- whether repeated `/confirm/` and `/cancel/` calls are idempotent;
- how an unconfirmed booking without courier tracking is cancelled;
- booking, confirm, label, cancel, and tracking rate limits;
- `Retry-After` behavior and allowed client concurrency;
- expected and maximum latency in the Dev and Production environments;
- whether a bulk booking API exists and preserves per-transaction correlation;
- exact price, protection, service, and tracking semantics for each enabled
  service; and
- the trusted delivery timestamp semantics used for the 72-hour inspection
  window.

If safe reconciliation after an uncertain booking cannot be demonstrated, the
direct booking flag cannot be enabled in Production.

## 17. References

- SHIPPOP Postman API documentation:
  <https://documenter.getpostman.com/view/10021496/Tzz8qwkE>
- SHIPPOP developer information:
  <https://www.shippop.com/for-developers>
- `docs/00_PRODUCT_BRIEF.md`
- `docs/01_USER_FLOWS_AND_STATE_MACHINE.md`
- `docs/02_UI_UX_AND_CONTENT_SPEC.md`
- `docs/03_BACKEND_TRANSACTION_RECORD.md`
- `docs/04_PAYMENT_SHIPPING_AND_DISPUTE_RULES.md`
- `docs/05_ACCEPTANCE_TESTS.md`
- `docs/06_OPEN_DECISIONS.md`
- `docs/07_REGULATORY_SOURCE_NOTES.md`
- `docs/08_SHIPPOP_PRODUCTION_FLOW.md`
- `docs/superpowers/specs/2026-07-29-shippop-production-shipping-design.md`
- `docs/superpowers/specs/2026-07-30-optional-parcel-protection-checkout-design.md`
