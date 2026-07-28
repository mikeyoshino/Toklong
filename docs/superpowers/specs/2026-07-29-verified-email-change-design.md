# Verified Email Change Design

**Date:** 2026-07-29  
**Status:** Approved design; written specification awaiting review

## Purpose

Add a focused account flow that lets an authenticated user change the email
used for future payment receipts and refund contact only after proving control
of the new email address.

This slice includes:

- the account-page entry point;
- a two-step mobile email-change flow;
- a six-digit email verification challenge;
- a provider-neutral email-sender boundary;
- a Development-only deterministic mock sender;
- a responsive Thai verification-email template using the TOKLONG brand; and
- audit, analytics, error, accessibility, and test requirements.

This slice does not change login identity. Users continue to authenticate with
their verified Thai mobile number. It does not change any transaction,
agreement snapshot, payment, fulfillment, dispute, refund, or payout state or
rule.

## Approved product decisions

- Only email editing is included. Editing first or last name is deferred.
- The account page shows the current email and a separate `แก้ไข` action.
- Editing uses a focused two-step flow rather than an expanded inline form.
- A six-digit code is sent to the new email address.
- The current email remains active until the new email is verified.
- A code is valid for 10 minutes.
- Another code may be requested after 60 seconds.
- Five incorrect verification attempts lock that challenge.
- Requesting another code invalidates the earlier code immediately.
- Raw verification codes are never stored in the database or written to logs.
- The production email provider remains undecided.
- Development uses a deterministic `123456` code behind the same sender
  abstraction. The API never returns that code and does not log it.
- The approved email visual is the minimal branded Template A.
- The email must work on desktop and mobile email clients.

## User-visible scope

### Account page

Under `ข้อมูลติดต่อ`, show:

- label `อีเมล`;
- the current confirmed email;
- action `แก้ไข`; and
- when an unexpired request exists, a restrained `รอยืนยัน` status and
  `ยืนยันต่อ` action.

The current confirmed email remains visually primary. A pending email must
never look active before successful verification.

The email row remains available only to an account that owns the buyer profile
containing the payment-contact email. This design does not invent a second
seller-owned email field.

### Step 1: enter the new email

Page title: `เปลี่ยนอีเมล`

Content:

- progress label `ขั้นที่ 1 จาก 2`;
- explanation
  `อีเมลปัจจุบันยังใช้งานต่อ จนกว่าคุณจะยืนยันอีเมลใหม่สำเร็จ`;
- field label `อีเมลใหม่`;
- an email keyboard and email autofill semantics; and
- primary action `ส่งรหัสยืนยัน`.

The client performs basic syntax validation for immediate feedback. The server
is authoritative and repeats validation. Submitting the same normalized value
as the confirmed email is rejected with
`อีเมลนี้เป็นอีเมลปัจจุบันของคุณแล้ว`.

The primary action is disabled while the request is in flight. Repeated taps
must not create parallel active challenges.

### Step 2: enter the code

Page title: `ยืนยันอีเมลใหม่`

Content:

- progress label `ขั้นที่ 2 จาก 2`;
- explanation `กรอกรหัส 6 หลักที่ส่งไปยัง`;
- the masked destination, for example `so•••••••@example.com`;
- one accessible numeric input presented visually as six positions;
- the server-derived expiry time;
- a resend countdown;
- secondary action `ส่งรหัสอีกครั้ง` when allowed; and
- primary action `ยืนยันอีเมลใหม่`.

The six visible positions must be backed by one focusable input, not six
separate accessibility stops. Pasting a complete six-digit code is supported.

After successful verification:

1. the server atomically activates the new email and consumes the challenge;
2. the app refreshes the authenticated profile from the server;
3. the flow returns to the account page; and
4. the user sees `เปลี่ยนอีเมลเรียบร้อยแล้ว`.

There is no separate success page.

### Resume behavior

