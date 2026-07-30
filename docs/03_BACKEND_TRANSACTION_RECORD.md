# 03 — Backend Transaction Record

## Purpose

The backend transaction record replaces a separate visible contract
drafting/signing workflow. It captures what was offered, the same agreement
core electronically accepted by both parties, how payment and delivery
progressed, and why payout or refund occurred. This is click acceptance backed
by authenticated accounts and immutable records; it must not be described as a
certificate-backed or qualified digital signature without separate legal and
identity-provider approval.

This record must be understandable to users and operations, while preserving technical evidence for audit and reconciliation.

## Core entities

### User

```text
id
role_capabilities
phone_verification_status
email_verification_status
identity_provider_reference
created_at
status
```

Do not store more identity data than necessary. Prefer provider references or tokenized identifiers where supported.

### Buyer profile

```text
buyer_id
full_name
phone_number
phone_verified_at
payment_contact_email_or_null_for_legacy_accounts
saved_address_line_or_null
saved_province_id_or_null
saved_province_name_or_null
saved_district_id_or_null
saved_district_name_or_null
saved_subdistrict_id_or_null
saved_subdistrict_name_or_null
saved_postal_code_or_null
saved_address_updated_at_or_null
created_at
```

The offer command accepts `buyer_id`, not editable name/contact fields, and
resolves the current buyer profile server-side. A buyer has zero or one saved
delivery address. Saving another address updates that single record rather than
creating an address book. Physical-offer creation resolves administrative IDs
against the bundled catalog server-side and stores a formatted private
delivery-address snapshot independently of later profile updates. Checkout
reads that locked snapshot and never replaces it.

### Seller payout profile

```text
seller_id
payout_provider
payout_beneficiary_reference
beneficiary_name_verification_status
onboarding_status
payout_eligibility_status
risk_flags
saved_shipping_address_line_or_null
saved_shipping_province_id_or_null
saved_shipping_province_name_or_null
saved_shipping_district_id_or_null
saved_shipping_district_name_or_null
saved_shipping_subdistrict_id_or_null
saved_shipping_subdistrict_name_or_null
saved_shipping_postal_code_or_null
saved_shipping_address_updated_at_or_null
created_at
updated_at
```

The seller has zero or one saved shipping origin. Administrative IDs are
resolved against the server-owned Thai address catalog. Saving another origin
replaces the profile value, while every accepted physical transaction keeps its
own immutable origin snapshot. Parcel weight and dimensions are not profile
defaults.

### Sale link

```text
id
seller_id_or_null_until_joined
initiator_role = buyer
public_token
status
expires_at
created_at
activated_at
closed_at
```

The public token must be unguessable and revocable.

### Buyer offer draft

```text
sale_link_id
buyer_id
intended_seller_phone
fulfillment_type
product_name
proposed_description
proposed_photo_asset_ids[]
proposed_item_price_satang
currency
fulfillment_duration_hours = 72 (system fixed)
seller_acceptance_deadline_at
buyer_payment_deadline_at_or_null
expiration_reason_or_null
created_at
updated_at
```

This is not a paid snapshot. `buyer_id` references a buyer account that supplied
first and last name during registration and later signed in by phone without
re-entering that name. The buyer supplies a product name, every material fact,
an optional managed photo, and the intended seller's normalized Thai mobile
number. The seller must authenticate with that exact phone and either accept
the record unchanged or decline it before checkout is enabled. If supplied,
the managed photo is included in the immutable agreement core and paid
snapshot; absence is stored explicitly as `null`. The unguessable link is a
routing identifier, not an authorization credential. The
payment-contact email is collected during registration and read server-side
from the authenticated buyer profile at checkout; the checkout client cannot
replace it. Existing legacy accounts may add or update it from the authenticated
account screen. Use a provider/customer reference where practical; do not store
refund bank details.

