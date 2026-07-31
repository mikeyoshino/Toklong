# Verified Account Name Change Design

**Date:** 2026-07-31
**Status:** Approved for implementation planning

## Summary

TOKLONG will let an authenticated mobile user change the first and last name
shown on their account. The entry point is an `แก้ไข` action inside the blue
profile card on `บัญชี`. A name change is completed only after a fresh
six-digit code sent to the account's current verified phone is successfully
verified.

The code proves control of the existing account. It does not prove that the new
name is a legal identity, and consumer copy must not describe the name as
identity-verified or KYC-verified.

The account has one current name across its buyer and seller roles. A successful
change affects the account, its active mobile sessions, and transactions created
after the applicable party snapshot point. It never rewrites a name already
copied into an existing transaction or agreement snapshot.

## Goals

- Let an authenticated user update separate first-name and last-name fields
  from `บัญชี`.
- Confirm account control through the existing verified phone before saving.
- Keep one current account name across buyer and seller roles.
- Preserve all existing transaction party-name snapshots and agreement hashes.
- Allow one first change at any time after registration, then one successful
  change every two calendar months.
- Limit code sends and verification attempts independently of the successful
  name-change cooldown.
- Reuse the shared mobile OTP presentation and accessibility behavior.
- Record immutable account audit events and privacy-safe analytics events.

## Non-goals

- Legal-name verification, identity-document collection, or KYC.
- Changing the registered phone number.
- Operations staff overriding a name or cooldown in this user flow.
- Rewriting party names in existing offers, transactions, labels, evidence, or
  agreements.
- Combining this feature with the verified email-change workflow.
- Building a generic profile-field mutation framework.

## Decided user experience

### Account entry point

The blue profile card on `AccountPage` retains the current name and the
`เบอร์โทรศัพท์ยืนยันแล้ว` status. It adds an `แก้ไข` action beside the name.
The action remains visible whether the user is eligible or in a cooldown.

The account page must not proactively display:

- the last name-change time;
- whether the first change entitlement remains unused; or
- the next time the user may change their name.

When the user taps `แก้ไข`, the app obtains server-authoritative eligibility.
If the user is eligible, it opens the name form. If not, it shows a modal and
does not open the form.

### Cooldown modal

The blocked-state modal uses plain consumer language:

- Title: `ยังเปลี่ยนชื่อไม่ได้`
- Body: `เพื่อความปลอดภัย ชื่อบัญชีเปลี่ยนได้ทุก 2 เดือน`
- Follow-up: `คุณจะเปลี่ยนได้อีกครั้งวันที่ {exact local date and time}`
- Primary action: `เข้าใจแล้ว`

The server returns the exact next-allowed instant. The mobile client formats it
in the user's local timezone and must not derive the date independently.

### Step 1: enter the new name

`ChangeNamePage` is a separate page with:

- back navigation to `บัญชี`;
- `ขั้นตอน 1 จาก 2`;
- heading `แก้ไขชื่อ`;
- helper copy explaining that the new name applies to future buying and selling
  records while existing records remain unchanged;
- required `ชื่อ` and `นามสกุล` fields;
- one primary action, `ส่งรหัสยืนยัน`.

The page does not expose an OTP field before a send request has been accepted.
A normalized name equal to the current name produces an inline validation
message. It does not send a code, consume a send allowance, or consume the
first-change entitlement.

### Step 2: verify the current phone

`VerifyNameChangePage` shows:

- `ขั้นตอน 2 จาก 2`;
- heading `ยืนยันการเปลี่ยนชื่อ`;
- the masked current phone;
- a summary of the exact normalized name awaiting confirmation;
- the shared `OtpVerificationFormView` and `OtpCodeInput`;
- primary action `ยืนยันและบันทึก`; and
- the shared expiry, resend, error, paste, deletion, screen-reader, and iOS
  one-time-code AutoFill behavior.

The name-change flow supplies copy and commands to the shared OTP component. It
must not fork or reproduce a separate six-box OTP implementation.

### Success

After authoritative verification succeeds, the app returns to `บัญชี`,
refreshes the profile, and shows a one-shot success summary:

