# SHIPPOP Sandbox Dual-Simulator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an opt-in local dual-simulator mode that uses the real SHIPPOP Dev API for quote, outbound booking, confirmation, tracking, and label download while preserving the deterministic Development default.

**Architecture:** A small sourced Bash library owns validation and the exact .NET configuration projection for `Development` and `ShippopSandbox`. The Stripe backend launcher captures and removes operator-facing SHIPPOP variables before starting the Stripe listener, then exports only the server configuration to API and Worker. The dual-simulator launcher keeps its existing `launchctl` path for Development, but starts the sandbox backend as a directly inherited environment child so SHIPPOP secrets never become `launchctl` command arguments.

**Tech Stack:** Bash 3.2-compatible shell, .NET 10 configuration environment variables, ASP.NET Core API, .NET Worker, Stripe CLI Test Mode, SHIPPOP Dev API, xUnit 2.9, iOS Simulator.

## Global Constraints

- Preserve `Development` as the default when `TOKLONG_SHIPPING_MODE` is unset.
- Accept only the exact opt-in value `ShippopSandbox`; reject every other non-empty value.
- Use only `http://mkpservice.shippop.dev` with `Shippop__AllowInsecureHttp=true` in this Development-only mode.
- Require `SHIPPOP_API_KEY`, `SHIPPOP_ACCOUNT_EMAIL`, `SHIPPOP_SERVICE_CODE`, and `SHIPPOP_QUOTE_SIGNING_SECRET` before starting Stripe, API, Worker, PostgreSQL, or simulators.
- Accept only `EMST`, `FLE`, `KRYX`, or `KRYS` as `SHIPPOP_SERVICE_CODE`.
- Require a quote-signing secret of at least 32 characters that differs from the API key.
- Never print, commit, persist in a launchd job, or pass a SHIPPOP secret as a command argument.
- API and Worker receive the same provider, credentials, selected profile, and absolute Data Protection key path.
- Enable only quote, outbound booking, confirmation, and operation lookup for the selected service.
- Keep return, insurance, optional protection, and real Counter QR disabled.
- Set `DevelopmentDemoSimulation__Enabled=false` in sandbox mode; never fall back to Development after sandbox startup.
- Do not change payment verification, immutable snapshots, tracking idempotency, dispute gates, delivery verification, the 72-hour hold, or payout transitions.
- Do not add a database migration or change committed production capability flags.
- Use synthetic provider-approved Sandbox names, phones, and addresses only.

---

## File map

- Create `scripts/lib/local-shipping-mode.sh` — validates the mode and projects one safe, explicit .NET shipping configuration.
- Create `tests/scripts/local-shipping-mode-tests.sh` — dependency-free Bash regression suite with fake Stripe, dotnet, launchctl, Docker, and simulator commands.
- Modify `scripts/run-stripe-test-api.sh` — captures secrets, isolates Stripe CLI from SHIPPOP credentials, and applies one configuration to API and Worker.
- Modify `scripts/run-local-dual-sim.sh` — validates before side effects, forwards Sandbox inputs without putting secrets in launchd arguments, and records the backend runner PID.
- Modify `scripts/stop-local-dual-sim.sh` — terminates a directly launched Sandbox backend runner and its children.
- Modify `README.md` — documents the opt-in command and default behavior.
- Modify `docs/08_IMPLEMENTATION.md` — documents the provider boundary, safety warning, observable flow, and Counter QR exclusion.

### Task 1: Add the local shipping-mode configuration boundary

**Files:**
- Create: `scripts/lib/local-shipping-mode.sh`
- Create: `tests/scripts/local-shipping-mode-tests.sh`

**Interfaces:**
- Produces: `toklong_validate_local_shipping_mode() -> 0 | 2`.
- Produces: `toklong_apply_local_shipping_mode(keys_path, api_key, account_email, service_code, quote_signing_secret) -> 0 | 2`.
- Produces: exported .NET variables `ShippingQuotes__Provider`, `DevelopmentDemoSimulation__Enabled`, `DataProtection__KeysPath`, and the `Shippop__*` tree.
- Consumes: `TOKLONG_SHIPPING_MODE` plus the four operator-facing `SHIPPOP_*` variables during validation.

