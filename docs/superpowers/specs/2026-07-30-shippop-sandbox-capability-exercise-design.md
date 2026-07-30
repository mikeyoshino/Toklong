# SHIPPOP Sandbox Capability Exercise Design

**Date:** 2026-07-30  
**Status:** Approved for written-spec review  
**Scope:** Development-only, real SHIPPOP Dev API certification for `EMST`,
`FLE`, `KRYX`, and `KRYS`

## 1. Goal

Give developers one safe, repeatable way to call the real SHIPPOP Dev API and
collect account-specific evidence for every supported service without changing
TOKLONG's deterministic local-development default or claiming that an
uncertified capability is production-ready.

The exercise attempts the complete provider path that can be verified safely:

```text
quote
  → unconfirmed booking
  → original-operation lookup
  → confirm
  → label
  → tracking
  → cancel when the provider permits it
  → return checks when the account contract supports them
```

The account owner has stated that this Dev Sandbox does not charge real money.
TOKLONG still limits mutations and prevents unattended repetition because
provider behavior, quotas, and cleanup rules are certification subjects rather
than assumptions.

## 2. Non-goals

- Do not enable any SHIPPOP capability in Production.
- Do not replace `ShippingQuotes:Provider=Development` as the normal local
  default.
- Do not commit an API key, account email, address fixture, password, raw
  provider response, or personal data.
- Do not weaken full-value parcel-insurance, trusted delivery-time, immutable
  snapshot, dispute, refund, or payout rules.
- Do not treat a successful HTTP status as complete service certification.
- Do not expose a mobile-client switch that can call SHIPPOP or advance
  shipping state.
- Do not replay an outcome-unknown booking, confirmation, cancellation, or
  return mutation merely to make a test pass.

## 3. Selected approach

Use a dedicated sandbox certification runner instead of globally enabling the
four service profiles in application settings.

The runner uses the existing `ShippopShippingProvider` boundary and real
`mkpservice.shippop.dev` endpoint, but runs only when the operator supplies an
explicit certification opt-in and secrets through environment variables. It
iterates over `EMST`, `FLE`, `KRYX`, and `KRYS` and writes a sanitized evidence
report outside source control.

This approach preserves three separate meanings:

1. **Supported by code** — the adapter recognizes the service code.
2. **Observed in Sandbox** — a dated exercise captured the actual account
   response contract.
3. **Enabled for Production** — every required capability passed, has a
   reviewable certification reference, and Production configuration was
   changed separately.

An observed or attempted capability never automatically becomes a Production
capability.

## 4. Configuration boundary

### 4.1 Normal application runtime

`appsettings.Development.json` continues to use the deterministic Development
shipping provider. Running the API, Worker, Web app, simulator scripts, or
payment smoke tests does not contact SHIPPOP unless the operator explicitly
selects the SHIPPOP provider.

Committed `appsettings.json` files keep all SHIPPOP service capabilities
disabled and keep credentials empty.

### 4.2 Sandbox exercise runtime

The runner requires these environment inputs:

```text
SHIPPOP_CERTIFY=1
SHIPPOP_BASE_URL=http://mkpservice.shippop.dev
SHIPPOP_ALLOW_INSECURE_HTTP=1
SHIPPOP_API_KEY=<secret>
SHIPPOP_ACCOUNT_EMAIL=<secret>
SHIPPOP_SYNTHETIC_ADDRESS_JSON=<absolute path>
SHIPPOP_EVIDENCE_DIRECTORY=<absolute path outside the repository>
```

`SHIPPOP_SERVICE_CODES` defaults to the exact ordered set
`EMST,FLE,KRYX,KRYS`. An operator may narrow it to a non-empty subset of those
four codes for diagnosis, but cannot add another code.

The HTTP opt-in is accepted only by the Development/certification runner. It
must remain rejected outside Development or Testing. The runner displays a
plain warning that the Dev endpoint lacks transport encryption and requires
synthetic, provider-approved contacts and addresses.

The quote-signing secret used by the isolated exercise is generated or
supplied separately from the API key. It is never printed and is not reused as
a Production secret.

## 5. Exercise modes and mutation safety

The runner has two explicit modes:

- `probe`: read-oriented quote and contract-shape checks for all selected
  services. This is the default.
- `mutate`: performs at most one active certification lifecycle per selected
  service after a separate `SHIPPOP_ALLOW_MUTATIONS=1` opt-in.

`mutate` mode uses a run identifier and one stable TOKLONG reference for each
service. Before sending a provider-changing request, it records the intended
operation and sanitized request fingerprint in the local evidence record.