`เปลี่ยนชื่อเรียบร้อยแล้ว ชื่อใหม่จะใช้กับรายการใหม่`

The profile card shows the new name and keeps the `แก้ไข` action. It does not
show the new cooldown. A later tap during the cooldown produces the modal
described above.

## Registration alignment

`CompleteRegistrationPage` changes from one combined `ชื่อและนามสกุล` input to
two required inputs:

- `ชื่อ`
- `นามสกุล`

The registration request and command accept the two values separately. The
server normalizes them with the same policy as name changes and constructs the
canonical full display name. Sign-in remains phone plus six-digit code and must
not ask for or overwrite either name field.

## Name validation and normalization

The server is authoritative for all name validation. Mobile validation mirrors
the server only to provide immediate feedback.

For each field:

- trim leading and trailing whitespace;
- collapse repeated internal whitespace to one space;
- require at least one character;
- allow Unicode letters and combining marks, internal spaces, hyphens, straight
  apostrophes, and curly apostrophes;
- reject digits, emoji, control characters, and other punctuation;
- limit the normalized field to 60 characters.

The canonical display name is `{FirstName} {LastName}` and remains limited to
120 characters for compatibility with existing account and transaction fields.

A name is unchanged only when both normalized fields exactly equal their
current normalized values. A case or diacritic change is a real change and uses
the entitlement after successful verification.

## Eligibility and cooldown

Eligibility is server-owned:

1. If the account has no successful user-initiated name change,
   it may change at any time.
2. After a successful change at `changed_at`, the next allowed instant is
   `changed_at.AddMonths(2)`.
3. At or after that exact instant, another change may complete.

This is two calendar months, not 60 elapsed days. The server converts
`changed_at` to `Asia/Bangkok`, adds two calendar months while preserving the
local wall-clock time, and converts the result back to a UTC instant for
storage and comparison. Boundary tests must cover month ends and leap years.

Starting, abandoning, expiring, failing, or superseding a challenge does not
start the two-month cooldown. Only a successful atomic name update sets
`NameChangedAt`.

If both buyer and seller records exist and have different legacy
`NameChangedAt` values, eligibility uses the later value. A successful change
then writes the same new value to both records.

## Code-send and verification limits

The name-change code policy is independent of the two-month cooldown:

- at least 60 seconds between sends;
- at most five accepted sends per account/current phone in any 24-hour period;
- each code expires 10 minutes after creation;
- at most five incorrect submissions per challenge;
- a locked or expired challenge cannot complete a change.

The five-per-24-hour account limit must be durable and keyed to the authenticated
account and current normalized phone. It must not reset on API restart and must
not depend only on an IP-address rate limiter. Existing authenticated and
network-level API rate limits remain additional defense.

Only a digest of the code is stored. Raw codes must not be persisted, returned
outside an explicitly development-only provider contract, added to analytics,
or written to normal logs.

## Domain model

### Buyer and seller accounts

`BuyerAccount` and `SellerAccount` gain structured name values:

- `FirstName`
- `LastName`
- `NameChangedAt`, nullable

Existing `FullName` and `DisplayName` consumers continue to receive the
canonical combined value. A domain method applies one normalized structured
name and timestamp. The verified completion handler calls the method on every
buyer or seller role attached to the authenticated account.

The new columns allow up to 120 characters so migration can preserve every
previously valid combined name without truncation. New registration and
name-change commands still enforce the approved 60-character per-field and
120-character combined limits.

### Legacy-name migration

For an existing buyer name, migration assigns the first whitespace-delimited
part to `FirstName` and the remaining text to `LastName`.

Migration preserves legacy text exactly after the existing whitespace
normalization. If a migrated part exceeds the new 60-character input limit, it
may remain readable as legacy data, but the next submitted name must satisfy
the new limits. Migration must never truncate or silently rewrite it.

For a seller linked by the same verified phone to a buyer, migration copies the
buyer's structured name. A synthetic seller placeholder such as
`ผู้ขาย 1234`, or a seller display name that cannot be split safely, remains a
display fallback with no structured name. That seller sees blank name fields
and must provide both fields on the first successful change.

