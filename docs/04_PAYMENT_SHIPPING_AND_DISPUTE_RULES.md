# 04 — Payment, Shipping, and Dispute Rules

## Payment rules

1. Use an approved payment partner integration appropriate to the final business model.
2. Payment status is confirmed only by verified provider events or authorized reconciliation.
3. A browser redirect or client callback may display “กำลังเช็กการจ่ายเงิน” but cannot mark the transaction paid.
4. The seller is instructed to fulfill only after the applicable provider-confirmed paid state is reached.
5. Fees, taxes, buyer total, and seller expected net must be calculated server-side and displayed before payment.
6. Provider references and event IDs must be retained for reconciliation.
7. Payment retries must not create duplicate paid transactions.
8. A buyer-created offer is not payable until the authenticated seller accepts the complete final agreement and passes the required policy/eligibility checks.
9. Registration must collect a syntactically valid buyer payment-contact email
   for receipts and provider refund instructions. PromptPay checkout reads it
   server-side from the authenticated buyer profile and never accepts an email
   override from the checkout client.
10. The current Stripe PromptPay flow has no manual capture. Do not describe an unpaid proposal as reserved, authorized, funded, or paid.
11. The seller-response deadline is 24 hours after offer activation. The buyer
    payment deadline is one hour after authenticated seller acceptance.
12. The server rejects seller acceptance and new/retried checkout after the
    applicable deadline; a client clock is never authoritative.
13. A verified provider payment confirmed at or before the payment deadline is
    valid even when its webhook arrives later. Payment confirmed after the
    deadline must not expose fulfillment and enters the idempotent refund path.
14. Authorized Stripe reconciliation must read the matching PaymentIntent and
    paid Charge with the expected integer-satang amount and currency. The Charge
    timestamp, not the later observation time, is used for deadline evaluation.
15. Buyer Protection fee policy version `buyer-protection-v2` is buyer-funded
    and uses marginal tiers: the first 5,000 THB at 4%, the portion above
    5,000 through 15,000 THB at 3.5%, and the portion above 15,000 through
    30,000 THB at 3%. The minimum fee is 59 THB and there is no separate fee
    cap. The weighted tier result is rounded up once to one satang. All
    arithmetic uses integer satang and basis points.
16. The domain absolute technical item-price maximum is 999,999 THB. The
    active `buyer-protection-v2` Pilot range remains 1,000–30,000 THB. The
    Pilot maximum is a TOKLONG risk limit, not a claimed Thai statutory limit.
    No rate above 30,000 THB is approved; the application rejects that range
    until a new versioned policy and the required provider, KYC, shipping,
    reserve, legal, risk, and operations gates are approved. See
    `docs/17_PRICING_AND_TRANSACTION_LIMITS.md`.
17. `buyer_total_satang = item_price_satang + shipping_fee_satang +
    buyer_protection_fee_satang + parcel_protection_customer_price_satang`.
    The final parcel-protection price is zero unless the buyer elected an
    available add-on. Provider payment, full refund, evidence, and
    reconciliation use that exact buyer total. The seller-funded platform fee
    is zero for new `buyer-protection-v2` transactions, so
    `seller_expected_net_satang = item_price_satang`; buyer-paid shipping,
    Buyer Protection, and parcel protection are not seller proceeds.
18. Seller acceptance freezes the item and delivery facts, not a parcel-
    protection election. After acceptance, the buyer is shown the optional
    choice only when it is applicable; the selection, combined price, limits,
    and terms are frozen only after the buyer elects and the exact booking is
    ready. Changed price, limit, expiry, or terms requires reconfirmation; a
    paid snapshot is never changed.

## Product snapshot and acceptance

Before payment, the buyer must be shown and accept the complete agreement record the buyer created and the seller accepted unchanged:

