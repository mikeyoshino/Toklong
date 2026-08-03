# 10 — Native Mobile App

## Purpose

`Toklong.Mobile` is the native Android and iOS client for the existing buyer-first
transaction flow. It uses .NET MAUI XAML and native controls. It is not Blazor
Hybrid, does not embed the website in a WebView, and does not introduce a second
transaction model.

The project also targets Mac Catalyst so the same native XAML experience can be
run directly on a Mac for development and desktop access. Android and iOS remain
the primary mobile release targets.

## Authentication entry

An unauthenticated launch starts on a lightweight welcome screen, not directly
inside a phone-number form. It uses the existing TOKLONG blue, purple, mint, and
white palette, a compact in-app brand lockup, and two explicit actions:
`เข้าสู่ระบบ` and `สมัครสมาชิก`. These paths stay separate so returning users
are never asked for their name and new users understand that registration is a
one-time step. Do not show social-login buttons unless the corresponding
identity provider and account-linking behavior are actually implemented.

Both paths use the same shared phone field, layout tokens, security note, and
primary-action style. The verification screen uses one focusable numeric input
behind six visible digit positions with underlines. It accepts paste, keeps only
the first six ASCII digits, requests the iOS one-time-code AutoFill hint, and
keeps `ขอรหัสใหม่` available. It must not render six independent editable
fields because that makes paste, AutoFill, deletion, and accessibility harder.
Consumer copy says `รหัสยืนยัน`, not `OTP`.

The first mobile slice concentrates on finding the next action across multiple
transactions. A person may be the buyer in one transaction and the seller in
another without changing accounts or entering a separate seller area.

## Navigation

The authenticated root action bar contains `ซื้อ | + สร้างดีล | ขาย` in that
order. Buy and Sell are separate fixed-role transaction workspaces. The raised
center action has the accessible name `สร้างข้อเสนอซื้อ`; it always opens the
buyer product-type choice and never creates a marketplace or seller listing.
It creates one private buyer offer for an item already agreed outside TOKLONG,
and only after final submission.

The root workspaces do not show a native navigation bar. `กิจกรรม` and `บัญชี` are
top-right actions and open as pushed Activity and Account pages. Pushed screens
show native Back navigation and hide the authenticated root action bar.
Authentication screens use the same 44-point custom back action beside the
brand lockup so their layout
does not shift between sign-in, registration, and verification. Returning must
restore the root chrome and preserve the transaction-list scroll offset;
reloading the collection must not jump past its header.

## Multiple-transaction model

Each transaction card shows:

- `ซื้อ` or `ขาย`;
- product description, counterparty, and total;
- plain-language state;
- an exact date and time when an action has a deadline;
- one primary next action.

`AWAITING_SELLER_ACCEPTANCE` uses the server-provided 24-hour exact deadline and
shows the buyer that the phone-targeted seller has not answered yet. It exposes
optional copy/share controls for the same invitation URL without treating
sharing as a required action. Seller acceptance starts
a server-provided one-hour payment deadline. Expired cards distinguish
`ผู้ขายไม่ได้ตอบ`, `หมดเวลาชำระ`, and `ผู้ซื้อไม่ได้จ่าย` from the persisted
expiration reason; the app never derives those deadlines from local time.
The visible transaction-detail screen refreshes from the API every five seconds
while waiting for either party or for payment confirmation, and stops polling
when the page is no longer visible.

The root action bar selects the fixed `ซื้อ` or `ขาย` workspace; there is no
mixed `ทั้งหมด` role and ordinary authenticated entry always opens `ซื้อ`.
Buyer status filters are `ทุกสถานะ`, `ต้องทำ`,
`กำลังดำเนินการ`, and `เสร็จแล้ว`. Seller filters are `ทุกสถานะ`, `ต้องตอบ`,
`ต้องส่ง`, `รอรับเงิน`, and `เสร็จแล้ว`.

Action-required transactions appear first, then in-progress transactions, then
completed transactions. Within action-required items, the nearest deadline comes
first. This sorting is deterministic and covered by unit tests.

## Buyer and seller actions

Buyer:

- create a complete offer with intended seller phone, product name, and
  product/defect photos;
