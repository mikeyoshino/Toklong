# 05 — Acceptance Tests

## Executable Stripe sandbox smoke test

`./scripts/test-stripe-payment.sh` must complete with
`State: PaidAwaitingShipment` using Stripe Test Mode. The command exercises a
real 1,000 THB PaymentIntent plus the 59 THB `buyer-protection-v2` minimum fee
and signature-verified webhook while leaving seller payout manual and
untouched. Live keys must be rejected before any Stripe request.

`./scripts/test-stripe-refund.sh` must complete with `State: Refunded` using
Stripe Test Mode. It extends the same paid physical-item flow through verified
development delivery, a Buyer `NotAsDescribed` dispute, separate Admin reviewer,
SuperAdmin recommender, and different SuperAdmin approver identities, a real
full Stripe refund for the immutable buyer total, and provider-verified refund
completion. The check must also verify the closed CRM case, applied resolution
action, immutable core audit events, external provider event, and Buyer/Seller
notification intents. It must not call a direct state-mutation or direct
dispute-resolution test endpoint.

The following scenarios are product and domain acceptance criteria. Adapt them into automated unit, integration, and end-to-end tests.

## A. Create and share link

### A0 — Buyer creates a private offer link

**Given** a buyer has already found a seller outside TOKLONG
**And** the buyer supplied first and last name during registration
**And** later signs in with only the registered phone number and six-digit verification step

**When** the buyer records the intended seller phone, product name, fulfillment
type, required details and condition, optional product photo, item price,
and—when physical—the complete delivery address, then creates an offer
**Then** the system creates an unguessable private link in `AWAITING_SELLER_ACCEPTANCE`
**And** stores the normalized intended seller phone and product name
**And** records one durable `buyer_offer_received` notification for only that
seller phone
**And** the fulfillment duration is fixed server-side at 72 hours without a buyer-editable field
**And** a physical offer stores one locked private delivery-address snapshot
whose derived province and postal code are available for seller review
**And** no checkout, payment, refund, or payout instruction is created
**And** the UI states that the seller has not yet accepted
**And** `seller_acceptance_deadline_at` equals activation time plus 24 hours.

### A0.0.0 — Forwarded link does not grant seller authority

**Given** an offer targets one verified seller phone
**When** another authenticated account opens, accepts, or declines its valid
unguessable link
**Then** the API returns forbidden without disclosing the offer
**And** no state transition or seller binding changes.

### A0.0.0.0 — Intended seller receives reusable notifications

**Given** the intended seller has an authenticated account
**When** the buyer creates the offer
**Then** the in-app activity feed shows `ได้รับข้อเสนอซื้อ`
**And** the seller transaction list shows the targeted pending offer as
`ตรวจข้อเสนอ` before the seller ID is bound
**And** while that list is visible it refreshes and shows the offer without
requiring an OS push or manual app restart
**And** its body contains the product name and integer-satang formatted total
**And** opening it routes to the seller offer
**And** a registered device receives the equivalent OS push when the configured
provider acknowledges delivery
**And** the push contains no phone, address, bank, credential, or evidence data.

### A0.0.0.1 — Buyer may resend without weakening seller authorization

**Given** an active offer is waiting for seller acceptance
**When** the buyer closes the creation result and later opens that transaction
**Then** the detail screen shows `รอผู้ขายตอบ` and the exact deadline
**And** the same optional URL can be copied or shared again
**And** another phone account opening that URL remains forbidden
**And** the transaction-list root has no generic clipboard-open action
**And** no new transaction or token is created.

### A0.0.0.2 — Seller opens a phone-targeted offer

**Given** the buyer targeted the seller's verified phone
**When** the seller opens the app or taps the owned notification
**Then** `ขาย` mode shows the pending offer as `ตรวจข้อเสนอ`
**And** opening it routes to the seller offer after phone authorization
**And** the transaction screen has no clipboard-open control.

### A0.0.0.3 — Buy and sell modes never mix lists

**Given** one account has both buyer and seller transactions
**When** the user selects `ซื้อ`
**Then** spotlight, filters, empty state, and list contain buyer transactions only
**And** `+ สร้างดีลซื้อ` is visible.
**And** the page starts with `รายการของคุณ` without a redundant TOKLONG badge
above the title.

**When** the user selects `ขาย`
**Then** spotlight, filters, empty state, and list contain seller transactions only
**And** `ต้องตอบ`, `ต้องส่ง`, `รอรับเงิน`, and `เสร็จแล้ว` are available
**And** no create, copy-link, or clipboard-open action is visible.

**When** either selected mode has no action-required transaction
**Then** `ยังไม่มีรายการ` reserves the same minimum height as a populated
action spotlight so the status filters do not jump vertically.

### A0.0.1 — Anonymous buyer cannot create

**Given** no authenticated buyer account exists
**When** a client attempts to create an offer
**Then** creation is rejected and no transaction is stored.

### A0.0.2 — Returning buyer sign-in does not request or overwrite name

**Given** a buyer account already has a first and last name
**When** the buyer signs in with its registered phone number and valid verification code
**Then** sign-in succeeds without a name field
**And** the saved name remains unchanged.

**Given** no buyer account exists for the verified phone
**When** the user attempts sign-in
**Then** sign-in is rejected with a registration action
**And** no incomplete buyer account is created.

### A0.0.3 — Thai mobile number is validated on the client and server

**Given** a user enters a phone number during registration or sign-in
**When** the value contains letters, more than 10 digits, a landline prefix, or
an unsupported country code
**Then** the mobile field keeps only the first 10 ASCII digits
**And** formats those digits as `092-103-1202` without counting the separators

as phone-number digits
**And** an eleventh digit does not appear in the field
**And** no verification request is sent unless the result is a 10-digit Thai
mobile number beginning with `06`, `08`, or `09`
**And** the API independently rejects an invalid number even when the client is
bypassed.

### A0.0.4 — Used or expired verification code can be replaced

**Given** a verification code was already used, is incorrect, or expired
**When** verification is rejected
**Then** the message names those possible causes without claiming only expiry
**And** the screen provides `ขอรหัสใหม่`
**And** a successful resend replaces the challenge used by the next
verification attempt.
**And** a resend during the cooldown returns `429` plus the remaining wait
instead of a generic service-unavailable error.
**And** after successful verification, string enum values returned by the
mobile API load the transaction list without terminating the app.
**And** a transaction-list loading failure is shown as a retryable inline
message rather than an unhandled application crash.
**And** a photo selected through the native picker is immediately copied into
app-owned draft storage rather than retaining the provider's temporary path.
**And** replacing or successfully uploading a draft photo removes the unused
local copy, while abandoned copies older than 24 hours are cleaned up.

