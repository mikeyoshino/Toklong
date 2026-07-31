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

---

# Task 5 Implementer Report — Round 2 Registration Composition Fix

## 1. What changed

- Replaced the raw EF transaction in mobile registration with one outer
  `IAccountPhoneTransactionManager` lease selected by a no-tracking
  ticket-to-phone preflight.
- Made `CompleteMobileRegistrationHandler` acquire its own same-phone lease,
  re-read and revalidate the pending registration under that lease, and re-read
  the authoritative same-phone seller before creating a buyer.
- A new buyer now inherits an existing seller's structured first and last name.
  A legacy seller without both structured fields falls back to the validated
  submitted registration name.
- Made the production manager scoped and same-phone reentrant. Npgsql retains
  its transaction-scoped advisory lock; SQLite runs the same ownership state
  machine with a real relational transaction for deterministic composition
  tests.
- Added explicit lease ownership rules: same-phone-only nesting, LIFO close,
  one physical outer commit, double-commit rejection, poison-on-uncommitted
  participant, rollback on uncommitted outer disposal, and safe cleanup after
  cancellation or disposal errors.
- Updated the in-memory API host's lock replacement to be scoped and reentrant,
  while adding separate tests against the real production manager so the test
  replacement cannot hide nested EF transaction misuse.

## 2. Requirements and transitions implemented

- Registration preflight selects only the normalized-phone lock key; it does
  not track, authorize, consume, or mutate a pending registration.
- After the outer lease is acquired, the handler authoritatively revalidates
  ticket existence, normalized phone, installation, idempotency key, expiry,
  terms version, and buyer absence under the serialized boundary.
- Handler and session service commits are nested participation acknowledgments.
  Only the endpoint-owned outer lease commits the database transaction.
- Buyer creation, terms acceptance, pending-ticket consumption, and first
  mobile-session creation now commit or roll back as one operation.
- Exact registration replay also participates in the same-phone boundary and
  cannot bypass current transaction ownership rules.

## 3. Tests added or updated

- Six real-manager SQLite tests cover normalized same-phone reentry, one live
  EF transaction, poisoned rollback, different-phone rejection, LIFO and
  double-commit misuse, canceled nested begin safety, and rejection of an
  externally owned raw EF transaction without adopting or corrupting it.
- Two real-manager registration composition tests prove seller-name
  inheritance, identical first-session display name, successful outer commit,
  and full rollback when the outer commit is omitted.
- A repository regression test proves ticket-to-phone preflight leaves no
  tracked entity and does not consume the ticket.
- Existing registration handler and HTTP authentication coverage was updated
  for the serialized participant model.

Fresh verification:

```text
Application tests: 451 passed, 0 failed, 8 skipped
API tests:          78 passed, 0 failed, 0 skipped
Domain tests:      193 passed, 0 failed, 0 skipped
EF pending model changes: none
git diff --check: passed
Focused real-manager tests after final cleanup: 8 passed
```

The eight Application skips require
`TOKLONG_POSTGRES_MIGRATION_TEST_CONNECTION`; they include the existing real
PostgreSQL advisory-lock and migration gates. The Domain command emitted only
`NU1900` because the sandbox could not reach NuGet's vulnerability feed.

## 4. Assumptions

- One verified normalized phone identifies one current account name across its
  buyer and seller roles.
- Mobile registration still requires valid submitted first and last names even
  when a same-phone structured seller name is authoritative and inherited.
- Production persistence remains PostgreSQL/Npgsql. SQLite support in the
  manager exists to exercise identical scoped ownership semantics in relational
  tests, without attempting PostgreSQL advisory SQL.

## 5. Open decisions or provider capabilities

- No new provider capability is required by this fix.
- Connected CI still needs
  `TOKLONG_POSTGRES_MIGRATION_TEST_CONNECTION` to execute the environment-gated
  PostgreSQL migration and advisory-lock tests.

