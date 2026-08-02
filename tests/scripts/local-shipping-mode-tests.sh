#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
library_path="${repo_root}/scripts/lib/local-shipping-mode.sh"
test_tmp="$(mktemp -d "${TMPDIR:-/tmp}/toklong-shipping-mode-tests.XXXXXX")"
failures=0

cleanup() {
    rm -r -- "${test_tmp}"
}
trap cleanup EXIT

if [[ ! -r "${library_path}" ]]; then
    echo "FAIL: missing ${library_path}" >&2
    exit 1
fi

# shellcheck source=../../scripts/lib/local-shipping-mode.sh
source "${library_path}"

run_test() {
    local name="$1"
    shift
    local status
    set +e
    (
        set -euo pipefail
        "$@"
    )
    status="$?"
    set -e
    if [[ "${status}" -eq 0 ]]; then
        echo "PASS: ${name}"
    else
        echo "FAIL: ${name}" >&2
        failures=$((failures + 1))
    fi
}

set_valid_sandbox_inputs() {
    TOKLONG_SHIPPING_MODE=ShippopSandbox
    SHIPPOP_API_KEY=test-api-key
    SHIPPOP_ACCOUNT_EMAIL=tester@example.invalid
    SHIPPOP_SERVICE_CODE=EMST
    SHIPPOP_QUOTE_SIGNING_SECRET=12345678901234567890123456789012
}

test_development_is_the_default() {
    unset TOKLONG_SHIPPING_MODE SHIPPOP_API_KEY \
        SHIPPOP_ACCOUNT_EMAIL SHIPPOP_SERVICE_CODE \
        SHIPPOP_QUOTE_SIGNING_SECRET

    toklong_validate_local_shipping_mode
    toklong_apply_local_shipping_mode \
        "${test_tmp}/development-keys" "" "" "" ""

    [[ "${ShippingQuotes__Provider}" == "Development" ]]
    [[ "${DevelopmentDemoSimulation__Enabled}" == "true" ]]
}

test_unknown_mode_is_rejected() {
    TOKLONG_SHIPPING_MODE=Sandbox
    local error_path="${test_tmp}/unknown-mode-error"

    if toklong_validate_local_shipping_mode 2>"${error_path}"; then
        return 1
    fi

    grep -Fq "TOKLONG_SHIPPING_MODE" "${error_path}"
    ! grep -Fq "Sandbox" "${error_path}"
}

test_missing_required_value_is_rejected() {
    local missing_name="$1"
    local error_path="${test_tmp}/missing-${missing_name}"
    set_valid_sandbox_inputs
    unset "${missing_name}"

    if toklong_validate_local_shipping_mode 2>"${error_path}"; then
        return 1
    fi

    grep -Fq "${missing_name}" "${error_path}"
}

test_service_code_is_rejected() {
    local service_code="$1"
    local error_path="${test_tmp}/service-${service_code//[^A-Za-z0-9]/_}"
    set_valid_sandbox_inputs
    SHIPPOP_SERVICE_CODE="${service_code}"

    if toklong_validate_local_shipping_mode 2>"${error_path}"; then
        return 1
    fi

    grep -Fq "SHIPPOP_SERVICE_CODE" "${error_path}"
    ! grep -Fq "${service_code}" "${error_path}"
}

test_service_code_is_accepted() {
    local service_code="$1"
    set_valid_sandbox_inputs
    SHIPPOP_SERVICE_CODE="${service_code}"

    toklong_validate_local_shipping_mode
}

test_short_signing_secret_is_rejected() {
    set_valid_sandbox_inputs
    SHIPPOP_QUOTE_SIGNING_SECRET=1234567890123456789012345678901
    local error_path="${test_tmp}/short-secret-error"

    if toklong_validate_local_shipping_mode 2>"${error_path}"; then
        return 1
    fi

    grep -Fq "SHIPPOP_QUOTE_SIGNING_SECRET" "${error_path}"
    ! grep -Fq "${SHIPPOP_QUOTE_SIGNING_SECRET}" "${error_path}"
}

test_api_key_cannot_be_the_signing_secret() {
    set_valid_sandbox_inputs
    SHIPPOP_API_KEY=12345678901234567890123456789012
    SHIPPOP_QUOTE_SIGNING_SECRET="${SHIPPOP_API_KEY}"
    local error_path="${test_tmp}/equal-secret-error"

    if toklong_validate_local_shipping_mode 2>"${error_path}"; then
        return 1
    fi

    grep -Fq "SHIPPOP_QUOTE_SIGNING_SECRET" "${error_path}"
    ! grep -Fq "${SHIPPOP_API_KEY}" "${error_path}"
}