- [ ] **Step 1: Write failing validation and projection tests**

Create a plain Bash test runner with `set -euo pipefail`, a failure counter, and isolated subshell cases. Include these exact behavioral cases:

```bash
test_development_is_the_default() {
    (
        unset TOKLONG_SHIPPING_MODE SHIPPOP_API_KEY \
            SHIPPOP_ACCOUNT_EMAIL SHIPPOP_SERVICE_CODE \
            SHIPPOP_QUOTE_SIGNING_SECRET
        toklong_validate_local_shipping_mode
        toklong_apply_local_shipping_mode \
            "/tmp/toklong-test-keys" "" "" "" ""
        [[ "${ShippingQuotes__Provider}" == "Development" ]]
        [[ "${DevelopmentDemoSimulation__Enabled}" == "true" ]]
    )
}

test_sandbox_rejects_missing_api_key() {
    (
        TOKLONG_SHIPPING_MODE=ShippopSandbox
        SHIPPOP_ACCOUNT_EMAIL=tester@example.invalid
        SHIPPOP_SERVICE_CODE=EMST
        SHIPPOP_QUOTE_SIGNING_SECRET=12345678901234567890123456789012
        ! toklong_validate_local_shipping_mode 2>"${test_tmp}/error"
        grep -Fq "SHIPPOP_API_KEY" "${test_tmp}/error"
    )
}

test_sandbox_projects_only_the_selected_service() {
    (
        TOKLONG_SHIPPING_MODE=ShippopSandbox
        SHIPPOP_API_KEY=test-api-key
        SHIPPOP_ACCOUNT_EMAIL=tester@example.invalid
        SHIPPOP_SERVICE_CODE=EMST
        SHIPPOP_QUOTE_SIGNING_SECRET=12345678901234567890123456789012
        toklong_validate_local_shipping_mode
        toklong_apply_local_shipping_mode \
            "${test_tmp}/keys" \
            "${SHIPPOP_API_KEY}" \
            "${SHIPPOP_ACCOUNT_EMAIL}" \
            "${SHIPPOP_SERVICE_CODE}" \
            "${SHIPPOP_QUOTE_SIGNING_SECRET}"
        [[ "${ShippingQuotes__Provider}" == "Shippop" ]]
        [[ "${DevelopmentDemoSimulation__Enabled}" == "false" ]]
        [[ "${Shippop__BaseUrl}" == "http://mkpservice.shippop.dev" ]]
        [[ "${Shippop__AllowInsecureHttp}" == "true" ]]
        [[ "${Shippop__ServiceCodes__0}" == "EMST" ]]
        [[ "${Shippop__Services__EMST__QuoteEnabled}" == "true" ]]
        [[ "${Shippop__Services__EMST__BookOutboundEnabled}" == "true" ]]
        [[ "${Shippop__Services__EMST__ConfirmEnabled}" == "true" ]]
        [[ "${Shippop__Services__EMST__OperationLookupEnabled}" == "true" ]]
        [[ "${Shippop__Services__EMST__ReturnEnabled}" == "false" ]]
        [[ "${Shippop__Services__EMST__InsuranceEnabled}" == "false" ]]
        [[ "${Shippop__Services__FLE__QuoteEnabled}" == "false" ]]
    )
}
```

Also cover an unknown mode; missing service code; lowercase `emst`; unsupported
`EMS`; comma-separated `EMST,FLE`; a 31-character signing secret; signing
secret equal to API key; a relative Data Protection path; and each of the four
accepted service codes.

- [ ] **Step 2: Run the shell suite and verify it fails**

Run:

```bash
bash tests/scripts/local-shipping-mode-tests.sh
```

Expected: FAIL because `scripts/lib/local-shipping-mode.sh` and its functions do not exist.

- [ ] **Step 3: Implement the Bash 3.2-compatible configuration library**

Implement `toklong_validate_local_shipping_mode` with exact-case mode matching and indirect expansion `${!name:-}` for required variables. Return `2` after a Thai error naming only the invalid variable; do not echo its value.

Implement `toklong_apply_local_shipping_mode` so Development exports only:

```bash
export ShippingQuotes__Provider=Development
export DevelopmentDemoSimulation__Enabled=true
```

