#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
test_root="$(mktemp -d)"
test_bin="${test_root}/bin"
capture="${test_root}/capture"
fixture="${test_root}/synthetic.json"
stdout_path="${test_root}/stdout"
stderr_path="${test_root}/stderr"
trap 'rm -rf -- "${test_root}"' EXIT

mkdir -p -- "${test_bin}"
printf '%s\n' '{}' > "${fixture}"

cat > "${test_bin}/dotnet" <<'STUB'
#!/usr/bin/env bash
set -euo pipefail
{
    printf 'CERTIFY=%s\n' "${SHIPPOP_CERTIFY:-}"
    printf 'MUTATIONS=%s\n' "${SHIPPOP_CERTIFY_MUTATIONS:-}"
    printf 'ARG=%s\n' "$@"
} > "${SHIPPOP_TEST_CAPTURE:?}"
STUB
chmod +x "${test_bin}/dotnet"

run_certification() {
    local mode="$1"
    local base_url="$2"
    local mutations="$3"
    rm -f -- "${capture}" "${stdout_path}" "${stderr_path}"
    set +e
    PATH="${test_bin}:${PATH}" \
    SHIPPOP_TEST_CAPTURE="${capture}" \
    SHIPPOP_BASE_URL="${base_url}" \
    SHIPPOP_API_KEY="forbidden-test-api-key" \
    SHIPPOP_ACCOUNT_EMAIL="forbidden@example.invalid" \
    SHIPPOP_SERVICE_CODE="EMST" \
    SHIPPOP_SYNTHETIC_ADDRESS_JSON="${fixture}" \
    SHIPPOP_CERTIFY_MUTATIONS="${mutations}" \
        "${repo_root}/scripts/shippop-certify.sh" "${mode}" \
        > "${stdout_path}" 2> "${stderr_path}"
    run_status=$?
    set -e
}

run_certification \
    full-lifecycle \
    http://mkpservice.shippop.dev \
    1
[[ "${run_status}" -eq 2 ]]
[[ ! -e "${capture}" ]]

run_certification \
    full-lifecycle \
    https://mkpservice.shippop.dev \
    0
[[ "${run_status}" -eq 2 ]]
[[ ! -e "${capture}" ]]

run_certification \
    full-lifecycle \
    https://mkpservice.shippop.dev \
    1
[[ "${run_status}" -eq 0 ]]
grep -Fxq 'CERTIFY=1' "${capture}"
grep -Fxq 'MUTATIONS=1' "${capture}"
grep -Fq \
    'Full_lifecycle_calls_every_current_endpoint_and_cleans_up' \
    "${capture}"

if grep -Fq 'forbidden-test-api-key' \
        "${stdout_path}" "${stderr_path}" "${capture}" ||
   grep -Fq 'forbidden@example.invalid' \
        "${stdout_path}" "${stderr_path}" "${capture}"; then
    echo "Runner leaked a fake secret." >&2
    exit 1
fi

run_certification \
    parcel-protection \
    https://mkpservice.shippop.dev/ \
    0
[[ "${run_status}" -eq 0 ]]
grep -Fq \
    'Protection_quote_and_booking_preserve_exact_values' \
    "${capture}"

echo "SHIPPOP certification runner tests passed."
