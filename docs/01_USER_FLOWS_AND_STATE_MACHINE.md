# 01 — User Flows and State Machine

## MVP entry model

TOKLONG MVP is buyer-first only. The buyer and seller already found each other and negotiated in an external chat. TOKLONG does not provide discovery, bidding, negotiation, or in-app chat.

The buyer creates one complete private offer for an intended seller's Thai
mobile number. The system also creates an unguessable seller link for routing
and sharing, but link possession never grants seller authority. Only an
authenticated account whose normalized phone matches the intended seller may
read, accept, or decline the offer. The seller cannot edit any offer field. The
buyer can pay only after that seller accepts and the buyer reviews the same
unchanged terms.

PromptPay is never collected before seller acceptance. The selected Stripe PromptPay flow has no manual capture, and refunding an offer that was never accepted creates unnecessary buyer action.

## Four user-facing stages

1. **ตกลงในแชต**
2. **สร้างและยืนยันดีล**
3. **ชำระและส่งสินค้า**
4. **ตรวจรับและรับเงิน**

The backend uses more detailed states for reliable payment, fulfillment, disputes, refunds, and payout.

## Account-name maintenance

Registration collects `ชื่อ` and `นามสกุล` separately after the phone proof.
From the blue `บัญชี` profile card, the authenticated user can always select
`แก้ไข`; the page does not show cooldown timing proactively. The server checks
eligibility on that action. A blocked action shows an exact Bangkok date and
time in a modal.

The first successful account-name change may occur at any time after
registration. Later successful changes are allowed at the exact instant two
Bangkok calendar months after the previous completion, preserving its local
wall-clock time. A challenge does not start this cooldown. The user enters the
two name fields, then proves control of the current verified phone with the
shared six-digit-code UI. Sends are separated by at least 60 seconds, limited
to five provider-accepted sends per normalized phone in a rolling 24 hours,
expire after 10 minutes, and lock after five incorrect submissions.

Successful completion atomically updates buyer and seller roles plus every
active mobile session for that phone and appends a protected account audit
event. This lifecycle is separate from `TransactionState`. Buyer name freezes
when the offer is sent; seller name freezes when the seller accepts. Later
profile changes never rewrite those stored party names, agreement data, hashes,
labels, evidence, or transaction history. Phone proof does not claim that the
new name is legally or KYC verified.

## Buyer journey

### B1 — Create a private offer

Without a valid mobile session, the app first shows a welcome screen with
separate `เข้าสู่ระบบ` and `สมัครสมาชิก` actions. A new buyer registers with
first and last name, payment-contact email, phone number, and the six-digit
verification step.
Returning buyers sign in with only their registered phone number and
verification code; sign-in must not ask for or overwrite their name. The
verification UI shows six underlined digit positions backed by one focusable
numeric input so paste, deletion, accessibility, and iOS one-time-code AutoFill
remain reliable. Consumer copy says `เข้าสู่ระบบด้วยเบอร์โทรศัพท์` and
`รหัสยืนยัน 6 หลัก`; OTP remains an internal technical term.

The buyer records:

- One physical item/bundle or one allow-listed transferable digital item/right.
- A short required product name.
- An optional managed product photo, plus explicit condition, defects, included
  items, and all details already agreed in chat. If supplied, the photo becomes
  part of the same immutable agreement snapshot; AI source screenshots never
  become product evidence automatically.
- The intended seller's valid 10-digit Thai mobile number. It must differ from
  the buyer's verified phone.
- One agreed item price. For a physical item, the seller's selected shipping
  charge is added later. Any optional parcel-protection charge is a buyer-only
  post-acceptance choice, not a seller-authored delivery term.
- For a physical item, one complete Thai delivery address selected from the
  server-owned hierarchy or the buyer's single saved address. The transaction
  stores a private address snapshot immediately; only its province and postal
  code are disclosed to the seller before payment.
