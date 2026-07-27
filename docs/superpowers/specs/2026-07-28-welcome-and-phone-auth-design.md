# Welcome and Thai Phone Authentication Design

**Date:** 2026-07-28  
**Status:** Approved design; written specification awaiting review

## Purpose

Redesign the unauthenticated mobile entry flow so TOKLONG feels simple and
immediately understandable, while making it unmistakable that authentication
uses a Thai mobile number and a one-time SMS code rather than an email/password.

The design covers:

- the Welcome page;
- phone-number sign-in;
- phone-first sign-up;
- the shared SMS-code verification page; and
- profile completion after a new phone number is verified.

The work does not change any transaction, payment, fulfillment, dispute, refund,
or payout state or rule.

## Approved visual direction

Use the approved Transaction Rail mark as the visual center of the
unauthenticated experience.

The Welcome page uses:

- a white background with a restrained soft-blue gradient at the top;
- the exact `brand_mark.svg` geometry in the existing Brand Blue-to-Purple
  rounded tile;
- the centered `TOKLONG` wordmark;
- the benefit-led heading `ซื้อขายออนไลน์ ง่ายขึ้น`;
- the supporting line
  `จัดการดีลและติดตามทุกขั้นตอนได้ในที่เดียว`;
- a full-width primary `เข้าสู่ระบบ` button; and
- a visually lighter `สมัครสมาชิก` action.

Remove the shield, truck, floating status badges, journey diagram, and other
hero illustrations from the Welcome page. The startup logo-build animation
already introduces the brand; the static Welcome page must not replay or add a
competing animation.

Authentication pages retain a smaller centered Transaction Rail tile and
wordmark so the form remains usable when the software keyboard or large text is
present. Use the exact production asset rather than redrawing the mark.

## Welcome page

The visual order is:

1. centered Transaction Rail tile;
2. `TOKLONG`;
3. `ซื้อขายออนไลน์ ง่ายขึ้น`;
4. `จัดการดีลและติดตามทุกขั้นตอนได้ในที่เดียว`;
5. primary `เข้าสู่ระบบ`;
6. secondary `สมัครสมาชิก`.

The content is vertically balanced on a normal phone. It becomes scrollable on
a short viewport or at large Dynamic Type sizes. Both actions preserve the
existing separate sign-in and sign-up routes.

The whole brand lockup has the single accessibility description
`โลโก้ TOKLONG`. The rail paths, Mint node, and visible wordmark are excluded
from separate accessibility announcements.

## Thai mobile-number field

The product accepts Thai mobile numbers only. Do not show a country picker,
flag, `+66` prefix, or international-number explanation in the interface.

The field contains:

- label `เบอร์มือถือไทย`;
- placeholder `081-234-5678`;
- the selected modern, thin-line smartphone icon;
- telephone input semantics and a numeric keyboard; and
- automatic `0XX-XXX-XXXX` display formatting.

The smartphone icon is decorative and excluded from the accessibility tree.
The visible field accepts ten local digits beginning with `06`, `08`, or `09`.
The application continues to normalize the value to E.164 `+66...` at the
service boundary. The normalized value is never presented as an additional
choice to a Thai-only user.

## Sign-in flow

### Enter phone

Copy:

- heading `เข้าสู่ระบบด้วยเบอร์มือถือ`;
- description `เราจะส่งรหัสยืนยัน 6 หลักให้คุณทาง SMS`;
- primary action `ส่งรหัสทาง SMS`;
- reassurance `เข้าสู่ระบบโดยไม่ต้องใช้รหัสผ่าน`; and
- switch action `ยังไม่มีบัญชี? สมัครสมาชิก`.

Submitting a valid number requests a sign-in challenge. The button is disabled
while the request is active and changes to a progress label. A successful
request navigates to verification without adding duplicate pages when the
button is tapped repeatedly.

### Verify sign-in code

Copy:

- heading `ใส่รหัสยืนยัน`;
- destination `ส่งทาง SMS ไปที่ 081-•••-5678`;
- secondary action `แก้ไขเบอร์`;
- primary action `ยืนยันและเข้าสู่ระบบ`; and
- resend countdown followed by `ขอรหัสใหม่`.

A successful verification creates the existing mobile session and routes to
`//transactions`.

## Phone-first sign-up flow

The approved direction is a three-step phone-first flow.

### Step 1: Enter phone

Copy:

- heading `สมัครด้วยเบอร์มือถือ`;
- description `เราจะส่งรหัสยืนยัน 6 หลักให้คุณทาง SMS`;
- primary action `ส่งรหัสทาง SMS`;
- reassurance `ใช้เบอร์นี้เพื่อเข้าสู่ระบบครั้งต่อไป`; and
- switch action `มีบัญชีอยู่แล้ว? เข้าสู่ระบบ`.

This step requests a sign-up challenge without collecting or sending a name or
email.

### Step 2: Verify phone

Use the shared six-position verification control backed by one focusable input.

