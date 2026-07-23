# 01 — User Flows and State Machine

## User-facing flow

The UI presents four stages:

1. **สร้าง / รับลิงก์ข้อตกลง**
2. **ผู้ซื้อชำระ**
3. **ส่งมอบ**
4. **ยืนยันรับ / รับเงิน**

The backend uses more states to make payment, shipment, disputes, refunds, and payout reliable.

## Initiation modes

TOKLONG supports two private, non-discovery entry paths that converge before checkout:

1. **Seller initiated:** the seller creates and shares the final agreement link; the buyer reviews and pays.
2. **Buyer initiated:** the buyer creates a proposed offer and shares its unguessable link; the seller joins, completes or confirms the material representations, accepts the final terms, and only then may the buyer pay.

The buyer-initiated path is modeled on the standalone transaction-link pattern used by Trustap, while the accept-before-charge and delivery-confirmed inspection pattern is also common to Mercari, Vinted, and Wallapop. It is not bidding: there is one buyer, one seller, one item/bundle, one active set of terms, and no public discovery.

For PromptPay, the MVP must not collect payment merely because the buyer created an offer. PromptPay does not provide manual capture in the selected Stripe flow, and refunding an unaccepted offer requires additional buyer action. Seller acceptance therefore precedes payment.

## Seller journey

### S1 — Onboard once

- Verify mobile number.
- Complete identity and payout onboarding required by the selected payment partner.
- Accept seller terms and prohibited-goods policy.

### S2 — Create agreement link

The seller and buyer have already found each other and negotiated elsewhere. The visible form records the material agreement; it is not a marketplace listing form.

Required visible fields:

- Fulfillment type: physical shipment or supported digital handoff.
- Agreed item or bundle name.
- One combined agreement description that states included items, condition, functionality, known defects, and other material representations.
- At least one agreement photo uploaded or captured directly; recommended minimum four for used goods.
- Price.
- Shipping fee for physical goods; digital goods must use zero shipping.
- Fulfillment duration or exact deadline.
- Confirmation that the seller possesses/controls the item or right, may transfer it, and it is not prohibited.

The normal flow must not require a category dropdown, separate condition dropdown, separate defects field, or a raw photo URL. The system may classify category and condition behind the scenes for policy and snapshot normalization, but it must ask a focused follow-up when the agreement text is insufficient rather than silently inventing a material fact.

The system validates the agreement, stores a draft, then creates a shareable link.

### S2B — Join a buyer-created offer

- Open the unguessable offer link and view the proposed item, amount, shipping, expected net payout, fulfillment deadline, offer-expiry time, and payout trigger.
- Verify mobile number and complete the required identity/bank onboarding.
- Provide or confirm the agreement photos, condition, known defects, possession/control, right to transfer, and prohibited-goods attestation.
- Accept or decline. The MVP does not provide bidding or in-app negotiation.
- If material terms need to change, the seller completes the revised final agreement before payment and the buyer must review that final version at checkout.

Until seller acceptance, consumer copy must say `ผู้ซื้อสร้างข้อเสนอแล้ว` or `ผู้ซื้อพร้อมชำระเมื่อคุณยอมรับ`, never `ผู้ซื้อชำระแล้ว`.

### S3 — Wait for payment

The seller sees one of:

- “รอผู้ซื้อชำระ”
- “การชำระไม่สำเร็จ”
- “ชำระแล้ว · ส่งสินค้าได้” for physical goods.
- “ชำระแล้ว · ส่งมอบข้อมูลได้” for digital goods.

Only the verified provider event changes the transaction to a funded/paid state.

### S4 — Fulfill

- Physical: seller selects a supported carrier, enters tracking, and sees the ship-by countdown.
- Digital: seller completes the handoff through the agreed external channel, then records a non-secret handoff note. Do not store passwords, recovery codes, private keys, or reusable credentials.
- A seller-entered digital handoff never releases payout.

### S5 — View release countdown and payout

- Carrier-confirmed `DELIVERED` starts the dispute window.
- Digital fulfillment has no automatic deadline release; buyer confirmation or authorized manual review is required.
- Seller sees exact release date/time.
- Early buyer confirmation may move the transaction toward payout.
- An open dispute changes the seller view to “พักการจ่ายระหว่างตรวจสอบ.”
- Provider-confirmed transfer changes the transaction to “รับเงินแล้ว.”

## Buyer journey

### B0 — Create an offer link

