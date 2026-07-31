# Account Name Change Round 1 Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Task 5 provider-safe across concurrency/process loss, serialize
all same-phone current-name writers, retain protected recoverable audit names,
and query only active sessions.

**Architecture:** Persist a verification operation before provider mutation,
then reconcile keyed provider evidence and complete under a PostgreSQL
transaction-scoped phone advisory lock. Store audit names in a purpose-bound
Data Protection payload and expose only the encryption writer to application
handlers.

**Tech Stack:** .NET 10, C#, EF Core 10, Npgsql/PostgreSQL advisory locks,
SQLite relational tests, ASP.NET Core Data Protection, xUnit.

## Global Constraints

- Do not add Task 6 API endpoints or UI behavior.
- Never persist or log a raw OTP.
- Existing paid/offer transaction snapshots and hashes remain immutable.
- Successful completion updates every same-phone role and active session.
- Every provider-changing verification uses a durable caller-stable key.
- Use integer/date domain rules already approved for the two-month cooldown.

---

### Task 1: Durable verification operation and provider evidence

**Files:**
- Create: `src/Toklong.Domain/Accounts/AccountNameVerificationOperation.cs`
- Modify: `src/Toklong.Application/Abstractions/ISellerRepository.cs`
- Modify: `src/Toklong.Application/Abstractions/IAccountNameChangeRepository.cs`
- Modify: `src/Toklong.Infrastructure/Services/DevelopmentOtpVerificationProvider.cs`
- Modify: `src/Toklong.Infrastructure/Services/HttpOtpVerificationProvider.cs`
- Modify: `src/Toklong.Infrastructure/ProductionConfigurationValidator.cs`
- Test: `tests/Toklong.Application.Tests/Accounts/AccountNameChangeConcurrencyTests.cs`
- Test: `tests/Toklong.Application.Tests/Authentication/HttpOtpVerificationProviderTests.cs`
- Test: `tests/Toklong.Application.Tests/Security/ProductionConfigurationValidatorTests.cs`

**Interfaces:**
- Produces `OtpProviderVerificationEvidence` with verification key, challenge,
  purpose, normalized phone, outcome, requested time, and completion time.
- Adds keyed `VerifyIdempotentlyAsync` and `LookupVerificationAsync`.
- Adds repository lookup/add methods for `AccountNameVerificationOperation`.

- [ ] Write a single-use accepted-response-loss test whose retry recovers one
  verified result and never creates a rejected attempt.
- [ ] Run the test and verify it fails because keyed evidence/lookup is absent.
- [ ] Add provider contract and Development/HTTP implementations with strict
  evidence validation and production capability gating.
- [ ] Run provider and production-configuration tests to green.

### Task 2: Persist claim before provider mutation and reconcile completion

**Files:**
- Modify: `src/Toklong.Application/Features/Accounts/NameChanges/VerifyAccountNameChange.cs`
- Modify: `src/Toklong.Infrastructure/Persistence/AccountNameChangeRepository.cs`
- Modify: `src/Toklong.Infrastructure/Persistence/ToklongDbContext.cs`
- Test: `tests/Toklong.Application.Tests/Accounts/AccountNameChangeVerificationTests.cs`
- Test: `tests/Toklong.Application.Tests/Accounts/AccountNameChangeConcurrencyTests.cs`

**Interfaces:**
- Consumes the durable operation repository and keyed provider evidence.
- Produces the existing `VerifiedAccountNameChange` result.

- [ ] Write failing tests for pre-provider claim persistence, exact
  interleaving convergence, process-loss retry, same-key/different-digest, and
  mismatched evidence.
- [ ] Run focused tests and record the expected failures.
- [ ] Implement claim-first save, lookup-first recovery, strict evidence
  binding, and completion retry without generating another provider key.
- [ ] Run focused tests to green.

### Task 3: Transaction-scoped normalized-phone serialization

**Files:**
- Create: `src/Toklong.Application/Abstractions/IAccountPhoneTransactionManager.cs`
- Create: `src/Toklong.Infrastructure/Persistence/PostgresAccountPhoneTransactionManager.cs`
- Modify: `src/Toklong.Infrastructure/DependencyInjection.cs`
- Modify: `src/Toklong.Application/Features/Accounts/NameChanges/VerifyAccountNameChange.cs`
- Modify: `src/Toklong.Application/Features/Sellers/SellerOnboarding.cs`
- Modify: `src/Toklong.Application/Features/Buyers/BuyerOnboarding.cs`
- Modify: `src/Toklong.Api/Security/MobileSessionTokenService.cs`
- Test: `tests/Toklong.Application.Tests/Accounts/AccountNameChangeConcurrencyTests.cs`
- Test: `tests/Toklong.Application.Tests/Sellers/SellerOnboardingTests.cs`
- Test: `tests/Toklong.Api.Tests/Security/MobileSessionTokenServiceTests.cs`