- wait for the offer to appear for the phone-targeted seller;
- after seller acceptance, review the unchanged record and pay;
- track physical delivery or review digital handoff;
- confirm receipt or report a problem.

Seller:

- review a buyer-created offer without editing it;
- accept or decline;
- only after provider-confirmed payment, download the provider-issued shipping
  label for a physical item or complete the applicable digital handoff;
- view payout progress.

## Notifications and targeted seller invitations

Creating an offer records an in-app notification only for the normalized seller
phone supplied by the buyer. Possession of an invitation link is not sufficient
authorization: every seller offer read, accept, and decline request rechecks the
authenticated phone on the server.

The pushed `กิจกรรม` page loads the authenticated notification inbox rather
than static sample data. Notification templates provide a reusable title, body,
and deep link for offer, payment, delivery, dispute, refund, and payout events.
The first invitation displays `ได้รับข้อเสนอซื้อ` and
`[ชื่อสินค้า] · ฿[ยอดรวม]`. Tapping it opens the seller-offer screen; other
transaction events open transaction detail. The main transaction list also
includes an unaccepted offer targeted to the session's verified seller phone
and presents `ตรวจข้อเสนอ`; it does not wait for `seller_id`, which is bound
only after acceptance. While the transaction screen is visible, it refreshes
from the API every five seconds so the pending offer appears during local
two-device testing even when no push provider is configured.

Device registration occurs only after authentication. The app sends a random
installation ID, platform, and opaque push token to the provider-neutral mobile
API; logout unregisters the installation best-effort. iOS registers with APNs
on a signed real device and supports foreground banners plus notification-tap
routing. Android FCM registration and production APNs/FCM sender credentials
remain release prerequisites, not simulated success paths.

In Development with `Notifications:Enabled=false`, no APNs/FCM banner is
claimed or sent. The durable in-app activity record and targeted pending-offer
list remain available through the API.

For physical fulfillment, the seller selects a SHIPPOP quote before accepting
the offer. After provider-confirmed payment, the mobile transaction response
contains `ShippingManagedByProvider=true`, the read-only carrier tracking
number, whether the label is available, and seller-only normalized
`CounterQrStatus`. The app shows `กำลังเตรียม QR เคาน์เตอร์` immediately for
the eligible paid managed path. Ready shows a large official PNG on white quiet
space, carrier/tracking/ship-by, optional expiry, and `แสดงเต็มหน้าจอ`; Error
shows `ลองโหลด QR อีกครั้ง`. The full-screen page keeps the screen awake and
restores the prior setting when hidden; it does not change brightness or read
the encoded payload aloud. `ดาวน์โหลดใบปะหน้า` opens the unchanged 4×6 HTML
attachment directly in the native share/save/print sheet. There is no outbound
label preview or WebView, and the manual tracking form remains hidden. Both QR
and label endpoints are seller-only and no-store; neither proves carrier
custody. Page exit cancels an in-flight QR image request and clears stale bytes;
an expired image is never reused, and full-screen expiry is checked every
second while visible. Seller and transaction authorization are refreshed at
least every five seconds and fail closed if they cannot be verified.
Authenticated-session reset performs the same clear, and
a late non-cooperative response cannot restore the image. The complete QR card
is hidden after the first trusted scan or when cancellation/refund, dispute,
legal hold, or shipment mismatch revokes access. SHIPPOP QR stays hidden or
unavailable until account- and service-specific certification is recorded.

The first production mapping contains Thailand Post, Flash Express, and KEX
Express. Tracking allocation is not proof that the carrier received the
parcel; the status card keeps the exact ship-by time visible until the first
provider-confirmed carrier scan.

The app must never expose seller fulfillment from checkout return, a screenshot,
or client state. It uses the backend transaction state.

The transaction-detail progress card is role-specific and contains only three
steps. Buyers see `สร้างข้อตกลง → จ่ายเงิน → ได้รับของ`. Sellers see
`ยอมรับข้อตกลง → ส่งของ → รับเงิน`. Completed steps use the same green across
the icon, outline, check marker, and label. Every incomplete step, including the
step currently in progress, is gray; the prominent role-specific status card
explains what is happening now. While a seller is waiting for provider-confirmed
payment, `ส่งของ` remains gray.