- Select physical shipment or supported digital handoff.
- Enter the proposed item/bundle, agreed amount, physical shipping fee if applicable, and expected fulfillment time.
- Add the seller-provided description/photos already available from the external conversation; mark them as proposed until the seller confirms them.
- Verify a contact email that Stripe may use for PromptPay refund instructions.
- Create an unguessable private offer link and share it with the seller.
- Wait for the seller to accept or decline. No payment is collected in this state.

### B1 — Open link

No marketplace account is required to browse the transaction page. Before payment, show:

- Agreement photos and frozen description, including the represented condition and known defects.
- Seller identity signals permitted by policy.
- Price, shipping, fees, and total.
- Applicable physical-shipping or digital-handoff deadline.
- Payout trigger.
- Physical seven-day dispute rule, or the digital no-auto-release rule.
- Problem-reporting and prohibited-item information.

For a buyer-created offer, B1 occurs after the seller has accepted the final material terms. The buyer must see any seller additions or corrections before accepting checkout terms.

### B2 — Verify and pay

- Verify phone number and/or email as required.
- Enter delivery address for physical goods only.
- Accept the transaction terms version.
- Pay through provider-hosted or approved checkout.

Browser success redirect is informational only. The app waits for verified provider confirmation.

### B3 — Track shipment

- Receive notification when seller submits verified tracking.
- View carrier and timeline.
- Receive delivery notification when carrier confirms delivery.

For digital agreements:

- Review that the seller marked the handoff complete.
- Confirm only after independently receiving and checking the agreed digital item/right.
- Report a problem instead of confirming when access, ownership, or details are incorrect.

### B4 — Inspect and decide

Buyer actions during the window:

- “ได้รับสินค้าแล้ว” — confirms early release eligibility.
- “แจ้งปัญหา” — creates a dispute and blocks payout.
- No action — payout becomes eligible after the exact deadline if no dispute is open.

For digital agreements there is no countdown-based release. No action leaves payout blocked for manual review.

## Primary happy path

```text
SELLER_DRAFT
  → LINK_ACTIVE
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

## Buyer-initiated happy path

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

The buyer-created offer may instead move from `AWAITING_SELLER_ACCEPTANCE` to `CANCELLED` when declined or `EXPIRED` when its exact acceptance deadline passes.

## Early confirmation path

```text
DELIVERED_DISPUTE_WINDOW
  → BUYER_CONFIRMED_RECEIPT
  → PAYOUT_ELIGIBLE
  → PAYOUT_PENDING
  → PAID_OUT
```

## Digital fulfillment path

```text
PAYMENT_PENDING
  → PAID_AWAITING_DIGITAL_DELIVERY
  → DIGITAL_DELIVERY_SUBMITTED
  → one of:
      BUYER_CONFIRMED_RECEIPT → PAYOUT_ELIGIBLE
      DISPUTED → RESOLUTION_PENDING
      authorized manual review → PAYOUT_ELIGIBLE or REFUND_PENDING
```

Elapsed time and a seller-entered handoff are never sufficient for digital payout eligibility.

## Dispute path

```text
DELIVERED_DISPUTE_WINDOW
  → DISPUTED
  → RESOLUTION_PENDING
  → one of:
      REFUND_PENDING → REFUNDED
      RETURN_REQUIRED → RETURN_IN_TRANSIT → REFUND_PENDING → REFUNDED
      PAYOUT_ELIGIBLE → PAYOUT_PENDING → PAID_OUT
      PARTIAL_RESOLUTION (future; disabled in MVP)
```

## Seller misses shipment deadline

```text
PAID_AWAITING_SHIPMENT
  → SHIPMENT_OVERDUE
  → CANCELLATION_REVIEW or AUTO_CANCEL_ELIGIBLE
  → REFUND_PENDING
  → REFUNDED
```

Automatic cancellation behavior depends on the payment partner and approved policy. The MVP must never silently leave a paid transaction without a next action.

## Unsupported or unverifiable tracking

```text
TRACKING_SUBMITTED
  → TRACKING_UNVERIFIED
  → MANUAL_TRACKING_REVIEW
```

Rules:

- Do not start the seven-day window.
- Do not auto-release payout.
- Allow the seller to correct the carrier or tracking number before deadline where safe.
- Buyer confirmation can still make the transaction eligible for authorized payout, subject to fraud checks.

## Payment failure or expiry

```text
LINK_ACTIVE or SELLER_ACCEPTED_AWAITING_PAYMENT
  → CHECKOUT_STARTED
  → PAYMENT_FAILED or PAYMENT_EXPIRED
  → LINK_ACTIVE or SELLER_ACCEPTED_AWAITING_PAYMENT or EXPIRED
