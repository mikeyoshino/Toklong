# Final Fix Report — Verified Email Change

## Status

The final verified-email-change fix wave is implemented and verified.

The changes close five remaining gaps:

1. distinct concurrent wrong-code submissions are counted from fresh database
   state instead of losing one attempt to optimistic-concurrency recovery;
2. a fresh top-level request cannot replace an open challenge during its
   first 60 seconds, while an exact idempotent replay remains available;
3. definitive sender and validation failures no longer trap the mobile flow
   behind an unusable idempotency key or superseded source challenge;
4. one- and two-character email local parts always hide at least one real
   character; and
5. verification accepts display whitespace around/grouping six ASCII digits,
   while malformed values are rejected before digesting or consuming an
   attempt.

No transaction, payment, fulfillment, dispute, refund, payout, agreement
snapshot, or provider receipt state was changed.

## 1. What changed

### Concurrent verification persistence

- Added a bounded five-attempt persistence loop to
  `VerifyBuyerEmailChangeHandler`.
- After a recognized concurrency or unique-key conflict, the handler clears
  tracked state, reloads the challenge and buyer, rechecks ownership and exact
  replay, and applies the submission to current state.
- The final recovery remains fail-closed: only an exact durable replay is
  returned after the bound is exhausted.
- No sleep, polling delay, unbounded loop, or process-local lock was added.

This makes five intentionally serialized concurrent wrong submissions produce
five durable attempt rows, `IncorrectAttempts == 5`, one `Locked` transition,
and one lock audit.

### Fresh-request cooldown

- Exact initial-request replay is checked before cooldown enforcement.
- A different request key against an open `PendingSend` or `Active` challenge
  now throws `RequestCooldownException` until the existing
  `ResendAvailableAt`.
- The exception carries the exact remaining interval.
- The mobile API maps it to `429` with `Retry-After`.
- At or after the stored timestamp, the old challenge is superseded and the
  fresh request proceeds normally.

### Definitive failure and idempotency recovery

- Step 1 retains its request idempotency key only for ambiguous transport
  failures (`HttpRequestException`, timeout, or cancellation classified as a
  network failure).
- A definitive server rejection, including sender failure, clears the key so
  an explicit retry uses a fresh request.
- Verification and resend follow the same rule: ambiguous transport retains
  the key; a received definitive rejection clears it.
- A resend sender failure is terminal for the local challenge flow. Confirm
  and resend actions are disabled, the countdown stops, and the primary
  recovery action starts a new top-level email-change request.
- The backend already persisted failed sends as `SendFailed`; regression tests
  now prove that a fresh top-level request succeeds after both initial-send
  and resend-send failures.

### Short-local masking

- The masking generator reveals at most two characters and never reveals the
  entire real local part.
- `a@example.com` becomes `••@example.com`.
- `ab@example.com` becomes `a••@example.com`.
- Longer local parts retain the existing first-two-character presentation.
- Domain validation now accepts a mask beginning at position zero, while
  rejecting any mask that exposes all real local characters.
- Challenge, API response, and audit tests assert the same redacted value.

### Verification-code boundary

- Permitted display whitespace is removed server-side.
- The authoritative value must then be exactly six ASCII digits.
- Full-width digits, letters, punctuation, and wrong lengths receive
  `กรอกรหัสยืนยัน 6 หลัก`.
- Validation occurs before `IEmailVerificationCodeService.Digest`.
- Malformed values create no verification-attempt row, consume no attempt,
  and do not change the confirmed buyer email.

## 2. Requirements and state transitions implemented

```text
request with exact original key + destination
  -> exact replay, including during cooldown

fresh request while PendingSend/Active and now < ResendAvailableAt
  -> 429 + exact remaining Retry-After
  -> existing challenge unchanged

fresh request at/after ResendAvailableAt
  -> existing challenge Superseded
  -> replacement PendingSend
  -> Active only after sender acceptance

definitive resend sender failure
  -> source remains Superseded
  -> replacement SendFailed
  -> mobile requires a fresh top-level request

ambiguous transport failure
  -> client keeps the same idempotency key for safe replay

distinct concurrent wrong verifications
  -> reload after each persistence conflict
  -> each accepted submission adds one durable attempt
  -> fifth attempt changes Active to Locked and writes one audit

malformed verification input
  -> rejected before HMAC/digest and persistence
  -> Active challenge and remaining attempts unchanged
```

Buyer ownership checks, exact replay checks, audit behavior, sender acceptance,
and confirmed-email activation remain server authoritative.

## 3. Tests added or updated

The full suite increased by 29 test cases, from 758 to 787.

Coverage added or strengthened includes:

- two distinct concurrent wrong codes are both counted;
- five concurrent distinct wrong codes lock exactly once;
- same- and different-destination fresh requests observe the active cooldown;
- a fresh request observes a `PendingSend` cooldown;
- exact replay remains available during cooldown;
- replacement works after the authoritative timestamp;
- API `429` and `Retry-After` behavior;
- initial and resend sender failures allow a fresh top-level request;
- mobile request, verification, and resend key lifecycle for definitive versus
  ambiguous failures;
- resend sender failure disables stale challenge actions and routes to
  `ChangeEmailPage`;
- one- and two-character local-part masks in domain, application, API, and
  audit persistence;
- grouped ASCII verification digits;
- malformed length, punctuation, letter, and full-width digit rejection before
  digest or attempt persistence; and
- the existing buyer/network rate-limit test now advances the challenge past
  domain cooldown before asserting network-partition independence.

### RED evidence

- The two new relational concurrency tests initially failed because the losing
  writers returned the non-exact-replay error and their attempts were absent.
- Three handler cooldown cases initially accepted a fresh replacement; the API
  case returned `200` instead of `429`.