If a response times out after the request may have reached SHIPPOP, the result
is `outcome_unknown`. The runner stops that service immediately. It does not
send the mutation again unless an implemented and observed provider lookup can
prove the original result or a documented provider idempotency guarantee makes
the replay safe.

The runner executes service codes sequentially. A failure for one service does
not enable or certify another service. Automatic retry is limited to definite
pre-send or safe read-only failures; mutations are never blindly retried.

## 6. Provider contract discovery

The current quote parser reads shipping `price` but assigns zero parcel
insurance because the verified field names, unit, rounding, code, and declared
value contract have not been established for this account.

The certification implementation therefore introduces a bounded,
provider-specific observation parser that may record only:

- whether candidate field names are present;
- JSON value kinds;
- normalized integer-satang values after an explicit unit rule is selected;
- service/carrier code matches;
- presence of provider references and timestamps; and
- sanitized status/error codes.

It must not persist raw request or response documents. It must not infer
insurance from the base shipping price. Insurance is considered passed only
when the response supplies a separate premium, an insurance code, and a
declared/covered value at least equal to the requested item value, using a
documented unit conversion.

After the real response contract is observed, the normal adapter may parse only
the exact certified fields. Unknown, missing, ambiguous, negative,
floating-point-unsafe, or insufficient coverage fails closed and omits that
quote from the enabled application flow.

All money remains integer satang. Decimal provider text is parsed with invariant
culture and converted using an explicit checked rounding rule; `double` and
`float` are prohibited.

## 7. Per-service capability matrix

Each selected service produces independent results for:

| Capability | Passing evidence |
|---|---|
| Quote | Matching service, positive base fee, known units |
| Insurance | Separate premium/code and full declared-value coverage |
| Unconfirmed booking | `force_confirm=0`, matching service/fee/reference |
| Operation lookup | Original result found by stable TOKLONG/provider reference |
| Duplicate safety | Repeated-safe behavior is documented or lookup prevents replay |
| Confirm | Exact reserved purchase confirmed once |
| Label | Provider 4×6 HTML returned and bounded by size/content rules |
| Tracking | Matching carrier and tracking references |
| First scan | Trusted provider occurrence timestamp is present |
| In transit | Status maps without starting the inspection window |
| Delivered/POD | `complete` includes a trusted carrier delivery timestamp |
| Missing POD | Fails closed without substituting poll time |
| Cancel | Unscanned shipment cancellation is observed and idempotent |
| Return | Distinct outbound/return references and trusted return tracking |
| Surcharge | Stable reference, type, amount, currency, occurrence time |
| Rate limit | Documented/observed limit and safe backoff behavior |

The report uses `pass`, `fail`, `blocked`, or `not_observed`; there is no
implicit success. `not_observed` and `blocked` keep the capability disabled.

## 8. Evidence output and secret handling

The runner writes one JSON result and one readable Markdown summary per run to
`SHIPPOP_EVIDENCE_DIRECTORY`. The directory must resolve outside the repository
and is created with user-only permissions where the platform supports it.

Allowed evidence fields are:

- opaque run ID;
- Dev host name;
- non-secret account/market reference when supplied;
- service code;
- endpoint action name;
- sanitized provider reference suffix or one-way digest;
- response status category;
- observed field names and normalized non-personal capability facts;
- start/end time;
- result and reason code;
- reviewer identity, added only when a human completes the review;
- certification date.

Forbidden evidence fields are:

- API key, password, authorization data, quote-signing secret;
- account email unless explicitly redacted;
- contact name, phone, street address, or full postal payload;
- raw request or raw response;
- printable label contents or barcode;
- reusable provider URL containing a credential.

Console output contains only progress, service, action, result, and the final
evidence path. Exception messages are sanitized before display or persistence.

## 9. Application enablement rules

The exercise never edits runtime capability flags automatically.

After human review, a separate configuration change may enable a capability for
one service only when:

- the evidence matrix contains every prerequisite for that capability;
- `CertificationReference` points to the reviewed record;
- `MaximumCoverageSatang` covers TOKLONG's active supported item-price maximum;
- handoff is certified as `DropOff`;
- booking has safe operation lookup or a provider idempotency guarantee;
- insurance covers the full supported value;
- API and Worker receive identical service profiles; and
- Production still uses HTTPS with `AllowInsecureHttp=false`.