- Agreed item identity and any supplied agreement photos.
- The frozen agreement description, including represented condition, included items, functionality, and known defects.
- Item price, shipping charge, Buyer Protection fee, final buyer-only
  parcel-protection charge when elected, and buyer total as separate
  integer-satang values.
- Physical ship-by or digital handoff deadline.
- Supported delivery method.
- For physical goods, destination province and postal code visible to the
  seller before acceptance.
- For physical goods, seller-origin province/postal code, parcel weight and
  dimensions, selected carrier/service, quote reference/expiry, and shipping
  charge. Full origin and destination street addresses remain private
  fulfillment data. Until account-specific certification establishes other
  requirements, weight and every dimension are mandatory.
- Physical 72-hour inspection and payout-hold rule or digital no-auto-release rule.
- The exact payout condition for the fulfillment type.
- Prohibited-item and problem-reporting policy.
- Applicable terms version.

Seller acceptance creates an immutable agreement-core hash and one append-only
acceptance record tied to the authenticated seller account. Buyer checkout must
recompute and validate that core. Seller acceptance does not create a provider
booking. The buyer must first make or resume the buyer-only protection election;
the system then revalidates it and records an exact durable booking. Only a
matching booking permits idempotent PaymentIntent provider preparation. The
provider reference may be created or reused before `BeginCheckout` persists
buyer acceptance and the v11 annex; a verified payment cannot progress unless
that persisted annex passes integrity validation. Actor identity, verified-phone
authentication method, terms hash/version, and server acceptance time are
retained; OTP values are not.

For a physical item, destination province and postal code are part of the
agreement core seen by the seller. The buyer supplies the full delivery address
when creating the offer; the server resolves and stores it as a private
fulfillment annex and derives the province/postal values in that core. Checkout
shows this locked address for review and never accepts replacement address
fields. The UI must make the disclosure boundary visible: the seller sees only
province/postal before payment and the full address after provider-confirmed
payment unlocks fulfillment. Any address correction requires the unpaid offer
to end and a new offer, or cancellation/refund handling after payment.

After both acceptances exist, either authenticated party may download
role-shaped hashed JSON evidence and a readable role-shaped HTML copy with
server acceptance times. Both copies carry the same agreement-core hash. The
validated v11 buyer checkout annex appears only in the buyer copy, so the buyer
and seller payload hashes intentionally differ and the seller never receives
buyer-only protection prices or coverage. The buyer copy omits uncertified
unknown coverage rather than presenting it as zero, and digital fulfillment has
no parcel-protection coverage rows. These are described as records of
`การยอมรับข้อตกลงทางอิเล็กทรอนิกส์`, not as a certificate-backed digital
signature.

After confirmed payment, material changes require a new transaction. Do not silently edit the paid snapshot.

Before payment, the seller may only accept or decline a buyer-created offer. Any correction requires the seller to decline and the buyer to create a new offer.

The text and normalized snapshot must preserve what the buyer specified and the seller accepted.

## Shipment rules

1. Before accepting a physical offer, the seller must supply a complete Thai
   shipping origin plus parcel weight and width/length/height. The backend
   derives postal codes, requests quotes through the configured shipping
   provider boundary, and independently validates the selected quote at
   acceptance.
2. A seller may keep exactly one saved origin. Explicitly saving a new origin
   replaces the prior value; the transaction always retains its own immutable
   origin snapshot. Package measurements are transaction-specific.
3. A quote must match origin postal code, destination postal code, weight,
   dimensions, disclosed fee, and provider reference. Delivery facts are frozen
   at seller acceptance, but no shipment is booked there. Any delivery-quote
   change requires a new seller acceptance.
4. After seller acceptance, checkout obtains the buyer-only protection
   availability. Within a verified included limit, it skips the prompt and
   auto-submits `AddProtection=false`, persisting `Declined` with no charge;
   that is not a distinct included-only election. An over-limit available
   add-on is accepted or explicitly declined once by the buyer.
   The election and durable booking intent are idempotent. Before provider
   mutation, the Worker revalidates elected price, included/selected limits,
   option, terms, and expiry. A changed or expired option requires buyer
   reconfirmation and no PaymentIntent.