```

The seller must not see a paid state.

## Cancellation before payment

- Seller may deactivate an unpaid link.
- Buyer may deactivate a proposed offer before seller acceptance.
- Seller may decline a buyer-created offer.
- Buyer opening a deactivated link sees a clear unavailable message.
- No refund flow is needed because payment was never confirmed.

## Cancellation after payment

- Not a simple client-side action.
- Requires policy checks, provider refund capability, shipment state checks, and immutable audit events.
- A refund must not be marked complete until provider-confirmed.

## State definitions

| State | Meaning | Primary actor action |
|---|---|---|
| `SELLER_DRAFT` | Seller is preparing product details | Complete and activate link |
| `BUYER_OFFER_DRAFT` | Buyer is preparing a private proposed offer | Complete and invite seller |
| `AWAITING_SELLER_ACCEPTANCE` | Buyer offer exists but seller has not accepted final terms | Seller joins, completes seller facts, accepts or declines |
| `SELLER_ACCEPTED_AWAITING_PAYMENT` | Seller accepted the final terms; no payment is confirmed | Buyer reviews final terms and pays before expiry |
| `LINK_ACTIVE` | Link can be opened and paid | Buyer reviews/pays; seller may deactivate if unpaid |
| `CHECKOUT_STARTED` | Buyer entered checkout | Complete payment |
| `PAYMENT_PENDING` | Provider has not yet confirmed final payment | Wait/reconcile |
| `PAID_AWAITING_SHIPMENT` | Provider-confirmed payment; seller may ship | Add supported tracking |
| `PAID_AWAITING_DIGITAL_DELIVERY` | Provider-confirmed payment; seller may complete digital handoff | Deliver through agreed channel and record handoff |
| `DIGITAL_DELIVERY_SUBMITTED` | Seller recorded digital handoff; payout remains blocked | Buyer confirms or reports a problem; otherwise manual review |
| `TRACKING_SUBMITTED` | Tracking recorded and verification initiated | Wait for carrier events |
| `TRACKING_UNVERIFIED` | Tracking cannot be verified | Correct or manual review |
| `IN_TRANSIT` | Carrier confirms movement | Wait for delivery |
| `DELIVERED_DISPUTE_WINDOW` | Verified delivery; deadline is active | Buyer confirms or reports problem |
| `BUYER_CONFIRMED_RECEIPT` | Buyer confirmed receipt | Evaluate payout eligibility |
| `DISPUTED` | Buyer opened a dispute before deadline | Evidence and resolution flow |
| `PAYOUT_ELIGIBLE` | All release conditions passed | Create payout instruction |
| `PAYOUT_PENDING` | Provider processing transfer | Wait/reconcile |
| `PAID_OUT` | Provider confirms seller transfer | Closed |
| `SHIPMENT_OVERDUE` | Seller missed ship-by deadline | Cancel/refund review |
| `REFUND_PENDING` | Provider processing refund | Wait/reconcile |
| `REFUNDED` | Provider confirms refund | Closed |
| `EXPIRED` | Unpaid link/payment window expired | Seller may create new link |
| `CANCELLED` | Transaction cancelled according to policy | Closed |

## Transition invariants

- `SELLER_ACCEPTED_AWAITING_PAYMENT` requires an authenticated eligible seller, seller acceptance timestamp, possession/right-to-transfer attestations, policy approval, and a complete final agreement record.
- A buyer-created offer cannot enter `CHECKOUT_STARTED` before `SELLER_ACCEPTED_AWAITING_PAYMENT`.
- A material seller revision before payment requires the buyer to review and accept the revised final record; no prior buyer proposal acceptance is treated as acceptance of changed terms.
- `PAID_AWAITING_SHIPMENT` requires provider-confirmed payment.
- `PAID_AWAITING_DIGITAL_DELIVERY` requires provider-confirmed payment and a digital fulfillment type.
- `DIGITAL_DELIVERY_SUBMITTED` requires seller authorization and a non-secret handoff statement; it cannot automatically become payout eligible.
- `DELIVERED_DISPUTE_WINDOW` requires a trusted carrier-delivered event and a recorded `delivered_at`.
- `PAYOUT_ELIGIBLE` requires no open dispute or refund. Physical goods may qualify through buyer confirmation or an expired carrier-verified dispute deadline. Digital goods require buyer confirmation or authorized manual review.
- `PAYOUT_PENDING` requires an idempotent payout instruction.
- `PAID_OUT`, `REFUNDED`, and provider-money failures require verified provider events or authorized reconciliation.
- Closed states cannot be reopened by normal users.