The pending request is server-side state and survives app termination,
sign-out, and sign-in. Opening the account page queries the pending state.
`ยืนยันต่อ` reopens Step 2 with only masked destination and timing metadata.

The app may cache the opaque challenge identifier for navigation convenience,
but the server remains authoritative. Sign-out clears the local cache. After
sign-in, the pending endpoint restores the flow.

An expired or locked challenge is not resumable. The account page continues to
show the confirmed email and allows a new request. A separate cancel endpoint
is not included in this smallest slice; requesting a different or replacement
email supersedes the prior active challenge.

## Email normalization and ownership semantics

Email remains profile and payment-contact data, not a login identifier.
Verification proves that the authenticated account could receive the
one-time code at that destination at that time; it does not establish legal
identity or ownership beyond mailbox control.

Before comparison and storage:

- trim leading and trailing whitespace;
- apply the existing server email-syntax rules;
- compare the domain case-insensitively;
- preserve a deliverable canonical value for display and sending; and
- do not add provider-specific transformations such as removing dots or
  `+tag` suffixes.

This design preserves the existing decision that email is not globally unique.
Two phone-authenticated accounts may use the same verified contact email.

## Challenge model

Introduce a persistent pending email-change challenge owned by one buyer
account. It contains:

- opaque challenge identifier;
- buyer account identifier;
- canonical pending email;
- masked pending email;
- code digest;
- code-digest nonce or challenge-bound salt;
- creation time;
- expiry time;
- resend-available time;
- incorrect-attempt count;
- status: `PendingSend`, `Active`, `Verified`, `Expired`, `Locked`,
  `Superseded`, or `SendFailed`;
- verified, locked, or superseded time where applicable; and
- concurrency/version metadata.

It does not contain the raw code.

Only one challenge may be pending or active for a buyer account. Creating or
resending a request transitions the previous pending or active challenge to
`Superseded` before the replacement is created as `PendingSend`. A database
constraint plus transactional application logic must prevent concurrent
pending or active challenges.

### Code generation and verification

For a real sender:

- generate exactly six decimal digits with a cryptographically secure random
  generator;
- compute a keyed digest bound to the challenge identifier and code;
- persist only the digest and required nonce/salt;
- pass the raw code directly to the sender/template boundary; and
- discard the raw code after the send call completes.

Verification recomputes the digest and compares it in constant time.

For Development:

- the mock code generator supplies `123456`;
- the challenge still persists only a digest;
- the request response never contains the code;
- application and HTTP logs never contain the code; and
- startup must reject the deterministic generator outside the Development
  environment.

The deterministic value is a development testing aid, not a hidden production
bypass.

### Expiry, retries, and concurrency

- `ExpiresAt = CreatedAt + 10 minutes`.
- `ResendAvailableAt = CreatedAt + 60 seconds`.
- The server, not the client countdown, enforces both values.
- The fifth incorrect verification attempt changes the challenge to `Locked`.
- Correct verification after expiry, lock, consumption, or supersession fails.
- A successful verification and BuyerAccount email update occur in one
  transaction.
- Concurrent successful requests for the same challenge produce one email
  update; a replay returns an idempotent already-completed result only when it
  resolves to that same completed challenge.
- A resend creates a new challenge and invalidates the old code before sending
  the replacement.

### Send activation pattern

Use a synchronous, fail-closed activation pattern for this slice:

1. transactionally supersede any earlier challenge and persist the replacement
   as `PendingSend`;
2. render the email and call the sender with a stable provider idempotency key;
3. when the sender accepts the instruction, transition the challenge to
   `Active` and return the challenge metadata; or
4. when the sender reports failure, transition it to `SendFailed` and return a
   retryable plain-language error.

`PendingSend` and `SendFailed` challenges cannot verify and are not returned by
the pending-change endpoint. If the process stops after provider acceptance but
before activation, the user may receive an unusable code, but the email is
never activated by assumption. A cleanup/reconciliation job marks stale
`PendingSend` records failed. The user can safely request a fresh code.