For Sandbox, require an absolute first argument, create it with mode `700`, and export:

```bash
export ShippingQuotes__Provider=Shippop
export DevelopmentDemoSimulation__Enabled=false
export DataProtection__KeysPath="${keys_path}"
export Shippop__BaseUrl=http://mkpservice.shippop.dev
export Shippop__AllowInsecureHttp=true
export Shippop__ApiKey="${api_key}"
export Shippop__AccountEmail="${account_email}"
export Shippop__QuoteSigningSecret="${quote_signing_secret}"
export Shippop__DirectBookingEnabled=true
export Shippop__DirectBookingCertificationReference=development-shippop-sandbox-not-production-certified
export Shippop__ServiceCodes__0="${service_code}"
```

Loop over `EMST FLE KRYX KRYS`, explicitly export every service as disabled, `HandoffMode=DropOff`, `MaximumCoverageSatang=0`, `IncludedCoverageSatang=0`, and a blank `CertificationReference`. Then enable only `QuoteEnabled`, `BookOutboundEnabled`, `ConfirmEnabled`, and `OperationLookupEnabled` for the selected service. Use `export "Shippop__Services__${code}__QuoteEnabled=false"` syntax rather than Bash associative arrays.

- [ ] **Step 4: Run syntax and behavior tests**

Run:

```bash
bash -n scripts/lib/local-shipping-mode.sh \
  tests/scripts/local-shipping-mode-tests.sh
bash tests/scripts/local-shipping-mode-tests.sh
```

Expected: both commands exit `0`; the test runner prints one PASS line per case and a final `local shipping mode tests passed`.

- [ ] **Step 5: Commit the configuration boundary**

```bash
git add scripts/lib/local-shipping-mode.sh \
  tests/scripts/local-shipping-mode-tests.sh
git commit -m "feat: add local shipping mode configuration"
```

### Task 2: Apply SHIPPOP Sandbox configuration to API and Worker

**Files:**
- Modify: `scripts/run-stripe-test-api.sh:1-171`
- Modify: `tests/scripts/local-shipping-mode-tests.sh`

**Interfaces:**
- Consumes: the two functions from Task 1.
- Produces: API and Worker processes with identical exported shipping configuration.
- Produces: Stripe CLI listener environment with no `SHIPPOP_*` or `Shippop__*` secret variable.

- [ ] **Step 1: Add failing backend-launch integration tests with command stubs**

In the shell test runner, create a temporary `fake-bin` and place executable `stripe`, `dotnet`, and `lsof` stubs there. The Stripe stub must return `whsec_test_local` for `listen --print-secret`; for the long-running listener invocation it writes `env | sort` to `${TOKLONG_TEST_CAPTURE}/stripe.env`. The dotnet stub selects `worker.env` or `api.env` from the project path in its arguments and writes `env | sort` there.

Run the backend launcher with fake Stripe Test keys and Sandbox inputs:

```bash
PATH="${fake_bin}:${PATH}" \
TOKLONG_TEST_CAPTURE="${capture}" \
TOKLONG_BACKEND_RUNTIME_DIR="${test_tmp}/runtime" \
STRIPE_SECRET_KEY=sk_test_local \
STRIPE_PUBLISHABLE_KEY=pk_test_local \
TOKLONG_SHIPPING_MODE=ShippopSandbox \
SHIPPOP_API_KEY=test-api-key \
SHIPPOP_ACCOUNT_EMAIL=tester@example.invalid \
SHIPPOP_SERVICE_CODE=EMST \
SHIPPOP_QUOTE_SIGNING_SECRET=12345678901234567890123456789012 \
bash scripts/run-stripe-test-api.sh \
    >"${capture}/output" 2>"${capture}/error"
```

Assert both `api.env` and `worker.env` contain `ShippingQuotes__Provider=Shippop`, the selected service flags, identical `DataProtection__KeysPath`, and the fake `Shippop__ApiKey`. Assert `stripe.env`, captured output, and captured error contain neither `test-api-key` nor `12345678901234567890123456789012`.

Add a second invocation without `TOKLONG_SHIPPING_MODE` and assert API/Worker use `Development` and the API has `DevelopmentDemoSimulation__Enabled=true`.