### A0.0.4.1 — AI prepares only a reviewable agreement draft

**Given** an authenticated buyer opens `ให้ AI ช่วยกรอก`
**When** the buyer selects a supported image/chat screenshot or pastes chat text
**Then** the authenticated API rate-limits the request and returns a structured
draft without persisting the source
**And** the app previews the extracted fields before applying them
**And** applying fills only form fields that are still blank
**And** existing buyer-entered values are not overwritten
**And** the buyer can edit every applied value before creating the offer.

**Given** the source contains an instruction, OTP, password, bank/card data, or
reusable credential
**When** extraction runs
**Then** the source is treated as untrusted data rather than an instruction
**And** secret or payment-authentication data is not returned as a transaction
draft.

**Given** AI returns incomplete, low-confidence, or incorrect values
**When** the buyer does not submit the ordinary validated offer form
**Then** no transaction, product photo, snapshot, payment, audit transition, or
notification is created.

### A0.0.4.2 — Buyer offer creation uses three full-page steps

**Given** an authenticated buyer opens offer creation
**Then** the header shows `สร้างข้อเสนอ`, the current step, and a three-segment
progress indicator
**And** `ข้อมูลดีล` contains seller phone, product name, item price, and
optional AI/photo/details
**And** `การรับสินค้า` contains physical/digital choice and the applicable
address
**And** physical fulfillment is selected by default
**And** Digital hides address and shipping content
**And** `ตรวจและส่ง` is a full page rather than a bottom sheet
**And** every step has one primary forward action.

**When** the buyer leaves the product photo empty and submits otherwise valid
details
**Then** the offer is created without an upload
**And** the seller may accept it
**And** the agreement core, paid snapshot, API response, and downloadable
evidence represent the photo as absent without failing hash validation.

**When** the buyer supplies a product photo
**Then** it remains part of the immutable agreement core and paid snapshot.

**When** the buyer advances from a step
**Then** only that step is validated
**And** each error appears beside its field
**And** focus moves to the first invalid field
**When** the buyer reaches `ตรวจและส่ง`
**Then** the page shows the exact offer summary and server-priced cost breakdown
**And** no separate shipment-deadline card is shown
**And** the buyer must select `ใหม่`, `มือสอง สภาพดี`, or `มีตำหนิ`
**And** the defect input is shown and required only for `มีตำหนิ`
**And** only `ส่งข้อเสนอให้ผู้ขาย` creates the offer.

**When** optional description is blank and the buyer submits a valid new or
used-good offer
**Then** the explicit description equals the trimmed product name
**And** known defects equal `ไม่มีตำหนิที่ผู้ซื้อระบุ`
**And** the later immutable paid snapshot remains complete.

### A0.0.4.3 — Buyer sees a server-priced cost preview before creating an offer

**Given** an authenticated buyer enters a valid item price between 1,000 and
30,000 THB with no more than two decimal places
**When** the buyer advances from `การรับสินค้า`
**And** the fresh pricing request for the exact current price succeeds
**Then** the server applies the active versioned Buyer Protection policy using
integer satang
**And** `ตรวจและส่ง` opens only after that exact matching response
**And** `ตรวจและส่ง` is the only price-breakdown surface
**And** a physical item labels the amount `ยอดก่อนค่าจัดส่ง` and the shipping
row `รอผู้ขายเลือก`
**And** a digital item labels the amount `ยอดเมื่อผู้ขายตอบรับ` and the
shipping row `ไม่มีค่าจัดส่ง`
**And** the review states `ยังไม่ตัดเงินในขั้นตอนนี้`
**And** it separates item price, Buyer Protection fee, shipping, and total
while keeping condition and final actions reachable
**And** no sticky total bar, separate pricing sheet, or shipment-deadline card
is present.

**When** the buyer edits the price or fulfillment type, leaves the review, or
leaves the page before an older request returns
**Then** the older response cannot open or replace `ตรวจและส่ง`
**And** no preview is shown until a valid matching server response arrives
after another review action.

**When** pricing fails
**Then** the wizard remains before `ตรวจและส่ง`
**And** the form shows a retryable message
**And** advancing again starts a fresh request.

### A0.0.4.4 — Authenticated home routes by chosen transaction role

**Given** an authenticated account has both buyer and seller transactions
**When** the user taps `ซื้อ` on the authenticated home
**Then** the existing transaction root opens with buying selected
**When** the user returns home and taps `ขาย`
**Then** the same root opens with selling selected
**And** ordinary transaction-root navigation still uses the remembered mode
**And** no seller-created link action is shown.

### A0.0.4.5 — Buyer offer wizard creates only on final submit

**Given** an authenticated buyer opens `สร้างข้อเสนอ`
**When** the buyer completes `ข้อมูลดีล` and `การรับสินค้า`
**Then** no transaction, snapshot, notification, payment, or audit transition
exists
**When** preview fails
**Then** entered values remain and retry is available
**When** the buyer reaches `ตรวจและส่ง` and taps
`ส่งข้อเสนอให้ผู้ขาย`
**Then** exactly one buyer-created offer is created.

### A0.0.4.6 — Dirty wizard exit uses plain warning copy

**Given** the buyer changed an offer value
**When** the buyer attempts to leave from the first step
**Then** the app shows `ยังสร้างข้อเสนอไม่เสร็จ`
and `ถ้าออกตอนนี้ ข้อมูลที่กรอกไว้จะหาย`
**And** `กลับไปกรอกต่อ` preserves values
**And** `ออกจากหน้านี้` discards only the in-memory wizard and temporary
product photo.

**When** any preview is requested or displayed
**Then** no transaction, immutable snapshot, agreement acceptance, notification,
payment, refund, payout, or financial audit event is created
**And** offer creation, seller acceptance, and checkout independently recalculate
and validate their authoritative amounts.

### A0.0.5 — Native mobile forms are visually consistent and accessible