**Interfaces:**
- Produces `BeginAsync(normalizedPhone)` returning an async transaction handle
  with explicit `CommitAsync`.

- [ ] Write relational blocking tests that insert a same-phone seller and an
  active session while completion is paused; assert both end with one current
  display name.
- [ ] Run tests and verify the old code permits stale names.
- [ ] Implement the Npgsql explicit transaction/advisory-lock manager.
- [ ] Make completion, seller creation/ensure, buyer registration, session
  creation, and seller attachment acquire the lock and re-read authoritative
  names before save/commit.
- [ ] Run concurrency, onboarding, and token-service tests to green.

### Task 4: Protected recoverable audit evidence

**Files:**
- Create: `src/Toklong.Application/Abstractions/IAccountNameAuditEvidenceWriter.cs`
- Create: `src/Toklong.Infrastructure/Security/AccountNameAuditEvidenceProtector.cs`
- Modify: `src/Toklong.Domain/Accounts/AccountNameChangeAuditEvent.cs`
- Modify: `src/Toklong.Application/Features/Accounts/NameChanges/VerifyAccountNameChange.cs`
- Modify: `src/Toklong.Infrastructure/DependencyInjection.cs`
- Modify: `src/Toklong.Infrastructure/Toklong.Infrastructure.csproj`
- Test: `tests/Toklong.Application.Tests/Accounts/AccountNameChangePersistenceTests.cs`
- Test: `tests/Toklong.Application.Tests/Accounts/AccountNameChangeVerificationTests.cs`

**Interfaces:**
- Produces an `aspnet-dp:v1` protected byte payload using purpose
  `Toklong.AccountNameAuditEvidence.v1`.
- Application DI exposes the writer only; no consumer API reader is
  registered.

- [ ] Write failing round-trip and ciphertext-not-plaintext tests.
- [ ] Run tests and verify HMAC evidence cannot satisfy recovery.
- [ ] Implement authenticated protection and honest entity fields.
- [ ] Update handler to protect old buyer, old seller, and new normalized name.
- [ ] Run audit and verification tests to green.

### Task 5: SQL active-session filtering and immutable seller snapshot

**Files:**
- Modify: `src/Toklong.Infrastructure/Persistence/MobileSessionRepository.cs`
- Modify: `tests/Toklong.Application.Tests/Accounts/AccountNameTransactionSnapshotTests.cs`
- Create: `tests/Toklong.Application.Tests/Persistence/MobileSessionRepositoryTests.cs`

**Interfaces:**
- Preserves `GetActiveByPartyAsync` while translating all active predicates.

- [ ] Write a failing repository test with active, expired, and revoked rows.
- [ ] Run it and verify inactive rows are materialized/tracked by the old query.
- [ ] Push `RevokedAt == null` and `ExpiresAt > now` into SQL.
- [ ] Add a later seller-acceptance snapshot test and run both tests to green.

### Task 6: EF migration, regression gates, report, and commit

**Files:**
- Create: `src/Toklong.Infrastructure/Persistence/Migrations/20260731110000_HardenAccountNameVerification.cs`
- Create: `src/Toklong.Infrastructure/Persistence/Migrations/20260731110000_HardenAccountNameVerification.Designer.cs`
- Modify: `src/Toklong.Infrastructure/Persistence/Migrations/ToklongDbContextModelSnapshot.cs`
- Modify: `.superpowers/sdd/2026-07-31-account-name-change/task-5-implementer-report.md`

**Interfaces:**
- Adds the verification-operation table and protected audit columns while
  retaining renamed legacy digests.

- [ ] Write migration/model assertions for constraints, indexes, bytea
  ciphertext, protection version, and legacy digest names.
- [ ] Run migration tests and verify the old schema fails.
- [ ] Add migration and update model snapshot/designer.
- [ ] Run focused Task 5 tests, full application/API tests, EF pending-model
  check, and `git diff --check`.
- [ ] Self-review provider safety, lock lifetime, audit access, snapshots, and
  absence of API/UI scope creep.