- [ ] **Step 2: Run the integration cases and verify they fail**

Run:

```bash
bash tests/scripts/local-shipping-mode-tests.sh
```

Expected: FAIL because `run-stripe-test-api.sh` still hard-codes `ShippingQuotes__Provider=Development` and simulation enabled.

- [ ] **Step 3: Capture and isolate operator-facing secrets**

Source `scripts/lib/local-shipping-mode.sh` immediately after `repo_root` is resolved. Validate before calling `stripe listen --print-secret`.

For Sandbox, copy the four operator values into unexported local shell variables, then run:

```bash
unset SHIPPOP_API_KEY SHIPPOP_ACCOUNT_EMAIL \
  SHIPPOP_SERVICE_CODE SHIPPOP_QUOTE_SIGNING_SECRET
```

This prevents the Stripe listener from inheriting credentials. Start the Stripe listener as it works today, then call:

```bash
data_protection_keys_path="${TOKLONG_BACKEND_RUNTIME_DIR:-${temporary_directory}}/data-protection-keys"
toklong_apply_local_shipping_mode \
  "${data_protection_keys_path}" \
  "${shippop_api_key}" \
  "${shippop_account_email}" \
  "${shippop_service_code}" \
  "${shippop_quote_signing_secret}"
```

Keep the captured values unprinted and unset them after the .NET configuration variables are exported.

- [ ] **Step 4: Remove fixed provider assignments from both process launches**

Delete both inline `ShippingQuotes__Provider=Development` assignments and the inline API `DevelopmentDemoSimulation__Enabled=true`. Let the sourced boundary provide those values. Keep `DevelopmentDemoSimulation__StepIntervalSeconds=3`; it is inert when simulation is disabled.

Print only a safe status line:

```bash
if [[ "${TOKLONG_SHIPPING_MODE:-Development}" == ShippopSandbox ]]; then
    echo "Shipping: SHIPPOP Sandbox (${shippop_service_code})"
else
    echo "Shipping: deterministic Development provider"
fi
```

- [ ] **Step 5: Run backend-launch tests**

Run:

```bash
bash -n scripts/run-stripe-test-api.sh \
  scripts/lib/local-shipping-mode.sh \
  tests/scripts/local-shipping-mode-tests.sh
bash tests/scripts/local-shipping-mode-tests.sh
```

Expected: PASS; captured API/Worker configuration matches and the Stripe capture has no SHIPPOP credential.

- [ ] **Step 6: Commit backend integration**

```bash
git add scripts/run-stripe-test-api.sh \
  tests/scripts/local-shipping-mode-tests.sh
git commit -m "feat: run Stripe backend with SHIPPOP sandbox"
```

### Task 3: Pass Sandbox mode through the dual-simulator lifecycle securely

**Files:**
- Modify: `scripts/run-local-dual-sim.sh:1-157`
- Modify: `scripts/stop-local-dual-sim.sh:1-75`
- Modify: `tests/scripts/local-shipping-mode-tests.sh`

**Interfaces:**
- Consumes: `toklong_validate_local_shipping_mode()` before any local side effect.
- Produces: `TOKLONG_IOS_APP_PATH` as a testable optional app-bundle override.
- Produces: `${runtime_directory}/backend-runner.pid` for a directly launched Sandbox backend.
- Preserves: existing `launchctl submit` path for credential-free Development mode.

- [ ] **Step 1: Add failing dual-simulator lifecycle tests**

Extend the fake command directory with `curl`, `docker`, `launchctl`, `open`, and `xcrun`. Make Docker `inspect` print `true`, curl return success, simulator/open commands return success, and launchctl log each argument to `${TOKLONG_TEST_CAPTURE}/launchctl.args` while returning nonzero for `print`.

Create an empty temporary app-bundle directory and invoke:

