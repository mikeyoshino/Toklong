# SHIPPOP Full Sandbox API Audit Design

**Date:** 2026-08-02

**Status:** Approved

**Scope:** Opt-in certification of every SHIPPOP endpoint used by the current
physical-shipping provider

## 1. Objective

Add a repeatable, fail-safe certification mode that exercises one complete
synthetic SHIPPOP Sandbox shipment through every provider endpoint currently
used by TOKLONG:

1. `pricelist/`;
2. `booking/`;
3. `confirm/`;
4. `label/`;
5. `tracking/`; and
6. `cancel/`.

The audit proves that the configured Sandbox account can call the current
provider contract over HTTPS. It does not prove a production carrier scan,
delivery, payment, refund, dispute, or payout flow.

## 2. Approved approach

Extend the certification tooling on current `main` rather than using a
throwaway script or the unfinished counter-QR worktree. The audit must call the
same `ShippopShippingProvider` implementation used by the application so that
request construction, response parsing, money conversion, validation, and
error handling are tested together.

The mode is disabled by default and runs only when both explicit gates are set:

```text
SHIPPOP_CERTIFY=1
SHIPPOP_CERTIFY_MUTATIONS=1
```

Normal unit and integration test runs must skip it. The only permitted Sandbox
origin is `https://mkpservice.shippop.dev`; insecure HTTP and every other
origin fail before a credential or synthetic customer record is sent.

## 3. Lifecycle

The audit creates at most one synthetic outbound shipment per run and performs
the following ordered checks.

### 3.1 Price list

Call `pricelist/` with fixed synthetic Thai origin, destination, parcel weight,
and dimensions. A passing result requires:

- at least one service supported by TOKLONG's current allow-list;
- a non-empty provider service code and display name; and
- a positive integer shipping price in satang after normalization.

The audit selects one returned supported service deterministically. If no
supported service is available, the lifecycle stops before mutation.

### 3.2 Booking

Call `booking/` once for the selected service, using one unique operation
reference and synthetic names, addresses, and phone numbers. A passing result
requires the provider identifiers needed by later operations and an exact
match to the selected service.

The audit must never retry booking automatically after a timeout, malformed
response, transport interruption, or another outcome where SHIPPOP may have
accepted the mutation.

### 3.3 Confirmation

Call `confirm/` once for the exact booking. A passing result requires a
confirmed provider shipment with a safe courier tracking identifier associated
with the same booking/service.

The audit must never retry confirmation automatically when the outcome is
unknown. A confirmation failure blocks label and tracking checks unless the
provider contract supplies enough authoritative identifiers for a safe read or
cleanup operation.

### 3.4 Label

Call `label/` for the confirmed shipment. A passing result requires bounded,
non-empty label HTML with an HTML document marker. The audit validates it in
memory only. It must not save, print, screenshot, return, or log the label or
any embedded barcode/QR content.

### 3.5 Tracking

Call `tracking/` for the confirmed courier tracking identifier. A passing
result requires a recognized, well-formed provider response associated with
the shipment. Sandbox status may legitimately remain pre-scan or pending; that
is not treated as delivered and must not create a delivery timestamp, buyer
inspection window, transaction transition, or payout eligibility.

### 3.6 Cancellation and cleanup

After confirmation has produced an authoritative courier tracking identifier,
attempt `cancel/` exactly once from a `finally`-style cleanup path whether later
checks pass or fail. A passing cleanup requires a provider response that
unambiguously confirms cancellation or an already-cancelled equivalent defined
by the certified contract.

If the lifecycle stops before an authoritative courier tracking identifier is
available, cancellation is `not_attempted`. It is safe only when the provider
contract proves that no confirmed carrier service exists. Otherwise the run is
`cleanup_required` and requires operator review.

If cancellation is rejected, times out, or has an unknown outcome, the final
result is failed with `cleanup_required`. The audit must stop and tell the
operator not to rerun it until the orphaned Sandbox record has been reviewed.
It must not retry cancellation blindly.