test_relative_data_protection_path_is_rejected() {
    set_valid_sandbox_inputs
    local error_path="${test_tmp}/relative-path-error"

    if toklong_apply_local_shipping_mode \
        "relative/keys" \
        "${SHIPPOP_API_KEY}" \
        "${SHIPPOP_ACCOUNT_EMAIL}" \
        "${SHIPPOP_SERVICE_CODE}" \
        "${SHIPPOP_QUOTE_SIGNING_SECRET}" \
        2>"${error_path}"; then
        return 1
    fi

    grep -Fq "DataProtection" "${error_path}"
}

test_sandbox_projects_only_the_selected_service() {
    set_valid_sandbox_inputs
    local keys_path="${test_tmp}/sandbox-keys"

    toklong_validate_local_shipping_mode
    toklong_apply_local_shipping_mode \
        "${keys_path}" \
        "${SHIPPOP_API_KEY}" \
        "${SHIPPOP_ACCOUNT_EMAIL}" \
        "${SHIPPOP_SERVICE_CODE}" \
        "${SHIPPOP_QUOTE_SIGNING_SECRET}"

    [[ "${ShippingQuotes__Provider}" == "Shippop" ]]
    [[ "${DevelopmentDemoSimulation__Enabled}" == "false" ]]
    [[ "${DataProtection__KeysPath}" == "${keys_path}" ]]
    [[ -d "${keys_path}" ]]
    [[ "$(stat -f '%Lp' "${keys_path}" 2>/dev/null || stat -c '%a' "${keys_path}")" == "700" ]]
    [[ "${Shippop__BaseUrl}" == "http://mkpservice.shippop.dev" ]]
    [[ "${Shippop__AllowInsecureHttp}" == "true" ]]
    [[ "${Shippop__ApiKey}" == "test-api-key" ]]
    [[ "${Shippop__AccountEmail}" == "tester@example.invalid" ]]
    [[ "${Shippop__QuoteSigningSecret}" == "12345678901234567890123456789012" ]]
    [[ "${Shippop__ServiceCodes__0}" == "EMST" ]]
    [[ "${Shippop__Services__EMST__QuoteEnabled}" == "true" ]]
    [[ "${Shippop__Services__EMST__BookOutboundEnabled}" == "true" ]]
    [[ "${Shippop__Services__EMST__ConfirmEnabled}" == "true" ]]
    [[ "${Shippop__Services__EMST__OperationLookupEnabled}" == "true" ]]
    [[ "${Shippop__Services__EMST__ReturnEnabled}" == "false" ]]
    [[ "${Shippop__Services__EMST__InsuranceEnabled}" == "false" ]]
    [[ "${Shippop__Services__EMST__OptionalProtectionEnabled}" == "false" ]]
    [[ "${Shippop__Services__EMST__HandoffMode}" == "DropOff" ]]
    [[ "${Shippop__Services__EMST__MaximumCoverageSatang}" == "0" ]]
    [[ "${Shippop__Services__FLE__QuoteEnabled}" == "false" ]]
    [[ "${Shippop__Services__KRYX__BookOutboundEnabled}" == "false" ]]
    [[ "${Shippop__Services__KRYS__ConfirmEnabled}" == "false" ]]
}

create_backend_fake_commands() {
    local fake_bin="$1"
    mkdir -p -- "${fake_bin}"

    cat >"${fake_bin}/stripe" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
if [[ "${1:-}" == "listen" && "${2:-}" == "--print-secret" ]]; then
    echo "whsec_test_local"
    exit 0
fi
env | sort >"${TOKLONG_TEST_CAPTURE}/stripe.env"
EOF

    cat >"${fake_bin}/dotnet" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
case "$*" in
    *Toklong.Worker*)
        env | sort >"${TOKLONG_TEST_CAPTURE}/worker.env"
        ;;
    *Toklong.Api*)
        env | sort >"${TOKLONG_TEST_CAPTURE}/api.env"
        ;;
    *)
        echo "unexpected dotnet invocation" >&2
        exit 1
        ;;
esac
EOF

    cat >"${fake_bin}/lsof" <<'EOF'
#!/usr/bin/env bash
exit 1
EOF

    chmod 700 \
        "${fake_bin}/stripe" \
        "${fake_bin}/dotnet" \
        "${fake_bin}/lsof"
}

