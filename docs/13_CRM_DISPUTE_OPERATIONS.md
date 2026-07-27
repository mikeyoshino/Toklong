# 13 — CRM Dispute Operations

## Principles

- Opening a dispute and blocking payout are one atomic domain operation.
- CRM does not decide from client state, screenshots, slips, or AI output.
- The immutable paid snapshot, verified provider events, trusted carrier
  events, party statements, and retained evidence are reviewed together.
- AI may summarize conflicts or missing evidence, but cannot choose or submit a
  binding outcome.
- A normal physical dispute must open before the stored inspection deadline.
  The 72-hour payout-hold deadline is not presented as the end of statutory or
  post-payout rights.
- Digital fulfillment never releases from elapsed time or seller assertion.

## Case workflow

```text
Unassigned
  -> Assigned
  -> AwaitingEvidence (when additional evidence is requested)
  -> UnderReview
  -> ReadyForApproval
  -> ApprovedForRefund | ApprovedForPayout
  -> Closed after provider-confirmed completion
```

CRM workflow values do not replace transaction states. The financial paths
remain:

```text
DISPUTED
  -> RESOLUTION_PENDING
  -> REFUND_PENDING
  -> REFUNDED (verified provider completion)
```

or:

```text
DISPUTED
  -> RESOLUTION_PENDING
  -> PAYOUT_ELIGIBLE
  -> PAYOUT_PENDING
  -> PAID_OUT (verified provider completion)
```

## Two-person decision

1. Admin claims and reviews the case.
2. Admin selects a supported recommendation, reason code, and written
   rationale.
3. The recommendation becomes immutable when submitted for approval.
4. A different SuperAdmin approves it or returns it with a written reason.
5. Approval creates one idempotent domain command carrying the trusted CRM
   actor, case, recommendation, rationale, review reference, and idempotency
   key.
6. A concurrent or repeated approval cannot create another transition,
   refund, payout, or audit event.

## Proposed operational SLA

These are proposed initial product values and require operations approval
before production:

| Event | Target |
|---|---|
| Notify both parties that payout is paused | Durable outbox committed with the dispute |
| Assign or place in Admin work queue | Within 4 business hours |
| First human review/evidence request | Within 1 business day |
| Party evidence response | 48 elapsed hours from the exact request deadline |
| Admin recommendation | Within 1 business day after evidence closes |
| SuperAdmin approval/return | Within 2 business days after `ready_for_approval_at` |
| Notify outcome | Durable outbox committed with the authorized decision |

All party-visible deadlines use exact Asia/Bangkok date and time. A delay or
extension is recorded, reasoned, and notified; it is never silently derived by
the client.

## Reason codes

Supported first-level codes:

- `ITEM_NOT_RECEIVED`
- `WRONG_ITEM`
- `MATERIALLY_NOT_AS_DESCRIBED`
- `UNDISCLOSED_DAMAGE`
- `MISSING_PARTS`
- `SUSPECTED_COUNTERFEIT`
- `EMPTY_OR_TAMPERED_PARCEL`
- `DIGITAL_NOT_RECEIVED`
- `DIGITAL_NOT_TRANSFERABLE`
- `OTHER`

`OTHER` requires a written explanation and triage classification. It is not a
route around prohibited-item, secret-storage, or authorization rules.

## Evidence baseline

System evidence is attached by reference, not retyped by operations:

- Immutable product/agreement snapshot and managed product photo, if supplied.
- Buyer and seller acceptance times.
- Integer-satang amounts and currency.
- Verified payment/refund/payout events.
- Shipping reservation, carrier, tracking, scans, delivery/POD and ingestion
  times.
- Dispute deadline and prior audit events.

Implemented party-image delivery:

- Buyer and Seller may upload only while the authoritative transaction is
  `Disputed` or `ResolutionPending`.
- Each request contains one image, an evidence type, a non-empty description,
  and an `Idempotency-Key`; retries are scoped to the submitting party.
- Input is limited to 6 MB and normalized to JPEG. Decoded images above
  24 million pixels are rejected before full decoding.
- The normalized image is encrypted at rest with AES-256-GCM. Only an opaque
  storage reference, length, SHA-256, type, party, and audit metadata are kept
  in PostgreSQL.
- A party may list and open only its own submissions. Active CRM Admin and
  SuperAdmin users may open submissions for the case through an authenticated,
  non-cacheable endpoint; every successful CRM access is audited.
- Each party may submit at most 10 images per dispute. Videos remain
  recommended when available but are not accepted by this image-only slice.
- When CRM requests evidence from Buyer, Seller, or both, a durable core
  notification is queued for each target with the requested items, deep link,
  and exact Asia/Bangkok deadline. Repeating the same CRM request does not
  duplicate the notification.
- Passwords, recovery codes, private keys, seed phrases, mnemonics, and other
  reusable credentials are rejected from descriptions and must never be
  included in an image.

Minimum party evidence:

