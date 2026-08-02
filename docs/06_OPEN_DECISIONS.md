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
- Decided implementation baseline: Thai mobile OTP uses ThaibulkSMS OTP v2
  directly through the production adapter. Public pricing currently starts at
  0.15 THB per SMS; the actual contracted tier, sender approval, throughput,
  delivery SLA, PDPA/DPA terms, and production credentials remain go-live
  gates. Sources checked 2026-07-27:
  https://www.thaibulksms.com/pricing-b/ and
  https://developer.thaibulksms.com/
- Bank payout remains behind the provider-neutral HTTPS API boundary with a
  stable idempotency key and signed completion reconciliation. Selecting the
  bank/bulk-transfer product, exact contract, credentials, beneficiary-name
  verification, fees, rejection mapping, and certification remains open; no
  bank-specific completion is inferred from an accepted instruction.
- Decided commercial Pilot baseline: `buyer-protection-v2` is buyer-funded and
  uses marginal tiers: first 5,000 THB at 4%, the portion through 15,000 THB at
  3.5%, and the portion through 30,000 THB at 3%, with a 59 THB minimum and no
  separate fee cap. The active range is 1,000–30,000 THB; the domain technical
  maximum is 999,999 THB and is not an active or legal-safe-harbor limit.
  Shipping is exact pass-through and the seller receives the full item price.
  The complete business and technical rule is in
  `docs/17_PRICING_AND_TRANSACTION_LIMITS.md`. Revalidate the schedule against
  actual loss ratio, chargebacks, bank payout fees, support cost, VAT
  treatment, and provider approval before live activation.
- Select and approve the production mobile push topology. iOS requires an
  Apple Push Notification service key/certificate, production app identifier,
  and signed push entitlement; Android requires a Firebase project, app
  configuration, and service-account-backed FCM sender. The code exposes a
  reusable device-registration and notification-gateway boundary and registers
  iOS APNs tokens, but no real remote push may be claimed until those external
  credentials and the Android Firebase client configuration are supplied and
  real-device delivery is verified.
- Required operating reserve and Stripe-balance process for PromptPay refunds after Stripe has settled funds to TOKLONG's bank.
- Complete one real Stripe Test Mode PromptPay payment/refund ceremony using an
  email address eligible to receive sandbox mail, then submit Stripe's approved
  test bank details through Stripe's hosted instruction flow. Automated tests
  already cover signed `requires_action → pending → requires_action →
  succeeded` events and reconciliation, but they do not claim that a synthetic
  webhook proves email delivery or the hosted bank form.
- Production approval and supply-chain review for the third-party
  `XDev.Stripe.PaymentSheets` MAUI bindings, because Stripe does not publish an
  official .NET MAUI SDK. Keep the wrapper and native Stripe artifacts pinned
  until that review is complete.
- The pinned wrapper compiles for the iOS simulator but currently produces
  duplicate bundle-resource warnings. Its Android dependency graph also resolves
  newer AndroidX packages outside several declared version ranges under .NET 10
  MAUI. Resolve these warnings and complete real-device payment tests before any
  store release.
- App Store and Play policy treatment for every allow-listed digital item.
  Do not enable in-app Stripe payment for a digital category until the applicable
  platform policy and legal review explicitly permit it.

## Transaction initiation

- Decided for MVP: buyer-first only. Buyer supplies first/last name once during registration, later signs in with only the registered phone and verification code → buyer proposes and shares → seller authenticates and accepts or declines without editing → buyer reviews final terms and pays → seller fulfills.
- Decided for MVP: both parties use electronic click acceptance backed by their
  verified-phone sessions and append-only evidence referencing one shared
  agreement-core hash. This is not presented as a certificate-backed digital
  signature.
- Decided for MVP: either transaction party can download role-shaped hashed
  JSON evidence with server acceptance times and a readable role-shaped HTML
  version for printing. Both roles receive the same agreement-core hash; only
  the buyer receives the validated buyer checkout-annex values, so payload
  hashes intentionally differ. Uncertified unavailable coverage remains unknown
  rather than zero, and digital evidence contains no parcel-protection coverage
  claim.
- Decided for MVP: physical agreement core shows and locks destination province
  and postal code before seller acceptance; the private full-address
  fulfillment annex is selected and locked at offer creation, reviewed without
  editing at checkout, and disclosed to the seller only after confirmed
  payment.