```bash
PATH="${fake_bin}:${PATH}" \
TOKLONG_TEST_CAPTURE="${capture}" \
TOKLONG_LOCAL_RUNTIME_DIR="${test_tmp}/dual-runtime" \
TOKLONG_IOS_APP_PATH="${test_tmp}/Toklong.Mobile.app" \
TOKLONG_SKIP_MOBILE_BUILD=1 \
STRIPE_SECRET_KEY=sk_test_local \
STRIPE_PUBLISHABLE_KEY=pk_test_local \
TOKLONG_SHIPPING_MODE=ShippopSandbox \
SHIPPOP_API_KEY=test-api-key \
SHIPPOP_ACCOUNT_EMAIL=tester@example.invalid \
SHIPPOP_SERVICE_CODE=EMST \
SHIPPOP_QUOTE_SIGNING_SECRET=12345678901234567890123456789012 \
bash scripts/run-local-dual-sim.sh \
    >"${capture}/dual-output" 2>"${capture}/dual-error"
```

Assert the command succeeds, `backend-runner.pid` is created, `launchctl.args` contains no `submit`, and neither output file contains either fake secret.

Add an early-failure case with Sandbox mode and no API key. Assert exit `2` and assert the fake Docker, xcrun, open, Stripe, and dotnet call logs are all absent.

For stop coverage, start `sleep 60`, write its PID to a temporary `backend-runner.pid`, invoke `stop-local-dual-sim.sh` with fake external commands, and poll with `kill -0` until the process no longer exists. The test must kill that exact temporary PID in its trap if an assertion fails.

- [ ] **Step 2: Run lifecycle tests and verify they fail**

Run:

```bash
bash tests/scripts/local-shipping-mode-tests.sh
```

Expected: FAIL because validation currently occurs after side effects, the app path cannot be overridden, Sandbox credentials are not forwarded, and the stopper does not terminate `backend-runner.pid`.

- [ ] **Step 3: Validate before PostgreSQL and simulator startup**

Source the local shipping library after resolving `repo_root` and call `toklong_validate_local_shipping_mode` before the required-command loop and before creating runtime directories. Change the app path declaration to:

```bash
app_path="${TOKLONG_IOS_APP_PATH:-${repo_root}/src/Toklong.Mobile/bin/Debug/net10.0-ios/iossimulator-arm64/Toklong.Mobile.app}"
```

Treat an active PID in `${runtime_directory}/backend-runner.pid` as an already-running environment; remove only a stale PID file.

- [ ] **Step 4: Add the inherited-environment Sandbox backend path**

Keep the current `launchctl submit` block unchanged for Development. For Sandbox, start the backend with a subshell and `exec`, redirecting stdout/stderr to the existing backend log:

```bash
(
    export TOKLONG_BACKEND_RUNTIME_DIR="${backend_runtime_directory}"
    export TOKLONG_STRIPE_TEST_PORT="${api_port}"
    exec "${repo_root}/scripts/run-stripe-test-api.sh"
) >"${backend_log}" 2>&1 &
backend_runner_pid="$!"
printf '%s\n' "${backend_runner_pid}" \
    >"${runtime_directory}/backend-runner.pid"
```

The subshell inherits the four already-exported SHIPPOP inputs, so none is inserted into an argument array or launchd job. In readiness checks, use `kill -0 "${backend_runner_pid}"` for Sandbox and the existing `launchctl print` check for Development. On failed startup, terminate only the recorded backend runner and allow `run-stripe-test-api.sh` traps to terminate Worker and Stripe listener.

- [ ] **Step 5: Stop the direct backend runner safely**

In `stop-local-dual-sim.sh`, after removing the launchd job, call:

```bash
stop_pid_file "${runtime_directory}/backend-runner.pid"
```

Poll that exact PID for at most five seconds, then continue stopping its recorded API, Worker, and Stripe listener PIDs. Remove only the explicit PID files already scoped under the runtime directory.

- [ ] **Step 6: Run syntax and lifecycle tests**

Run:

```bash
bash -n scripts/run-local-dual-sim.sh \
  scripts/stop-local-dual-sim.sh \
  scripts/run-stripe-test-api.sh \
  scripts/lib/local-shipping-mode.sh \
  tests/scripts/local-shipping-mode-tests.sh
bash tests/scripts/local-shipping-mode-tests.sh
```

Expected: PASS; no real Docker, Stripe, SHIPPOP, simulator, or dotnet process is called by the stubbed tests.

- [ ] **Step 7: Commit lifecycle integration**