For physical offers, `proposed_item_price_satang` excludes shipping. Shipping
is not buyer-authored: the intended seller selects a validated quote before
acceptance, which freezes delivery facts only. The buyer-only optional
parcel-protection election, final combined charge, and buyer total are created
after acceptance and before PaymentIntent creation.
The domain accepts no item price above the absolute 999,999 THB technical
boundary. The application independently enforces the lower active commercial
maximum from the versioned fee policy before storing the offer. Supporting the
technical value does not activate it for sale.

### Product draft

```text
sale_link_id
fulfillment_type
category
name
description
condition_code
known_defects
photo_asset_ids[]
price_satang
currency
ship_by_duration_hours = 72 (system fixed)
supported_carriers[]
prohibited_goods_attestation
transfer_rights_attestation
updated_at
```

The buyer UI always collects `name` and `condition_code`; a managed product
photo is optional. For Quick Deal presentation, `description` is optional on the first
screen and deterministically falls back to the trimmed product name when the
buyer leaves it blank. `known_defects` is required when
`condition_code = USED_DEFECTS`; otherwise the client sends the explicit value
`ไม่มีตำหนิที่ผู้ซื้อระบุ`. Condition and defects are confirmed in the final
review-before-submit sheet, not omitted from the transaction record. The seller
UI renders these explicit fields read-only and exposes only accept or decline.
The backend retains them for the buyer-facing record and paid snapshot.

When the buyer chooses a product photo, capture/upload must produce a managed
asset reference. A public image URL may be imported as assistance, but the
buyer is never required to type a raw photo URL. Saving a photo must work
independently from optional AI analysis. AI source images are not promoted into
the product snapshot automatically.

The optional agreement-draft extractor accepts authenticated, rate-limited
multipart requests containing pasted chat text and up to three supported
images. Sources are processed in memory and are not stored as product evidence,
transaction fields, logs, or snapshot attachments. The structured AI result is
an untrusted draft containing only seller phone, product name, description,
known defects, price, condition, confidence, and extracted-field names. The
mobile client previews it and fills only empty controls after explicit buyer
confirmation. The ordinary offer command still validates every field and
prohibited-goods rules; AI output cannot activate a link or change transaction
state.

### Agreement core snapshot

Created atomically when the authenticated seller accepts the buyer-authored
offer. It freezes every term that both parties must accept:

```text
transaction_id
agreement_core_snapshot_json
agreement_core_snapshot_hash
agreement_core_snapshot_created_at
terms_snapshot_json
terms_snapshot_hash
agreement_core_schema_version = 11 (inside JSON)
buyer_id
seller_id
buyer_and_seller_display_identity
item_description_condition_defects_and_optional_photo
item_price_shipping_fee_buyer_protection_fee_platform_fee_seller_net_and_currency
fulfillment_type_and_duration
delivery_province_name_and_postal_code_for_physical_goods
origin_province_name_and_postal_code_for_physical_goods
package_weight_and_dimensions_for_physical_goods
shipping_quote_provider_reference_expiry_carrier_and_service
inspection_or_digital_release_rules
terms_and_fee_policy_versions
seller_accepted_at
buyer_payment_deadline_at
```

The core JSON is canonical for its schema version and protected by SHA-256.
Checkout rebuilds it from the transaction and must fail if any material value,
party identity, fee, deadline, terms document, or hash differs.

### Agreement acceptance

One append-only row is stored per transaction party:

```text
id
transaction_id
role = buyer | seller
actor_user_id
verified_phone_number
authentication_method = verified-phone-session
agreement_core_snapshot_hash
terms_version
terms_snapshot_hash
accepted_at
correlation_id
idempotency_key
```

`(transaction_id, role)` and `(transaction_id, idempotency_key)` are unique.
The seller row is created by `ยอมรับข้อเสนอ`; the buyer row is created by the
final `ยอมรับข้อตกลง` action before provider checkout. Both rows must reference
the exact same core and terms hashes. The record stores neither the OTP value
nor reusable authentication credentials. There is no update method for an
acceptance, and the persistence layer rejects `Modified` or `Deleted`
acceptance entries. Corrections require a new offer.

### Buyer-only parcel-protection annex