- Decided for MVP: do not persist IP addresses, device fingerprints,
  advertising IDs, hardware IDs, or precise location as agreement evidence or
  separate security evidence. Transient OTP rate limiting uses only a
  per-process HMAC partition key and never logs or stores the raw address.
- Decided for MVP: retain the transaction agreement, acceptance, fulfillment,
  payment-state, audit, and dispute evidence for five years from the latest
  terminal transaction or final dispute-closure time. An authorized legal hold
  pauses deletion only for the affected record. Accounting/tax records follow
  their separately approved schedule, up to seven years where required.
- Implemented: terminal transitions calculate the five-year expiry; the Worker
  purges due transaction aggregates and later removes minimized financial
  tombstones at year seven. Signed internal operations support preview and
  audited legal-hold placement/release. Remote deletion execution is not
  exposed.
- Decided for MVP: seller acceptance expires exactly 24 hours after offer
  activation.
- Decided for MVP: provider-confirmed buyer payment is due exactly one hour
  after seller acceptance.
- Decide whether the seller may revise a buyer proposal in-product or must decline and ask for a new offer. The MVP must not become bidding or in-app negotiation.
- The single MVP entry CTA is `สร้างข้อเสนอซื้อ`.

## Fees and taxes

- Implemented working rule: for physical offers the buyer pays item price plus
  the shipping charge selected before seller acceptance and, only when elected,
  the buyer-only combined parcel-protection price. Seller expected net remains
  item price minus the disclosed platform fee; neither buyer charge is seller
  proceeds. Legal, tax, provider, and final commercial approval of this
  allocation remains required.
- Buyer fee, seller fee, or mixed fee model beyond that working shipping
  allocation.
- VAT treatment and invoicing party.
- Withholding-tax workflow, if relevant.
- Refund of platform/payment fees.
- Rates and activation gates above the decided 30,000 THB Pilot maximum; no
  higher-value rate may be extrapolated from the Pilot tiers.

## Shipping

- Implemented: SHIPPOP is the production adapter boundary. The seller supplies
  one saved-or-new Thai origin and
  transaction-specific parcel weight/dimensions, requests a quote, and locks one
  carrier/service before accepting. Seller acceptance freezes these delivery
  facts only. Weight plus width, length, and height remain required until
  account-specific evidence certifies different provider fields/units. Quote
  validation is server-side and the paid tracking carrier cannot silently
  change.
- Implemented: after seller acceptance, the buyer alone prepares, accepts, or
  declines optional parcel protection. A within-limit verified included outcome
  is auto-submitted as `AddProtection=false` and persisted as `Declined`, not
  as a separate included-only election. The durable buyer annex records the
  election and internal split; buyer presentation exposes the combined price at
  choice and in the payment summary when a paid add-on is accepted, while the
  maximum remains at choice/details and seller projections expose none of it.
  The payment request creates and validates the matching unconfirmed booking
  synchronously before PaymentIntent provider preparation. That booking does not alter
  the one-hour payment deadline. Verified payment queues `ConfirmOutbound`,
  which allocates tracking and enables a 4×6 label. Provider-managed
  transactions have no manual tracking entry.
- Production direct booking remains disabled until SHIPPOP supplies evidence
  for unconfirmed-booking expiry/cost, TOKLONG-reference lookup or idempotency,
  account rate limits, and latency under the approved concurrency. The
  production configuration requires a non-empty certification reference before
  this feature can be enabled.
- Implemented security decision: do not consume SHIPPOP callbacks because the
  documented webhook payload has no verifiable signature. Use server-side
  polling until SHIPPOP supplies and contractually documents an authenticated,
  replay-safe callback.
- Implemented refund ordering: an unused confirmed shipment is cancelled before
  the Stripe refund is created; a discovered carrier scan is audited and routed
  as an operational exception.
- Approved production design: provider-changing outbound and return calls use
  durable shipping operations with unique idempotency keys, leases, and
  outcome-unknown reconciliation. Booking is never replayed blindly after a
  timeout.
- Approved production design: consumer shipping uses four milestones
  (`เตรียมจัดส่ง`, `ขนส่งรับพัสดุแล้ว`, `กำลังจัดส่ง`, `ส่งถึงแล้ว`) plus
  detailed carrier events. SHIPPOP completion without a trusted delivery
  timestamp enters review and cannot start the 72-hour window.