5. The matching unconfirmed booking records the exact carrier/service,
   delivery quote, buyer election, and final price without changing the
   seller-acceptance timestamp or one-hour payment deadline. Booking failure,
   timeout, unknown outcome, or mismatch blocks PaymentIntent creation. After
   verified payment for the exact final buyer total, the Worker confirms that
   booking. Buyer-paid shipping and optional protection are never added to
   seller proceeds.
6. SHIPPOP supplies the courier tracking number and 4×6 HTML label. The seller
   may open, zoom, save, share, or print the label only after authenticated
   authorization and provider confirmation. The in-app preview disables
   JavaScript and external top-level navigation; save/share uses the unchanged
   provider HTML. A provider-managed transaction rejects manual carrier or
   tracking replacement. Showing the barcode on a phone does not by itself
   prove that a selected counter accepts screen scanning.
7. `ship_by_at` is fixed at provider-confirmed payment time plus 72 hours for
   the MVP. No buyer, seller, form, or client command may supply a different
   duration. Merely allocating a label or tracking number does not satisfy this
   deadline; the managed path requires a first carrier scan.
8. The Worker polls SHIPPOP tracking on a configurable, jittered schedule that
   stays within the certified provider rate limit. Repeated unchanged statuses
   are safe and database heartbeat writes are throttled. `shipping` maps to
   verified in-transit. `complete` maps to delivered only with a trusted
   carrier delivery timestamp; poll-observation time is never substituted.
   Problem/invalid/return states, missing delivery time, and carrier/tracking
   mismatches block normal release as unverified. Carrier event IDs are
   deterministic and replay-safe.
9. The SHIPPOP webhook contract documented for this integration does not
   provide a verifiable signature field. TOKLONG therefore does not expose an
   unsigned SHIPPOP webhook endpoint; authenticated server-to-provider polling
   is the authoritative production reconciliation boundary.
10. A seller-entered “delivered” status is never authoritative. The app should
    display the carrier event timestamp and app ingestion timestamp separately
    when relevant.
11. If tracking belongs to another transaction, is reused, has a mismatched
    carrier, or shows suspicious prior delivery, block normal auto-release and
    route to review.
12. If there is no carrier scan by `ship_by_at`, notify both parties and enter
    the full-refund path. Before creating the Stripe refund, the Worker cancels
    a confirmed but unscanned SHIPPOP shipment. If the provider already shows a
    carrier scan, cancellation is skipped and the fact is audited.
13. The first trusted carrier acceptance scan is the responsibility boundary
    for a provider-managed shipment:
    - no trusted scan by `ship_by_at` means seller non-fulfillment for the
      automatic missed-shipment path;
    - a trusted scan occurring at or before `ship_by_at` means the seller
      handed the parcel to the locked carrier on time;
    - a label, tracking allocation, seller statement, receipt image, or
      client-supplied event does not satisfy this boundary.
14. A timely acceptance scan does not prove delivery or release payout. A
    subsequent delay, loss, failed delivery, return-to-sender, or delivery
    conflict blocks automatic payout and enters carrier-exception review.
15. If reconciliation discovers a timely trusted scan while TOKLONG is trying
    to cancel an apparently unscanned shipment, the system must stop the
    automatic refund before provider instruction and return the transaction to
    payout-blocked tracking review. A scan after `ship_by_at` is not timely
    Seller Protection and follows the approved late-shipment exception policy.
16. Seller Protection is eligibility for a carrier-failure remedy, not a
    promise of payout from buyer funds. Seller compensation requires an
    approved carrier insurance, declared-value, or TOKLONG protection funding
    policy. Buyer refund and seller compensation are separate obligations.