Seller acceptance neither writes nor displays this annex. The authenticated
buyer creates it only in `SELLER_ACCEPTED_AWAITING_PAYMENT`; it is read and
written only through buyer-authorized checkout commands. Every money value is
integer satang. The persisted selection contains:

```text
election
customer_price_satang
provider_cost_satang
toklong_service_fee_satang
included_coverage_limit_satang
selected_coverage_limit_satang
protection_terms_version
provider_option_reference
quoted_at
expires_at
buyer_elected_at
```

`provider_cost_satang` and `toklong_service_fee_satang` are internal accounting
fields. The buyer sees only the combined `customer_price_satang`, and only at
the choice surface, together with the disclosed maximum. The seller sees none
of these annex values. A declined, unavailable, or included-only outcome has a
zero customer price and no provider option reference. Unavailable never means
or implies zero included coverage.

The election is immutable once checkout begins. Before that point, a buyer
change supersedes an unmutated booking intent, or durably cancels a reserved
attempt before a replacement is booked. Previous attempts and change requests
remain auditable. A pending, unknown, or review-needed provider mutation blocks
both a change and PaymentIntent creation.

The immutable audit trail records the buyer-safe lifecycle with sanitized
events: `parcel_protection.offered`, `parcel_protection.unavailable`, elected
outcomes, `parcel_protection.reconfirmation_required`,
`parcel_protection.booking_succeeded`, `parcel_protection.booking_outcome`,
and `parcel_protection.changed`. Booking-outcome audit metadata contains no
raw address, provider credential, or provider response. These audit events
record authorization and state evidence; they never make payment, refund, or
payout successful.

Mobile analytics are coarse and non-sensitive: `parcel_protection_offered`,
`parcel_protection_accepted` (combined customer price only),
`parcel_protection_declined`, `parcel_protection_unavailable`,
`parcel_protection_changed`, `parcel_protection_price_changed`, and
`parcel_protection_checkout_converted`. Analytics contain no address, phone,
provider reference, quote, terms text, or credential-shaped key.
`parcel_protection_checkout_converted` means only that PaymentSheet reported
completion; provider-confirmed payment still requires the verified webhook.

### Private fulfillment annex and paid transaction snapshot

For a physical offer, the private full-delivery-address annex is created and
locked with the offer before seller acceptance. When the seller accepts, a
private full-origin snapshot and the selected shipping quote are also locked.
Only resolved province/postal values, parcel measurements, carrier/service,
quote metadata, and shipping charge enter the shared core; street-level origin
and destination remain private fulfillment data. After the buyer has elected
or recorded the applicable protection outcome, a Worker creates and validates
the matching unconfirmed booking. The buyer then accepts the validated core and
the final buyer-only annex before the payment intent is established. The product
snapshot references the shared core hash, both already-locked address records,
and the buyer acceptance time. The full destination is not disclosed to the
seller before provider-confirmed payment.
Once payment is confirmed, the snapshot is sealed and material fields are
immutable.

```text
transaction_id
sale_link_id
seller_id
buyer_id
fulfillment_type
product_snapshot_json
product_snapshot_hash
snapshot_schema_version
agreement_snapshot_created_at
agreement_snapshot_sealed_at
price_satang
shipping_fee_satang
parcel_protection_customer_price_satang
buyer_total_satang
buyer_protection_fee_satang
platform_fee_satang
seller_expected_net_satang
currency
shipping_origin_address
shipping_origin_address_line
shipping_origin_subdistrict_name
shipping_origin_district_name
shipping_origin_province_name
shipping_origin_postal_code
package_weight_grams
package_width_centimeters
package_length_centimeters
package_height_centimeters
shipping_quote_provider
shipping_quote_reference
shipping_quote_expires_at
carrier_code
shipping_service_code
shipping_service_name
parcel_protection_election
parcel_protection_provider_cost_satang
parcel_protection_service_fee_satang
parcel_protection_included_coverage_limit_satang
parcel_protection_selected_coverage_limit_satang
parcel_protection_terms_version
parcel_protection_option_reference
parcel_protection_quoted_at
parcel_protection_expires_at
parcel_protection_buyer_elected_at
ship_by_at
inspection_window_duration_hours
terms_version
terms_snapshot_json
terms_snapshot_hash
buyer_acceptance_at
seller_acceptance_at
initiator_role
created_at
```