- Approved production design: launch is drop-off only. `EMST`, `FLE`, `KRYX`,
  and `KRYS` remain individually disabled until account-specific certification
  proves quote, booking, confirmation, cancellation, tracking/POD, label,
  duplicate/replay, rate-limit, protection availability/limits/terms, and
  parcel-field/unit behavior. Checked-in flags stay off until this evidence is
  recorded.
- Approved production design: optional protection is buyer-funded only when
  elected; no full-value coverage is assumed. Provider cost and TOKLONG fee
  split are internal. TOKLONG absorbs post-payment surcharge from operational
  reserve without mutating the paid snapshot or seller net.
- Approved production design: carrier exceptions and insurance claims enter
  authorized CRM review and block automatic payout/refund. An approved return
  creates a distinct provider-managed return shipment; TOKLONG advances return
  shipping and refund waits for trusted return delivery or authorized manual
  resolution.
- Development uses a deterministic implementation of the same quote,
  reservation, confirmation, label, tracking, and cancellation interfaces. It
  is not SHIPPOP and is unavailable by default outside Development.
- The implementation allow-lists `THAIPOST`, `FLASH`, and `KERRY` for SHIPPOP
  service codes `EMST`, `FLE`, `KRYX`, and `KRYS`.
- Still required before live launch: execute the commercial SHIPPOP contract,
  provision/fund the production account, obtain live credentials, validate the
  enabled service codes and billable-weight/pricing behavior against that
  account, and run provider sandbox/live certification.
- Provider launch blocker: confirm whether SHIPPOP offers an idempotency
  guarantee or TOKLONG-reference lookup for `booking`, and repeated-call
  guarantees for `confirm` and `cancel`.
- Provider launch blocker: confirm how to cancel an unconfirmed booking without
  a courier tracking code and whether such bookings naturally expire.
- Provider launch blocker: certify the trusted delivery status and timestamp,
  enabled drop-off behavior, rate limits, add-on field names, included/maximum
  limits, integer-satang price units, terms/code, post-election exact booking,
  lookup/replay, cancellation before first scan, surcharge fields, claims SLA,
  and required parcel fields/units separately for each service.
- Confirm per carrier whether a counter may scan an official provider QR from
  a phone screen and whether a printed 4×6 label remains mandatory. Until
  confirmed for a service, `CounterQrEnabled` stays false and mobile retains
  only the seller label-download fallback; it never synthesizes a SHIPPOP QR.
- Counter-QR response observation is discovery only. A candidate field in a
  booking or confirmation response does not enable a service. Production
  remains blocked until SHIPPOP documents the official counter purpose, exact
  representation, expiry/rotation behavior, a read-only post-confirmation
  retrieval path with safe repeated-read semantics, and controlled counter-
  acceptance evidence for the specific account and service code.
- Handling of pickup points, locker delivery, recipient refusal, failed delivery, return-to-sender, and carrier status correction.
- Decided for MVP: ship-by is fixed at 72 hours after provider-confirmed payment and is not user-configurable.
- Whether same-day/local courier deliveries are supported.
- Optional-protection launch blocker: do not enable a service until the
  account-specific SHIPPOP evidence confirms add-on field names, included and
  maximum limits, integer-satang price units, terms/code, post-election booking
  support, and safe replay/lookup. If the provider cannot return a separable
  option after buyer election, leave optional protection disabled and use only
  any certified included coverage. No full-value coverage assumption is
  approved.
- Optional-protection disclosure blocker: do not enable a Production service
  until the approved buyer terms and exclusions document is versioned,
  authenticated, and linked from the choice flow. The current placeholder is
  not a disclosure and Production validation rejects enablement without this
  capability.

### Seller Protection and failed delivery

- Decided for the implementation boundary: for provider-managed physical
  shipments, only a matching trusted carrier acceptance scan at or before
  `ship_by_at` proves timely seller handoff. Label allocation, a tracking
  number, seller statement, receipt image, or client event does not.
- Decided safety behavior: if a timely scan is discovered while cancelling an
  apparently unscanned shipment, stop the automatic missed-shipment refund
  before provider instruction and route to payout-blocked tracking review.