create_dual_sim_fake_commands() {
    local fake_bin="$1"
    create_backend_fake_commands "${fake_bin}"

    cat >"${fake_bin}/curl" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >>"${TOKLONG_TEST_CAPTURE}/curl.calls"
exit 0
EOF

    cat >"${fake_bin}/docker" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >>"${TOKLONG_TEST_CAPTURE}/docker.calls"
if [[ "${1:-}" == "inspect" ]]; then
    echo "true"
fi
EOF

    cat >"${fake_bin}/launchctl" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >>"${TOKLONG_TEST_CAPTURE}/launchctl.args"
if [[ "${1:-}" == "print" ]]; then
    exit 1
fi
exit 0
EOF

    cat >"${fake_bin}/open" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >>"${TOKLONG_TEST_CAPTURE}/open.calls"
EOF

    cat >"${fake_bin}/xcrun" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >>"${TOKLONG_TEST_CAPTURE}/xcrun.calls"
EOF

    chmod 700 \
        "${fake_bin}/curl" \
        "${fake_bin}/docker" \
        "${fake_bin}/launchctl" \
        "${fake_bin}/open" \
        "${fake_bin}/xcrun"
}

wait_for_file() {
    local path="$1"
    local attempt
    for attempt in 1 2 3 4 5 6 7 8 9 10; do
        if [[ -f "${path}" ]]; then
            return 0
        fi
        sleep 0.01
    done
    return 1
}

test_backend_launcher_applies_sandbox_to_api_and_worker() {
    local case_root="${test_tmp}/backend-sandbox"
    local fake_bin="${case_root}/fake-bin"
    local capture="${case_root}/capture"
    mkdir -p -- "${capture}"
    create_backend_fake_commands "${fake_bin}"

    PATH="${fake_bin}:${PATH}" \
    TOKLONG_TEST_CAPTURE="${capture}" \
    TOKLONG_BACKEND_RUNTIME_DIR="${case_root}/runtime" \
    STRIPE_SECRET_KEY=sk_test_local \
    STRIPE_PUBLISHABLE_KEY=pk_test_local \
    TOKLONG_SHIPPING_MODE=ShippopSandbox \
    SHIPPOP_API_KEY=test-api-key \
    SHIPPOP_ACCOUNT_EMAIL=tester@example.invalid \
    SHIPPOP_SERVICE_CODE=EMST \
    SHIPPOP_QUOTE_SIGNING_SECRET=12345678901234567890123456789012 \
        bash "${repo_root}/scripts/run-stripe-test-api.sh" \
        >"${capture}/output" 2>"${capture}/error"

    wait_for_file "${capture}/stripe.env"
    wait_for_file "${capture}/worker.env"
    wait_for_file "${capture}/api.env"

    local environment_path
    for environment_path in \
        "${capture}/worker.env" \
        "${capture}/api.env"; do
        grep -Fxq "ShippingQuotes__Provider=Shippop" \
            "${environment_path}"
        grep -Fxq "DevelopmentDemoSimulation__Enabled=false" \
            "${environment_path}"
        grep -Fxq "Shippop__BaseUrl=http://mkpservice.shippop.dev" \
            "${environment_path}"
        grep -Fxq "Shippop__ApiKey=test-api-key" \
            "${environment_path}"
        grep -Fxq "Shippop__ServiceCodes__0=EMST" \
            "${environment_path}"
        grep -Fxq "Shippop__Services__EMST__QuoteEnabled=true" \
            "${environment_path}"
        grep -Fxq "Shippop__Services__FLE__QuoteEnabled=false" \
            "${environment_path}"
    done

    local api_keys_path
    local worker_keys_path
    api_keys_path="$(grep -F 'DataProtection__KeysPath=' \
        "${capture}/api.env")"
    worker_keys_path="$(grep -F 'DataProtection__KeysPath=' \
        "${capture}/worker.env")"
    [[ "${api_keys_path}" == "${worker_keys_path}" ]]
    [[ "${api_keys_path}" == \
       "DataProtection__KeysPath=${case_root}/runtime/data-protection-keys" ]]

    ! grep -Fq "test-api-key" "${capture}/stripe.env"
    ! grep -Fq "12345678901234567890123456789012" \
        "${capture}/stripe.env"
    ! grep -Fq "test-api-key" "${capture}/output"
    ! grep -Fq "test-api-key" "${capture}/error"
    ! grep -Fq "12345678901234567890123456789012" \
        "${capture}/output"
    ! grep -Fq "12345678901234567890123456789012" \
        "${capture}/error"
}