## 4. Failure and dependency rules

The report uses four statuses:

- `pass`: the endpoint contract was exercised and validated;
- `fail`: the endpoint was called and returned a definite invalid or rejected
  outcome;
- `blocked`: an earlier prerequisite failed or had an unknown mutation
  outcome, so calling this endpoint would be unsafe or meaningless; and
- `cleanup_required`: cancellation did not finish with an authoritative safe
  outcome.

No downstream mutation is attempted after an uncertain mutation result. A
read-only check is performed only when the required provider identifiers are
authoritative and consistent. The process exit code is non-zero when any
required endpoint is not `pass` or cleanup is not confirmed.

## 5. Data and secret handling

The audit uses fixed synthetic customer data marked as testing data. It never
uses a real TOKLONG user, transaction, address, phone number, product photo, or
payment record. Credentials come from the existing runtime configuration and
are never accepted as command-line arguments.

Console output and checked-in evidence contain only:

- endpoint/capability name;
- sanitized status;
- sanitized reason code;
- cleanup status; and
- run time and non-sensitive environment label.

The audit must not output or persist API keys, account email, raw provider
requests/responses, names, addresses, phone numbers, booking or purchase IDs,
tracking numbers, label HTML, barcode/QR content, or reusable provider URLs.
Provider exceptions are mapped to an allow-listed sanitized reason rather than
printed verbatim.

## 6. Application and domain isolation

The audit calls the provider adapter directly and does not create or update:

- a TOKLONG agreement or paid transaction snapshot;
- payment, refund, dispute, delivery, or payout state;
- an application shipment or tracking-event record;
- a domain transition or audit event; or
- an analytics event representing consumer behavior.

This isolation is intentional because certification is an operator diagnostic,
not a consumer transaction. Existing application state-transition,
authorization, webhook, delivery verification, dispute blocking, and payout
rules remain unchanged.

## 7. Implementation boundaries

The implementation will:

- add a dedicated full-lifecycle certification mode to the existing SHIPPOP
  certification project and runner;
- update the certification endpoint guard to require the current HTTPS Sandbox
  origin;
- reuse production provider DTO parsing and validation;
- centralize sanitized result reporting and the single cleanup path;
- keep existing optional capability exercises separate; and
- remain skipped unless both explicit opt-in gates are present.

It will not merge or modify the separate counter-QR feature worktree, enable a
production SHIPPOP capability, add a new consumer UI, or change transaction
behavior.

## 8. Verification

Automated tests must verify before the live Sandbox exercise:

1. both opt-in gates are required;
2. only the approved HTTPS Sandbox origin is accepted;
3. HTTP, production, lookalike, path-confused, and credential-bearing URLs are
   rejected;
4. endpoint order and dependency blocking are deterministic;
5. booking, confirmation, and cancellation are never blindly retried;
6. cleanup runs once after a safe cleanup identifier exists, including when
   label or tracking validation fails;
7. no cancellation is attempted without the required safe identifier;
8. report output cannot contain provider identifiers, synthetic personal data,
   credentials, raw bodies, label content, or encoded artifacts; and
9. an incomplete endpoint or cleanup result produces a non-zero exit code.

The live run succeeds only when all six endpoint rows pass and cleanup is
authoritatively confirmed. A successful run is reported as capability evidence
for the configured Sandbox account at that moment, not as production
certification.

## 9. Assumptions and open provider limits

- The configured SHIPPOP credentials belong to the Sandbox account and permit
  price lookup, booking, confirmation, label, tracking, and cancellation.
- SHIPPOP Sandbox may not emit real carrier scans; therefore tracking contract
  validity can be checked, but delivery progression cannot be certified by
  this run.
- Cancellation semantics must be interpreted only from the current provider
  contract. An ambiguous response remains `cleanup_required`.
- Counter-QR availability is outside this audit because the current main
  provider does not expose a certified official counter-QR endpoint.