**Given** a user opens authentication, offer creation, payment, fulfillment,
address, tracking, or dispute input on a supported iPhone size
**When** the form is shown, scrolled, focused, validated, or submitted
**Then** screen-edge and safe-area padding remain consistent
**And** every field label, required marker, input, helper message, and validation
message follows the shared form alignment and spacing
**And** entries and selectors render exactly one application-defined outline
without an additional native border inside it
**And** single-line inputs are at least 52 points high
**And** phone-number fields use the shared 16-point input role and 52-point
minimum container height
**And** the verification code is presented as six underlined positions backed
by one focusable numeric input, not six independent fields or an outlined box
**And** verification accepts paste, keeps at most six ASCII digits, and exposes
the iOS one-time-code AutoFill hint
**And** multiline input is at least 112 points high and expands for longer text
**And** every interactive control has a touch target of at least 44 by 44 points
**And** the active input and primary action remain reachable when the keyboard is
shown
**And** longer Thai text and Dynamic Type can wrap without clipping or changing
the transaction behavior
**And** opening buyer or seller transaction detail while its API data is still
loading never supplies a null color to a native gradient or terminates the app
**And** a managed root-relative product-photo path is returned to mobile as an
HTTP or HTTPS URL on the current API host, never as an iOS `file:` URL
**And** buyer progress contains only `สร้างข้อตกลง`, `จ่ายเงิน`, and
`ได้รับของ`
**And** seller progress contains only `ยอมรับข้อตกลง`, `ส่งของ`, and `รับเงิน`
**And** every completed step uses green consistently for its icon, outline,
check marker, and label
**And** every incomplete step remains gray, including the step currently in
progress
**And** the prominent status card, rather than progress color alone, explains
the current action or wait
**And** buyer detail uses the blue role treatment while seller detail uses the
purple role treatment
**And** payout-pending copy tells the buyer that receipt confirmation is done
and tells the seller that transfer to their account is being processed
**And** seller `ส่งของ` remains gray until payment is provider-confirmed.

### A0.0.5.1 — Authentication starts with an explicit choice

**Given** no valid mobile session exists
**When** the native app opens
**Then** the welcome screen appears before either form
**And** it offers `เข้าสู่ระบบ` and `สมัครสมาชิก` as separate actions
**And** the exact TOKLONG mark is centered above
`ซื้อขายออนไลน์ ง่ายขึ้น`
**And** no shield, truck, payment-status, social-login, country, or currency
artwork is shown
**And** sign-in says `เข้าสู่ระบบด้วยเบอร์มือถือ` and asks only for the
registered Thai mobile number
**And** the shared field says `เบอร์มือถือไทย`, uses the selected thin
smartphone icon, and shows `081-234-5678` without `+66`, a flag, or a country
picker
**And** registration asks for only the Thai mobile number before SMS
**And** both paths continue to the same six-digit verification experience
**And** no unavailable social-login action is shown.

### A0.0.5.2 — New registration completes only after verified phone proof

**Given** a Thai mobile number with no buyer or seller account
**When** the user verifies the SMS code in sign-up mode
**Then** no account or authenticated session is created yet
**And** the verified registration proof expires after 15 minutes and is bound
to the same app installation
**And** the app opens `ตั้งค่าบัญชีให้เสร็จ`
**And** that screen shows the verified masked phone read-only
**And** it requires `ชื่อและนามสกุล` and
`อีเมลสำหรับใบเสร็จและการคืนเงิน`
**And** tappable underlined Terms and Privacy links appear immediately before
`สร้างบัญชีและเริ่มใช้งาน`
**And** the sentence states that pressing the button records acceptance
without a separate checkbox
**And** the account, immutable terms acceptance, proof consumption, and mobile
session complete atomically
**And** an exact retry returns a session for the same buyer without a second
account or terms acceptance
**And** a different request key, installation, expired proof, or old terms
version cannot reuse the proof
**And** neither API responses nor logs contain a one-time-code hash or
registration-proof hash.

### A0.0.5.3 — Authentication resume and accessibility are deterministic

**Given** the app starts after interruption
**When** startup state is resolved
**Then** a valid authenticated session always routes to `//home`
**And** without a session, a valid pending registration routes to profile
completion
**And** without either, the app routes to Welcome
**And** an invalid or expired secure-storage record is cleared and does not
crash startup
**And** authenticated push/deep-link initialization does not run for a pending
registration
**And** the brand exposes one `โลโก้ TOKLONG` semantic element
**And** decorative mark and smartphone images are excluded from the
accessibility tree
**And** the six visible code positions remain backed by one focusable numeric
input with paste, deletion, VoiceOver, large text, and iOS one-time-code
AutoFill support.

### A0.0.6 — Returning to the transaction list preserves its layout

**Given** the buyer opens create-offer or transaction detail from the native
transaction list
**When** the buyer navigates back without completing an action
**Then** the root navigation bar remains hidden and the bottom tabs remain visible
**And** the transaction list preserves its previous scroll offset
**And** refreshing the backing collection does not jump directly to the first
transaction card or hide the list header
**And** the pull-to-refresh indicator is shown only for an explicit pull refresh,
not for the normal reload performed when the page appears.
**And** both buyer and seller modes order transactions by creation time from
newest to oldest, including after refresh and within each status filter.

### A0.1 — Seller acceptance enables buyer checkout preparation

**Given** a buyer-created offer in `AWAITING_SELLER_ACCEPTANCE`
**When** the authenticated eligible seller reviews the buyer-specified record, completes only payout/attestations, and accepts
**Then** the transaction moves to `SELLER_ACCEPTED_AWAITING_PAYMENT`
**And** the buyer is notified to review the same unchanged seller-accepted
terms and prepare the buyer-only parcel-protection outcome before payment
**And** the seller is not told that payment has completed
**And** the server stores a valid agreement-core and terms hash
**And** for a physical offer the core includes the destination province and
postal code shown to the seller before acceptance
**And** exactly one seller acceptance references that core hash, authenticated
seller ID, verified-phone session, terms version, and server time.

### A0.1.1 — Both parties accept the same immutable agreement core

**Given** the authenticated seller accepted a buyer-created offer
**When** the authenticated buyer reviews its unchanged agreement core, records
the applicable buyer-only parcel-protection outcome, and accepts after the
matching durable booking is ready
**Then** exactly one seller and one buyer acceptance exist
**And** both reference the same agreement-core and terms hashes
**And** each actor ID and acceptance time matches the corresponding transaction
party
**And** the checkout/product snapshot references that same core hash
**And** neither record stores an OTP code or reusable credential
**And** duplicate acceptance for either role is rejected by the domain and
database uniqueness rules
**And** persisted acceptance rows cannot be updated or deleted through the
application persistence layer.
**And** each authenticated party can download role-shaped JSON evidence
containing the same agreement-core hash and server acceptance times
**And** the v11 buyer copy contains the validated buyer checkout-annex election,
combined customer price, coverage, terms version, annex hash, and
product-snapshot linkage
**And** uncertified unavailable coverage is omitted rather than represented as
zero, and digital fulfillment is marked not applicable without coverage rows
**And** the seller copy omits those buyer-only values, so the two role-shaped
payload hashes intentionally differ
**And** the ordinary transaction screen does not render the raw hash,
terms-version code, or acceptance audit details
**And** a non-party receives no evidence
**And** neither JSON nor printable HTML contains an OTP value or reusable
credential.