## 6. Next smallest vertical slice

Connect the approved two-field account form to the hardened account-name
endpoints, reuse the existing six-digit OTP component, and show cooldown timing
only in the blocked-action error modal.

---

# Task 5 Implementer Report — Round 3 Authoritative Session Names

## 1. What changed

- Added a minimal `IMobileSessionAccountNameReader` that projects buyer and
  seller account id, phone, structured first name, structured last name, and
  display name without materializing account entities.
- The infrastructure reader executes explicit `AsNoTracking` scalar queries,
  so a role tracked by OTP verification before the phone lease cannot be
  identity-resolved as the post-lock current name.
- `MobileSessionTokenService.CreateAsync` now acquires or joins the phone lease
  before invoking that reader and no longer resolves current names through
  tracking buyer/seller repositories.
- `AttachSellerAsync` uses the same authoritative reader for seller
  authorization and both current role names after acquiring its phone lease.
- When both roles exist, every role must match the normalized session phone and
  structured first name, last name, and display name must agree. Buyer
  precedence is retained only after that consistency check; divergence fails
  before a session is created or mutated.

## 2. Requirements and transitions implemented

- OTP proof may still update and track account rows before session issuance,
  but those tracked instances are not accepted as current-name authority.
- Session creation serializes with account-name completion, then reads the
  committed role values directly from the database under the same-phone lease.
- Buyer-only and seller-only sign-in preserve their current role behavior.
  Dual-role sign-in requires one current normalized name across both roles.
- Seller attachment ignores the possibly stale `SellerProfile` name supplied
  by the earlier onboarding step and uses post-lock authoritative values.
- Existing session concurrency tokens continue to reject attachment races that
  also changed the tracked session row.

## 3. Tests added or updated

- Added an actual OTP sign-in composition theory for buyer-only and seller-only
  accounts. `VerifyMobileCodeHandler` and session issuance share one scoped
  SQLite DbContext, the old role remains tracked, another context commits the
  new name while issuance waits, and the persisted session must use the new
  name without refreshing the stale tracked entity.
- The test was observed RED in both cases against the tracking repository path:
  expected `สมศักดิ์ ใจดี`, received `สมชาย ใจดี`.
- Added a dual-role divergence test proving no session is issued. A mutation
  run with the consistency guard disabled failed because no exception was
  thrown; restoring the guard returned the test to green.
- Added seller-attachment coverage where buyer and seller are tracked with the
  old name before the lease and both committed names change before attachment
  proceeds.
- Updated registration composition and existing token tests to use the
  production current-name reader boundary.

Fresh verification:

```text
Focused API sign-in/session/registration: 17 passed, 0 failed
Focused Application sign-in:              10 passed, 0 failed
Application tests:                       451 passed, 0 failed, 8 skipped
API tests:                                82 passed, 0 failed, 0 skipped
EF pending model changes: none
git diff --check: passed
```

The eight Application skips still require
`TOKLONG_POSTGRES_MIGRATION_TEST_CONNECTION` and cover the environment-gated
PostgreSQL migration/advisory-lock tests.

## 4. Assumptions

- Buyer and seller roles sharing one normalized phone are one identity and must
  have the same structured/display name before a dual-role session is safe.
- Seller-only legacy rows may continue to use their stored display fallback;
  pairing such a divergent legacy row with a structured buyer fails safely
  until account data is reconciled.
- Scalar projection values are authoritative only because callers execute them
  after acquiring or joining the normalized-phone transaction boundary.

## 5. Open decisions or provider capabilities

- No new OTP, payment, or carrier provider capability is required.
- Connected CI still needs
  `TOKLONG_POSTGRES_MIGRATION_TEST_CONNECTION` for the skipped PostgreSQL gates.

## 6. Next smallest vertical slice

Connect the approved two-field account form to the hardened account-name
endpoints, reuse the existing six-digit OTP component, and show cooldown timing
only in the blocked-action error modal.
