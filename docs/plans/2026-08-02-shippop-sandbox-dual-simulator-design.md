# SHIPPOP Sandbox Dual-Simulator Design

**Date:** 2026-08-02  
**Status:** Approved direction; implementation pending

## Context

`scripts/run-local-dual-sim.sh` currently starts the mobile simulators through
`scripts/run-stripe-test-api.sh`. That backend launcher forces
`ShippingQuotes__Provider=Development` for both the API and Worker and enables
`DevelopmentDemoSimulation`. Supplying a SHIPPOP test API key therefore does not
cause the local mobile flow to call SHIPPOP.

The requested first slice is the real SHIPPOP sandbox physical-shipping flow:
quote, outbound booking, confirmation, tracking, and label download. Real
SHIPPOP counter QR is deliberately excluded until its sandbox response contract
has been observed and certified.

## Goals

- Add an explicit `ShippopSandbox` shipping mode to the existing dual-simulator
  launcher.
- Configure the API and Worker identically so both processes call the SHIPPOP
  sandbox.
- Exercise the existing buyer-payment and seller-fulfillment workflow without
  changing production behavior or domain state transitions.
- Fail early and clearly when required sandbox configuration is missing.
- Never print or commit SHIPPOP credentials.
- Prevent development shipping simulation from advancing real sandbox
  shipments.

## Non-goals

- Enabling SHIPPOP Counter QR.
- Changing buyer payment, dispute, delivery-verification, or payout rules.
- Falling back to the Development shipping provider after a SHIPPOP error.
- Enabling return, insurance, or optional-protection capabilities without
  separate sandbox evidence.
- Changing production SHIPPOP service flags or certification references.

## Considered approaches

### 1. Add a mode to the existing launcher — selected

Use `TOKLONG_SHIPPING_MODE=ShippopSandbox` to select sandbox configuration while
keeping the existing default `Development` mode. This keeps one documented
entry point for the two simulators and avoids duplicating simulator lifecycle
logic.

### 2. Add a separate SHIPPOP launcher

This isolates the sandbox command but duplicates backend and simulator startup
behavior, increasing the chance that the two launchers drift.

### 3. Require manual environment overrides

This avoids changing scripts but is error-prone because the same settings must
be supplied consistently to both the API and Worker, and development simulation
must be disabled manually.

## Configuration contract

The existing launcher remains in Development shipping mode unless the caller
sets:

```text
TOKLONG_SHIPPING_MODE=ShippopSandbox
```

Sandbox mode requires these environment variables:

```text
SHIPPOP_API_KEY
SHIPPOP_ACCOUNT_EMAIL
SHIPPOP_SERVICE_CODE
SHIPPOP_QUOTE_SIGNING_SECRET
```

Rules:

- `SHIPPOP_API_KEY` and `SHIPPOP_ACCOUNT_EMAIL` must be non-empty.
- `SHIPPOP_SERVICE_CODE` must be one allow-listed TOKLONG service code.
- `SHIPPOP_QUOTE_SIGNING_SECRET` must contain at least 32 characters and must
  differ from the API key.
- The launcher supplies the exact approved sandbox base URL and the explicit
  insecure-HTTP development opt-in; callers do not choose an arbitrary host.
- Secrets are passed only through process environment and are never echoed.
- API and Worker receive the same provider, SHIPPOP credentials, service
  profile, and shared absolute Data Protection key path.

The intended invocation is:

```bash
TOKLONG_SHIPPING_MODE=ShippopSandbox \
SHIPPOP_API_KEY='...' \
SHIPPOP_ACCOUNT_EMAIL='...' \
SHIPPOP_SERVICE_CODE='EMST' \
SHIPPOP_QUOTE_SIGNING_SECRET='at-least-32-characters-long-secret' \
./scripts/run-local-dual-sim.sh
```

## Runtime behavior

In the default Development mode, current behavior remains unchanged.

In `ShippopSandbox` mode, the backend launcher will:

1. Validate all required values before starting Stripe, API, or Worker
   processes.
2. Set `ShippingQuotes__Provider=Shippop` for both API and Worker.
3. Configure the SHIPPOP sandbox base URL and insecure-HTTP opt-in for both
   processes.
4. Enable only quote, outbound booking, confirmation, and operation lookup for
   the selected service profile.
5. Keep return, insurance, optional protection, and Counter QR disabled.
6. Set `DevelopmentDemoSimulation__Enabled=false`.
7. Use one absolute, local-only Data Protection key directory for API and
   Worker so protected provider data can be exchanged safely.
8. Stop startup on invalid configuration instead of silently using the
   Development provider.

## User flow under test

1. The seller creates a physical-product agreement.
2. TOKLONG requests real sandbox shipping quotes for the selected service.
3. The buyer reviews the immutable transaction snapshot and pays with Stripe
   Test Mode.
4. Only after a verified Stripe webhook confirms payment does the seller see
   the fulfillment action.
5. The Worker creates and confirms the outbound SHIPPOP sandbox shipment.
6. The seller sees the resulting shipment/tracking status and can download the
   label when SHIPPOP provides one.
7. Tracking is refreshed through the existing idempotent operation workflow.
8. Payout remains blocked until verified delivery plus the configured
   inspection rules, or an allowed buyer/manual confirmation path, is
   satisfied.

Counter QR remains visibly unavailable in this slice; the UI must not fabricate
or substitute a development QR while sandbox mode is active.

## Failure handling

- Missing or invalid local configuration stops the launcher with a Thai error
  naming the variable, never its value.
- SHIPPOP request failures remain provider failures and are retried only through
  the existing bounded/idempotent operation mechanism.
- The launcher never falls back to Development after sandbox startup begins.
- Stripe webhook verification remains required for payment success.
- A failed or unverified tracking lookup cannot mark delivery or start the
  72-hour payout clock.

## Verification

Implementation will add or update tests covering:

- Default launcher behavior remains Development.
- Sandbox mode rejects missing credentials, unsupported service codes, short
  signing secrets, and a signing secret equal to the API key.
- Sandbox mode sends matching SHIPPOP configuration to API and Worker.
- Sandbox mode disables development simulation.
- Only the selected service profile receives the approved sandbox capabilities.
- Counter QR, return, insurance, and optional protection remain disabled.
- Shell syntax checks pass for changed scripts.
- Existing type checking, unit/integration, state-transition, authorization,
  payment-webhook, carrier-idempotency, and payout-blocking tests continue to
  pass.

The real sandbox smoke test will be run only with user-supplied test credentials
and will avoid recording raw request/response secrets in the repository.

## Rollout and rollback

This is an opt-in local-development mode. Not setting
`TOKLONG_SHIPPING_MODE=ShippopSandbox` preserves current behavior. Rollback is
therefore removing the opt-in or reverting the launcher changes; no production
configuration or database migration is involved.

## Open provider capability

Real SHIPPOP Counter QR remains blocked pending sandbox contract observation and
certification of the response field, representation, expiry, and counter-scan
behavior. It will be handled as a separate vertical slice.
