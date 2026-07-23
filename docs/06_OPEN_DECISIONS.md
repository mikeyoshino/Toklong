# 06 — Open Decisions

Items below require explicit product, operations, legal, payment-provider, logistics, or risk decisions. AI agents must not invent final values.

## Brand and positioning

- Final product/company name and domain.
- Whether “protected payment link” or another phrase is clearest in Thai.
- Final public wording for `ลิงก์ข้อตกลง`; it must not imply a lawyer-drafted contract, marketplace listing, or legally approved escrow.
- Whether to mention Facebook/Marketplace in public copy and trademark disclaimers.

## Payment provider and fund flow

- Working assumption from the 2026-07-23 product discussion: Stripe Thailand Direct Payment collects buyer PromptPay into TOKLONG's Stripe account, Stripe settles to a dedicated TOKLONG bank account, and TOKLONG initiates seller payout through an approved bank payout/bulk-transfer service. Obtain and retain Stripe's written approval for the complete third-party-seller and delayed-payout use case.
- Whether provider supports delayed seller payout, separate charges/transfers, safeguarded funds, refunds after shipment, and payout holds.
- Who is merchant of record, seller of record, or payment facilitator in the final structure.
- Provider onboarding and KYC requirements for sellers.
- Chargeback allocation and negative-balance handling.
- Payout timing and bank cutoff behavior.
- Approved legal/product language; whether any escrow terminology is permitted.
- Selected payout bank/API, beneficiary-name verification, idempotency mechanism, completion-status source, rejected-transfer handling, and reconciliation SLA.
- Required operating reserve and Stripe-balance process for PromptPay refunds after Stripe has settled funds to TOKLONG's bank.

## Transaction initiation

- Recommended direction: support both seller-created agreement links and buyer-created offer links; both converge before checkout.
- Buyer-created MVP sequence: buyer proposes and shares → seller authenticates/completes seller facts/accepts → buyer reviews final terms and pays → seller fulfills.
- Confirm seller acceptance deadline; international comparators commonly use a short explicit offer window such as 24 hours.
- Confirm buyer payment deadline after seller acceptance.
- Decide whether the seller may revise a buyer proposal in-product or must decline and ask for a new offer. The MVP must not become bidding or in-app negotiation.
- Decide which entry CTA leads the landing page experiment: `สร้างข้อเสนอซื้อ` or `สร้างลิงก์ข้อตกลง`.

## Fees and taxes

- Buyer fee, seller fee, or mixed fee model.
- VAT treatment and invoicing party.
- Withholding-tax workflow, if relevant.
- Refund of platform/payment fees.
- Minimum and maximum transaction amounts.

## Shipping

- Supported carriers and tracking aggregator.
- Definition of trusted delivery event for each carrier.
- Handling of pickup points, locker delivery, recipient refusal, failed delivery, return-to-sender, and carrier status correction.
- Ship-by default: 48, 72, or other hours.
- Whether same-day/local courier deliveries are supported.
- Insurance and declared-value policy.

## Digital fulfillment

- Initial allow-list of transferable digital items/rights and excluded platforms.
- Platform-specific account-transfer terms and evidence requirements.
- Manual-review roles, service levels, and outcomes when the buyer does not confirm.
- Secure external handoff guidance; TOKLONG must not collect reusable credentials.
- Digital dispute/evidence requirements, account-recovery risk, and post-confirmation exception policy.
- Whether any provider supports this fund flow and category risk.

## Seven-day window

- Confirm fixed 168 hours versus calendar days.
- International benchmark observed on 2026-07-23: Trustap 24 hours, Vinted 48 hours, Wallapop 48 hours, and Mercari 72 hours after confirmed delivery. The current TOKLONG MVP remains 168 hours until product, risk, legal, and operations explicitly approve a change.
- Timezone behavior.
- Whether seller/category risk can extend the window.
- Required reminder schedule.
- Whether buyer confirmation is reversible for a short period.
- Exceptional support route after deadline.

## Dispute operations

- Supported reason codes.
- Required evidence by category.
- Human reviewer roles and service-level targets.
- Seller response deadline.
- Return shipping responsibility and label generation.
- Authenticity checks for branded/luxury goods.
- Whether partial resolutions are allowed.
- Appeal process.

## Identity and trust

- Minimum seller identity verification.
- Buyer verification threshold.
- Displayed identity signals and privacy limits.
- Account age, transaction limits, velocity limits, and risk scoring.
- High-value review thresholds.

## Product categories

- Initial allow-list.
- Prohibited and restricted goods policy.
- Electronics serial-number requirements.
- Branded-goods authenticity requirements.
- Maximum item value for first launch.

## Consumer terms and platform obligations

- Final terms of service and transaction terms.
- Cancellation/refund terms.
- Complaint and dispute channels.
- Data retention and deletion schedules.
- Digital-platform-service notification and reporting obligations.
- Consumer-protection disclosures.

## Launch operations

- Customer support hours and emergency fraud channel.
- Manual reconciliation schedule.
- Monitoring and alerting owners.
- Incident communication template.
- Payout failure and seller bank-account correction workflow.