**Given** a physical offer
**When** creation omits the full address or supplies a district/sub-district
outside the selected parent hierarchy
**Then** offer creation is rejected before seller notification or acceptance.

**Given** a physical offer whose seller accepted the destination province and
postal code
**When** the buyer opens checkout
**Then** the complete address locked at creation is shown for review
**And** the accepted-offer buyer screen shows the locked full address exactly
once
**And** no address field or saved-address choice is accepted by checkout
**And** changing the address requires a new offer
**And** the buyer sees item price, buyer-protection fee, shipping charge,
final buyer-only parcel-protection price when elected, and exact total before
payment
**And** confirmation and the exact-total payment action follow that breakdown
without a separate pre-payment card
**And** seller offer and seller transaction views do not show the
buyer-protection amount or buyer total
**And** seller views still show applicable shipping information and exact
expected net payout.

**Given** a seller-accepted agreement-core field, party identity, delivery
quote, deadline, or normalized terms changes after seller acceptance
**When** the buyer attempts to accept or pay
**Then** checkout is rejected before creating a buyer acceptance or payment
object
**And** the seller acceptance remains unchanged
**And** the parties must use a new offer.

**Given** only the buyer-only parcel-protection price, limit, expiry, or terms
changes after seller acceptance and before payment
**When** the Worker revalidates the option
**Then** the prior election is superseded and buyer reconfirmation is required
**And** the existing seller acceptance and payment deadline remain unchanged
**And** no PaymentIntent is created until replacement booking succeeds.

### A0.2 — Buyer cannot pay before seller acceptance

**Given** a buyer-created offer still awaiting seller acceptance
**When** the buyer attempts to start checkout or create a PaymentIntent
**Then** the request is rejected
**And** no provider payment object is created
**And** the rejection is audited.

### A0.2.1 — Seller response deadline expires safely

**Given** a buyer-created offer remains in `AWAITING_SELLER_ACCEPTANCE`
**When** the exact 24-hour seller-response deadline is reached
**Then** the transaction moves once to `EXPIRED`
**And** its reason is `SELLER_DID_NOT_RESPOND`
**And** no payment, refund, payout, or fulfillment action is created
**And** seller acceptance is rejected
**And** the buyer sees that no money was collected.

### A0.2.2 — Seller acceptance starts one-hour payment window

**Given** an eligible seller accepts before the response deadline
**When** acceptance succeeds
**Then** `buyer_payment_deadline_at` equals the acceptance time plus one hour
**And** both parties see its exact date and time
**And** the seller is told not to fulfill before confirmed payment.

### A0.2.3 — Buyer payment window expires safely

**Given** an accepted offer has no provider-confirmed payment
**When** the exact one-hour payment deadline is reached
**Then** the transaction moves once to `EXPIRED`
**And** its reason is `BUYER_DID_NOT_PAY`
**And** checkout creation and retry are rejected
**And** the seller is told the item no longer needs to be reserved
**And** a physical offer with an unconfirmed managed booking queues exactly one
provider cancellation operation
**And** the offer remains expired while cleanup retries in the background
**And** a cleanup failure cannot reactivate or extend the offer.

### A0.2.4 — Delayed and late provider events are distinguished

**Given** a payment webhook arrives after the payment deadline
**When** the provider's authoritative confirmation time is at or before the
deadline
**Then** the verified payment may continue to the applicable paid fulfillment
state exactly once
**When** the provider's authoritative confirmation time is after the deadline
**Then** fulfillment remains hidden
**And** the transaction enters `REFUND_PENDING`
**And** refund completion still requires a verified provider event.

### A0.2.5 — Visible mobile detail refreshes deadline state

**Given** the buyer keeps a waiting transaction open in the native app
**When** the seller accepts or an unpaid deadline expires on the server
**Then** the detail screen reflects the new state without requiring navigation
away and back
**And** polling stops when the detail screen is no longer visible.

### A0.3 — Seller revisions require buyer review

**Given** a buyer-created proposal
**When** the seller views the offer before accepting
**Then** product facts, any supplied photo, item price, selected shipping charge
and buyer total where applicable, and the system-fixed 72-hour fulfillment rule
are read-only
**And** any correction requires decline and a new buyer-created offer.

### A1 — Buyer chooses a fulfillment type

**Given** the buyer creates an offer

**When** `สินค้าที่จับต้องได้` is selected
**Then** ship-by copy, address, tracking, and carrier rules apply
**When** `สินค้าดิจิทัล` is selected
**Then** address/tracking are omitted, and the no-auto-release rule is visible.

### A2 — Unsupported item is blocked

**Given** the item category or content matches a prohibited or unsupported rule
**When** the buyer creates the invitation or the seller accepts the final details

**Then** the action is blocked

**And** the reason is shown in plain language
**And** an audit/risk event is written.

## B. Buyer review and payment

### B0 — Physical offer creation resolves, locks, and optionally saves one address

**Given** an authenticated buyer creates a physical offer
**When** the buyer supplies an address line and selects a valid province,
district, and sub-district from the bundled Thai address catalog
**Then** the server validates the complete hierarchy and derives the postal code
**And** offer creation stores a private delivery-address snapshot.

**When** the buyer checks `จำที่อยู่นี้ไว้`
**Then** the profile contains exactly one saved address
**And** a later save updates that address rather than adding another.

**Given** the buyer already has a saved address
**When** physical offer creation opens
**Then** `ใช้ที่อยู่ที่บันทึกไว้` is selected by default.

**Given** a district or sub-district does not belong to its selected parent
**When** offer creation is submitted
**Then** offer creation is rejected server-side and no seller notification is
created.

**Given** a seller views a physical offer before payment
**Then** the seller receives only destination province and postal code
**And** the full address is omitted.

**When** provider-confirmed payment unlocks fulfillment
**Then** the seller may retrieve the full locked address for shipping.

### B0.1 — Seller locks origin, parcel, quote, and may save one origin

**Given** the intended seller reviews a physical offer
**When** the seller supplies a valid Thai origin, weight in grams, and width,
length, and height in centimeters
**Then** the backend derives origin and destination postal codes and returns
only provider quote options matching those inputs
**And** no client-computed shipping fee is trusted.

**When** the seller selects `จำต้นทางนี้ไว้` and accepts with a still-valid quote
**Then** the seller profile contains exactly one saved origin
**And** a later save replaces that profile origin
**And** the accepted transaction retains its own origin, package, quote,
carrier/service, and shipping-fee snapshot
**And** seller acceptance freezes delivery only; it creates neither a parcel-
protection election nor a provider booking
**And** no fulfillment action, paid status, or PaymentIntent is exposed.

**Given** a saved seller origin exists
**When** another physical offer opens
**Then** the saved origin is selected by default
**And** the seller must still enter package measurements and request a quote
for that transaction.

