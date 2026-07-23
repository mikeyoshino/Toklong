# 04 — Payment, Shipping, and Dispute Rules

## Payment rules

1. Use an approved payment partner integration appropriate to the final business model.
2. Payment status is confirmed only by verified provider events or authorized reconciliation.
3. A browser redirect or client callback may display “กำลังตรวจสอบการชำระ” but cannot mark the transaction paid.
4. The seller is instructed to fulfill only after the applicable provider-confirmed paid state is reached.
5. Fees, taxes, buyer total, and seller expected net must be calculated server-side and displayed before payment.
6. Provider references and event IDs must be retained for reconciliation.
7. Payment retries must not create duplicate paid transactions.
8. A buyer-created offer is not payable until the authenticated seller accepts the complete final agreement and passes the required policy/eligibility checks.
9. PromptPay checkout must collect a verified or deliverable buyer email for receipts and provider refund instructions.
10. The current Stripe PromptPay flow has no manual capture. Do not describe an unpaid proposal as reserved, authorized, funded, or paid.

## Product snapshot and acceptance

Before payment, the buyer must be shown and accept the final agreement record created by the seller or completed and accepted by the seller from a buyer-created proposal:

- Agreed item identity and agreement photos.
- The frozen agreement description, including represented condition, included items, functionality, and known defects.
- Price and applicable shipping fee.
- Physical ship-by or digital handoff deadline.
- Supported delivery method.
- Physical seven-day dispute rule or digital no-auto-release rule.
- The exact payout condition for the fulfillment type.
- Prohibited-item and problem-reporting policy.
- Applicable terms version.

After confirmed payment, material changes require a new transaction. Do not silently edit the paid snapshot.

Before payment, a seller may complete or correct a buyer-created proposal. Any material difference must be shown prominently to the buyer at final review. The buyer's act of creating the proposal is not acceptance of later seller changes.

Combining condition and defects into one seller-facing agreement-details field does not remove them from the material record. The text and normalized snapshot must preserve what the seller represented and what the buyer accepted.

## Shipment rules

1. Seller must submit a supported carrier and tracking number by `ship_by_at`.
2. Tracking format validation is necessary but not sufficient; the system should verify the tracking number with the carrier/aggregator.
3. A seller-entered “delivered” status is never authoritative.
4. Carrier events are stored idempotently in original order where possible.
5. The app should display the carrier event timestamp and the app ingestion timestamp separately when relevant.
6. If tracking belongs to another transaction, is reused, or shows suspicious prior delivery, block normal auto-release and route to review.
7. If the seller misses the deadline, notify both parties and enter the approved cancellation/refund process.

## Digital fulfillment rules

1. Digital agreements must be allow-listed and transferable by the seller.
2. The app stores only a non-secret handoff statement and timestamp. Never store passwords, recovery codes, private keys, wallet secrets, or reusable credentials in transaction fields or normal logs.
3. A seller-entered handoff does not prove delivery and cannot release payout.
4. There is no time-based automatic payout for digital agreements.
5. Payout eligibility requires explicit buyer confirmation or an authorized manual-review outcome, with no open dispute/refund/hold.
6. If the buyer does nothing, payout remains blocked for manual review.
7. Buyer confirmation must clearly explain that it can begin the seller-payout process.

## Seven-day dispute window

Current MVP default:

```text
window_duration_hours = 168
window_starts_at = carrier_confirmed_delivered_at
window_ends_at = window_starts_at + 168 hours
```

Display the exact local date/time and timezone. Avoid only saying “เหลือ 7 วัน.”

### Early release

Buyer may press `ได้รับสินค้าแล้ว` after delivery. This may transition to payout eligibility only when:

- Payment remains valid and unreversed.
- No dispute is open.
- No refund is pending.
- Risk/operations holds do not block payout.

### Automatic eligibility

At deadline, a scheduled job may mark payout eligible only when:

- Delivery was verified by a trusted carrier event.
- The deadline has passed.
- No dispute is open.
- No refund/cancellation/reversal is active.
- No risk or legal hold is active.

This automatic deadline path applies only to physical agreements with trusted carrier delivery. Digital agreements are always excluded.

