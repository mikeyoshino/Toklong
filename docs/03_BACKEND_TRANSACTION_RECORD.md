# 03 — Backend Transaction Record

## Purpose

The backend transaction record replaces a visible contract drafting/signing workflow. It captures what was offered, what the buyer accepted, how payment and delivery progressed, and why payout or refund occurred.

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

### Seller payout profile

```text
seller_id
payout_provider
payout_beneficiary_reference
beneficiary_name_verification_status
onboarding_status
payout_eligibility_status
risk_flags
created_at
updated_at
```

### Sale link

```text
id
seller_id_or_null_until_joined
initiator_role
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
fulfillment_type
proposed_name
proposed_description
proposed_photo_asset_ids[]
proposed_price_satang
proposed_shipping_fee_satang
currency
proposed_fulfillment_duration_hours
seller_acceptance_deadline_at
buyer_contact_email_reference
created_at
updated_at
```

This is a proposal, not a paid snapshot and not a seller representation. The seller must authenticate, complete or confirm the material facts, and accept the final agreement before checkout is enabled. Prefer a provider/customer reference for the buyer email where practical; do not store refund bank details.

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
shipping_fee_satang
currency
ship_by_duration_hours
supported_carriers[]
prohibited_goods_attestation
transfer_rights_attestation
updated_at
```

The compact seller UI may collect `name`, `description`, and `known_defects` through one agreement-details experience. The backend still normalizes and retains category, condition, defects, and photo asset references so the buyer-facing record and paid snapshot remain explicit. A hidden/default classifier value must never be presented as a seller assertion when it was not stated; use a neutral `as described` condition and preserve the actual agreement text.

Photo capture/upload must produce a managed asset reference. A public image URL may be imported as assistance, but the seller is never required to type a raw photo URL. Saving a photo must work independently from optional AI analysis.

### Paid transaction snapshot

Created atomically when checkout terms are accepted and payment intent is established. Once payment is confirmed, material fields are immutable.

```text
transaction_id
sale_link_id
seller_id
buyer_id
fulfillment_type
product_snapshot_json
product_snapshot_hash
price_satang
shipping_fee_satang
platform_fee_satang
buyer_total_satang
seller_expected_net_satang
currency
ship_by_at
terms_version
terms_snapshot_hash
buyer_acceptance_at
buyer_acceptance_ip_or_risk_reference
seller_acceptance_at
initiator_role
created_at
```

The exact fields retained for IP/device/risk evidence require privacy and legal review.

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

The settlement ledger is an internal append-only liability/reconciliation record, not a user wallet or stored-value balance. It must distinguish buyer funds received, provider fees, TOKLONG fees, refund liabilities, seller payable, seller payout, and corrections. Entries use integer satang and corrections are new entries rather than edits.

### Shipment

```text
transaction_id
carrier_code
tracking_number
tracking_verification_status
submitted_at
first_carrier_scan_at
in_transit_at
delivered_at
carrier_delivery_event_id
delivery_raw_reference
last_checked_at
```

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

`starts_at` must equal trusted `delivered_at` for the default flow.

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
instructions_email_reference
```

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
- Buyer receipt confirmation.
- Digital handoff submission and authorized manual-review resolution.
- Dispute opening.
- Deadline release job.

Each operation needs a stable idempotency key and unique constraint appropriate to the provider/event.

## Deadline processing

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

- Create/deactivate unpaid links.
- Join, complete, accept, or decline a buyer-created offer before payment.
- View their transactions.
- Submit tracking before policy deadline.
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