**Given** the origin, destination, measurements, disclosed fee, or quote
reference does not match, or the quote expires before the buyer payment window
ends
**When** the seller attempts acceptance
**Then** acceptance is rejected and a new quote is required.

### B0.2 — Buyer-only optional parcel protection is elected and booked before payment

**Given** an accepted physical offer whose item price is within the certified
included coverage limit
**When** the buyer starts payment preparation
**Then** no choice is shown and no parcel-protection charge is added
**And** the runtime auto-submits `AddProtection=false` and persists `Declined`,
not a distinct included-only election
**And** verified included coverage may appear in status/details
**And** the system creates the matching durable booking and does not prepare a
PaymentIntent until that booking is ready.

**Given** an accepted physical offer above the included limit with a certified
available add-on
**When** the buyer starts payment preparation
**Then** the buyer sees one choice surface with the disclosed maximum and one
combined price
**And** the buyer may accept the add-on or explicitly decline it
**And** neither provider cost, TOKLONG fee split, provider identity, address,
nor option reference is exposed to the buyer UI or any seller projection.

**When** the buyer accepts
**Then** the final buyer total includes exactly the disclosed combined price
**And** that combined price remains visible in the buyer payment breakdown
**And** the maximum is shown only in the choice/details surface
**And** the verified Stripe amount must equal that final integer-satang total.

**When** the buyer declines
**Then** the final buyer total contains no optional-protection charge
**And** the buyer payment breakdown omits the parcel-protection row
**And** the result remains buyer-only and does not alter seller net.

**Given** an over-limit offer whose add-on is unavailable or uncertified
**When** the buyer continues
**Then** no charge or coverage claim is created
**And** the buyer can proceed only with the verified included-coverage outcome,
which may be zero
**And** the buyer payment breakdown omits the parcel-protection row.

**Given** the buyer closes a required choice without deciding
**When** the buyer tries payment again
**Then** the same choice appears once again
**And** no election, booking, or PaymentIntent was created by closing it.

**Given** a buyer election whose price, selected/included limit, expiry, or
terms changes before booking
**When** revalidation runs
**Then** the old booking operation is superseded before provider mutation
**And** the buyer must reconfirm before a new booking or PaymentIntent.

**Given** a matching durable booking completes before the existing payment
deadline
**When** the buyer prepares payment
**Then** PaymentIntent creation is allowed without extending that deadline.

**When** the application prepares that PaymentIntent
**Then** it may create or reuse its idempotent provider reference before
`BeginCheckout` persists buyer acceptance and the v11 annex evidence
**And** a verified payment cannot progress unless the persisted v11 annex hash
and canonical payload pass integrity validation.

**Given** booking fails, times out, is unknown, or returns a mismatch
**When** payment preparation is requested
**Then** no PaymentIntent is created and the outcome is auditable.

**Given** a buyer changes a stored election before a PaymentIntent exists
**When** an outbound booking is already reserved
**Then** the prior booking is durably cancelled before the replacement is booked
**And** historical attempts remain queryable
**And** payment remains blocked through cancel-and-rebook.

**Given** a seller or another buyer requests parcel-protection data
**When** the API or transaction projection is read or written
**Then** access is forbidden and no annex price, limit, terms, option, booking,
or change-request value is disclosed.

**Given** the buyer is offered, accepts, declines, changes, or finds optional
protection unavailable
**When** checkout presentation records analytics
**Then** only the approved coarse event and, for acceptance, the combined
customer price are emitted
**And** no address, phone, provider reference, raw quote, terms text, or
credential-shaped key is emitted
**And** `parcel_protection_checkout_converted` may be recorded only after
PaymentSheet completion and never marks payment successful.

**Given** checked-in SHIPPOP production configuration
**Then** quote, booking, confirmation, return, insurance, and optional-
protection capabilities remain disabled until account-specific certification
evidence passes
**And** parcel weight plus width, length, and height remain required until the
certified provider field/unit evidence says otherwise.

### B1 — Buyer sees material terms before payment

**Given** an active link
**When** the buyer opens checkout
**Then** the buyer sees any supplied agreement photos, the frozen agreement
description including represented condition and defects, item price, shipping
charge, Buyer Protection fee, final optional parcel-protection price when
elected, buyer total, selected service, ship-by deadline, payout trigger,
dispute window, and terms version before confirming payment
**And** the buyer-funded fee uses `buyer-protection-v2` marginal tiers: the
first 5,000 THB at 4%, the portion through 15,000 THB at 3.5%, and the portion
through 30,000 THB at 3%, with a 59 THB minimum and one final round-up to satang
**And** 1,000 / 5,000 / 15,000 / 30,000 THB produce Buyer Protection fees of
59 / 200 / 550 / 1,000 THB respectively
**And** a normal application command rejects an item price above the active
30,000 THB Pilot limit even though the domain technical boundary is 999,999 THB
**And** the seller-funded platform fee is zero and seller expected net equals
item price, without adding buyer-paid shipping, parcel protection, or Buyer
Protection fee
**And** a full refund uses the complete buyer total including that fee.

### B1.1 — PromptPay checkout collects refund contact

**Given** the buyer selects PromptPay
**When** checkout is prepared
**Then** the backend uses the payment-contact email stored on the authenticated
buyer profile for receipts and Stripe refund instructions
**And** the checkout request and screen contain no editable email field
**And** a legacy account without an email is directed to add one from its
account screen before payment
**And** TOKLONG does not request or persist a refund bank-account number.

### B2 — PaymentSheet completion does not mark paid

**Given** PaymentSheet reports completion to the app
**And** no verified success webhook has been processed
**Then** the transaction remains `PAYMENT_PENDING`
**And** the seller does not see any physical or digital fulfillment action.

**Given** a physical transaction has item price and a locked shipping quote
**When** PaymentSheet is prepared or a payment/refund webhook is validated
**Then** the expected amount equals item price plus shipping charge plus
Buyer Protection fee plus the final buyer-elected parcel-protection price in
integer satang
**And** an event for item price alone is rejected as an amount mismatch.

### B3 — Verified payment enables shipment

**Given** a valid provider success event
**When** the webhook signature and idempotency checks pass
**Then** the transaction moves to `PAID_AWAITING_SHIPMENT` once
**And** an immutable paid snapshot exists
**And** it contains separate item price, shipping charge, buyer-only parcel-
protection outcome/price, buyer total, seller-origin snapshot, package
measurements, and selected quote/service
**And** its normalized product and terms documents each match their stored SHA-256 hash
**And** the snapshot seal time equals the authoritative provider confirmation time
**And** the shipping Worker confirms the exact reserved certified-provider
purchase idempotently
**And** the seller receives the provider-issued tracking number, label action,
and exact ship-by notification
**And** the seller cannot replace the managed tracking number manually.