### Unverifiable delivery

Do not auto-release. Options:

- Buyer confirms receipt.
- Seller corrects tracking.
- Operations reviews evidence.
- Transaction follows another approved resolution path.

## Required notifications

At minimum:

- Seller payment confirmation and ship-by deadline.
- Buyer tracking submitted.
- Buyer carrier-confirmed delivery and exact dispute deadline.
- Buyer reminder before automatic payout, recommended 24 hours before deadline.
- Both parties when dispute opens.
- Seller when payout instruction starts and when provider confirms transfer.
- Buyer when refund starts and when provider confirms refund.

Notification delivery failure should be logged and retried according to channel policy, but does not silently change the legal/product deadline unless the approved terms require successful notice.

## Dispute opening

Buyer may open a dispute before `window_ends_at` using reason codes such as:

- Item not received despite carrier status.
- Wrong item.
- Materially not as described.
- Undisclosed damage or defect.
- Suspected counterfeit.
- Empty or tampered parcel.
- Other supported reason.

Opening a dispute must be atomic with the transition that blocks payout.

## Dispute evidence

Prompt for:

- Written explanation.
- Photos of item and packaging.
- Shipping label.
- Unboxing evidence where available.
- Relevant chat screenshots, with privacy warning.

Seller can respond with:

- Packing photos/video.
- Product serial/unique marks.
- Drop-off receipt.
- Shipment weight.
- Original listing evidence.
- Explanation.

AI may summarize conflicts and missing evidence, but must label the output as an aid and not a decision.

## Resolution outcomes

MVP should support only outcomes confirmed by policy and provider capability. Potential outcomes include:

- Full payout to seller.
- Full refund to buyer.
- Return required, then refund after verified return.
- Cancellation before shipment.

Partial refund/payout is operationally and technically more complex and should remain disabled until explicitly approved.

Every resolution requires:

- Authorized human or mutually confirmed actor.
- Reason code.
- Written rationale.
- Audit event.
- Idempotent provider instruction.

## Payout rules

1. Payout instruction amount is calculated server-side from immutable transaction amounts and approved fee rules.
2. Payout is blocked for open dispute, refund, payment reversal, sanctions/risk hold, seller onboarding failure, or unsupported provider state.
3. `PAYOUT_PENDING` means the provider is processing, not that the seller received money.
4. `PAID_OUT` requires provider confirmation.
5. Failed payouts remain reconcilable and visible; they do not revert the transaction to unpaid.
6. If payout is executed through a bank outside the payment-collection provider, `PAID_OUT` requires the bank's authenticated completion status or an authorized bank reconciliation result. Creating a bank transfer request is only `PAYOUT_PENDING`.

## Refund rules

1. Refund instruction is idempotent.
2. `REFUND_PENDING` is distinct from `REFUNDED`.
3. The UI must show expected provider processing time without guaranteeing an exact bank posting time unless supported.
4. A payment chargeback/reversal is a separate external event and may require a dedicated risk state.
5. For Stripe PromptPay, the refund may enter `requires_action` while Stripe asks the buyer by email for the bank account used to make the payment. Consumer copy must tell the buyer to check that email.
6. Do not store the buyer's refund bank-account number as a normal TOKLONG transaction field when Stripe can collect it directly.
7. PromptPay refund completion requires the verified Stripe refund status `succeeded`; creating the refund or sending instructions is not completion.

## Operational safety rules

- Keep settlement funds and their transaction ledger operationally separate from TOKLONG operating expenses, subject to the final bank, accounting, and legal structure.
- Reconcile provider-confirmed payments and refunds, Stripe settlement references, settlement-bank movements, seller-payable liabilities, and bank-confirmed payouts. Ledger corrections are append-only.
- The internal seller-payable ledger is not a customer wallet and must not be presented as spendable or withdrawable stored value.
- Do not allow ordinary support agents to change transaction states directly in the database.
- Use authorized commands with required reason and audit trail.
- High-value or high-risk categories may require delayed payout or manual review.
- Prohibited goods trigger transaction prevention, evidence retention as legally appropriate, and account review.