```bash
git add scripts/run-local-dual-sim.sh \
  scripts/stop-local-dual-sim.sh \
  tests/scripts/local-shipping-mode-tests.sh
git commit -m "feat: launch dual simulators with SHIPPOP sandbox"
```

### Task 4: Document and verify the complete opt-in flow

**Files:**
- Modify: `README.md:213-239`
- Modify: `docs/08_IMPLEMENTATION.md:200-240`

**Interfaces:**
- Consumes: the final environment contract from Tasks 1–3.
- Produces: one copy-paste command and an explicit list of supported/unsupported Sandbox capabilities.

- [ ] **Step 1: Update the local simulator documentation**

Keep the existing no-variable command as the deterministic default. Add this Sandbox example without a real credential:

```bash
TOKLONG_SHIPPING_MODE=ShippopSandbox \
SHIPPOP_API_KEY='<SHIPPOP test API key>' \
SHIPPOP_ACCOUNT_EMAIL='<SHIPPOP sandbox account email>' \
SHIPPOP_SERVICE_CODE='EMST' \
SHIPPOP_QUOTE_SIGNING_SECRET='<a separate random value of at least 32 characters>' \
./scripts/run-local-dual-sim.sh
```

State that this uses real SHIPPOP Sandbox quote, booking, confirmation, tracking, and label endpoints; Stripe remains Test Mode; Development simulation is off; no Development fallback occurs; Counter QR, return, insurance, and optional protection remain disabled. Warn that the endpoint is HTTP and must receive synthetic Sandbox data only. Explain that the API key belongs in the command environment or a local secret manager, never `appsettings*.json` or Git.

- [ ] **Step 2: Run repository checks**

Run:

```bash
git diff --check
bash -n scripts/lib/local-shipping-mode.sh \
  scripts/run-stripe-test-api.sh \
  scripts/run-local-dual-sim.sh \
  scripts/stop-local-dual-sim.sh \
  tests/scripts/local-shipping-mode-tests.sh
bash tests/scripts/local-shipping-mode-tests.sh
dotnet build Toklong.slnx --no-restore
dotnet test Toklong.slnx --no-restore
```

Expected: every command exits `0`. No accessibility page changed, so existing accessibility coverage remains unchanged.

- [ ] **Step 3: Scan the staged diff for credentials and forbidden production changes**

Stage the two documentation files, then run:

```bash
git add README.md docs/08_IMPLEMENTATION.md
git diff --cached --check
git diff --cached -- src/Toklong.Api/appsettings.json \
  src/Toklong.Worker/appsettings.json
if rg -n '"ApiKey": "[^" ]+"' \
    src/Toklong.Api/appsettings*.json \
    src/Toklong.Worker/appsettings*.json; then
    echo "พบ API key ใน committed appsettings" >&2
    exit 1
fi
if git diff --cached | rg -n \
    'sk_live_[[:alnum:]_]{16,}|pk_live_[[:alnum:]_]{16,}|whsec_[[:alnum:]_]{16,}'; then
    echo "พบ credential ที่มีลักษณะเป็นค่าจริง" >&2
    exit 1
fi
```

Expected: the appsettings diff is empty and both credential guards reach the
end without exiting. Placeholder documentation may contain the literal
variable name but no value; shell tests may contain only the explicit fake
values shown in this plan.

- [ ] **Step 4: Commit documentation**

```bash
git commit -m "docs: explain SHIPPOP sandbox simulator flow"
```

- [ ] **Step 5: Perform the operator-controlled smoke test**

With provider-approved synthetic contacts and addresses, run the documented command. In the apps verify:

1. A physical agreement returns a quote from the selected SHIPPOP service.
2. Buyer payment changes state only after the signed Stripe Test webhook.
3. The existing pre-payment booking returns provider references without a retry-unknown replay.
4. Worker confirmation produces the provider tracking number.
5. Seller label download succeeds when SHIPPOP returns label HTML.
6. Tracking refresh is idempotent and an unverified delivery cannot start the 72-hour clock.
7. Counter QR remains unavailable and no Development QR is substituted.
8. `./scripts/stop-local-dual-sim.sh` terminates backend children and local dependencies according to its keep flags.

If booking or confirmation has an outcome-unknown result, stop the smoke test and inspect the existing durable operation/booking attempt; do not repeat the mutation blindly.