### B4 — Duplicate payment webhook is safe

**Given** the same provider event is delivered twice
**When** both requests are processed
**Then** only one state transition and one audit event occur
**And** no duplicate financial instruction is created.

### B5 — Confirmed payment posts one settlement liability

**Given** a verified provider payment success event
**When** it is processed or replayed
**Then** the append-only settlement ledger records the buyer funds and seller-payable liability exactly once
**And** all amounts are integer satang
**And** no user wallet or spendable balance is created.

## C. Shipment and tracking

### C1 — Managed shipment confirms and issues tracking

**Given** a physical transaction has one unconfirmed SHIPPOP booking
**And** Stripe has provider-confirmed the exact buyer total
**When** the shipping Worker processes the transaction
**Then** it confirms the stored purchase reference once
**And** records the matching courier tracking number through the domain
transition service
**And** the transaction becomes `TRACKING_SUBMITTED`
**And** the authenticated seller may open the 4×6 label full-screen, pinch to
zoom, and invoke native save/share/print actions
**And** the in-app preview removes executable scripts and blocks top-level
navigation while the exported file remains the original provider HTML
**And** a non-seller or a transaction without provider-confirmed payment and
shipping confirmation cannot retrieve the label
**And** the seller and buyer cannot submit or replace carrier/tracking fields
**And** retrying the Worker does not create a duplicate confirmation or audit
event.

### C1.1 — Development carrier simulation remains authorized

**Given** deterministic Development shipping has issued a tracking number
**When** a local test sends an HMAC-signed carrier event with a fresh timestamp
through the internal reconciliation endpoint
**Then** the same state transition and immutable carrier event used by a real
adapter are applied
**And** replaying the event does not duplicate it
**And** an unsigned, stale, mismatched-carrier, or mismatched-tracking request
cannot change state
**And** no mobile client bypass can mark a parcel in transit or delivered.

### C1.2 — Interactive demo advances without client bypasses

**Given** the API runs in Development with demo simulation explicitly enabled
**When** the Development shipping booking is confirmed after simulated payment
**Then** the backend advances one idempotent carrier event per configured
interval from submitted to in-transit and then delivered
**And** the buyer must still explicitly confirm receipt before payout starts
**And** after buyer confirmation creates a manual-bank payout instruction, the
backend records one idempotent payout-completion event
**And** enabling this worker outside Development fails startup
**And** the mobile client has no flag or endpoint that can forge delivery,
receipt confirmation, or payout completion.

### C1.3 — Production tracking polling is authoritative and replay-safe

**Given** an active provider-managed shipment
**When** the Worker polls SHIPPOP and receives `shipping`
**Then** one deterministic carrier event moves the transaction to `IN_TRANSIT`
**And** a repeated unchanged response creates no duplicate external event.

**When** SHIPPOP later returns `complete` with a `POD` timestamp
**Then** that provider event time, not polling time, starts the exact inspection
window
**And** a mismatched tracking number or carrier is rejected.

**Given** the documented SHIPPOP callback has no verifiable signature
**Then** TOKLONG exposes no unsigned SHIPPOP webhook endpoint.

### C1.4 — Unscanned managed shipment is cancelled before refund

**Given** a managed tracking number was allocated but no carrier scan occurred
by `ship_by_at`
**When** deadline processing moves the transaction to `REFUND_PENDING`
**Then** Stripe refund creation remains blocked until the Worker cancels the
SHIPPOP shipment and audits the cancellation
**And** provider cancellation and refund retries remain idempotent.

**Given** SHIPPOP reports a carrier scan before cancellation
**And** the trusted scan occurred at or before `ship_by_at`
**Then** cancellation is skipped and the exception is audited
**And** the automatic missed-shipment refund is stopped before a provider
refund instruction
**And** the transaction returns to payout-blocked tracking review
**And** retrying reconciliation does not duplicate the recovery transition.

### C1.5 — Carrier acceptance is the Seller Protection boundary

**Given** a provider-managed physical shipment
**When** only a label or tracking number exists at `ship_by_at`
**Then** Seller Protection is not eligible
**And** the missed-shipment cancellation/refund path may begin.

**When** a matching trusted carrier scan occurred at or before `ship_by_at`
**Then** the seller handoff is confirmed
**And** Seller Protection is eligible for a later carrier failure
**And** carrier delay, loss, return, or delivery conflict cannot be classified
as seller non-fulfillment
**And** neither payout nor seller compensation starts from the scan alone.

**When** the first trusted carrier scan occurred after `ship_by_at`
**Then** it is retained as evidence
**But** it does not establish timely Seller Protection
**And** the approved late-shipment exception policy controls the outcome.

### C1.6 — Shipping mutations are durable and outcome-safe

**Given** a provider-managed shipping mutation is required
**When** the domain command commits
**Then** one operation with a unique idempotency key is committed atomically
with the transaction intent
**And** two Workers cannot hold a live processing lease for that operation.

**When** a Worker stops after sending a booking request but before recording a
response
**Then** the operation becomes or is recovered as `OUTCOME_UNKNOWN`
**And** it is not replayed until the original provider result is found or
provider idempotency is proven
**And** an unresolved result enters review rather than creating another
booking.

### C1.7 — Trusted delivery time is mandatory

**Given** SHIPPOP reports `complete`
**But** no trusted carrier delivery timestamp can be parsed
**When** reconciliation runs
**Then** the transaction enters tracking review
**And** `delivered_at`, inspection-window start/end, and payout eligibility
remain empty
**And** poll-observation time is not used as delivery time.

### C1.8 — Optional protection and post-payment adjustments preserve paid amounts

**Given** a certified service makes an optional parcel-protection add-on
available after seller acceptance
**When** the buyer accepts it and the exact booking is ready before payment
**Then** the paid snapshot retains the final combined buyer price and immutable
buyer annex evidence
**And** the buyer total includes that price
**And** it is not seller proceeds.

**When** SHIPPOP later reports a fuel, remote, travel, island, weight, or other
surcharge
**Then** the adjustment is append-only
**And** the immutable buyer total and seller net do not change
**And** TOKLONG operational reserve and CRM review handle the difference.

### C1.9 — Carrier exceptions fail closed

**Given** SHIPPOP reports problem, invalid, return, an unknown status, or
mismatched carrier/tracking evidence
**When** reconciliation runs
**Then** automatic payout and automatic refund are blocked
**And** one authorized carrier-exception case is created
**And** replaying the same evidence creates no duplicate case or audit event.