17. Provider-changing booking, confirmation, cancellation, and return calls are
    durable operations with unique idempotency keys and processing leases. A
    timeout after sending a mutation becomes an unknown provider outcome and is
    reconciled before retry. If SHIPPOP cannot supply safe booking lookup or an
    idempotency guarantee, the affected service remains disabled.
18. If the buyer does not pay by the one-hour deadline, the offer expires
    immediately and an unconfirmed-booking cancellation is queued. Provider
    cleanup retries in the background and never extends the consumer deadline.
19. Each enabled service is drop-off only and must pass account-specific
    certification for quote, booking, confirmation, cancellation, label,
    tracking/POD timestamp, rate limit, optional-protection availability,
    limits, terms, safe lookup/replay, cancellation before first scan, and
    weight/dimension fields and units. All SHIPPOP service flags remain off
    until that evidence is recorded.
20. No service is assumed to provide full-value coverage. Where an optional
    buyer add-on is certified and elected, disclose its combined buyer price and
    maximum at choice, then retain the combined price in the buyer payment
    summary while keeping the maximum at choice/details. Provider cost and
    TOKLONG fee split remain internal. Included coverage may be zero, and
    unavailable capability creates no charge or coverage claim.
21. A post-payment carrier surcharge is recorded as an append-only TOKLONG
    operational cost and CRM case. It never changes the paid snapshot, requests
    more money automatically, or reduces seller net.
22. An authorized return resolution creates a separate provider-managed return
    shipment. TOKLONG advances the return cost. Refund remains blocked until
    trusted return delivery or an authorized manual resolution, and refund
    completion still requires provider confirmation.

The Development provider uses the same reserve, confirm, label, tracking, and
cancel boundary with deterministic local data. It is not SHIPPOP pricing and
must never be enabled or described as production shipping. Production selects
`ShippingQuotes:Provider=Shippop`, requires HTTPS plus server-only SHIPPOP
credentials and a quote-signing secret, and fails startup when those settings
are incomplete.

An HTTP-only SHIPPOP Dev environment may be used for local Development and
provider certification only when an explicit insecure-transport opt-in is set.
The opt-in defaults off and remains rejected outside Development/Testing.
Production SHIPPOP traffic continues to require HTTPS.

Local testing must not add a client flag that can mark delivery. A developer may
submit an HMAC-signed, fresh, replay-safe carrier event through the internal API
using `scripts/simulate-carrier-event.sh`. Production requires its own
non-development reconciliation secret. An unsigned request, stale request, or
seller/client request cannot move the shipment state.

For interactive demos, an explicitly enabled Development-only backend worker
may submit the same idempotent carrier and manual-bank reconciliation commands.
It advances one step per configured interval, never accepts a mobile-client
bypass, and must fail startup if enabled outside the Development environment.
The buyer must still explicitly confirm receipt in the app before payout can
start; demo automation may only confirm the already-created manual-bank payout
after that authorization.

## Digital fulfillment rules

1. Digital agreements must be allow-listed and transferable by the seller.
2. The app stores only a non-secret handoff statement and timestamp. Never store passwords, recovery codes, private keys, wallet secrets, or reusable credentials in transaction fields or normal logs.
3. A seller-entered handoff does not prove delivery and cannot release payout.
4. There is no time-based automatic payout for digital agreements.
5. Payout eligibility requires explicit buyer confirmation or an authorized manual-review outcome, with no open dispute/refund/hold.
6. If the buyer does nothing, payout remains blocked for manual review.
7. Buyer confirmation must clearly explain that it can begin the seller-payout process.

## 72-hour physical inspection and payout-hold window

Current MVP default:

```text
window_duration_hours = 72
window_starts_at = carrier_confirmed_delivered_at
window_ends_at = window_starts_at + 72 hours
```

Display the exact local date/time and timezone. Avoid only saying “เหลือ 3 วัน.”
Payment time, shipment creation, shipped/in-transit status, seller-entered
delivery, and unverified tracking events never start this window.