Snapshot schema version 11 adds a single append-only buyer checkout-annex
acceptance record. Its canonical payload binds the product-snapshot hash,
currency, final integer-satang buyer total, election, combined price, internal
cost fields, coverage limits, terms, quote/expiry, and buyer-election time; its
SHA-256 hash is retained in a buyer checkout audit event. It contains no
address, account, or provider credentials. Schema 11 requires this valid annex
evidence for financial progression.

Snapshot schema version 10 remains readable and progresses under its prior
validation rules; it is never backfilled with fabricated buyer-annex evidence.
Earlier schemas remain historical records. No schema version permits seller
acceptance to stand in for the buyer's later election or booking result.

The record retains version 5's optional photo as its managed reference or
explicit `null`, and version 4's rule that the private physical address is
locked at offer creation. Seller acceptance creates the core and terms
documents; buyer election and matching booking precede buyer checkout
acceptance; provider-confirmed payment seals the snapshot. A missing
acceptance, actor mismatch, shared-hash mismatch, missing v11 annex evidence,
or content/hash mismatch blocks payment confirmation and subsequent financial
release.

Schema versions 1–6 remain readable for historical paid records. Existing rows
must never be assigned invented acceptances, delivery addresses, delivery
regions, or core snapshots. An unpaid legacy physical offer without a complete
address must end and be recreated rather than collecting or mutating its
address at checkout.

The MVP does not persist IP addresses, device fingerprints, advertising IDs,
hardware IDs, or precise location as agreement evidence or as a separate
acceptance/security-evidence dataset. OTP rate limiting transiently transforms
the connection address with a random per-process HMAC key; only that
non-reusable in-memory partition key reaches the limiter and it is never
written to application storage or logs.

The agreement core, private fulfillment annex, checkout snapshot, party
acceptances, fulfillment events,
payment/refund/payout state evidence, audit trail, and dispute evidence are
retained for five years from the later of the terminal transaction time or
final dispute-closure time. An authorized legal hold pauses deletion only for
the affected transaction. Separately classified accounting and tax records may
follow an approved schedule of up to seven years where required. At expiry,
personal data must be securely deleted or irreversibly anonymized; immutable
hashes must not be presented as recoverable transaction evidence after their
underlying record has been deleted.

Terminal transitions to `PAID_OUT`, `REFUNDED`, `CANCELLED`, or `EXPIRED`
atomically set:

```text
retention_starts_at
retention_expires_at = retention_starts_at + 5 years
```

If a terminal transaction follows a resolved dispute, the later timestamp is
used. `EXPIRED` may still receive an authorized late provider event; any later
terminal transition replaces the schedule with the new terminal time.

Legal hold fields are:

```text
legal_hold_placed_at
legal_hold_reference
legal_hold_reason
```

Only signed internal operations can place or release a hold. Both actions are
idempotent and append audit events. A held transaction is excluded from every
purge query.

The retention worker deletes the complete transaction aggregate, including
party identity, contacts, address, item details, photos, snapshots,
acceptances, evidence, notifications, and audit events, in the same database
commit that creates a minimized `financial_retention_records` row. That row
contains only transaction ID, terminal state, integer monetary values,
currency, provider references, retention dates, and purge time. It contains no
party identity, address, product description, photo, acceptance record, or
agreement hash and is deleted at year seven.

Managed photo deletion uses `retention_file_deletions` as a transactional
outbox. The queue row is committed with the database purge; the Worker then
deletes the owned file idempotently and removes the queue row. A storage error
leaves the queue row for retry, so a database commit cannot silently orphan a
managed image.

### Downloadable agreement evidence

After both append-only acceptances exist, an authenticated buyer or seller on
that transaction may download:

- canonical JSON containing schema version, item/amount/terms, accepted
  delivery region, shared hashes, party roles, and server acceptance times;