### C1.10 — Authorized return uses a distinct managed shipment

**Given** an authorized dispute resolution requires return
**When** return shipping is created
**Then** its purchase and tracking references are distinct from outbound
shipping
**And** TOKLONG advances the return charge without mutating the paid agreement.

**When** trusted return delivery is confirmed
**Then** the authorized refund path may start
**But** provider-confirmed refund completion is still required.

**When** return delivery cannot be verified
**Then** automatic refund remains blocked for manual review.

### C2 — Unverified tracking does not start the clock

**Given** tracking cannot be verified
**Then** the transaction enters `TRACKING_UNVERIFIED` or review
**And** no `delivered_at` or dispute deadline is created
**And** automatic payout is blocked.

### C3 — Seller-entered delivery is ignored

**Given** a seller claims the item was delivered
**But** no trusted carrier event or buyer confirmation exists
**Then** the system does not start the 72-hour physical inspection window.

### C3a — Shipped or in-transit status does not start the inspection window

**Given** trusted tracking reports only `TRACKING_SUBMITTED` or `IN_TRANSIT`
**And** no trusted delivered event exists
**Then** no `window_starts_at` or `window_ends_at` is created
**And** automatic payout remains blocked.

### C4 — Trusted delivery starts exact deadline

**Given** a verified carrier delivery event at `2026-07-20T14:18:00+07:00`
**When** it is processed
**Then** the transaction enters `DELIVERED_DISPUTE_WINDOW`
**And** `window_ends_at` equals `2026-07-23T14:18:00+07:00`
**And** both parties see that exact deadline.

### C5 — Digital seller handoff never releases payout

**Given** provider-confirmed payment for a digital agreement
**When** the seller records a non-secret handoff statement
**Then** the state becomes `DIGITAL_DELIVERY_SUBMITTED`
**And** no dispute deadline or payout instruction is created.

### C6 — Digital buyer confirmation creates eligibility

**Given** a digital agreement in `DIGITAL_DELIVERY_SUBMITTED` with no dispute/refund/hold
**When** the authenticated buyer confirms receipt
**Then** the transaction becomes payout eligible and creates at most one payout instruction.

### C7 — Digital elapsed time never auto-releases

**Given** a digital agreement in `DIGITAL_DELIVERY_SUBMITTED`
**When** any deadline/release job runs at any later time
**Then** the state remains unchanged and no payout instruction is created.

### C8 — Digital dispute blocks payout

**Given** a digital agreement in `DIGITAL_DELIVERY_SUBMITTED`
**When** the buyer reports non-receipt, inaccessible credentials, or material mismatch
**Then** the transaction enters the dispute path atomically and payout remains blocked.

## D. Receipt confirmation and release

### D1 — Buyer confirms early

**Given** the transaction is in the delivered dispute window
**And** no dispute/refund/hold exists
**When** the buyer selects `ยืนยันว่าได้รับของเรียบร้อย` and confirms the disclosure that seller payout can begin
**Then** the system records the confirmation once
**And** evaluates the transaction as payout eligible
**And** creates at most one payout instruction
**And** the physical delivered-window card shows the trusted
`dispute_window_ends_at` as an exact localized date and time
**And** a digital handoff card shows no automatic deadline
**And** the problem form remains collapsed until the buyer selects the neutral
problem action.

### D2 — Deadline creates payout eligibility

**Given** the verified dispute deadline has passed
**And** no dispute/refund/hold exists
**When** the release job runs
**Then** the transaction moves to `PAYOUT_ELIGIBLE` once
**And** payout creation is queued idempotently.

### D3 — Open dispute blocks deadline release

**Given** a dispute opened before the deadline
**When** the release job runs after the deadline
**Then** no payout instruction is created
**And** the transaction remains in the dispute/resolution flow.

### D4 — Provider payout processing is not completion

**Given** a payout instruction has been accepted for processing
**Then** the transaction is `PAYOUT_PENDING`
**And** the UI does not claim the seller has received funds.

### D5 — Provider confirmation closes payout

**Given** a verified payout-completed provider event or authorized bank reconciliation result
**When** it is processed
**Then** the transaction moves to `PAID_OUT` once
**And** the seller is notified
**And** the transaction summary includes the provider reference.

## E. Disputes

### E1 — Buyer opens dispute before deadline

**Given** the deadline has not passed
**When** the buyer submits a supported reason and required statement
**Then** dispute creation and payout blocking occur atomically
**And** both parties are notified.

### E2 — Buyer cannot open normal dispute after deadline/payout eligibility

**Given** the deadline has passed and the transaction was already made payout eligible
**When** the buyer attempts the normal dispute action
**Then** the action is rejected or routed to an exceptional support process
**And** no state is silently rolled back.

### E3 — AI cannot resolve a dispute

**Given** AI generates a case summary
**When** no authorized human or mutually confirmed resolution exists
**Then** the system cannot create refund or payout instructions from the AI output.

## F. Missed shipment and refund

### F1 — Shipment deadline expires without valid tracking

**Given** a paid transaction has passed `ship_by_at`
**And** no valid shipment exists
**When** the deadline job runs
**Then** the transaction enters the approved overdue/cancellation path
**And** both parties receive a clear next-step notification
**And** payout remains blocked.

### F2 — Refund success requires provider confirmation

**Given** a refund was requested
**Then** the transaction is `REFUND_PENDING`
**And** only a verified provider refund-completed event or authorized
server-to-provider reconciliation moves it to `REFUNDED`.

### F3 — PromptPay refund may require buyer action

**Given** a Stripe PromptPay refund is created
**Then** the transaction remains `REFUND_PENDING`
**When** Stripe reports `requires_action`
**Then** the transaction remains `REFUND_PENDING`
**And** the provider status and available action/instruction timestamps are retained
**And** the buyer is told to check the email from Stripe and provide the
account used for payment directly to Stripe
**And** TOKLONG does not display, request, log, or persist that bank-account number
**And** replaying the same webhook or reconciliation status does not duplicate
the Buyer notification
**When** Stripe reports `pending` after the Buyer responds
**Then** the transaction remains `REFUND_PENDING`
**And** the UI no longer says that Buyer action is currently required
**When** Stripe later returns from `pending` to `requires_action`
**Then** one new Buyer notification is created for the new action cycle
**When** Stripe later reports `succeeded` through a verified event
**Then** the transaction moves to `REFUNDED` once
**And** only a verified matching Stripe `succeeded` refund event or authorized
server-to-provider reconciliation may mark it `REFUNDED`.

**Given** a PromptPay refund is created
**Then** its Stripe `instructions_email` comes from the original PaymentIntent
receipt email
**And** a changed Buyer profile email or client-supplied value cannot redirect
the provider's refund instructions.

