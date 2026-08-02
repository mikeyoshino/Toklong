# Confirm-and-Prepare Sale Flow Design

Date: 2026-08-02

## Objective

Shorten the buyer-initiated private-deal flow for physical items and supported
game accounts by removing the separate consumer-facing `ยอมรับข้อเสนอ` step.
The seller instead reviews the offer and prepares the applicable fulfillment
path on one `เตรียมขาย` surface, then uses one `ยืนยันพร้อมขาย` action. The
buyer reviews the final payable terms and uses one
`ยืนยันและชำระ ฿[ยอดทั้งหมด]` action.

This is a presentation and orchestration simplification. It does not remove
seller or buyer consent evidence, bypass payment, or unlock fulfillment before
provider-confirmed payment.

## Non-goals

- Do not remove agreement-acceptance records or agreement hashes.
- Do not add a new transaction state or database migration.
- Do not allow the seller to edit buyer-authored item facts or price.
- Do not allow physical shipment or digital handoff before verified payment.
- Do not change the one-hour buyer payment deadline.
- Do not add marketplace discovery, negotiation, counteroffers, chat, wallets,
  stored value, or unsupported digital goods.
- Do not store usernames, passwords, recovery codes, OTPs, private keys, or
  other reusable credentials.

## Consumer flow

```text
Buyer creates offer
  → Seller reviews and prepares the sale on one surface
  → Seller taps “ยืนยันพร้อมขาย”
  → Buyer reviews the exact payable terms
  → Buyer taps “ยืนยันและชำระ ฿[ยอดทั้งหมด]”
  → Payment provider confirms payment
  → Physical shipment or game-account handoff becomes available
```

### Buyer creates the offer

The buyer selects `สินค้าที่จัดส่ง` or `ไอดีเกม`, enters the existing required
offer details, and uses `ส่งข้อเสนอให้ผู้ขาย`. No payment object, snapshot,
refund, payout, or fulfillment action is created at this point.

The buyer sees:

> ส่งข้อเสนอแล้ว
>
> รอผู้ขายตรวจสอบและเตรียมขาย

### Seller prepares a physical sale

The seller opens one `เตรียมขาย` surface containing:

- the read-only buyer-authored item details, condition, defects, photo, and
  price;
- possession, right-to-transfer, prohibited-goods, payout-account, and terms
  confirmations;
- the buyer destination region allowed before payment;
- seller origin, parcel weight, width, length, and height;
- fresh supported shipping quotes and one selected service;
- buyer-paid shipping charge and expected seller net, without exposing
  buyer-only protection values.

The seller cannot change buyer-authored facts. Incorrect details lead to
`ปฏิเสธรายการ` with copy instructing the seller to ask the buyer to create a
new offer.

The single primary action is `ยืนยันพร้อมขาย`. It confirms the seller's
agreement, locks the current valid shipping selection, records acceptance
evidence, and opens the buyer payment window. It does not confirm payment or
permit shipment.

### Seller prepares a digital sale

The same `เตรียมขาย` structure shows the read-only game-account details, price,
agreed external handoff method, possession/control and right-to-transfer
attestations, payout account, and seller terms. It contains no address,
carrier, parcel, or tracking fields.

The page warns the seller not to enter or upload usernames, passwords, OTPs,
recovery codes, private keys, QR login data, or other reusable secrets. The
single primary action is also `ยืนยันพร้อมขาย`. It records seller consent and
readiness, but does not assert that delivery has occurred and does not unlock
payout.

### Buyer reviews and pays

After the seller is ready, the buyer receives:

> ผู้ขายพร้อมขายแล้ว
>
> ตรวจยอดและชำระภายใน [exact date and time]

The buyer payment surface has no separate `ยอมรับข้อเสนอ` button and no
standalone agreement checkbox. It still displays every material term before
payment: item details, represented condition and defects, supplied photo,
item price, shipping charge where applicable, Buyer Protection fee, optional
parcel-protection outcome where applicable, total, fulfillment deadline,
payout trigger, dispute rule, and terms version.

The primary action is `ยืนยันและชำระ ฿[ยอดทั้งหมด]`. Activating it records the
buyer's acceptance against the unchanged agreement-core hash and current terms,
then begins the existing protected payment-preparation flow. A client redirect,
PaymentSheet completion, slip, screenshot, or database assumption never marks
payment successful.

### Fulfillment after payment

Only a verified payment-provider webhook or authorized reconciliation may move
the transaction into the applicable paid state.

- Physical: the seller sees `เปิดใบปะหน้าและส่งพัสดุ` only after confirmed
  payment and the matching managed booking is confirmed.
- Digital: the seller sees `ส่งมอบไอดีเกม` only after confirmed payment.
  Seller-reported handoff does not release payout automatically; explicit buyer
  confirmation or authorized manual review remains required.

## State mapping

The existing domain states and transition service remain authoritative:

```text
AWAITING_SELLER_ACCEPTANCE
  -- seller taps “ยืนยันพร้อมขาย” -->
SELLER_ACCEPTED_AWAITING_PAYMENT
  -- buyer taps “ยืนยันและชำระ” -->
CHECKOUT_STARTED / PAYMENT_PENDING
  -- provider confirms payment -->
PAID_AWAITING_SHIPMENT
  or PAID_AWAITING_DIGITAL_DELIVERY
```

The UI wording changes; the underlying acceptance transition, role checks,
allow-list, audit event, and immutable agreement evidence remain. This avoids a
new state, migration, and risky semantic change while removing the separate
acceptance experience.