Quote may be enabled before mutations only when its price and insurance
contract are fully certified. `BookOutboundEnabled` requires both
`InsuranceEnabled` and `OperationLookupEnabled`. Confirm, cancel, return,
tracking, and label capabilities remain independently fail-closed.

If the provider contract drifts, the affected service/capability is disabled
through configuration and an operations case is opened. Existing paid snapshots
remain immutable.

## 10. Error handling

- Invalid configuration exits before contacting SHIPPOP.
- Unsupported service codes exit before contacting SHIPPOP.
- Missing or unsafe evidence paths exit before contacting SHIPPOP.
- Authentication and provider validation failures are recorded as sanitized
  failures without retrying mutations.
- Rate limiting obeys `Retry-After` when safe; otherwise it stops that service.
- Oversized, malformed, deeply nested, or contract-ambiguous responses fail
  closed.
- Timeout after a possible mutation produces `outcome_unknown` and stops the
  service.
- Cleanup failure is visible in evidence and never changes a failed result to
  passed.

No provider observation changes a transaction, payment, refund, payout, or
delivery state in the normal TOKLONG database during certification.

## 11. Testing strategy

### Unit tests

- Parse allow-listed service-code lists and reject unknown/empty lists.
- Enforce `probe` as the default and require explicit mutation opt-in.
- Reject HTTP without the explicit Dev-only insecure-transport opt-in.
- Reject evidence output inside the repository.
- Redact secrets and personal fields from console and evidence serializers.
- Parse certified insurance money using integer satang and reject ambiguous
  units, missing code, or insufficient coverage.
- Stop after an outcome-unknown mutation without issuing a second request.
- Preserve per-service isolation when another service fails.

### Adapter contract tests

Use scripted HTTP responses to verify quote, insurance, booking, lookup,
confirm, label, tracking, cancellation, return, surcharge, malformed response,
rate limit, timeout, and duplicate behavior. Assert the exact number and order
of outgoing calls.

### Real Dev certification

The real test is opt-in and skipped explicitly when credentials are absent. It
runs against synthetic provider-approved data and produces evidence for all
four selected services. A skipped, blocked, or partially observed run is not a
pass.

### Regression checks

Run type checking plus unit/integration suites covering:

- shipping state transitions and authorization;
- durable-operation idempotency and lease recovery;
- trusted delivery time and the 72-hour inspection window;
- dispute and shipping exceptions blocking payout;
- no digital auto-release;
- payment webhook signature/idempotency/replay behavior; and
- secret and personal-data redaction.

## 12. Documentation changes

Implementation will update:

- `docs/SHIPPOP_CERTIFICATION_RUNBOOK.md` with the multi-service runner,
  probe/mutate commands, evidence format, and review process;
- `docs/08_SHIPPOP_PRODUCTION_FLOW.md` with the distinction between Sandbox
  observation and Production enablement;
- `docs/06_OPEN_DECISIONS.md` only with provider facts actually observed;
- `docs/05_ACCEPTANCE_TESTS.md` with executable Sandbox safety criteria; and
- `docs/08_IMPLEMENTATION.md` with server-only environment configuration.

Provider facts discovered during the exercise remain blockers until reviewed.
They must not exist only in terminal output or chat.

## 13. Success criteria

The feature is complete when:

1. one command probes all four supported services against the real SHIPPOP Dev
   endpoint;
2. a separately opted-in command performs bounded mutations without blind
   retry;
3. sanitized, per-service evidence clearly distinguishes pass, fail, blocked,
   and not observed;
4. real response fields can be inspected without retaining secrets, personal
   data, or raw payloads;
5. the normal Development provider and all Production capability defaults
   remain unchanged;
6. no service is marked production-ready without full insurance, safe operation
   lookup, trusted POD time, and the required remaining capability evidence;
7. automated tests prove no duplicate mutation is sent after an unknown
   outcome; and
8. all required repository checks pass.

## 14. Assumptions and open provider questions

Assumptions:

- The supplied Dev credentials are authorized for synthetic certification.
- The account owner has confirmed that Dev Sandbox activity does not move real
  money.
- `EMST`, `FLE`, `KRYX`, and `KRYS` are the only service codes in this exercise.

Still requiring provider evidence:

- exact insurance fields, currency/unit, rounding, limits, exclusions, and
  claims flow;
- lookup by TOKLONG reference and duplicate semantics for each mutation;
- cancellation of unconfirmed bookings and natural expiry;
- trusted first-scan and POD timestamps;
- surcharge schema;
- return-booking contract;
- per-service drop-off behavior, label requirements, and rate limits.