- A fixed fulfillment deadline of 72 hours after provider-confirmed payment; the buyer does not choose this value.
- Buyer identity and phone come from the authenticated buyer account, not editable offer fields.
- Confirmation that the buyer-specified record is complete and will be reviewed again before payment.

The result is:

```text
BUYER_OFFER_DRAFT
  → AWAITING_SELLER_ACCEPTANCE
```

The buyer receives a private transaction record. The server retains an
unguessable seller-routing token, but normal mobile UX does not ask either
party to copy, paste, or resend it. The durable notification outbox records one
`buyer_offer_received` notification addressed only to the normalized intended
seller phone. No checkout, payment, refund, or payout instruction exists yet.

The seller-response deadline is fixed server-side at 24 hours after activation.
It is stored on the transaction and shown as an exact local date and time;
clients must not derive or extend it independently.

The buyer wait page refreshes state while open. It must also remain usable after
closing and reopening. The intended seller sees the notification in the app
when authenticated, and the same pending offer appears as an action-required
sale in the seller's transaction list by matching the verified phone before a
seller ID is bound. An OS push is attempted only for a registered device and
configured notification provider; the UI must not falsely claim external
delivery merely because an outbox record exists.

### B2 — Wait for seller

The buyer sees:

- `รอผู้ขายยืนยัน`
- Confirmation that the offer was routed to the account verified with the
  seller phone entered at creation.
- An optional invitation card with the same URL, `คัดลอกลิงก์`, and
  `แชร์ให้ผู้ขาย`. Sharing is a delivery convenience, not authorization:
  opening or accepting still requires the exact verified seller phone.
- Clear copy that no payment has been collected.
- A final-review action only after seller acceptance.

If the seller declines:

```text
AWAITING_SELLER_ACCEPTANCE → CANCELLED
```

If the seller does not respond by the exact deadline:

```text
AWAITING_SELLER_ACCEPTANCE → EXPIRED
expiration_reason = SELLER_DID_NOT_RESPOND
```

No payment exists. The buyer may share the same invitation again while active
or create a new offer after expiry. An expired invitation cannot be accepted or
silently reactivated.

### B3 — Review final terms and pay

After seller acceptance, the buyer sees the same buyer-specified description,
condition, defects, any supplied photos, item price, selected shipping service
and charge, Buyer Protection fee, fulfillment deadline, payout trigger, and
dispute rule. Seller acceptance must not mutate the buyer-authored item fields
and freezes delivery facts only.

For a physical offer, checkout first obtains the buyer-only parcel-protection
availability. If the item is within a verified included limit, it skips the
prompt, auto-submits `AddProtection=false`, and persists `Declined` without a
charge; the verified included coverage may still be shown in applicable status
or details. If an available add-on is needed, it asks the buyer once to accept
or explicitly decline; only that card shows the disclosed maximum and one
combined price. The buyer may change an election before a PaymentIntent exists.
A change after an unconfirmed booking is reserved must durably cancel that
attempt before creating a replacement; unknown or review-needed provider
outcomes block a change and payment. An unavailable add-on records no charge
and permits continuation only with the verified included-coverage outcome,
which may be zero; it never claims coverage beyond that amount.

Seller acceptance creates a fixed one-hour payment deadline. The seller sees
that the item is reserved only until that exact time and must not fulfill before
provider-confirmed payment.

At seller acceptance, the backend creates the normalized agreement-core and
terms snapshots, computes their SHA-256 hashes, and appends a seller-acceptance
record tied to the authenticated phone account. The buyer's final-review action
must validate that core unchanged and append a buyer-acceptance record pointing
to the exact same agreement-core hash. Neither acceptance stores an OTP code.
For physical goods, the seller must first select a valid shipping quote using
the destination region, a complete seller origin, and parcel weight and
dimensions. Until account-specific certification supplies different evidence,
weight and every dimension remain required. The shared core includes the
destination province/postal code, seller origin province/postal code, parcel
measurements, selected service, shipping charge, and Buyer Protection fee.
Full street addresses remain private fulfillment data. The buyer-only
protection annex and final total are created after this acceptance, never in
the seller's accepted core.

