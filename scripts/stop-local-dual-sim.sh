#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
runtime_directory="${TOKLONG_LOCAL_RUNTIME_DIR:-${TMPDIR:-/tmp}/toklong-local-dual-sim}"
backend_runtime_directory="${runtime_directory}/backend"
backend_launch_label="${TOKLONG_BACKEND_LAUNCH_LABEL:-th.co.toklong.local-backend}"
launch_domain="gui/$(id -u)"
postgres_started_marker="${runtime_directory}/postgres-started"
buyer_simulator="${TOKLONG_BUYER_SIMULATOR_UDID:-FD76A775-469E-44B3-8561-52B61A406DE4}"
seller_simulator="${TOKLONG_SELLER_SIMULATOR_UDID:-FBE4866E-8265-4264-8053-23A4828AC85C}"
bundle_id="${TOKLONG_IOS_BUNDLE_ID:-th.co.toklong.mobile}"

stop_pid_file() {
    local pid_file="$1"
    if [[ ! -r "${pid_file}" ]]; then
        return
    fi
    local process_pid
    process_pid="$(<"${pid_file}")"
    if [[ "${process_pid}" =~ ^[0-9]+$ ]] &&
        kill -0 "${process_pid}" 2>/dev/null; then
        kill "${process_pid}" 2>/dev/null || true
    fi
}

echo "Closing TOKLONG on both simulators..."
xcrun simctl terminate "${buyer_simulator}" "${bundle_id}" \
    >/dev/null 2>&1 || true
xcrun simctl terminate "${seller_simulator}" "${bundle_id}" \
    >/dev/null 2>&1 || true

echo "Stopping API, Worker, and Stripe webhook listener..."
launchctl remove "${backend_launch_label}" \
    >/dev/null 2>&1 || true

for _ in {1..50}; do
    if ! launchctl print \
        "${launch_domain}/${backend_launch_label}" \
        >/dev/null 2>&1; then
        break
    fi
    sleep 0.1
done

for child_name in api worker stripe-listener; do
    stop_pid_file "${backend_runtime_directory}/${child_name}.pid"
done
rm -f -- \
    "${runtime_directory}/backend-runner.pid" \
    "${backend_runtime_directory}/api.pid" \
    "${backend_runtime_directory}/worker.pid" \
    "${backend_runtime_directory}/stripe-listener.pid"

if [[ "${TOKLONG_KEEP_POSTGRES_RUNNING:-0}" != 1 ]]; then
    echo "Stopping PostgreSQL..."
    docker compose \
        -f "${repo_root}/compose.yml" \
        stop postgres
    rm -f -- "${postgres_started_marker}"
else
    echo "Leaving PostgreSQL running."
fi

if [[ "${TOKLONG_KEEP_SIMULATORS_BOOTED:-0}" != 1 ]]; then
    echo "Shutting down buyer and seller simulators..."
    xcrun simctl shutdown "${buyer_simulator}" >/dev/null 2>&1 || true
    xcrun simctl shutdown "${seller_simulator}" >/dev/null 2>&1 || true
fi

echo "TOKLONG local environment stopped."