- a readable HTML rendering suitable for printing or saving as PDF.

The evidence payload has its own SHA-256 hash. Contacts are masked and the file
contains no OTP value, reusable credential, full delivery or shipping-origin
street address, IP address, or device identifier. These files are evidence of
electronic click acceptance, not a certificate-backed digital signature.

### Payment

```text
transaction_id
provider
provider_payment_reference
amount_satang
currency
status
confirmed_at
failed_at
refunded_amount_satang
last_provider_event_id
reconciled_at
provider_settlement_reference
settled_to_bank_at
```

For schema-6 and later physical transactions, `amount_satang` equals
`buyer_total_satang`, not item price alone. Full refunds validate against the
same amount.

### Settlement ledger entry

```text
id
transaction_id
entry_type
amount_satang
currency
provider_reference
bank_reference
effective_at
created_at
idempotency_key
```

The settlement ledger is an internal append-only liability/reconciliation
record, not a user wallet or stored-value balance. It must distinguish buyer
funds received, shipping-cost liability, provider fees, TOKLONG fees, refund
liabilities, seller payable, seller payout, and corrections. Entries use
integer satang and corrections are new entries rather than edits.

### Shipment

```text
transaction_id
shipping_origin_address
shipping_origin_address_line
shipping_origin_subdistrict_name
shipping_origin_district_name
shipping_origin_province_name
shipping_origin_postal_code
package_weight_grams
package_width_centimeters
package_length_centimeters
package_height_centimeters
shipping_quote_provider
shipping_quote_reference
shipping_quote_expires_at
carrier_code
shipping_service_code
shipping_service_name
shipping_fee_satang
shipping_purchase_reference
shipping_provider_tracking_code
shipping_courier_tracking_code
shipping_reserved_at
shipping_confirmed_at
shipping_last_provider_status
shipping_last_reconciled_at
shipping_cancelled_at
tracking_number
tracking_verification_status
submitted_at
first_carrier_scan_at
in_transit_at
delivered_at
carrier_delivery_event_id
carrier_delivery_event_received_at
delivery_raw_reference
last_checked_at
```

New provider-managed outbound and return shipping uses a `managed_shipments`
child entity. Existing embedded outbound fields remain readable for historical
snapshots and are migrated without inventing provider facts. A transaction has
one outbound managed shipment and at most one active return managed shipment.

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
exception_resolution_reference
exception_resolved_by
exception_resolved_at
created_at
version
```

Outbound and return references are never interchangeable. A paid outbound
snapshot is immutable; an approved return creates another managed shipment.
`tracking_unverified` and `carrier_exception` block payout/refund while
`exception_resolved_at` is null. An authorized resolution retains the shipment
status as evidence, records actor/reference/time, and a later exception event
clears that resolution and blocks money flow again.

### Shipping operation

Every provider-changing shipping instruction is first committed as a durable
operation:

```text
id
transaction_id
managed_shipment_id
operation_type =
  book_outbound | confirm_outbound | cancel_outbound |
  book_return | confirm_return | cancel_return
status =
  pending | processing | retry_scheduled |
  outcome_unknown | succeeded | needs_review
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

The idempotency key is unique. Provider credentials, raw address payloads, and
unredacted responses are not operation fields. A mutation timeout becomes
`outcome_unknown`; it cannot be replayed until the original provider outcome is
reconciled or provider idempotency is proven. An authorized retry requires a
step-up CRM actor, reason, provider-outcome reference, and immutable audit
event. `needs_review` has the same replay-proof requirement.

### Provider shipping adjustment

Post-payment carrier charges are append-only TOKLONG operational costs:

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
resolution_code
resolved_by
resolved_at
version
```

An adjustment is payout/refund-blocking while `resolved_at` is null. Only an
authorized reconciliation actor may close it, and closing it records an
immutable audit event. It never mutates the paid buyer total or seller payable.

### Shipping insurance case

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

Insurance case progression is operational evidence, not a binding refund or
payout decision.

For a provider-managed shipment, `first_carrier_scan_at` is the trusted
seller-to-carrier handoff fact. The application derives, rather than stores as
an editable claim:

```text
seller_handoff_confirmed =
  first_carrier_scan_at is not null