```text
SELLER_ACCEPTED_AWAITING_PAYMENT
  → choice exists: buyer explicitly accepts or declines
  → within verified included limit: auto-submit AddProtection=false → Declined
  → over limit with no certified add-on → Unavailable
  → buyer taps pay → synchronous unconfirmed booking is validated and committed
  → CHECKOUT_STARTED → PAYMENT_PENDING
```

For physical goods, checkout shows the complete delivery address already locked
when the buyer created the offer. It has no address editor. If the address is
missing or needs correction, the buyer must create a new offer; the accepted
offer is never silently changed. The buyer accepts the final transaction terms
and uses provider-hosted or approved checkout. A redirect is informational
only. Payment success requires a verified provider webhook or authorized
reconciliation.

If provider-confirmed payment has not occurred by the deadline, the unpaid
offer becomes `EXPIRED` with reason `BUYER_DID_NOT_PAY`. A provider event whose
authoritative confirmation time is at or before the deadline remains valid even
if its webhook arrives later. Payment confirmed after the deadline must not
expose fulfillment and enters `REFUND_PENDING`.

For a physical offer, expiry also queues cancellation of its unconfirmed
provider booking. The consumer offer closes at the exact deadline even while
provider cleanup retries in the background. A cleanup failure cannot reactivate
or extend the offer.

### B4 — Track, inspect, and decide

Physical:

- View supported carrier and tracking.
- Trusted carrier-confirmed delivery starts the exact 72-hour inspection and payout-hold window.
- After inspecting the item, confirm that everything is satisfactory to release early, or report a problem.

Digital:

- Review the seller's non-secret handoff statement.
- Confirm only after receiving and checking the transferable item/right.
- Report a problem instead of confirming when incorrect.
- Elapsed time and seller assertion never auto-release payout.

## Seller journey

### S1 — Open and authenticate

The seller may tap the notification or open the pending offer in the app's
`ขาย` mode. The seller signs in with a phone number and six-digit verification
code. Before any offer details are disclosed,
the server requires the normalized authenticated phone to match the intended
seller phone stored by the buyer. A forwarded link opened by another account
must return forbidden without disclosing the offer. The seller sets up a payout
bank account if none exists. The current implementation verifies the phone and
saves the bank account; real identity/KYC and beneficiary-name verification
remain provider capabilities that must not be claimed before integration.

The opaque routing token remains server-controlled and may be used by an owned
notification deep link or the optional buyer-shared URL, but the mobile list is
the normal entry point. The session gains seller rights only after the same
verified phone owns the seller profile, and the same phone account cannot
accept its own buyer offer.

### S2 — Review and respond

The seller sees:

- Proposed item, item price, and destination province/postal code.
- Expected fulfillment rule.
- Payout trigger and dispute rule.
- Plain-language status `เมื่อคุณตกลง ผู้ซื้อจะจ่ายเงินได้`.

For a physical offer, the seller also:

- Uses the single saved shipping origin by default, or enters a new complete
  Thai origin and may select `จำต้นทางนี้ไว้`; saving replaces the previous
  origin rather than creating an address book.
- Enters actual parcel weight in grams and width, length, and height in
  centimeters.
- Requests available shipping quotes, selects one service, and reviews item
  price, shipping charge, zero seller platform fee, and expected seller net.
  The seller does not see buyer-funded Buyer Protection or parcel-protection
  values, coverage limits, election, provider option, or buyer total.
- Requests a new quote after the origin, measurements, or selected quote
  expires. The seller cannot accept using client-supplied or stale pricing.
- Accepting freezes the delivery selection but does not book a shipment. The
  buyer's later protection election drives the durable, matching unconfirmed
  booking. A timeout becomes an unknown provider outcome and is reconciled
  before any retry; it never permits a second booking or a PaymentIntent.

