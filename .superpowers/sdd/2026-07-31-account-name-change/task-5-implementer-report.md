# Task 5 Implementer Report — Round 1 Review Fixes

## 1. What changed

- Added a durable `AccountNameVerificationOperation` that is committed before
  an OTP provider mutation. It binds the challenge, client idempotency key,
  submitted-code HMAC, stable provider verification key, expected phone,
  provider challenge, lifecycle status, and authoritative provider timestamps.
- Extended the OTP provider contract with idempotent verification,
  authoritative verification lookup, bound evidence, and an explicit
  `SupportsVerificationLookup` capability. Development and HTTP providers now
  implement that contract; production configuration cannot enable name change
  without certified lookup support.
- Added normalized-phone transaction serialization. The Npgsql implementation
  opens an explicit transaction and holds
  `pg_advisory_xact_lock(hashtextextended(normalized_phone, 0))` through
  authoritative reads, save, and commit.
- Made account-name completion, buyer registration, seller creation/ensure,
  mobile-session creation, and seller-session attachment participate in that
  serialized boundary. New roles and sessions re-read and inherit the current
  same-phone account name.
- Replaced irreversible new audit HMACs with an authenticated encrypted payload
  containing the separate old buyer name, old seller name, and new name.
  ASP.NET Core Data Protection uses purpose
  `Toklong.AccountNameAuditEvidence.v1`; consumer APIs receive only the writer
  interface.
- Added a data-preserving migration. Historical `OldName`/`NewName` digest
  columns are renamed to honest `LegacyOldNameDigest` and
  `LegacyNewNameDigest` columns, while new events use nullable `bytea`
  `ProtectedNameEvidence` plus `ProtectionVersion`.
- Pushed revoked/expired session predicates into the relational query, so
  inactive historical sessions and refresh-token hashes are not materialized
  into the completion DbContext.
- Preserved immutable transaction snapshots. Existing buyer and seller
  snapshots remain unchanged; a later buyer offer and later seller acceptance
  capture the new current name.

## 2. Requirements and transitions implemented

- Verification now follows claim → lookup/verify → serialized completion.
  Exact retries reuse one provider key and converge on one authoritative
  outcome. Same-key/different-code requests are rejected before provider
  access.
- A lost accepted response can be recovered by lookup without consuming the
  one-time code again, including a retry after the local challenge expiry when
  the provider completed verification before expiry.
- Evidence is accepted only when provider key, provider challenge, purpose,
  normalized phone, outcome, and timestamps match the durable operation and
  the challenge lifetime.
- Known verified evidence transitions the challenge to `Verified`, updates all
  current same-phone roles and active sessions, records the attempt, marks the
  operation `ProviderVerified`, and appends protected audit evidence in one
  database transaction.
- Known rejected evidence retains the existing `Incorrect`/`Locked` behavior
  and marks the durable operation `ProviderRejected`.
- Missing or invalid authoritative evidence leaves the operation pending and
  returns the safe unknown-outcome error; it never fabricates success.
- Same-phone role/session inserts cannot commit an older name across a
  concurrent completion because all current-name writers acquire the same
  transaction-scoped advisory lock.
- Existing paid transaction and offer snapshots, canonical JSON, integrity
  hashes, terms snapshots, and transaction audit events are never rewritten.

## 3. Tests added or updated

- Durable claim, lookup-first recovery, accepted-response loss after expiry,
  exact concurrent replay, same-key/different-digest, and mismatched-evidence
  tests.
- Development provider single-use concurrent idempotency/lookup test and HTTP
  provider keyed verification/response-loss lookup tests.
- Production configuration gate for authoritative verification lookup.
- Blocking same-phone seller and mobile-session insertion tests, plus an
  environment-gated PostgreSQL advisory-lock test using local and E.164 forms
  of the same phone.
- Protected audit Data Protection round-trip and ciphertext opacity tests.
- Migration/model/repository tests for the durable operation, restrictive
  foreign keys, legacy digest preservation, protected audit columns, and
  migration down metadata.
- Relational active-session test proving only unrevoked, unexpired rows are
  tracked and inactive rows remain untouched.
- Immutable snapshot coverage for both a later buyer offer and a later seller
  acceptance.

Fresh verification:

```text
Application tests: 444 passed, 0 failed, 8 skipped
API tests:         76 passed, 0 failed, 0 skipped
Domain tests:     193 passed, 0 failed, 0 skipped
Migration SQL generation: passed
EF pending model changes: none
git diff --check: passed
```

The eight Application skips require
`TOKLONG_POSTGRES_MIGRATION_TEST_CONNECTION`; they include the new real
PostgreSQL advisory-lock check. The Domain run emitted only `NU1900` because
the sandbox could not reach NuGet's vulnerability feed. A standalone
Infrastructure `dotnet build --warnaserror` process was cancelled after it
remained silent for 60 seconds; the fresh Application and API test commands
compiled Domain, Application, Infrastructure, API, and both test assemblies
without compiler warnings.

## 4. Assumptions

- Production account persistence is PostgreSQL/Npgsql. Non-PostgreSQL API test
  hosts explicitly replace the production transaction manager with an
  in-process keyed test lock.
- Buyer and seller records sharing one normalized phone are one account
  identity and must expose one current name.
- The deployed ASP.NET Core Data Protection key ring is retained and restricted
  under the repository's existing production key-storage controls.
- Provider verification lookup is authoritative for the stable provider key
  and returns the bound evidence shape documented by the HTTP contract.

## 5. Open decisions or provider capabilities

- A real OTP provider must implement and certify
  `POST v1/otp/verifications` with idempotency and
  `GET v1/otp/verifications/by-request/{key}` before
  `Otp:AccountNameChangeVerificationLookupEnabled` may be enabled. Repository
  defaults remain disabled.
- The PostgreSQL migration and advisory-lock tests could not execute without
  the external connection environment; they are present for the connected CI
  gate.
- Authorized audit-reader UI/API, reader authorization, and retention workflow
  remain outside this task. The consumer API registers no audit reader.

## 6. Next smallest vertical slice

Expose the hardened workflow through the account-name endpoints, map cooldown,
OTP, and unknown-outcome errors to the approved modal copy, then connect the
two-field account form and reusable OTP component without exposing the next
eligible change date on the normal account screen.