seller_protection_eligible_for_carrier_failure =
  first_carrier_scan_at <= ship_by_at
  and carrier/tracking match the locked provider shipment
```

Eligibility protects the seller from being classified as “did not ship” when a
carrier problem happens later. It never authorizes payout by itself. If a
timely scan is discovered while an unused-shipment cancellation is being
attempted, the pending automatic refund is stopped before provider instruction
and the transaction returns to a payout-blocked tracking review state.

### Digital fulfillment

```text
transaction_id
handoff_statement
seller_submitted_at
buyer_confirmed_at
manual_review_reference
manual_reviewed_at
release_reason
```

`handoff_statement` must not contain credentials or reusable secrets. Seller submission is evidence of an asserted handoff only; it is not authoritative delivery and cannot by itself create payout eligibility.

### Dispute window

```text
transaction_id
starts_at
ends_at
source_delivery_event_id
status
buyer_confirmed_at
release_reason
```

For the default physical flow, `starts_at` must equal trusted `delivered_at`
and `ends_at` must equal `starts_at + inspection_window_duration_hours`.
New offers use 72 hours. Rows created under the former disclosed rule retain
168 hours rather than being shortened retroactively. A shipped, in-transit,
seller-entered, or unverified event must not populate either timestamp.

### Dispute

```text
dispute_id
transaction_id
opened_by_user_id
reason_code
statement
status
opened_at
resolution_type
resolution_actor_id
resolved_at
```

### Evidence

```text
evidence_id
dispute_id
submitted_by_user_id
asset_id
asset_type
caption
created_at
integrity_hash
```

### Payout

```text
transaction_id
provider
provider_payout_reference
payout_provider
amount_satang
currency
status
instruction_created_at
provider_confirmed_at
failure_code
last_provider_event_id
reconciled_at
```

### Refund

```text
transaction_id
provider_refund_reference
amount_satang
currency
status
requested_at
provider_confirmed_at
failure_code
last_provider_event_id
action_required_at
action_expires_at
instructions_sent_at
```

The transaction also retains `refund_requested_at`, `refund_confirmed_at`,
`refund_provider_status`, `refund_action_required_at`,
`refund_action_expires_at`, `refund_instructions_sent_at`,
`dispute_resolved_at`, and `dispute_resolution_reference`. The status and
timestamps are provider evidence; TOKLONG does not duplicate the refund
instruction email address or store the bank-account number submitted to Stripe.
The immutable PaymentIntent receipt email is supplied to Stripe as
`instructions_email`. A resolution reference is the authorized human case
reference, never an AI decision.

### Notification outbox

```text
notification_id
transaction_id
audience
recipient
template
created_at
available_at
attempts
last_attempt_at
sent_at
provider_reference
```

State and notification intent commit together. Delivery is retried later with
backoff and is not considered sent without a provider reference. The exact
24-hour pre-payout reminder is scheduled from the carrier-confirmed dispute
deadline.

The outbox is also the source for the authenticated in-app activity feed.
Templates are reusable lifecycle event identifiers; a formatter derives the
consumer title, body, and deep link from current transaction data. The initial
`buyer_offer_received` record is addressed to the normalized intended seller
phone and is created in the same transaction as the offer. Device registration
is separate and binds a random installation ID, authenticated recipient,
platform, and opaque provider push token. Provider tokens must not appear in
logs, consumer API responses, or transaction records.

### Audit event

```text
id
transaction_id
actor_type
actor_id_or_system
name
from_state
to_state
metadata_json
created_at
correlation_id
idempotency_key
```

Audit events are append-only. Corrections create new events; they do not overwrite history.

## Source-of-truth hierarchy

1. Verified payment-provider webhook or authorized provider reconciliation for payment and refund states.
2. Authenticated payout-bank/provider completion event or authorized bank reconciliation for payout states.
3. Verified carrier webhook/API event for shipment and delivery states.
4. Explicit authenticated buyer confirmation for digital receipt, or authorized manual review.
5. Authorized domain service for other user actions and internal transitions.
6. Client/browser state and seller-entered digital delivery are never authoritative for money completion.

## Required idempotency

- Payment creation.
- Buyer offer creation and seller acceptance.
- Payment webhook processing.
- Refund creation and webhook processing.
- Payout creation and webhook processing.
- Settlement-ledger posting and Stripe-to-bank-to-payout reconciliation.
- Carrier subscription/registration.
- Carrier webhook/event ingestion.
- Provider-managed outbound and return booking, confirmation, and cancellation.
- Shipping-operation claim, lease recovery, and provider-outcome
  reconciliation.
- Buyer receipt confirmation.
- Digital handoff submission and authorized manual-review resolution.
- Dispute opening.
- Deadline release job.

Each operation needs a stable idempotency key and unique constraint appropriate to the provider/event.

## Deadline processing

A scheduled unpaid-offer job evaluates:

```text
state == AWAITING_SELLER_ACCEPTANCE
and seller_acceptance_deadline_at <= now
  → EXPIRED / SELLER_DID_NOT_RESPOND