| Reason | Buyer | Seller |
|---|---|---|
| Item not received | Statement confirming the delivery issue | Drop-off/shipping evidence |
| Wrong item | Full-item, package, and shipping-label photos | Pre-pack item and unique-mark evidence |
| Material mismatch | Exact mismatch and focused photos | Pre-shipment condition/description evidence |
| Undisclosed damage | Item and packaging photos | Packing and pre-shipment condition evidence |
| Missing parts | Photo of all received contents | Packing checklist or pre-close package evidence |
| Suspected counterfeit | Labels/serials and basis for suspicion | Provenance, receipt, or authenticity evidence |
| Empty/tampered parcel | Package, seals, label, and unboxing evidence when available | Packing evidence, drop-off receipt, recorded weight |
| Digital not received | Checked channel and non-secret problem statement | Non-secret handoff record |
| Digital not transferable | Non-secret error and platform restriction | Transfer right and non-secret handoff evidence |

An unboxing video is recommended when available, never universally mandatory.
Chat screenshots are optional, show a privacy warning, and are not trusted
without comparison to the immutable transaction record.

Category additions:

- Cameras/electronics: serial/unique mark, operating condition, included
  accessories.
- Sneakers/apparel: size/SKU labels, overall condition, relevant seams/soles.
- Bags/fashion accessories: serial/date code when present and provenance when
  authenticity was represented.
- Collectibles: edition/serial, certificate when present, seal and package
  condition.
- Hobby/household: model/identifier, included pieces, operating condition.
- Digital rights: transfer permission and non-secret evidence of sending or
  access. Passwords, recovery codes, private keys, seed phrases, or reusable
  credentials are rejected.

## Missing evidence

- A missed evidence deadline does not itself mark provider success or silently
  choose a financial state.
- Operations decides from the admissible evidence available and explains the
  weight given to missing evidence.
- A party may receive an explicitly authorized extension with an exact new
  deadline and reason.
- The system records who requested, extended, supplied, viewed, and evaluated
  evidence.

## Carrier exception and Seller Protection triage

Carrier operations are separated from the three buyer product-problem routes.
Admin first checks the trusted handoff boundary:

```text
No matching trusted acceptance scan by ship_by_at
  -> seller non-fulfillment
  -> cancel unused shipment, then full-refund path

Matching trusted acceptance scan at or before ship_by_at
  -> timely seller handoff
  -> carrier-custody exception; payout remains blocked
```

For the second branch, CRM must show the acceptance time, ship-by time,
carrier/tracking match, current provider status, and last reconciliation time.
It must not ask Admin to infer custody from a label, tracking allocation,
seller-uploaded receipt, or screenshot.

Seller Protection at this stage prevents an incorrect non-fulfillment
classification. It does not choose the financial result. Delay, loss,
failed delivery, recipient refusal, wrong address, return-to-sender, and
delivered-but-denied remain separately reasoned carrier exceptions until their
provider status mapping, cost allocation, insurance, and compensation policy
are approved.

## Return-required outcome

`RETURN_REQUIRED` is a planned outcome, not an enabled MVP financial decision.
It remains blocked until the shipping provider and operations policy support a
managed return label, trusted scans, delivery confirmation, and exception
handling.

Proposed policy for later approval:

- Seller pays managed return shipping for a wrong item, material
  misdescription, or undisclosed defect attributable to the seller.
- Buyer remorse for an item accurately described is not a normal Toklong
  dispute reason, without limiting applicable legal rights.
- Non-receipt or an empty parcel normally has no item to return.
- Suspected counterfeit, prohibited, hazardous, or legally sensitive goods are
  not automatically returned.
- Refund eligibility follows trusted return delivery, not a buyer-entered
  return claim.

Until this path is approved, CRM must not promise a return label or expose an
enabled Return Required action.

## Post-deadline complaints

After the normal dispute deadline, the ordinary dispute command remains
rejected. Support may create a separate exceptional complaint that does not
rewind transaction state.

Exceptional review may be appropriate for:

- A verified carrier correction.
- A system/provider processing error.
- Credible account takeover, fraud, counterfeit, or prohibited-goods evidence.
- New material evidence that could not reasonably have been supplied earlier.
- A legal or regulator request.

If payout has not been instructed, an authorized risk/legal hold may block the
worker without inventing a new provider state. If payout is pending or
completed, any recovery or remedy is a separate controlled process; `PAID_OUT`
is not falsely reversed.

## External benchmark notes

The policy adapts, but does not copy, these provider patterns:

- PayPal uses distinct dispute/claim stages, evidence requests, return
  requirements in some cases, and explicit response deadlines:
  https://www.paypal.com/ad/webapps/mpp/avoid-disputes-chargebacks
- American Express presents a case reason, timeline, reply-by date, and days
  remaining:
  https://www.americanexpress.com/us/merchant/support-center/disputes/managing-a-disputes-case.html
- Stripe organizes evidence by dispute category and treats final submission as
  a consequential, deadline-bound action:
  https://docs.stripe.com/disputes/responding

Those services operate under different payment and chargeback rules. Their
deadlines and legal effects are not Toklong terms.