This intentionally favors a harmless failed verification over accepting a code
whose send outcome is unknown. The adapter's accepted response still does not
claim inbox delivery. A production provider may later add provider-status
reconciliation without changing the domain rule.

The existing authenticated API throttling must also partition requests by
buyer account and a transient HMAC-derived network key. Raw IP addresses are
not stored or logged. The 60-second resend rule is mandatory; any additional
rolling provider-cost limit remains configurable until a production provider
is selected.

## API contract

All routes require the existing authenticated mobile session and derive the
buyer identity from that session. No request may supply a buyer identifier.

### Get pending change

`GET /api/mobile/me/email-change`

Returns either no active request or:

- challenge identifier;
- masked pending email;
- expiry timestamp;
- resend-available timestamp; and
- remaining verification attempts.

The response never returns the full pending email or code.

### Request a change

`POST /api/mobile/me/email-change`

Request:

- new email; and
- client-generated idempotency key.

Response:

- challenge identifier;
- masked pending email;
- expiry timestamp;
- resend-available timestamp; and
- remaining verification attempts.

The endpoint validates the authenticated buyer profile, email syntax, current
email comparison, cooldown, and idempotency key. A successful request sends one
verification email through the sender abstraction.

### Resend

`POST /api/mobile/me/email-change/{challengeId}/resend`

The server verifies that the challenge belongs to the authenticated buyer and
that the resend time has arrived. It supersedes the old challenge, creates a
new challenge, sends a new code, and returns the new opaque identifier and
timing metadata.

The app replaces its cached identifier. The earlier code fails even if it had
time remaining.

### Verify

`POST /api/mobile/me/email-change/{challengeId}/verify`

Request:

- six-digit code; and
- client-generated idempotency key.

Response:

- confirmed current email; and
- completion timestamp.

The route never trusts a client-supplied destination email. It uses the
canonical pending email stored with the challenge.

## Email sender boundary

Define a provider-neutral application interface whose input contains:

- recipient email;
- subject;
- rendered text body;
- rendered HTML body;
- message purpose;
- correlation identifier that is not the code; and
- provider idempotency key.

The interface returns an accepted provider reference or a typed transient or
permanent failure. Acceptance means only that the provider accepted the send
instruction; it does not mean the mailbox received or opened the email.

No provider name appears in domain or mobile code.

### Development mock

The Development adapter:

- performs no network send;
- receives the same rendered subject, text, and HTML as a real provider;
- stores messages only in a bounded in-memory test inbox;
- exposes that inbox to automated tests through dependency injection, not
  through a mobile API endpoint;
- never writes the recipient, body, or code to normal logs; and
- is unavailable outside Development and test hosts.

Tests retrieve the captured message directly from the mock instance and assert
the Thai copy, escaped content, responsive structure, logo fallback, and
`123456` presentation.

## Responsive Thai email template

Subject:

`รหัสยืนยันอีเมลใหม่ของคุณจาก TOKLONG`

Body order:

1. actual TOKLONG Transaction Rail logo with visible `TOKLONG` text;
2. heading `ยืนยันอีเมลใหม่ของคุณ`;
3. copy
   `กรอกรหัสนี้ในแอป TOKLONG เพื่อยืนยันการเปลี่ยนอีเมล`;
4. prominent six-digit code grouped visually as `123 456`;
5. expiry copy `รหัสนี้ใช้ได้ภายใน 10 นาที`;
6. security note
   `หากคุณไม่ได้ขอเปลี่ยนอีเมล ไม่ต้องดำเนินการใด ๆ และห้ามบอกรหัสนี้กับผู้อื่น`;
7. footer
   `TOKLONG จะไม่ขอรหัสผ่าน เลขบัตร หรือข้อมูลบัญชีธนาคารผ่านอีเมลนี้`.