Before acceptance the seller must confirm:

- The buyer-specified item, description, condition, defects, included items,
  functionality, any supplied managed photo, item price, selected physical
  shipping service and charge where applicable, seller expected net, plus the
  system-fixed 72-hour fulfillment rule.
- Possession/control, right-to-transfer, prohibited-goods, and seller-terms attestations.
- Owned payout account.

The seller cannot edit the proposal. If any field is wrong, the seller declines and the buyer creates a new offer. MVP has no counteroffer or in-app negotiation.

The `ยอมรับข้อเสนอ` action is the seller's electronic acceptance of the
displayed agreement core. It creates an append-only acceptance record containing
the authenticated seller ID, verified-phone authentication method, shared
agreement-core hash, terms hash/version, and server timestamp.

```text
AWAITING_SELLER_ACCEPTANCE
  → SELLER_ACCEPTED_AWAITING_PAYMENT
```

Until verified payment, seller copy must never say `ผู้ซื้อชำระแล้ว`.
The buyer has one hour after acceptance to obtain provider-confirmed payment.
Until then seller copy says `รอผู้ซื้อจ่ายถึง [exact time] ยังไม่ต้องส่งสินค้า`.

### S3 — Fulfill only after confirmed payment

```text
PAYMENT_PENDING
  → PAID_AWAITING_SHIPMENT
  or PAID_AWAITING_DIGITAL_DELIVERY
```

- Physical with a certified, enabled managed service: the background worker
  confirms the pre-payment reservation only after provider-confirmed buyer
  payment. The provider supplies the carrier tracking number and printable 4×6
  label; the seller
  opens the label full-screen, may zoom it, and may save, share, or print the
  original provider HTML before handing the parcel to the selected carrier by
  `ship_by_at`. The seller does not type or replace tracking. Consumer copy
  must not claim that every service is drop-off or that every counter scans a
  phone screen; those instructions depend on the selected provider service.
- A carrier scan, not merely allocation of a label/tracking number, satisfies
  the managed-shipment deadline. If no scan occurs by `ship_by_at`, the
  shipment enters cancellation/refund handling.
- The consumer shipping card uses `เตรียมจัดส่ง`,
  `ขนส่งรับพัสดุแล้ว`, `กำลังจัดส่ง`, and `ส่งถึงแล้ว`.
  `ส่งถึงแล้ว` requires both the SHIPPOP completed state and a trusted carrier
  delivery timestamp. A completed value without that timestamp enters
  tracking review and never starts the 72-hour window from poll time.
- Digital: deliver through the agreed external channel and store only a non-secret handoff statement.
- Seller-entered delivery or a slip never authorizes payout.

### S4 — View payout status

- Trusted physical carrier-confirmed delivery starts the 72-hour inspection and payout-hold window.
- Digital has no time-based automatic payout.
- Buyer confirmation may create payout eligibility early.
- Any open dispute blocks payout.
- `PAYOUT_PENDING` is not completed transfer.
- `PAID_OUT` requires authenticated bank/provider completion or authorized reconciliation.

## Happy paths

### Physical

```text
BUYER_OFFER_DRAFT
  → AWAITING_SELLER_ACCEPTANCE
  → SELLER_ACCEPTED_AWAITING_PAYMENT
  → CHECKOUT_STARTED
  → PAYMENT_PENDING
  → PAID_AWAITING_SHIPMENT
  → TRACKING_SUBMITTED
  → IN_TRANSIT
  → DELIVERED_DISPUTE_WINDOW
  → PAYOUT_ELIGIBLE
  → PAYOUT_PENDING
  → PAID_OUT
```

Early confirmation:

```text
DELIVERED_DISPUTE_WINDOW
  → BUYER_CONFIRMED_RECEIPT
  → PAYOUT_ELIGIBLE
  → PAYOUT_PENDING
  → PAID_OUT
```

### Digital

