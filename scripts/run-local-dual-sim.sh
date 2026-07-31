#!/usr/bin/env bash

set -euo pipefail
umask 077

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
runtime_directory="${TOKLONG_LOCAL_RUNTIME_DIR:-${TMPDIR:-/tmp}/toklong-local-dual-sim}"
backend_runtime_directory="${runtime_directory}/backend"
backend_log="${runtime_directory}/backend.log"
backend_launch_label="${TOKLONG_BACKEND_LAUNCH_LABEL:-th.co.toklong.local-backend}"
launch_domain="gui/$(id -u)"
buyer_simulator="${TOKLONG_BUYER_SIMULATOR_UDID:-FD76A775-469E-44B3-8561-52B61A406DE4}"
seller_simulator="${TOKLONG_SELLER_SIMULATOR_UDID:-FBE4866E-8265-4264-8053-23A4828AC85C}"
bundle_id="${TOKLONG_IOS_BUNDLE_ID:-th.co.toklong.mobile}"
api_port="${TOKLONG_STRIPE_TEST_PORT:-5181}"
api_ready_url="http://127.0.0.1:${api_port}/health/ready"
mobile_project="${repo_root}/src/Toklong.Mobile/Toklong.Mobile.csproj"
app_path="${repo_root}/src/Toklong.Mobile/bin/Debug/net10.0-ios/iossimulator-arm64/Toklong.Mobile.app"
postgres_started_marker="${runtime_directory}/postgres-started"

for command_name in curl docker dotnet launchctl open stripe xcrun; do
    if ! command -v "${command_name}" >/dev/null 2>&1; then
        echo "Missing required command: ${command_name}" >&2
        exit 1
    fi
done

mkdir -p -- "${runtime_directory}" "${backend_runtime_directory}"
chmod 700 "${runtime_directory}" "${backend_runtime_directory}"

if launchctl print \
    "${launch_domain}/${backend_launch_label}" \
    >/dev/null 2>&1; then
    echo "TOKLONG local environment is already running."
    echo "Stop it with ./scripts/stop-local-dual-sim.sh"
    exit 0
fi

cleanup_failed_start() {
    local exit_code="$?"
    trap - EXIT
    if [[ "${exit_code}" -eq 0 ]]; then
        return
    fi
    echo "Startup failed. Cleaning up processes started by this command." >&2
    launchctl remove "${backend_launch_label}" \
        >/dev/null 2>&1 || true
    xcrun simctl terminate "${buyer_simulator}" "${bundle_id}" \
        >/dev/null 2>&1 || true
    xcrun simctl terminate "${seller_simulator}" "${bundle_id}" \
        >/dev/null 2>&1 || true
    if [[ -f "${postgres_started_marker}" ]]; then
        docker compose \
            -f "${repo_root}/compose.yml" \
            stop postgres >/dev/null 2>&1 || true
        rm -f -- "${postgres_started_marker}"
    fi
}
trap cleanup_failed_start EXIT

postgres_running="$(
    docker inspect \
        --format '{{.State.Running}}' \
        toklong-postgres 2>/dev/null || true
)"
if [[ "${postgres_running}" != true ]]; then
    echo "Starting PostgreSQL..."
    docker compose \
        -f "${repo_root}/compose.yml" \
        up -d --wait postgres
    : >"${postgres_started_marker}"
else
    rm -f -- "${postgres_started_marker}"
    echo "PostgreSQL is already running."
fi

echo "Booting buyer simulator ${buyer_simulator}..."
xcrun simctl boot "${buyer_simulator}" >/dev/null 2>&1 || true
xcrun simctl bootstatus "${buyer_simulator}" -b

echo "Booting seller simulator ${seller_simulator}..."
xcrun simctl boot "${seller_simulator}" >/dev/null 2>&1 || true
xcrun simctl bootstatus "${seller_simulator}" -b
open -a Simulator

if [[ "${TOKLONG_SKIP_MOBILE_BUILD:-0}" != 1 ]]; then
    echo "Building the iOS simulator app..."
    DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 \
        dotnet build "${mobile_project}" \
        -p:TargetFrameworks=net10.0-ios \
        -f net10.0-ios \
        -p:RuntimeIdentifier=iossimulator-arm64 \
        -nodeReuse:false
fi

if [[ ! -d "${app_path}" ]]; then
    echo "iOS simulator app not found: ${app_path}" >&2
    exit 1
fi

echo "Installing TOKLONG on both simulators..."
xcrun simctl terminate "${buyer_simulator}" "${bundle_id}" \
    >/dev/null 2>&1 || true
xcrun simctl terminate "${seller_simulator}" "${bundle_id}" \
    >/dev/null 2>&1 || true
xcrun simctl install "${buyer_simulator}" "${app_path}"
xcrun simctl install "${seller_simulator}" "${app_path}"

echo "Starting API, Worker, and Stripe webhook listener..."
: >"${backend_log}"
backend_environment=(
    /usr/bin/env
    "HOME=${HOME}"
    "PATH=${PATH}"
    "TMPDIR=${TMPDIR:-/tmp}"
    "TOKLONG_BACKEND_RUNTIME_DIR=${backend_runtime_directory}"
    "TOKLONG_STRIPE_TEST_PORT=${api_port}"
)
if [[ -n "${STRIPE_CONFIG_PATH:-}" ]]; then
    backend_environment+=(
        "STRIPE_CONFIG_PATH=${STRIPE_CONFIG_PATH}"
    )
fi
launchctl submit \
    -l "${backend_launch_label}" \
    -o "${backend_log}" \
    -e "${backend_log}" \
    -- "${backend_environment[@]}" \
    "${repo_root}/scripts/run-stripe-test-api.sh"

ready=false
for _ in {1..90}; do
    if curl -fsS "${api_ready_url}" >/dev/null 2>&1; then
        ready=true
        break
    fi
    if ! launchctl print \
        "${launch_domain}/${backend_launch_label}" \
        >/dev/null 2>&1; then
        echo "Backend stopped during startup. See ${backend_log}" >&2
        tail -n 40 "${backend_log}" >&2 || true
        exit 1
    fi
    sleep 1
done
if [[ "${ready}" != true ]]; then
    echo "Backend did not become ready. See ${backend_log}" >&2
    exit 1
fi

echo "Launching buyer and seller apps..."
xcrun simctl launch "${buyer_simulator}" "${bundle_id}"
xcrun simctl launch "${seller_simulator}" "${bundle_id}"

trap - EXIT
echo
echo "TOKLONG local environment is ready."
echo "API: http://127.0.0.1:${api_port}"
echo "Backend log: ${backend_log}"
echo "Stop everything with: ./scripts/stop-local-dual-sim.sh"