- Open: carrier status taxonomy for lost, delayed, failed delivery,
  recipient-refused, wrong-address, return-to-sender, and delivered-but-denied
  for each enabled service.
- Open: who funds seller compensation after a covered carrier loss or damage,
  the maximum protected item value, evidence requirements, exclusions,
  deductible, and claim SLA.
- Open: allocation of outbound and return shipping when failure is caused by
  buyer-supplied address, buyer absence/refusal, carrier fault, or seller
  packaging.
- Until those commercial/provider decisions are approved, Seller Protection
  means protection from an incorrect automatic “seller did not ship”
  classification. It does not promise seller payout or buyer refund in every
  carrier exception.

## Digital fulfillment

- Initial allow-list of transferable digital items/rights and excluded platforms.
- Platform-specific account-transfer terms and evidence requirements.
- Manual-review roles, service levels, and outcomes when the buyer does not confirm.
- Secure external handoff guidance; TOKLONG must not collect reusable credentials.
- Digital dispute/evidence requirements, account-recovery risk, and post-confirmation exception policy.
- Whether any provider supports this fund flow and category risk.

## 72-hour physical inspection window

- Decided for MVP on 2026-07-25: fixed 72 elapsed hours after trusted carrier-confirmed delivery, not three calendar days.
- International benchmark observed on 2026-07-23: Trustap 24 hours, Vinted 48 hours, Wallapop 48 hours, and Mercari 72 hours after confirmed delivery. TOKLONG selected 72 hours to give buyers time to inspect while bounding the seller's wait.
- Timezone behavior.
- Whether seller/category risk can extend the window.
- Required reminder schedule.
- Whether buyer confirmation is reversible for a short period.
- Exceptional support route after deadline.

## Dispute operations

- Decided CRM baseline: workforce authentication uses Microsoft Entra ID Free
  with Security Defaults for development and the internal pilot. Entra ID P1
  plus CRM-specific Conditional Access is a production gate before CRM may
  execute real refund or payout decisions.
- Decided CRM baseline: application roles are `Admin` and `SuperAdmin`;
  consumer Buyer/Seller identity never grants CRM access.
- Decided CRM baseline: every financial dispute resolution requires an Admin
  recommendation and approval by a different SuperAdmin, regardless of amount.
- Proposed pending operations approval: assignment within four business hours,
  first human review within one business day, a 48-elapsed-hour evidence
  deadline, Admin recommendation within one business day after evidence closes,
  and SuperAdmin approval/return within two business days after the case is
  ready for approval.
- Proposed reason/evidence baseline is documented in
  `docs/13_CRM_DISPUTE_OPERATIONS.md`; operations, legal, privacy, and provider
  owners must approve it before production.
- Return shipping responsibility and label generation.
- Authenticity checks for branded/luxury goods.
- Decided MVP baseline: partial resolutions remain disabled. Supported binding
  financial outcomes are full refund or full payout.
- Appeal process.
- Number and named owners of the initial independent SuperAdmin accounts.
- Enhanced high-risk review ownership for counterfeit, prohibited-goods,
  carrier-conflict, fraud, and legal-hold cases.

## CRM architecture and access

- Decided MVP baseline: `Toklong.Crm` is a separately deployed Blazor
  Interactive Server application.
- Decided MVP baseline: CRM uses the existing PostgreSQL database with a
  separate `crm` schema, `CrmDbContext`, migration history, cookie, Data
  Protection application name, and database principal.
- Decided MVP baseline: CRM workflow never replaces `TransactionState`, and
  financial state changes use authorized domain commands rather than direct
  database edits.
- Decided production gate: at least one Admin and two independent SuperAdmins
  must exist before CRM financial approval is enabled.
- Open: production hostname, network-access boundary, Entra tenant/application
  identifiers, database grants, and incident-alerting channel.

## Identity and trust

- Minimum seller identity verification.
- Decided MVP baseline: buyer phone verification plus first and last name before offer creation. Higher-value KYC thresholds remain open.
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
- Digital-platform-service notification and reporting obligations.
- Consumer-protection disclosures.

## Launch operations

- Customer support hours and emergency fraud channel.
- Manual reconciliation schedule.
- Monitoring and alerting owners.
- Incident communication template.
- Payout failure and seller bank-account correction workflow.