## UI surfaces and copy

### Seller offer surface

- Page title: `เตรียมขาย`.
- Physical section title: `เตรียมการจัดส่ง`.
- Digital section title: `เตรียมส่งมอบไอดีเกม`.
- Primary action: `ยืนยันพร้อมขาย`.
- Secondary action: `ปฏิเสธรายการ`.
- Pre-payment warning: `ยังไม่ต้องส่งสินค้า จนกว่าระบบจะแจ้งว่ายืนยันยอดชำระแล้ว`.

The primary action remains disabled until all applicable confirmations and,
for physical goods, one fresh valid shipping quote are present.

### Buyer transaction surface

- Awaiting seller: `รอผู้ขายตรวจสอบและเตรียมขาย`.
- Ready for payment: `ผู้ขายพร้อมขายแล้ว · ชำระภายใน [exact date and time]`.
- Payment action: `ยืนยันและชำระ ฿[ยอดทั้งหมด]`.

No consumer-facing label or action in this flow uses `ยอมรับข้อเสนอ`.

## Failure handling

### Incorrect offer details

The seller declines. Material item facts, price, condition, defects, and photo
are never edited in place. The buyer creates a new offer.

### Physical preparation failures

The seller cannot confirm readiness if the origin is incomplete, parcel
measurements are incomplete, no supported quote is selected, the quote is
expired, or the shipping-provider result is unknown or requires review.
Entered values remain available. A stale quote is cleared and must be fetched
again. Unknown provider outcomes are reconciled before retry; retries cannot
create duplicate bookings.

### Repeated actions and interrupted requests

`ยืนยันพร้อมขาย` and payment preparation remain idempotent. Repeated taps,
timeouts, app restarts, and network interruptions re-read server state and do
not create duplicate acceptances, bookings, PaymentIntents, audit events, or
notifications.

### Payment expiry

Seller readiness starts the existing exact one-hour payment window. If no
provider-confirmed payment exists by the deadline, the offer expires, the
seller must not fulfill, and any unconfirmed physical booking is cancelled
through the existing safe cleanup path. A late authoritative payment follows
the existing refund-safe rules and never exposes fulfillment incorrectly.

## Notifications and presentation

- Offer created: seller receives `ได้รับข้อเสนอซื้อ` and opens `เตรียมขาย`.
- Seller ready: buyer receives `ผู้ขายพร้อมขายแล้ว` with the exact payment
  deadline.
- Payment confirmed: seller receives type-specific fulfillment guidance.

Transaction lists use action-oriented copy matching those same states. They do
not claim payment or readiness based on a client request.

## Analytics and audit

UI analytics distinguish physical and digital readiness actions, readiness
validation failures, seller decline, buyer payment start, and payment-provider
outcome. Analytics contain no credentials, raw payment data, full addresses,
or other sensitive values.

The existing immutable seller and buyer agreement-acceptance audit evidence
remains tied to the authenticated actor, shared agreement-core hash, terms
hash/version, and server timestamp. The seller readiness wording does not
weaken authorization or evidence requirements.

## Test requirements

### UI and accessibility

- The new seller flow contains no consumer-facing `ยอมรับข้อเสนอ` action.
- Physical and digital surfaces use `ยืนยันพร้อมขาย` and the applicable
  preparation content.
- The buyer payment surface has no separate acceptance button or agreement
  checkbox and uses `ยืนยันและชำระ ฿[ยอดทั้งหมด]`.
- Exact deadlines and material terms remain visible.
- Primary actions meet touch-target, focus order, screen-reader, contrast, and
  dynamic-content accessibility requirements.

### Domain, authorization, and evidence

- Only the intended authenticated seller may confirm readiness.
- Only the authenticated buyer may begin payment.
- Seller and buyer acceptance evidence points to the same unchanged agreement
  core and valid terms version.
- Every transition uses the domain transition service and writes one immutable
  audit event.
- Repeated commands are idempotent and replay-safe.

### Physical

- Readiness is blocked without complete seller origin, parcel measurements,
  and one fresh authoritative supported quote.
- Expired, stale, mismatched, unknown, or review-needed shipping outcomes block
  readiness and payment safely.
- Shipment, label access, and complete destination address remain unavailable
  before provider-confirmed payment.

### Digital

- The ordinary offer and readiness flows contain no reusable credential fields
  or logs.
- Digital handoff remains unavailable before provider-confirmed payment.
- Seller assertion and elapsed time never release payout automatically.

### Payment and payout safety

- Buyer payment is unavailable before seller readiness.
- Payment webhook signature, idempotency, replay, amount, currency, and
  reconciliation tests continue to pass.
- No fulfillment begins from a client redirect or PaymentSheet result.
- Any open dispute blocks payout in every relevant path.

## Assumptions

- `ยืนยันพร้อมขาย` replaces only the separate consumer-facing seller-acceptance
  experience; it still invokes the existing authorized acceptance transition.
- `ยืนยันและชำระ` combines buyer acceptance and payment start in one explicit
  action after all material terms are visible.
- Physical shipping price must remain known before buyer payment, so parcel
  preparation stays on the seller readiness surface.
- The current payment, shipping, dispute, and payout providers and constraints
  remain unchanged.

## Smallest implementation slice

Update seller and buyer mobile presentation around the existing states and
commands without changing persistence or domain states: rename the seller
surface and action, remove the buyer's standalone checkbox/acceptance surface,
make the payment action record consent directly, update notifications and list
copy, and add UI/state/idempotency tests. Provider integration behavior remains
unchanged.
