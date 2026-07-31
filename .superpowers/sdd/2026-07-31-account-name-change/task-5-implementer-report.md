# Task 5 Implementer Report

## 1. What changed

- Added `VerifyAccountNameChangeCommand`, `VerifiedAccountNameChange`, and the
  verification handler.
- Added purpose-bound HMAC provenance for submitted OTP codes and HMAC
  references for audit name values. Raw OTP codes and raw names are not stored
  in verification attempts or name-change audit events.
- Added active-session lookup by buyer and/or seller account and synchronized
  the new display name to every active, same-phone session.
- Added optimistic concurrency protection to buyer and seller
  `NameChangedAt`.
- Added atomic verification, concurrency, persistence, and immutable
  transaction-snapshot tests.
- No API or UI files were changed.

## 2. Requirements and transitions implemented

- Verification requires an active authenticated session, challenge ownership,
  the challenge's original session, the current normalized phone number, and
  `OtpPurpose.AccountNameChange`.
- A provider result is accepted only when its normalized phone equals the
  account's current normalized phone.
- Incorrect attempts are durably recorded; the fifth incorrect attempt moves
  the challenge from `Active` to `Locked`.
- Expired challenges move from `Active` to `Expired` without invoking the OTP
  provider.
- Successful verification moves the challenge from `Active` to `Verified`,
  applies the pending first and last name to all same-phone buyer/seller roles,
  sets their common `NameChangedAt`, updates all active sessions, records one
  verification attempt, and appends one immutable audit event in one
  `SaveChangesAsync` transaction.
- Exact retries use the challenge-bound code HMAC and verification idempotency
  key. Reusing a key with another code is rejected.
- Persistence-conflict retries reload authoritative state and never invoke the
  provider more than once within one handler execution.
- `NameChangedAt` is the eligibility basis, so a different challenge cannot
  overwrite a just-completed name during the two-calendar-month cooldown.
- Existing paid/offer transaction party snapshots, canonical snapshot JSON,
  integrity hashes, terms snapshots, and transaction audit history remain
  unchanged. Offers created later capture the new current account name.

## 3. Tests added or updated

- Added 11 atomic verification/failure/replay/authorization tests.
- Added 2 SQLite relational concurrency tests:
  - exact concurrent requests converge on one authoritative result;
  - distinct concurrent challenges produce one winner and one cooldown loser.
- Added 2 immutable transaction snapshot tests, covering the renamed account
  as both buyer and seller and a later offer capturing the updated name.
- Updated persistence/security tests for audit HMAC references and
  `NameChangedAt` concurrency metadata.

Fresh verification:

```text
Focused Task 5 tests: 15 passed, 0 failed, 0 skipped
Application test suite: 432 passed, 0 failed, 7 skipped
git diff --check: passed
```

The seven skipped tests are existing PostgreSQL environment-gated tests. The
Task 5 SQLite relational concurrency tests ran and passed. `dotnet test`
compiled the Domain, Application, Infrastructure, and Application.Tests
projects successfully.

The full solution/standalone build command did not produce a compiler
diagnostic and remained silent until the five-minute build timeout/cancel
path. This repository includes mobile workload targets unavailable to this
gate; completion relies on the successful non-mobile compile performed by
`dotnet test` and the passing application suite.

## 4. Assumptions

- Buyer and seller records using the same normalized phone represent the same
  user identity and must be updated together even when the request subject
  carries only one role.
- Only sessions that are active at commit time and still have the account's
  normalized phone are synchronized; expired, revoked, and mismatched-phone
  sessions remain unchanged.
- Audit columns named `OldName` and `NewName` retain their existing schema but
  now hold 64-character HMAC references, because Task 5 explicitly forbids raw
  names in this audit.
- A successful ordinary completion uses exactly one unit-of-work save.

## 5. Open decisions or provider capabilities

- PostgreSQL migration/concurrency integration tests require the repository's
  configured PostgreSQL test environment and were not available here.
- The handler does not re-invoke the OTP provider after a persistence
  conflict. Cross-request behavior still depends on the provider's documented
  verification/idempotency guarantees; no new provider reservation protocol or
  schema was introduced in Task 5.

## 6. Next smallest vertical slice

Expose the already-tested application workflow through the account-name API,
map cooldown/OTP errors to the approved modal copy, apply endpoint rate limits,
and connect the existing two-field account form and reusable OTP component.