Copy:

- heading `ใส่รหัสยืนยัน`;
- destination `ส่งทาง SMS ไปที่ 081-•••-5678`;
- secondary action `แก้ไขเบอร์`;
- primary action `ยืนยันเบอร์มือถือ`; and
- resend countdown followed by `ขอรหัสใหม่`.

For a phone number without an account, successful verification returns a
single-use registration ticket instead of a mobile session. For a phone number
that already owns an account, successful verification returns the normal
session and routes to `//transactions`. Account existence is not disclosed
until the user has proved control of the phone number.

### Step 3: Complete profile

Copy:

- heading `ตั้งค่าบัญชีให้เสร็จ`;
- description
  `ข้อมูลนี้ใช้กับบัญชี ใบเสร็จ และขั้นตอนคืนเงิน`;
- read-only verified local phone number with a success indicator;
- required `ชื่อและนามสกุล`;
- required `อีเมลสำหรับใบเสร็จและการคืนเงิน`;
- terms and privacy acknowledgement immediately before the primary action; and
- primary action `สร้างบัญชีและเริ่มใช้งาน`.

The terms acknowledgement names and links the applicable Terms of Service and
Privacy Policy. The completion request creates the account and mobile session
atomically. It records the accepted terms version without storing an SMS code
or raw registration ticket.

Email is profile and payment-contact data, not a login identifier. This design
does not introduce email uniqueness; two phone-based accounts may use the same
valid email address.

## Registration ticket

The server issues a cryptographically random opaque registration ticket after a
new phone number successfully completes sign-up verification.

Properties:

- at least 256 bits of randomness;
- valid for 15 minutes;
- accepted only by the registration-completion endpoint;
- able to complete registration exactly once, with only an exact idempotent
  retry allowed afterward;
- stored server-side only as a SHA-256 hash;
- bound to the normalized verified phone number and sign-up purpose;
- invalid for a new completion after successful completion or expiry; and
- never treated as a bearer session for any authenticated mobile API.

The persistent record contains:

- ticket identifier;
- ticket hash;
- normalized verified phone number;
- creation and expiry timestamps;
- consumption timestamp; and
- the installation identifier used for the flow;
- the completion idempotency key after the first completion attempt; and
- the created buyer identifier after successful completion.

It contains no SMS code, name, email, password, reusable credential, or raw
ticket.

The app stores the raw pending ticket, expiry, installation identifier, and one
client-generated completion idempotency key in platform secure storage only
long enough to resume profile completion. It clears them after account
creation, user cancellation, or detected expiry. Clearing a local cancelled
ticket does not call a cancellation API; its server record becomes unusable at
expiry. On cold launch, a valid pending ticket routes to profile completion
after the startup presentation. An expired ticket is removed and returns the
user to the Welcome page with an explanation that phone verification must be
repeated.

An authorized cleanup job permanently deletes expired and consumed ticket
records after 24 hours. The cleanup never handles raw tickets.

## API contract

Keep the existing OTP request and verification routes, but make the result
explicit.

### Request a code

`POST /api/mobile/auth/otp/request`

- Sign-in accepts mode and phone number.
- Sign-up accepts mode and phone number only.
- Supplying profile fields in a sign-up request is rejected rather than
  silently retained.
- The response remains the opaque challenge identifier, masked local phone
  number, and development-only code where applicable.

### Verify a code

`POST /api/mobile/auth/otp/verify`

The request contains challenge identifier, code, mode, and the installation
identifier for sign-up. It contains no profile fields.

The response is a discriminated result:

- `session`: the existing access/refresh session payload for sign-in or an
  already-registered phone verified through the sign-up path;
- `registration_required`: registration ticket, expiry timestamp, and masked
  verified phone number for a new sign-up.

An invalid, consumed, or expired challenge returns the existing plain-language
verification error.

### Complete registration

`POST /api/mobile/auth/registration/complete`

The request contains:

- registration ticket;
- full name;
- email;
- terms version; and
- installation identifier.

The request also carries a stable client-generated UUID in the
`Idempotency-Key` header. The server requires the installation identifier to
match the ticket, validates the ticket and profile, then consumes the ticket,
creates the buyer account, records terms acceptance, and issues the mobile
session in one database transaction.

A unique phone-number constraint, ticket consumption check, and idempotency key
protect concurrent retries. Repeating the same ticket, installation identifier,
and idempotency key returns a session for the already-created account without
creating another account or terms-acceptance record. Reusing the ticket with a
different idempotency key or installation identifier is rejected.

## Component boundaries

Use focused reusable units:

1. `CenteredAuthBrandView` renders the exact compact mark and wordmark with one
   accessibility description.
2. `ThaiMobilePhoneField` wraps the existing formatter and entry with the
   selected smartphone adornment, local copy, validation placement, and
   telephone semantics.
3. Sign-in and sign-up phone pages share the field and visual tokens but retain
   separate view models and explicit navigation.