- Mobile sender recovery initially reused the definitive failed request key and
  left resend confirmation/retry actions enabled.
- Definitive wrong-code and cooldown responses initially reused their operation
  keys instead of reserving replay only for ambiguous transport.
- Short-local RED selection: 3 of 4 domain cases failed and both application
  masking cases exposed the full real local part.
- Verification input RED selection: grouped whitespace was treated as a wrong
  attempt, and all four malformed forms reached digest/attempt handling.

### Focused GREEN evidence

```text
BuyerEmailChangeConcurrencyTests
  7 passed, 0 failed

concurrent two/five contender selection
  2 passed, 0 failed

cooldown handler selection
  5 passed, 0 failed

cooldown API selection
  1 passed, 0 failed

sender/idempotency mobile and client selection
  6 passed, 0 failed

short-local domain selection
  4 passed, 0 failed

short-local and verification-input handler selection
  7 passed, 0 failed

short-local and verification-input API selection
  7 passed, 0 failed
```

## 4. Full verification

### Tests and type checking

```text
Toklong.Domain.Tests
  109 passed, 0 failed, 0 skipped

Toklong.Application.Tests
  233 passed, 0 failed, 0 skipped

Toklong.Api.Tests
  64 passed, 0 failed, 0 skipped

Toklong.Crm.Tests
  45 passed, 0 failed, 0 skipped

Toklong.Mobile.Core.Tests
  336 passed, 0 failed, 0 skipped
```

Total: **787 passed, 0 failed, 0 skipped**.

The first full API run found one stale test setup: its cross-network request
was still inside the newly enforced domain cooldown. After the test advanced
the stored challenge timestamp, the focused case and full 64-test API project
passed.

### iOS builds

Signed device target:

```text
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj \
  -f net10.0-ios \
  -p:RuntimeIdentifier=ios-arm64 \
  --no-restore
```

Result: build succeeded with 0 errors and the same 3 known warnings:

- obsolete `IMediaPicker.PickPhotoAsync`;
- the profile lacks `aps-environment`; and
- the profile lacks `com.apple.developer.associated-domains`.

The sandboxed signing attempt could not enumerate the keychain. The authorized
host-access rerun resolved the configured Apple Development identity and
provisioning profile and completed successfully.

Simulator target:

```text
dotnet restore src/Toklong.Mobile/Toklong.Mobile.csproj \
  -p:TargetFrameworks=net10.0-ios \
  -p:RuntimeIdentifier=iossimulator-arm64

dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj \
  -f net10.0-ios \
  -p:RuntimeIdentifier=iossimulator-arm64 \
  --no-restore
```

Result: restore succeeded; the final build succeeded with 0 warnings and
0 errors. The sandboxed build first failed in MSBuild task-host IPC with
`MSB4216`/`MSB4027`; the single authorized host-process rerun completed in
four seconds.

### Migration verification

```text
DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 \
dotnet ef migrations has-pending-model-changes \
  --no-build \
  --project src/Toklong.Infrastructure/Toklong.Infrastructure.csproj \
  --startup-project src/Toklong.Infrastructure/Toklong.Infrastructure.csproj
```

Result:

```text
No changes have been made to the model since the last migration.
```

The regenerated 71-line migration range creates only:

- `buyer_email_change_challenges`;
- `buyer_email_change_audit_events`;
- `buyer_email_verification_attempts`;
- their foreign keys and indexes; and
- the EF migration-history entry.

There is no `UPDATE`, `DELETE`, or `ALTER` DML/DDL statement and no transaction,
payment, refund, payout, agreement, or snapshot table reference.

### Diff, privacy, and secret checks

- `git diff --check`: exit `0`.
- Literal production-source scan for `"123456"`, `new@example.com`, and
  `private@example.com`: no matches outside the approved Development-only
  exclusions.
- The broader `123456` scan found only substrings in documented phone and
  carrier examples.
- Exact `CodeDigest` or `PendingEmail` property scan in `MobileApi.cs`: no
  matches.
- Credential-pattern scan across every file in this fix wave: no matches.
- Raw password, recovery-code, private-key, wallet-secret, or reusable
  credential field scan across production files in this wave: no matches.

The pre-existing unrelated dirty and untracked seller, transaction, simulator,
splash, project, and `MobileApi.cs` work remains outside this fix wave and is
not staged.

## 5. Assumptions

- `ResendAvailableAt` remains the single authoritative 60-second boundary for
  both resend and fresh-request replacement.
- Unicode characters classified by .NET as whitespace are presentation
  separators and may be removed; the remaining verification characters must
  be ASCII `0` through `9`.
- Five bounded persistence attempts are sufficient for the required five
  concurrent contenders. Higher contention fails closed rather than spinning
  indefinitely.
- A received HTTP/domain rejection is definitive. Only a transport failure
  without an authoritative response is eligible to reuse the same client
  idempotency key.
- Buyer email remains intentionally non-unique under the approved product
  design.
- No schema change is required for this wave.

## 6. Open decisions and blocked provider capabilities

- Select and configure the production transactional-email provider and sender
  identity.
- Complete sender-domain authentication and define bounce/suppression
  handling.
- Resolve the existing iOS provisioning mismatch by granting the requested
  push and associated-domain entitlements or explicitly changing the app
  capability decision, then regenerate and inspect the profile.
- Bring the configured physical iPhone online before claiming install,
  secure-storage cold-resume, or VoiceOver evidence.
- Approve production rolling/distributed limits if the single-instance
  defaults are not sufficient for deployment topology.

## 7. Next smallest vertical slice

Implement and verify the approved production transactional-email adapter
against a provider sandbox without changing the now-verified domain, API, or
mobile contracts.
