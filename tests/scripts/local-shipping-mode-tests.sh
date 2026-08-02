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
    if (set -euo pipefail; "$@"); then
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

if [[ "${failures}" -ne 0 ]]; then
    echo "${failures} local shipping mode test(s) failed" >&2
    exit 1
fi

echo "local shipping mode tests passed"