Migration does not set `NameChangedAt`; migrated accounts retain the one
first-change entitlement.

### Name-change challenge

A dedicated aggregate records the pending authorization. It includes:

- challenge ID and authenticated account party IDs;
- the normalized current verified phone and masked phone;
- pending first and last name;
- code digest;
- created, expiry, resend-available, send-accepted, verified, locked,
  superseded, and failed timestamps as applicable;
- incorrect-attempt count;
- request, resend, and verification idempotency keys;
- status and concurrency version.

Only one active logical name-change challenge is allowed for an account. A new
accepted request or resend supersedes the previous code safely. An unknown SMS
send outcome remains reconcilable and must not be treated as confirmed delivery.

### Audit

Verification appends an immutable account-name audit event containing:

- account party IDs;
- old and new normalized names;
- authenticated session ID;
- challenge ID;
- completion timestamp; and
- event type and version.

Names are personal data. Audit access and retention follow account security and
privacy policy. Analytics receives event names and bounded failure reasons only,
never the old name, new name, phone, or code.

## Application and API boundaries

The dedicated application feature contains:

- `GetAccountNameChangeEligibilityQuery`
- `RequestAccountNameChangeCommand`
- `GetPendingAccountNameChangeQuery`
- `ResendAccountNameChangeCodeCommand`
- `VerifyAccountNameChangeCommand`

Mobile endpoints follow the existing authenticated account convention:

- `GET /api/mobile/me/name-change/eligibility`
- `GET /api/mobile/me/name-change`
- `POST /api/mobile/me/name-change`
- `POST /api/mobile/me/name-change/{challengeId}/resend`
- `POST /api/mobile/me/name-change/{challengeId}/verify`

Eligibility returns an explicit allowed/blocked result. A blocked result includes
`NextAllowedAt`; it is expected product state rather than a generic server
failure. Request and verify commands independently recheck authorization and
eligibility so a stale client cannot bypass the rule.

Every state-changing request uses a caller-stable idempotency key. Exact replays
return the original result. Reuse of a key with different content is rejected.

### Atomic completion

Successful verification executes in one database transaction:

1. verify challenge status, digest, attempts, and idempotency;
2. lock or concurrency-check the attached account records;
3. recheck the latest successful change and two-month eligibility;
4. apply the structured name and the same `NameChangedAt` to buyer and seller
   records that exist;
5. update the display name on active mobile sessions for those party IDs;
6. mark the challenge verified;
7. append the immutable audit event; and
8. commit.

Concurrent verifications may produce only one successful name change. An exact
replay of that winning verification returns success; another challenge observes
the new cooldown and cannot overwrite it.

## Transaction snapshot rule

Account-name persistence is separate from transaction party-name persistence.
The name-change handler must not query or update `SaleTransaction` records.

- The buyer name is copied into the transaction when the buyer sends the offer.
- The seller name is copied when the seller accepts.
- Once a party's name has been copied at that point, later account changes do
  not modify it, even if the transaction has not yet been paid.
- A transaction created after the account change uses the new current name at
  the applicable party snapshot point.
- Agreement-core snapshots, acceptance records, hashes, labels, evidence, and
  historical transaction presentation continue to use the stored transaction
  names.

For example, an offer sent as `สมชาย ใจดี` retains that buyer name after the
account changes to `สมศักดิ์ ใจดี`. A later offer uses `สมศักดิ์ ใจดี`.
Operations may correlate the account through protected IDs and the account audit
timeline; consumer transaction history continues to show the transaction
snapshot.

## Error behavior

- **Not eligible:** show the cooldown modal with the server-provided exact date
  and time.
- **Unchanged name:** show inline validation; do not create or send a challenge.
- **Invalid name:** show the server's field-specific consumer-safe message.
- **Send cooldown:** keep the form state and show the exact retry duration.
- **Daily send limit:** explain that too many codes were requested and when the
  server permits another request.
- **SMS rejected or failed:** do not expose the OTP screen as active, do not
  change the name, and do not start the name cooldown.