Buyer transaction detail uses the established blue gradient and blue-tinted
status surfaces. Seller transaction detail uses the established purple accent
and purple-tinted status surfaces. This visual distinction must not replace the
explicit `รายการซื้อ` / `รายการขาย`, `ซื้อ` / `ขาย`, counterparty, or
role-specific status copy. At payout pending, the buyer sees that their receipt
confirmation is complete while the seller sees that receipt is confirmed and
the bank transfer is being processed.

Both transaction-list modes and every status filter sort by immutable
transaction creation time from newest to oldest. Status and deadline do not
override that order; refresh preserves the same deterministic ordering.

After seller acceptance, transaction detail shows `หลักฐานข้อตกลงร่วม`, the
shared agreement-core hash, and separate seller/buyer acceptance status and
exact time. Consumer copy says each acceptance came from an account verified
by phone; it does not expose internal actor IDs or claim a certificate-backed
digital signature. Before buyer checkout, the same card correctly shows that
the seller accepted and the buyer has not yet accepted.

For physical offers, creation requires a complete Thai delivery address from
the bundled hierarchy or the buyer's saved address. The private address is
locked to the offer, while seller review shows only its province/postal code
before acceptance. Checkout displays the locked address without an editor.
Provider-confirmed payment unlocks the full address for seller fulfillment.
After both parties accept, the same card lets either role download the JSON
evidence file with hashes and server times; Web additionally exposes the
readable print/PDF version.

## Stripe PaymentSheet boundary

The native client never contains a Stripe secret key and never creates a trusted
payment state. For the first approved integration:

1. Mobile submits the authenticated transaction ID and terms acceptance to
   `POST /api/mobile/transactions/{id}/payment-sheet`.
   The backend loads the receipt/refund email from the authenticated buyer
   profile; mobile cannot override it.
2. The backend authorizes buyer ownership, validates the agreement core created
   at seller acceptance, appends the buyer acceptance against the same hash,
   verifies that a physical offer already has its locked private address,
   validates integer-satang fee/net values, and creates or retrieves a Stripe
   PaymentIntent with a stable idempotency key.
3. The backend returns the PaymentIntent client secret, publishable key, and
   server-selected buyer email.
   The native iOS/Android PaymentSheet collects payment details in the app;
   TOKLONG never receives raw card data.
4. PaymentSheet completion changes the UI to
   `กำลังรอ Stripe ยืนยัน` only.
5. A signature-verified, idempotent Stripe webhook or authorized reconciliation
   changes the transaction to a confirmed payment state.
6. Mobile refreshes the transaction from the backend. Only that state may expose
   shipping or digital handoff.

Expected response:

```json
{
  "clientSecret": "pi_..._secret_...",
  "publishableKey": "pk_...",
  "receiptEmail": "buyer@example.com"
}
```

Registration collects the payment-contact email. PaymentSheet receives that
server-selected email as default billing details and does not render a separate
TOKLONG checkout email field. Refund completion still requires a verified
Stripe refund event; the app must not collect the buyer's refund bank account.

The MAUI package is a third-party binding around Stripe's native Android and iOS
PaymentSheet SDKs, not an official Stripe .NET MAUI SDK. Pin and review the
wrapper and all transitive native artifacts before production. The server uses
the official Stripe.net library.

## Current implementation boundary

The native navigation, responsive XAML screens, role/state filters, secure phone
session, authenticated transaction list/detail and offer upload, Thai address
catalog, native PaymentSheet, tracking, digital handoff, receipt confirmation,
and dispute initiation are implemented against `Toklong.Api`.

The API uses a separate deployable project. It rate-limits OTP requests and
verification, stores refresh-token hashes only, rotates refresh tokens, checks
the persisted session on every access-token request, and redacts internal
transaction tokens from mobile responses.

Debug builds use the separately running local API on port `5181`: iOS and Mac
Catalyst use `http://localhost:5181`, while the Android emulator uses
`http://10.0.2.2:5181`. Release builds use the configured HTTPS production
endpoint. Platform transport exceptions permit cleartext only for these local
development hosts; they must not become a general production exception.