```text
SELLER_ACCEPTED_AWAITING_PAYMENT
  → CHECKOUT_STARTED
  → PAYMENT_PENDING
  → PAID_AWAITING_DIGITAL_DELIVERY
  → DIGITAL_DELIVERY_SUBMITTED
  → one of:
      BUYER_CONFIRMED_RECEIPT → PAYOUT_ELIGIBLE
      DISPUTED → RESOLUTION_PENDING
      authorized manual review → PAYOUT_ELIGIBLE or REFUND_PENDING
```

## Exception paths

Payment failure or expiry:

```text
SELLER_ACCEPTED_AWAITING_PAYMENT
  → CHECKOUT_STARTED
  → PAYMENT_FAILED or PAYMENT_EXPIRED
  → SELLER_ACCEPTED_AWAITING_PAYMENT or EXPIRED
```

Seller-response and buyer-payment expiry:

```text
AWAITING_SELLER_ACCEPTANCE
  → EXPIRED (SELLER_DID_NOT_RESPOND)

SELLER_ACCEPTED_AWAITING_PAYMENT or PAYMENT_PENDING
  → EXPIRED (BUYER_DID_NOT_PAY)
```

Missed physical shipment:

```text
PAID_AWAITING_SHIPMENT
or managed TRACKING_SUBMITTED / TRACKING_UNVERIFIED with no carrier scan
  → at ship_by_at reconcile the provider before assigning responsibility
  → no trusted carrier acceptance scan
      → SHIPMENT_OVERDUE
      → cancel the unused provider shipment when still cancellable
      → REFUND_PENDING
      → REFUNDED
  → trusted acceptance scan occurred at or before ship_by_at
      → carrier-custody exception review
      → payout remains blocked; do not auto-refund as seller non-fulfillment
```

For a provider-managed physical shipment, the first trusted carrier acceptance
scan is the responsibility boundary. A label, tracking allocation, seller
statement, or drop-off photo alone does not cross it. A timely trusted scan
records that the seller handed the parcel to the locked carrier; it does not
prove delivery or make payout eligible. Carrier delay, loss, failed delivery,
return-to-sender, or delivery conflict after that scan remains blocked from
automatic payout and follows the carrier-exception policy.

Unverified tracking:

```text
TRACKING_SUBMITTED
  → TRACKING_UNVERIFIED
  → MANUAL_TRACKING_REVIEW
```

This never starts the dispute clock or automatic payout.

Provider carrier exception:

```text
PROBLEM / INVALID / RETURN / TRACKING MISMATCH /
COMPLETE WITHOUT TRUSTED DELIVERY TIME
  → TRACKING_UNVERIFIED or CARRIER_EXCEPTION_REVIEW
  → payout and automatic refund remain blocked
  → authorized CRM resolution
```

An approved return-required resolution creates a separate provider-managed
return shipment. It has its own booking, tracking, carrier events, and delivery
evidence:

```text
AUTHORIZED_RETURN
  → RETURN_BOOKING_PENDING
  → RETURN_TRACKING_SUBMITTED
  → RETURN_IN_TRANSIT
  → RETURN_DELIVERED
  → REFUND_PENDING
  → REFUNDED
```

The original outbound shipment is immutable. Missing trusted return delivery
blocks automatic refund unless an authorized manual resolution explicitly
permits another outcome.

Dispute:

```text
DELIVERED_DISPUTE_WINDOW or DIGITAL_DELIVERY_SUBMITTED
  → DISPUTED
  → RESOLUTION_PENDING
  → REFUND_PENDING → REFUNDED
  or PAYOUT_ELIGIBLE → PAYOUT_PENDING → PAID_OUT
```

AI may assist with evidence but cannot select the binding outcome.

## State definitions