state in (
  SELLER_ACCEPTED_AWAITING_PAYMENT,
  CHECKOUT_STARTED,
  PAYMENT_PENDING)
and buyer_payment_deadline_at <= now
  → EXPIRED / BUYER_DID_NOT_PAY
```

The same checks run before seller response, checkout preparation, and
transaction reads so an inactive worker cannot make an expired offer usable.
Each expiration writes one immutable system audit event.

A scheduled release job may evaluate transactions where:

```text
state == DELIVERED_DISPUTE_WINDOW
and dispute_window.ends_at <= now
and no open dispute
and no refund in progress
and payment remains eligible
and tracking delivery remains trusted
```

The job transitions to `PAYOUT_ELIGIBLE`, then a separate idempotent payout worker creates the provider payout instruction.

Digital transactions must be excluded from this job. Do not combine eligibility evaluation and provider success into one state.

## Permissions

Seller can:

- Join, complete, accept, or decline a buyer-created offer before payment.
- View their transactions.
- For a provider-managed physical shipment, download the label and hand the
  parcel to the locked carrier before the policy deadline; the native app may
  render a script-disabled full-screen copy and share/save/print the unchanged
  original provider file. Tracking is read-only and provider-issued.
- Submit tracking only on a non-managed legacy shipping path.
- Add dispute evidence.
- View payout status.

Buyer can:

- Create/deactivate a proposed offer before seller acceptance.
- View the transaction associated with their verified checkout identity.
- Pay.
- View tracking.
- Confirm receipt.
- Open a dispute before the deadline.
- Add dispute evidence.

Operations can, with role and audit controls:

- Review exceptions and disputes.
- Request additional evidence.
- Apply an authorized resolution.
- Retry/reconcile external operations.

No human role should directly edit provider-confirmed historical events.

## Security and privacy

- Encrypt sensitive personal data at rest and in transit.
- Use signed, expiring URLs for private evidence files.
- Scan uploaded files and restrict type/size.
- Redact sensitive identifiers from normal logs.
- Separate analytics identifiers from operational identity where practical.
- Apply retention schedules to transaction, evidence, support, and payment-provider data.
- The automated worker enforces the five-year evidence purge and seven-year
  minimized-financial-record purge. Signed internal operations provide dry-run
  preview and narrowly scoped legal-hold placement/release; there is
  intentionally no remote HTTP endpoint that executes deletion.
- Record access to dispute evidence and sensitive transaction details.

## Downloadable transaction summary

A user-facing summary may include:

- Transaction ID.
- Product snapshot and photos.
- Seller and buyer display identifiers.
- Amounts and fees.
- Terms version and acceptance times.
- Payment confirmation time/reference.
- Shipment and delivery timeline.
- Dispute deadline and outcome.
- Payout or refund confirmation.

It must not be described as legal advice or a lawyer-reviewed contract unless that service is actually provided.