Seller invitation acceptance and payout-account editing are implemented
natively. The app accepts only an opaque 32–64 character hexadecimal token from
`toklong://offer/{token}` or the owned HTTPS hosts
`toklong.co.th`, `www.toklong.co.th`, and `app.toklong.co.th`. It preserves the
token through phone authentication, then shows the immutable offer, fee,
expected net amount, exact deadline, payout account, rights attestation, and
accept/decline actions.

The Web host serves Apple and Android association documents only after the real
Apple Team ID and Android release-certificate SHA-256 fingerprint are supplied.
Until those production values and DNS/TLS routes are deployed, custom-scheme
testing works but verified universal/app links are not considered live.

The account screen opens a native payout settings page. Updating an existing
account may leave the account-number field blank to retain the stored number;
the API never returns the raw account number.

### Local carrier-event simulation

After deterministic Development shipping confirms a booking and issues
tracking, local development can exercise the remaining flow without a live
SHIPPOP account:

```bash
./scripts/simulate-carrier-event.sh \
  TRANSACTION_ID FLASH TH1234567890 in_transit

./scripts/simulate-carrier-event.sh \
  TRANSACTION_ID FLASH TH1234567890 delivered
```

The script calls the API's signed internal reconciliation boundary, not a
mobile endpoint. It uses the local development secret by default; production
startup requires a different strong secret. The request is timestamp-bounded,
signature-verified, role-independent, idempotent, carrier/tracking matched, and
audited through the normal domain transition service.

## Compact layout system

UI consistency work must preserve the existing color palette, branding,
navigation, screen structure, validation, and transaction behavior. The same
layout system applies to authentication, offer creation, transaction detail,
transaction list, activity, and account screens.

The shared spacing scale is intentionally small:

- `SpacingXs` = 4 points for status details and required markers;
- `SpacingSm` = 8 points for label-to-input, icon-to-text, and
  input-to-helper/error spacing;
- `SpacingMd` = 12 points for related actions;
- `SpacingLg` = 20 points for screen edges, card padding, and field-to-field
  spacing;
- `SpacingXl` = 28 points for major sections.

Native form controls use these minimum dimensions:

- single-line input 52 points;
- compact selector or icon action 44 points;
- multiline input 112 points, expanding with text;
- primary action 52 points;
- secondary action 48 points.

Semantic type roles are screen title (30 bold), section title (18 bold), field
label (14 bold), input and placeholder (16), body (14), helper and validation
(13), button label (15 bold), and caption (12). Phone-number, registration,
address, tracking, and dispute fields all use the same 16-point input role. The
six visible verification digits use a single documented 25-point bold role so
the short code remains easy to scan; the hidden native input keeps the normal
input semantics and AutoFill behavior. Only a monetary amount may use the
documented 18-point amount role. Input containers use a 14-point radius,
primary buttons 16, cards 20, small status elements 12, and pills use a fully
rounded shape.

`FormLabelView` owns the field-label and required-marker alignment. Refined form
styles and layout tokens live in `App.xaml`; individual screens must not
reintroduce local copies of the same spacing and dimensions. All native forms
must use `RefinedInputBorder`, `RefinedEntry`, `RefinedPicker`, or the matching
multiline styles. Root screens use the same 20-point edge padding and 28-point
major-section rhythm, while pushed form screens additionally account for the
software keyboard.

## Accessibility and platform rules

- Interactive controls have at least a 44-point logical touch target.
- Text uses platform scaling; no fixed-height body text containers.
- Status uses words in addition to color.
- Secure storage uses MAUI platform APIs on physical devices and production
  builds. On a Debug iOS Simulator without an Apple signing identity, the
  session is held only in process memory and is lost when the app closes. This
  avoids persisting credentials outside Keychain while working around the
  Xcode 26 simulator entitlement regression.
- iOS supports iOS 15 or later; Android supports API 24 or later; Mac Catalyst
  provides the same native XAML workspace on macOS.
- Product photos require only the operating-system photo picker permission.
- Real-device checks must cover large text, screen reader labels, dark-mode
  contrast decisions, safe areas, offline errors, and return from background.