This is a TOKLONG payout-hold rule, not a statement that either party's legal
rights expire after 72 hours. Applicable statutory, contractual, warranty, and
post-payout complaint rights remain separate.

### Early release

After inspecting the item, the buyer may press `ตรวจแล้ว ทุกอย่างเรียบร้อย`.
Before confirmation, explain that this action can begin the seller-payout
process. It may transition to payout eligibility only when:

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

- Intended seller receives an in-app invitation when a buyer creates an offer.
  When a registered push device and provider are available, it also receives
  `ได้รับข้อเสนอซื้อ` with product name and total, linked to the same offer.
- Seller payment confirmation and ship-by deadline.
- Buyer tracking submitted.
- Buyer carrier-confirmed delivery and exact dispute deadline.
- Buyer reminder before automatic payout, recommended 24 hours before deadline.
- Both parties when dispute opens.
- Seller when payout instruction starts and when provider confirms transfer.
- Buyer when refund starts and when provider confirms refund.

Notification delivery failure should be logged and retried according to channel policy, but does not silently change the legal/product deadline unless the approved terms require successful notice.
OS notification content must avoid phone numbers, addresses, bank details,
payment credentials, evidence, and other sensitive data. Opening any
notification still requires an authenticated, authorized API request.

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
8. The refund Worker obtains the instruction email from the immutable Stripe
   PaymentIntent created at checkout, not from a later client request or a
   potentially changed account-profile value.
   It sends Stripe's `instructions_email` parameter only when the
   provider-confirmed charge payment-method type is `promptpay`; card refunds
   must not receive that method-specific parameter.
9. Persist only the current provider status and action/instruction timestamps.
   Do not persist `next_action` URLs, bank-account data, or an extra copy of the
   instruction email.
10. A signed webhook or authorized reconciliation may record
    `requires_action`, `pending`, and `succeeded`. Only `succeeded` moves the
    transaction to `REFUNDED`.
11. Notify the Buyer when the refund first enters `requires_action`, and again
    only if it leaves that status and later re-enters it. Replayed events and
    repeated reconciliation while the status is unchanged must not send
    duplicate instructions.
12. Consumer copy directs the Buyer to the email from Stripe and says to submit
    the account used for payment directly to Stripe. TOKLONG support and CRM
    must never request that account number in chat, notes, evidence, or the app.

Implementation note: the refund worker creates one full Stripe refund using a
transaction-derived idempotency key. The stored state remains `REFUND_PENDING`
until a verified Stripe refund event or authorized server-to-provider
reconciliation matches the transaction metadata, PaymentIntent, refund
reference, full integer-satang amount, currency, and `succeeded` status. A late
payment or a missed 72-hour fulfillment deadline enters this same path.
The same validation applies before recording non-terminal refund progress.
When Stripe does not expose the exact refund status-transition timestamp, an
authorized reconciliation records the server observation time rather than
misrepresenting the earlier refund-request creation time as completion time.

## Operational safety rules

- Keep settlement funds and their transaction ledger operationally separate from TOKLONG operating expenses, subject to the final bank, accounting, and legal structure.
- Reconcile provider-confirmed payments and refunds, Stripe settlement references, settlement-bank movements, seller-payable liabilities, and bank-confirmed payouts. Ledger corrections are append-only.

The current provider-neutral bank adapter may create an instruction only after
release eligibility. Provider acceptance is `PAYOUT_PENDING`; a signed bank
completion result is required for `PAID_OUT`.
- The internal seller-payable ledger is not a customer wallet and must not be presented as spendable or withdrawable stored value.
- Do not allow ordinary support agents to change transaction states directly in the database.
- Use authorized commands with required reason and audit trail.
- High-value or high-risk categories may require delayed payout or manual review.
- Prohibited goods trigger transaction prevention, evidence retention as legally appropriate, and account review.