- **SMS outcome unknown:** block automatic resend until reconciliation produces
  a safe outcome.
- **Incorrect code:** use the shared OTP error target and remaining-attempt
  behavior.
- **Expired or locked code:** prevent verification and offer the allowed
  restart/resend action.
- **Network response lost after completion:** an exact verification replay
  returns the authoritative success; profile refresh is safe.
- **Session changed or signed out:** clear pending presentation and never expose
  another account's challenge, name, or completion message.

## Analytics

The mobile application emits:

- `account_name_change_opened`
- `account_name_change_started`
- `account_name_change_code_resent`
- `account_name_change_verified`
- `account_name_change_failed`
- `account_name_change_blocked`

Failure and blocked events use a bounded reason enumeration such as
`cooldown`, `unchanged`, `invalid`, `send_limit`, `expired`, `locked`,
`network`, or `provider`. Event properties must not contain names, phone
numbers, codes, free-form errors, or other personal data.

## Testing

### Domain and application tests

- First successful change is immediately eligible after registration.
- Incomplete, failed, expired, locked, and superseded challenges do not consume
  the first-change entitlement.
- A successful change blocks completion until exactly `AddMonths(2)`.
- Calendar boundaries include short months, month ends, and leap years.
- The later legacy timestamp controls when buyer and seller timestamps differ.
- First and last name normalization and every allowed/rejected character class
  are covered.
- An unchanged normalized name creates no challenge and sends no code.
- Send cooldown, durable five-per-24-hour limit, 10-minute expiry, and five
  incorrect attempts are enforced.
- Raw codes never appear in persisted challenge or audit fields.
- Request, resend, and verification idempotency and replay behavior are covered.
- Concurrent completions permit one winner and one authoritative blocked result.
- Buyer, seller, and active session names change atomically.
- Failure before commit changes none of those records and writes no success
  audit event.
- A successful completion writes exactly one immutable audit event.

### API integration tests

- Every endpoint requires the authenticated mobile account and rejects access
  to another account's challenge.
- Eligibility returns a server-owned exact `NextAllowedAt`.
- Request and verification recheck eligibility independently.
- Expected cooldown, request-limit, expired, locked, and provider outcomes map
  to consumer-safe responses.
- API and durable account limits both apply.
- Lost-response exact replays return the prior result.
- No response exposes code digests, raw codes, unmasked phone data beyond the
  user's already authorized profile contract, or internal exception details.

### Transaction immutability tests

- A buyer name captured before a name change remains unchanged afterward.
- A seller name captured at acceptance remains unchanged afterward.
- A later transaction captures the new name.
- Existing agreement core and terms hashes remain byte-for-byte unchanged.
- No account-name completion path writes a transaction or transaction audit
  state transition.

### Mobile tests

- `AccountPage` places `แก้ไข` in the profile card and never proactively shows
  cooldown timing.
- Tapping while blocked shows the modal and exact server-provided local time.
- Eligible tapping opens the two-step name flow.
- Registration and name change use separate first and last name inputs.
- The verification page composes the shared `OtpVerificationFormView` and
  `OtpCodeInput`; it does not implement another OTP input.
- Paste, deletion, iOS AutoFill, screen-reader labels, focus order, dynamic
  text, loading, retry, and error targets remain accessible.
- Success refreshes the profile, returns to `บัญชี`, and appears once.
- Session reset and account switching clear stale forms, challenges, and
  success presentation.
- Analytics events use only the approved bounded properties.

## Assumptions and provider dependencies

- The currently configured phone-verification provider can issue a separate
  purpose-bound name-change code. Production enablement still requires approved
  provider templates, credentials, delivery monitoring, and cost controls.
- A six-digit code confirms control of the existing phone only.
- The initial mobile/API release can update the registration request contract
  atomically; no supported older client requires the combined-name payload.
- Authorized operations override remains out of scope until a reviewed support
  workflow, roles, and audit requirements exist.

## Next smallest vertical slice

Implement the structured registration-name model and migration first, including
normalization and compatibility tests, without yet exposing account name
changes. This establishes reliable first/last name data for the dedicated
challenge workflow that follows.