- [ ] Append the implementer report and commit with
  `fix: harden account name verification`.

### Task 7: Reentrant mobile registration transaction composition

**Files:**
- Modify: `src/Toklong.Application/Abstractions/IPendingMobileRegistrationRepository.cs`
- Modify: `src/Toklong.Application/Features/Authentication/CompleteMobileRegistration.cs`
- Modify: `src/Toklong.Api/Api/MobileApi.cs`
- Modify: `src/Toklong.Infrastructure/Persistence/PendingMobileRegistrationRepository.cs`
- Modify: `src/Toklong.Infrastructure/Persistence/PostgresAccountPhoneTransactionManager.cs`
- Test: `tests/Toklong.Application.Tests/Authentication/PhoneFirstAuthenticationTests.cs`
- Test: `tests/Toklong.Api.Tests/Security/MobileSessionTokenServiceTests.cs`
- Test: `tests/Toklong.Application.Tests/Persistence/AccountNameChangePostgreSqlTests.cs`

**Interfaces:**
- Adds `GetPhoneByTicketHashAsync` as a no-tracking, non-authorizing preflight.
- Preserves `IAccountPhoneTransactionManager.BeginAsync` while making its
  scoped implementation same-phone reentrant with outermost commit ownership.

- [ ] Write real-manager SQLite tests for same-phone nesting, different-phone
  rejection, LIFO/poison behavior, and one physical commit or rollback.
- [ ] Run them and verify the current manager fails on nested transactions.
- [ ] Implement the scoped lease state machine; keep Npgsql advisory SQL and
  use transaction-only SQLite behavior for relational composition tests.
- [ ] Run manager tests to green.
- [ ] Write a failing registration composition test with an existing
  seller-only structured name and first-session issuance under one outer lease.
- [ ] Replace the raw endpoint EF transaction with preflight plus outer phone
  lease. Make the handler acquire a nested lease, re-read pending/buyer/seller,
  inherit the seller name, save, and mark its nested participation committed.
- [ ] Run the composition test to green and add rollback evidence.
- [ ] Run focused registration/session tests, full Application/API tests, the
  EF pending-model check, and `git diff --check`.
- [ ] Append the Task 5 implementer report, self-review transaction ownership,
  then commit with `fix: compose mobile registration transaction`.

### Task 8: Authoritative post-lock mobile-session name reads

**Files:**
- Create: `src/Toklong.Application/Abstractions/IMobileSessionAccountNameReader.cs`
- Create: `src/Toklong.Infrastructure/Persistence/MobileSessionAccountNameReader.cs`
- Modify: `src/Toklong.Infrastructure/DependencyInjection.cs`
- Modify: `src/Toklong.Api/Security/MobileSessionTokenService.cs`
- Modify: `tests/Toklong.Api.Tests/Security/MobileSessionTokenServiceTests.cs`
- Modify: `tests/Toklong.Api.Tests/Security/MobileRegistrationTransactionCompositionTests.cs`
- Modify: `.superpowers/sdd/2026-07-31-account-name-change/task-5-implementer-report.md`

**Interfaces:**
- Produces `IMobileSessionAccountNameReader.GetBuyerAsync(Guid, ct)` and
  `GetSellerAsync(Guid, ct)`, returning a scalar
  `MobileSessionAccountName(Guid AccountId, string PhoneNumber,
  string FirstName, string LastName, string DisplayName)` or null.
- The infrastructure implementation uses `AsNoTracking` scalar projections;
  no account entity crosses this boundary.

- [ ] Add one buyer-only and one seller-only OTP sign-in composition case that
  first tracks the old role in the session DbContext, blocks session creation
  on the phone lease, commits a new name in another DbContext, releases the
  lease, and expects the issued and persisted session name to be the new
  literal value.
- [ ] Run the focused test and verify the tracking repositories return the old
  name, proving the race before changing production code.
- [ ] Add the minimal reader interface, no-tracking projections, and scoped DI
  registration. Replace token-service role repository reads after lease
  acquisition in both creation and seller attachment.
- [ ] Preserve buyer precedence only after every present role matches the
  normalized phone and both structured/display names agree; otherwise throw
  before creating or mutating a session.
- [ ] Add fail-safe dual-role divergence and authoritative attachment tests,
  then run the session/sign-in/registration tests to green.
- [ ] Run full Application and API tests, EF pending-model check, and
  `git diff --check`.
- [ ] Append the Round 3 Task 5 report and commit with
  `fix: read session names after phone lock`.