test_backend_launcher_keeps_development_default() {
    local case_root="${test_tmp}/backend-development"
    local fake_bin="${case_root}/fake-bin"
    local capture="${case_root}/capture"
    mkdir -p -- "${capture}"
    create_backend_fake_commands "${fake_bin}"

    unset TOKLONG_SHIPPING_MODE SHIPPOP_API_KEY \
        SHIPPOP_ACCOUNT_EMAIL SHIPPOP_SERVICE_CODE \
        SHIPPOP_QUOTE_SIGNING_SECRET
    PATH="${fake_bin}:${PATH}" \
    TOKLONG_TEST_CAPTURE="${capture}" \
    TOKLONG_BACKEND_RUNTIME_DIR="${case_root}/runtime" \
    STRIPE_SECRET_KEY=sk_test_local \
    STRIPE_PUBLISHABLE_KEY=pk_test_local \
        bash "${repo_root}/scripts/run-stripe-test-api.sh" \
        >"${capture}/output" 2>"${capture}/error"

    wait_for_file "${capture}/worker.env"
    wait_for_file "${capture}/api.env"
    grep -Fxq "ShippingQuotes__Provider=Development" \
        "${capture}/worker.env"
    grep -Fxq "ShippingQuotes__Provider=Development" \
        "${capture}/api.env"
    grep -Fxq "DevelopmentDemoSimulation__Enabled=true" \
        "${capture}/api.env"
}

test_dual_sim_sandbox_uses_direct_backend_runner() {
    local case_root="${test_tmp}/dual-sandbox"
    local fake_bin="${case_root}/fake-bin"
    local capture="${case_root}/capture"
    local runtime="${case_root}/runtime"
    local app_path="${case_root}/Toklong.Mobile.app"
    mkdir -p -- "${capture}" "${app_path}"
    create_dual_sim_fake_commands "${fake_bin}"

    PATH="${fake_bin}:${PATH}" \
    TOKLONG_TEST_CAPTURE="${capture}" \
    TOKLONG_LOCAL_RUNTIME_DIR="${runtime}" \
    TOKLONG_IOS_APP_PATH="${app_path}" \
    TOKLONG_SKIP_MOBILE_BUILD=1 \
    STRIPE_SECRET_KEY=sk_test_local \
    STRIPE_PUBLISHABLE_KEY=pk_test_local \
    TOKLONG_SHIPPING_MODE=ShippopSandbox \
    SHIPPOP_API_KEY=test-api-key \
    SHIPPOP_ACCOUNT_EMAIL=tester@example.invalid \
    SHIPPOP_SERVICE_CODE=EMST \
    SHIPPOP_QUOTE_SIGNING_SECRET=12345678901234567890123456789012 \
        bash "${repo_root}/scripts/run-local-dual-sim.sh" \
        >"${capture}/dual-output" 2>"${capture}/dual-error"

    [[ -f "${runtime}/backend-runner.pid" ]]
    ! grep -Fq "submit" "${capture}/launchctl.args"
    ! grep -Fq "test-api-key" "${capture}/launchctl.args"
    ! grep -Fq "12345678901234567890123456789012" \
        "${capture}/launchctl.args"
    ! grep -Fq "test-api-key" "${capture}/dual-output"
    ! grep -Fq "test-api-key" "${capture}/dual-error"
    ! grep -Fq "12345678901234567890123456789012" \
        "${capture}/dual-output"
    ! grep -Fq "12345678901234567890123456789012" \
        "${capture}/dual-error"
    ! grep -Fq "test-api-key" "${runtime}/backend.log"
    ! grep -Fq "12345678901234567890123456789012" \
        "${runtime}/backend.log"
}

test_dual_sim_rejects_invalid_sandbox_before_side_effects() {
    local case_root="${test_tmp}/dual-invalid"
    local fake_bin="${case_root}/fake-bin"
    local capture="${case_root}/capture"
    local runtime="${case_root}/runtime"
    local app_path="${case_root}/Toklong.Mobile.app"
    mkdir -p -- "${capture}" "${app_path}"
    create_dual_sim_fake_commands "${fake_bin}"
    unset SHIPPOP_API_KEY

    local status
    set +e
    PATH="${fake_bin}:${PATH}" \
    TOKLONG_TEST_CAPTURE="${capture}" \
    TOKLONG_LOCAL_RUNTIME_DIR="${runtime}" \
    TOKLONG_IOS_APP_PATH="${app_path}" \
    TOKLONG_SKIP_MOBILE_BUILD=1 \
    STRIPE_SECRET_KEY=sk_test_local \
    STRIPE_PUBLISHABLE_KEY=pk_test_local \
    TOKLONG_SHIPPING_MODE=ShippopSandbox \
    SHIPPOP_ACCOUNT_EMAIL=tester@example.invalid \
    SHIPPOP_SERVICE_CODE=EMST \
    SHIPPOP_QUOTE_SIGNING_SECRET=12345678901234567890123456789012 \
        bash "${repo_root}/scripts/run-local-dual-sim.sh" \
        >"${capture}/dual-output" 2>"${capture}/dual-error"
    status="$?"
    set -e

    [[ "${status}" -eq 2 ]]
    grep -Fq "SHIPPOP_API_KEY" "${capture}/dual-error"
    [[ ! -e "${capture}/docker.calls" ]]
    [[ ! -e "${capture}/xcrun.calls" ]]
    [[ ! -e "${capture}/open.calls" ]]
    [[ ! -e "${capture}/stripe.env" ]]
    [[ ! -e "${capture}/worker.env" ]]
    [[ ! -e "${capture}/api.env" ]]
}