The template also supplies an equivalent plain-text body.

Compatibility requirements:

- content width at most 600 CSS pixels on desktop;
- fluid width on narrow screens;
- table-based structural layout and inline critical styles for broad email
  client support;
- reduced mobile padding without reducing the code's prominence;
- minimum 16-pixel primary body text on mobile where supported;
- sufficient contrast against white and pale-blue surfaces;
- no action link or button is required;
- the message remains complete when images are blocked; and
- the logo image has meaningful `alt="TOKLONG"` text and fixed dimensions to
  prevent layout shift.

Use a PNG export of the exact production Transaction Rail geometry rather than
redrawing the logo in the template. The final production image must use a
stable HTTPS asset URL or a provider-supported embedded-content mechanism.
Provider selection and production asset hosting remain deployment decisions.
The text wordmark and complete instructions remain visible if the image cannot
load.

All dynamic values are HTML-escaped. The code is formatted for display only;
verification accepts exactly six digits after removing display whitespace.

## Effects on payments and existing transactions

Successful verification updates the buyer profile contact email for future
operations only.

- A later checkout reads the newly confirmed server-side profile email.
- A client still cannot override the payment-contact email in a checkout
  request.
- An already-created provider payment record, paid transaction, receipt
  destination, refund instruction record, and immutable agreement snapshot are
  not rewritten.
- No transaction audit history is mutated.
- No payment or payout state transition is triggered by an email change.

## Failure behavior and Thai copy

- Invalid syntax: `กรอกอีเมลให้ถูกต้อง`
- Same as current email: `อีเมลนี้เป็นอีเมลปัจจุบันของคุณแล้ว`
- Request still cooling down:
  `กรุณารอสักครู่ก่อนส่งรหัสอีกครั้ง`
- Incorrect code with attempts remaining:
  `รหัสไม่ถูกต้อง ลองตรวจสอบแล้วกรอกอีกครั้ง`
- Fifth incorrect attempt:
  `กรอกรหัสไม่ถูกต้องครบจำนวนแล้ว กรุณาขอรหัสใหม่`
- Expired code:
  `รหัสหมดอายุแล้ว กรุณาขอรหัสใหม่`
- Superseded code:
  `มีการส่งรหัสใหม่แล้ว กรุณาใช้รหัสล่าสุด`
- Temporary sender failure:
  `ยังส่งอีเมลไม่ได้ กรุณาลองอีกครั้ง`
- Offline or timeout:
  preserve the confirmed email and pending server state, then show
  `เชื่อมต่อไม่สำเร็จ กรุณาลองอีกครั้ง`

Error copy must not reveal whether another account uses an email and must not
include internal terms such as digest, provider, challenge, or idempotency.

If persistence succeeds but sending fails, the challenge becomes `SendFailed`
and remains unusable. It is never presented as an active pending change.

## Audit and analytics

Append security audit events for:

- email change requested;
- replacement code requested;
- challenge locked;
- email change verified; and
- sender instruction failed.

Events include buyer identifier, opaque challenge identifier, event time,
result, and a one-way destination partition/hash or masked destination where
operationally necessary. They never include the raw code or full email address
in general application logs.

Emit privacy-minimized analytics events:

- `account_email_change_started`;
- `account_email_change_code_resent`;
- `account_email_change_verified`; and
- `account_email_change_failed`, with a coarse reason category.

Analytics contains no email, code, phone number, or provider response body.

## Accessibility

- The account email row and `แก้ไข` action have distinct accessible names.
- Progress text is exposed as text, not only color.
- The code control is one labeled input with numeric keyboard semantics.
- Errors are announced and focus moves to the relevant field.
- Resend disabled state includes the remaining time in its accessible label.
- Masked destination is readable by a screen reader without announcing each
  bullet separately.
