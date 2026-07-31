# Task 6 Implementer Report — Authenticated Account Name-Change API

## 1. What changed

- Added the authenticated account-name endpoints for eligibility, pending
  recovery, request, resend, and verification.
- Built the account-name subject exclusively from the authenticated buyer/seller
  claims, session id, and verified phone; request bodies cannot select an
  account, session, or phone.
- Added consumer-safe name-change problem responses with stable codes,
  cooldown timestamps only for the blocked action, retry metadata, and no
  provider exception text.
- Added account-and-network-partitioned request and verification rate-limit
  policies. Durable send limits remain enforced by the application layer.
- Added explicit durable send-limit and resend-cooldown codes so the API does
  not infer them from provider details.
- Enabled the API test OTP provider's certified account-name capabilities and
  idempotent verification contract.

## 2. Requirements and transitions implemented

- `GET /api/mobile/me/name-change/eligibility` returns the server-authoritative
  allowed state and the exact next instant only when blocked.
- `GET /api/mobile/me/name-change` returns 204 or the authenticated session's
  own pending challenge; another account cannot discover it.
- Each state-changing route accepts the established caller-stable body
  idempotency key and delegates exact replay/conflict semantics to the
  application handler.
- Request, resend, and verify are endpoint-rate-limited by authenticated
  account plus protected network key, while the durable five-accepted-sends
  rule remains authoritative across scopes.
- Successful verification exposes only the verified structured/display name;
  transaction snapshots remain outside this API slice.

## 3. Tests added or updated

- Added `MobileNameChangeApiTests` covering all unauthenticated routes,
  first eligibility, request, pending resume, verification, profile refresh,
  blocked cooldown, cross-account isolation, exact request/verification
  replay, resend cooldown, durable five-per-day send limit, rate limiting,
  redaction, headers, and ignored body subject fields.
- Updated `MobileApiFactory` OTP fake for purpose-bound account-name request
  and idempotent verification evidence.

Fresh verification:

```text
Focused MobileNameChange API: 10 passed
Full API tests:               92 passed
Full Application tests:       451 passed, 8 skipped
Focused account-name app:      57 passed, 5 PostgreSQL-gated skipped
Focused account-name domain:   18 passed
git diff --check: passed
```

## Round 2 in progress

- Separated malformed idempotency input from idempotency conflict and mapped
  malformed OTP input to a 422 `name_change_code_invalid` contract.
- Exact outcome-unknown operation replays with a different submitted digest now
  translate to the typed idempotency-conflict boundary rather than generic
  invalid input.

The PostgreSQL-gated skips require `TOKLONG_POSTGRES_MIGRATION_TEST_CONNECTION`.
The domain restore emitted only `NU1900` because the environment could not
retrieve NuGet vulnerability data.

## 4. Assumptions

- The established mobile API convention keeps caller-stable idempotency keys
  in state-changing JSON DTOs; no additional header is required.
- A blocked eligibility response may include `NextAllowedAt` for the modal
  flow, while profile and pending responses never include cooldown timing.
- A 60-second request policy and a 10-minute verification policy are safe
  outer controls; the application remains the source of truth for the rolling
  send quota and per-challenge attempt rules.

## 5. Open decisions or blocked provider capabilities

- Production use remains gated by the OTP provider's certified account-name
  request lookup and verification lookup support, as documented by Task 5.
- An authorized operations audit-reader workflow remains out of scope.

## 6. Next smallest vertical slice

Add mobile contracts and error-to-copy mapping, then connect the two-field
account form and the shared six-digit OTP component without showing cooldown
timing proactively on the account screen.

---

## Round 1 review fixes

- Foreign and missing resend/verify challenges now return the same `404`
  `name_change_challenge_unavailable` problem response; regression coverage
  compares both complete contracts.
- Replaced account-name API message-substring mapping with bounded flow
  exceptions for input fields, unchanged names, idempotency conflicts,
  verification outcomes, and provider availability/throttle/unknown outcomes.
  Incorrect-code responses can carry authorized remaining attempts.
- Durable accepted-send counts now use canonical normalized phone only, so a
  buyer and seller sharing a verified phone share one rolling quota.
- Updated focused application expectations for the explicit flow outcomes.

Verification after the fix:

```text
Focused account-name Application: 57 passed, 5 PostgreSQL-gated skipped
Full API:                         92 passed
git diff --check: passed
```