test_stopper_terminates_direct_backend_runner() {
    local case_root="${test_tmp}/stop-direct-runner"
    local fake_bin="${case_root}/fake-bin"
    local capture="${case_root}/capture"
    local runtime="${case_root}/runtime"
    mkdir -p -- "${capture}" "${runtime}/backend"
    create_dual_sim_fake_commands "${fake_bin}"

    local runner_pid
    runner_pid="$(bash -c \
        'sleep 60 </dev/null >/dev/null 2>&1 & echo "$!"')"
    printf '%s\n' "${runner_pid}" >"${runtime}/backend-runner.pid"

    local stopped=false
    trap 'kill '"${runner_pid}"' 2>/dev/null || true' RETURN
    PATH="${fake_bin}:${PATH}" \
    TOKLONG_TEST_CAPTURE="${capture}" \
    TOKLONG_LOCAL_RUNTIME_DIR="${runtime}" \
    TOKLONG_KEEP_POSTGRES_RUNNING=1 \
    TOKLONG_KEEP_SIMULATORS_BOOTED=1 \
        bash "${repo_root}/scripts/stop-local-dual-sim.sh" \
        >"${capture}/stop-output" 2>"${capture}/stop-error"

    local attempt
    for attempt in 1 2 3 4 5 6 7 8 9 10; do
        if ! kill -0 "${runner_pid}" 2>/dev/null; then
            stopped=true
            break
        fi
        sleep 0.02
    done
    trap - RETURN
    if [[ "${stopped}" != true ]]; then
        kill "${runner_pid}" 2>/dev/null || true
    fi
    [[ "${stopped}" == true ]]
}

run_test "Development is the default" test_development_is_the_default
run_test "unknown mode is rejected without echoing it" \
    test_unknown_mode_is_rejected
for required_name in \
    SHIPPOP_API_KEY \
    SHIPPOP_ACCOUNT_EMAIL \
    SHIPPOP_SERVICE_CODE \
    SHIPPOP_QUOTE_SIGNING_SECRET; do
    run_test "missing ${required_name} is rejected" \
        test_missing_required_value_is_rejected "${required_name}"
done
for rejected_service in emst EMS EMST,FLE ' EMST '; do
    run_test "service code '${rejected_service}' is rejected" \
        test_service_code_is_rejected "${rejected_service}"
done
for accepted_service in EMST FLE KRYX KRYS; do
    run_test "service code ${accepted_service} is accepted" \
        test_service_code_is_accepted "${accepted_service}"
done
run_test "short signing secret is rejected without echoing it" \
    test_short_signing_secret_is_rejected
run_test "API key cannot equal signing secret" \
    test_api_key_cannot_be_the_signing_secret
run_test "relative Data Protection path is rejected" \
    test_relative_data_protection_path_is_rejected
run_test "Sandbox projects only the selected service" \
    test_sandbox_projects_only_the_selected_service
run_test "backend applies Sandbox config without leaking secrets to Stripe" \
    test_backend_launcher_applies_sandbox_to_api_and_worker
run_test "backend keeps Development as its default" \
    test_backend_launcher_keeps_development_default
run_test "dual simulator uses direct runner for Sandbox" \
    test_dual_sim_sandbox_uses_direct_backend_runner
run_test "dual simulator rejects invalid Sandbox before side effects" \
    test_dual_sim_rejects_invalid_sandbox_before_side_effects
run_test "stopper terminates direct backend runner" \
    test_stopper_terminates_direct_backend_runner

if [[ "${failures}" -ne 0 ]]; then
    echo "${failures} local shipping mode test(s) failed" >&2
    exit 1
fi

echo "local shipping mode tests passed"