4. `VerifyCodePage` keeps one input and six visual positions. Mode-specific
   configuration supplies the heading and primary-action copy.
5. `CompleteRegistrationPage` owns profile validation and terms presentation.
6. `IPendingRegistrationService` stores, restores, and clears the short-lived
   ticket through secure storage without exposing it to logs or analytics.
7. Application commands separately verify a phone and complete a registration;
   neither command mutates transaction state.

The Welcome page and authentication pages share colors, spacing, typography,
button styles, and the centered brand component. Do not add a WebView, auth UI
package, country-picker dependency, social login, or password field.

## Validation and failure behavior

- Invalid local phone:
  `กรอกเบอร์มือถือไทย 10 หลัก เช่น 081-234-5678`.
- Request failure preserves the entered number and exposes a retry action.
- OTP cooldown uses the exact retry interval returned by the API.
- An incorrect code remains visible for correction and is not logged.
- An expired or fully consumed challenge makes requesting a new code the
  primary action.
- An expired registration ticket clears secure storage and requires phone
  verification again.
- Invalid full name or email is shown under the related field without consuming
  the registration ticket.
- A completion request disables its primary action and is idempotent across
  network retries.
- Navigation is guarded so rapid taps cannot stack duplicate phone,
  verification, or completion pages.
- Unexpected failures use plain language and never expose provider names,
  tokens, hashes, stack traces, or internal authentication terminology.

## Accessibility

- All interactive targets are at least 44 points high; primary buttons target
  48 points or more.
- The phone field uses telephone content semantics and a numeric keyboard.
- The smartphone icon is decorative.
- The OTP control is one focusable input, supports paste and platform AutoFill,
  and visually presents six positions.
- Validation changes are announced and do not rely on color alone.
- Focus order follows brand, heading, description, fields, primary action, then
  secondary action.
- Every page supports Dynamic Type and scrolls when content, the keyboard, or
  accessibility sizes exceed the viewport.
- Reduced-motion settings require no special authentication animation because
  these pages are static.

## Security, audit, and analytics

- Never store or log an OTP value, raw registration ticket, password, recovery
  code, payment credential, or reusable digital credential.
- Continue signature, rate-limit, retry, and replay protections around the OTP
  provider.
- Rate-limit registration completion by ticket hash and the existing
  privacy-preserving client-address partition. Failed completion responses do
  not reveal whether a ticket hash, installation identifier, email, or phone
  account exists.
- Record account creation and terms acceptance with the stable account ID and
  terms version.
- Record security diagnostics for challenge verification and ticket
  consumption using opaque identifiers, outcomes, and timestamps without raw
  phone, email, code, or token values.
- Analytics may record screen views and coarse outcomes for Welcome, phone
  submitted, code verified, profile completed, and failure category. It must
  not include phone number, email, name, OTP, challenge, or ticket.

## Verification

Add or update tests for:

- Welcome hierarchy, approved copy, exact shared mark, and removed hero
  illustrations;
- sign-in and sign-up using the local Thai phone field and selected smartphone
  icon without visible `+66`;
- local input formatting and E.164 normalization at the service boundary;
- separate sign-in and sign-up navigation;
- sign-up verification returning a registration ticket for a new phone;
- sign-up verification returning a session for an existing verified account;
- registration ticket expiry, replay, hashing, purpose binding, and one-time
  consumption;
- registration installation mismatch, idempotency-key mismatch, and scheduled
  cleanup after the 24-hour retention window;
- concurrent completion requests creating no duplicate account or terms event;
- an exact completion retry returning a session for the same account without
  duplicating account or acceptance records;
- invalid profile input preserving a usable ticket;
- secure-storage resume and cleanup on success, cancellation, and expiry;
- OTP paste, AutoFill semantics, resend cooldown, and masked-phone copy;
- duplicate-tap guards and request loading states;
- focus order, semantic labels, validation announcements, Dynamic Type, and
  keyboard-safe scrolling; and
- all existing authorization and transaction-state suites remaining unchanged
  and passing.

Run type checking, unit tests, API integration tests, mobile core tests,
authentication UI tests, and changed-page accessibility checks. Manually verify
the flow on a short phone, a current iPhone simulator, large text, VoiceOver,
wrong/expired codes, network retry, app restart during profile completion, and
an existing-account phone entered through sign-up.

## Non-goals

- International phone numbers, country selection, or visible `+66`.
- Email/password authentication.
- Social login or account linking.
- Password creation, reset, or recovery.
- A general anonymous or partially authenticated session.
- New marketplace, chat, wallet, escrow, payment, shipping, dispute, refund, or
  payout behavior.
- Changes to the approved startup logo animation.

## Assumptions

- TOKLONG mobile authentication remains limited to Thai mobile numbers.
- SMS remains the only production verification channel for this slice.
- Registration tickets expire after 15 minutes.
- Email remains required for receipts and refund communications but is not
  unique and is never used to sign in.