| State | Meaning | Primary action |
|---|---|---|
| `BUYER_OFFER_DRAFT` | Buyer is preparing one private offer | Create invitation |
| `AWAITING_SELLER_ACCEPTANCE` | Seller has not accepted final terms | Seller authenticates and responds |
| `SELLER_ACCEPTED_AWAITING_PAYMENT` | Seller accepted; payment is not confirmed | Buyer reviews and pays |
| `CHECKOUT_STARTED` | Buyer accepted final terms | Complete provider checkout |
| `PAYMENT_PENDING` | Provider has not confirmed payment | Wait/reconcile |
| `PAID_AWAITING_SHIPMENT` | Confirmed payment; managed shipment is awaiting provider confirmation | System confirms booking, then seller downloads label |
| `PAID_AWAITING_DIGITAL_DELIVERY` | Confirmed payment for digital item/right | Seller completes handoff |
| `DIGITAL_DELIVERY_SUBMITTED` | Seller asserted handoff; payout blocked | Buyer confirms/reports or manual review |
| `TRACKING_SUBMITTED` | Tracking recorded | Wait for carrier |
| `TRACKING_UNVERIFIED` | Tracking not trusted | Correct/manual review |
| `IN_TRANSIT` | Carrier confirms movement | Wait |
| `DELIVERED_DISPUTE_WINDOW` | Trusted delivery starts deadline | Confirm after inspection or report |
| `BUYER_CONFIRMED_RECEIPT` | Buyer confirmed receipt | Evaluate payout |
| `DISPUTED` / `RESOLUTION_PENDING` | Payout is blocked | Human-controlled resolution |
| `PAYOUT_ELIGIBLE` | Release conditions passed | Create payout instruction |
| `PAYOUT_PENDING` | Transfer is processing | Reconcile |
| `PAID_OUT` | Transfer completion confirmed | Closed |
| `SHIPMENT_OVERDUE` | Ship-by missed | Cancellation/refund review |
| `REFUND_PENDING` / `REFUNDED` | Refund processing/completed | Wait/closed |
| `EXPIRED` / `CANCELLED` | Unpaid offer ended | Closed |

`SELLER_DRAFT` and `LINK_ACTIVE` may remain in storage code only for historical records created before the buyer-first decision. No MVP command, route, CTA, or acceptance test may create a new seller-first transaction.

## Transition invariants

- Every transition uses the allow-listed domain transition service, role authorization, and immutable audit event.
- Buyer-offer creation creates no payment/refund/payout object.
- Checkout is rejected before `SELLER_ACCEPTED_AWAITING_PAYMENT`.
- Seller acceptance is rejected at or after
  `seller_acceptance_deadline_at`.
- Checkout creation/retry is rejected at or after
  `buyer_payment_deadline_at`.
- A payment confirmed by the provider after the buyer-payment deadline never
  exposes fulfillment and requires refund handling.
- Offer creation requires an authenticated buyer account with phone verification and first/last name.
- Seller acceptance requires authenticated seller identity, an owned payout account, attestations, and policy approval.
- Seller and buyer electronic acceptances must reference the same valid
  agreement-core hash; actor IDs and server timestamps must match the
  transaction parties and acceptance times.
- Offer details are buyer-authored and read-only for the seller; any correction requires decline and a new offer.
- Seller acceptance creates the immutable agreement core and freezes delivery
  only. The buyer's post-acceptance protection election is buyer-only and must
  be durably booked and revalidated against the selected service before buyer
  checkout can create a PaymentIntent. Booking success never changes the
  seller-acceptance time or buyer-payment deadline.
- Buyer acceptance references the private physical delivery-address annex
  locked at offer creation and the immutable buyer checkout annex; provider-
  confirmed payment seals the paid snapshot and is the only path exposing the
  full address for fulfillment.
- Trusted carrier delivery is the only default source of the physical 72-hour clock; shipped or in-transit status never starts it.
- Digital handoff never becomes payout eligible from seller assertion or time.
- Any dispute/refund/hold blocks payout.
- `PAID_OUT` and `REFUNDED` require verified external completion or authorized reconciliation.
- Closed states cannot be reopened by ordinary users.