- Dynamic Type may wrap and scroll without hiding either primary action.
- Screen reader and keyboard focus order follows the visual order.
- The logo and visible wordmark form one accessible brand label in the email
  preview; decorative logo internals are not separately announced.

## Testing strategy

### Domain tests

- creating a challenge establishes the approved timestamps;
- a replacement supersedes the previous challenge;
- wrong attempts increment atomically and the fifth locks the challenge;
- expired, locked, superseded, and consumed challenges cannot verify;
- the correct digest verifies in constant-time comparison code paths;
- successful verification consumes the challenge exactly once; and
- concurrent verification cannot activate two changes.

### Application tests

- requests require the authenticated buyer profile;
- current email stays unchanged before verification;
- request/resend cooldown is server-enforced;
- request and verification idempotency are replay-safe;
- successful verification atomically updates BuyerAccount email;
- failed sending leaves an unusable `SendFailed` challenge;
- audit and analytics events contain no raw code or full email; and
- Development fixed-code components cannot activate outside Development/test.

### API tests

- every route rejects unauthenticated access;
- one buyer cannot read, resend, or verify another buyer's challenge;
- request responses never expose the code or full pending email;
- invalid syntax and same-current-email cases return plain-language errors;
- five wrong codes lock the challenge;
- resend invalidates the previous code;
- the correct Development code verifies;
- replay of a completed verification is idempotent;
- profile reads show the new email only after verification; and
- existing paid transaction/payment email records remain unchanged.

### Email template tests

- subject and Thai body copy are correct;
- HTML and plain-text bodies both contain the code and 10-minute expiry;
- HTML uses the exact exported brand asset reference and `TOKLONG` fallback;
- dynamic values are escaped;
- critical layout is table-based, fluid, and capped at 600 pixels;
- mobile padding and font fallbacks are present;
- the template remains understandable without the image; and
- the mock inbox captures the rendered message without using logs or an API.

### Mobile tests

- the account page shows the confirmed email and edit action;
- Step 1 and Step 2 each expose one primary action;
- invalid input, loading, retry, expiry, lock, and resend states render;
- one focusable code input supports paste and Dynamic Type;
- pending verification resumes after app restart/sign-in;
- sign-out clears only local pending navigation data;
- successful verification refreshes the server profile; and
- accessibility labels, focus order, contrast, and scrolling pass on changed
  pages.

## Assumptions

- The authenticated buyer account remains the source of payment-contact email.
- Existing registration and checkout behavior continue treating email as
  non-login, non-unique profile data.
- Application clocks use the repository's injectable UTC clock; the client
  displays countdowns from server timestamps.
- The Development mock is sufficient for automated and local functional
  testing before a production provider is selected.
- No support-agent override or manual email change is included.

## Open decisions and provider blocks

- Select the production transactional-email provider. Twilio SendGrid is a
  candidate, not an approved dependency.
- Approve sender domain, SPF, DKIM, DMARC, return-path, suppression handling,
  bounce handling, throughput, DPA/PDPA terms, credentials, and delivery SLA.
- Choose and provision the stable HTTPS logo asset or supported embedded-image
  mechanism.
- Decide the production rolling send limit beyond the mandatory 60-second
  cooldown after provider cost and abuse characteristics are known.
- Define the production provider-status reconciliation and stale
  `PendingSend` cleanup interval. No flow may claim inbox delivery merely
  because a provider accepted a request.
- Name editing and the stronger proof required for a legal-name change remain
  a separate future design.

## Next smallest vertical slice

Implement the end-to-end Development slice:

1. persistent challenge and domain rules;
2. authenticated request, pending, resend, and verify application/API paths;
3. provider-neutral sender plus deterministic Development mock;
4. responsive Thai HTML/plain-text template using the production logo export;
5. account entry and two-step mobile flow; and
6. domain, application, API, template, mobile, and accessibility tests.

Production provider integration remains a later adapter-only slice after the
provider and sender infrastructure are approved.