### F3.1 — Missed refund webhook is recoverable

**Given** Stripe has completed a full refund but its webhook was not processed
**When** the authorized Worker reads the stored refund directly from Stripe
**Then** the transaction moves to `REFUNDED` only when the transaction metadata,
PaymentIntent, refund reference, complete immutable buyer total, currency, and
`succeeded` status all match
**And** replaying reconciliation or receiving the webhook later does not repeat
the state transition or audit event.

### F4 — Missed fulfillment enters refund processing

**Given** provider-confirmed payment and no valid fulfillment by the exact
72-hour deadline
**When** the deadline worker runs
**Then** it records the missed deadline and enters `REFUND_PENDING` atomically
**And** no seller fulfillment or payout action becomes available.

### F5 — Dispute decision is signed and audited

**Given** an open dispute
**When** an authorized human reviewer submits a fresh HMAC-signed full-refund or
full-payout outcome with a review reference
**Then** the decision and reference are immutable audit events
**And** an unsigned, stale, partial, or AI-generated outcome cannot change money
state.

### F6 — Notifications use a durable outbox

**Given** a state transition requiring a party notification
**When** the notification provider is unavailable
**Then** the transaction commit retains a pending outbox record and retries with
backoff
**And** no message is marked sent without a provider reference.

**Given** the same recipient has more than one lifecycle notification
**When** the activity feed is loaded
**Then** it returns reusable template-derived title, body, and deep-link data
ordered newest first
**And** authorization is based on the authenticated normalized phone.

## G. Immutability and authorization

### G1 — Paid product details cannot be edited

**Given** payment is confirmed
**When** the seller attempts to edit item price, shipping charge,
buyer-only parcel-protection outcome, origin/package snapshot, selected
carrier/service, buyer total, condition, photos, defects, fulfillment deadline,
or terms
**Then** the paid snapshot remains unchanged
**And** the user is instructed to cancel/resolution and create a new link where allowed.

### G1.1 — Snapshot mismatch blocks financial progression

**Given** a version-1 agreement snapshot no longer matches either its stored
hash or the material transaction fields from which it was created
**When** payment confirmation, trusted delivery, receipt confirmation,
deadline release, or payout creation is attempted
**Then** the operation is rejected
**And** no financial state transition is written.

### G1.2 — Existing paid terms are not shortened retroactively

**Given** an existing physical transaction retained the former 168-hour
inspection duration before the 72-hour rule took effect
**When** trusted carrier delivery is later recorded
**Then** its deadline uses the stored 168-hour duration
**And** new offers continue to store 72 hours.

### G2 — Buyer cannot alter tracking

**Given** a buyer-authenticated session
**When** it attempts the seller tracking endpoint
**Then** access is denied and audited.

### G3 — User cannot directly force financial states

**Given** any ordinary client
**When** it calls an endpoint attempting to set `PAID`, `REFUNDED`, or `PAID_OUT`
**Then** the request is rejected
**And** no domain state changes.

### G4 — Digital secrets are not accepted as fulfillment data

**Given** a seller records digital handoff
**When** the handoff statement appears to contain a password, recovery code, private key, or seed phrase
**Then** the submission is rejected before persistence
**And** the UI instructs the seller to deliver through the agreed external channel.

### G5 — Terminal transaction schedules evidence retention

**Given** a transaction reaches `PAID_OUT`, `REFUNDED`, `CANCELLED`, or
`EXPIRED`
**When** the transition is committed
**Then** `retention_starts_at` is the later of terminal time and final
dispute-closure time
**And** `retention_expires_at` is exactly five calendar years later.

### G6 — Legal hold blocks purge

**Given** a transaction whose evidence retention has expired
**When** an authorized signed operation places a legal hold
**Then** an immutable audit event is written
**And** preview and Worker purge exclude that transaction
**And** replaying the same hold reference creates no additional effect.

**When** the matching signed release operation succeeds
**Then** release is audited
**And** the transaction becomes eligible for the next Worker pass.

### G7 — Retention purge minimizes then deletes

**Given** a due terminal transaction without a legal hold
**When** the retention Worker executes
**Then** the complete transaction aggregate and its personal/evidence children
are deleted atomically
**And** one minimized financial record retains only transaction ID, terminal
state, integer money, currency, provider references, retention dates, and purge
time
**And** it contains no party, contact, address, product, photo, snapshot,
acceptance, or agreement-hash data.

**And** managed photo deletion is queued in the same commit
**And** a failed file deletion remains queued for retry
**And** absolute managed-media URLs cannot escape the configured storage
directory.

**Given** that minimized record reaches seven years after terminal time
**When** the Worker executes
**Then** the minimized record is deleted.

### G8 — Remote clients cannot execute retention deletion

**Given** an internal or ordinary HTTP client
**When** it searches for an endpoint to execute retention deletion
**Then** no such endpoint is mapped
**And** signed HTTP operations are limited to preview and legal-hold
management.

## H. Accessibility and reduced motion

### H1 — Landing walkthrough works without autoplay

**Given** reduced motion is enabled
**Then** autoplay is disabled or effectively paused
**And** the user can navigate all four scenes with controls
**And** the static four-step section communicates the full flow.

### H2 — Mobile startup logo respects motion and routing

**Given** the native mobile app starts from a cold launch with normal motion
**When** the static launch surface hands off to the app
**Then** the two Transaction Rail layers assemble, the Mint node confirms once,
and the TOKLONG wordmark enters in exactly 1.2 seconds
**And** authentication lookup occurs concurrently
**And** the intro is not placed in Shell history
**And** the animation does not replay on foreground resume.

**Given** the platform requests reduced motion
**When** the app starts
**Then** the completed static mark appears immediately
**And** no animation-duration delay is added
**And** the same authenticated or unauthenticated route is selected.

**Given** startup session lookup fails
**When** the animation or static reduced-motion presentation completes
**Then** the app opens the unauthenticated welcome route
**And** no credential, session content, payment state, or success claim is
displayed.

### Direct SHIPPOP checkout ordering

**Given** a physical offer has a recorded buyer election and shipment intent
**When** the buyer requests payment with a new idempotency key
**Then** one unconfirmed SHIPPOP booking is validated and committed before
Stripe is called
**And** replaying the key does not call SHIPPOP twice.

**Given** direct booking times out, mismatches, is rejected by admission
control, or exceeds three attempts
**When** checkout returns
**Then** no PaymentIntent is created and the response contains only a stable,
consumer-safe retry or reconfirmation code.

**Given** Stripe later sends a valid signed success webhook
**Then** exactly one `ConfirmOutbound` operation is committed
**And** replaying the webhook does not add another confirmation.
