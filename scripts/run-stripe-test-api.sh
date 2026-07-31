#!/usr/bin/env bash

set -euo pipefail
umask 077

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
api_port="${TOKLONG_STRIPE_TEST_PORT:-5181}"
api_base_url="http://127.0.0.1:${api_port}"
stripe_config_path="${STRIPE_CONFIG_PATH:-${HOME}/.config/stripe/config.toml}"
temporary_directory="$(mktemp -d "${TMPDIR:-/tmp}/toklong-stripe-api.XXXXXX")"
runtime_directory="${TOKLONG_BACKEND_RUNTIME_DIR:-}"
listener_pid=""
worker_pid=""
api_pid=""
cleaned_up=false

write_runtime_pid() {
    local name="$1"
    local value="$2"
    if [[ -z "${runtime_directory}" ]]; then
        return
    fi
    mkdir -p -- "${runtime_directory}"
    chmod 700 "${runtime_directory}"
    printf '%s\n' "${value}" >"${runtime_directory}/${name}.pid"
}

cleanup() {
    if [[ "${cleaned_up}" == true ]]; then
        return
    fi
    cleaned_up=true
    if [[ -n "${api_pid}" ]] &&
        kill -0 "${api_pid}" 2>/dev/null; then
        kill "${api_pid}" 2>/dev/null || true
        wait "${api_pid}" 2>/dev/null || true
    fi
    if [[ -n "${worker_pid}" ]] &&
        kill -0 "${worker_pid}" 2>/dev/null; then
        kill "${worker_pid}" 2>/dev/null || true
        wait "${worker_pid}" 2>/dev/null || true
    fi
    if [[ -n "${listener_pid}" ]] &&
        kill -0 "${listener_pid}" 2>/dev/null; then
        kill "${listener_pid}" 2>/dev/null || true
        wait "${listener_pid}" 2>/dev/null || true
    fi
    if [[ -n "${runtime_directory}" ]]; then
        rm -f -- \
            "${runtime_directory}/api.pid" \
            "${runtime_directory}/worker.pid" \
            "${runtime_directory}/stripe-listener.pid"
    fi
    if [[ -d "${temporary_directory}" ]]; then
        rm -r -- "${temporary_directory}"
    fi
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
trap 'exit 129' HUP

read_stripe_config_value() {
    local key="$1"
    awk -F= -v requested_key="${key}" '
        $1 ~ "^[[:space:]]*" requested_key "[[:space:]]*$" {
            value = substr($0, index($0, "=") + 1)
            gsub(/^[[:space:]]+|[[:space:]]+$/, "", value)
            gsub(/^["\047]|["\047]$/, "", value)
            print value
            exit
        }
    ' "${stripe_config_path}"
}

for command_name in stripe dotnet lsof; do
    if ! command -v "${command_name}" >/dev/null 2>&1; then
        echo "ต้องติดตั้งคำสั่ง '${command_name}' ก่อนรัน API" >&2
        exit 1
    fi
done

if lsof -nP -iTCP:"${api_port}" -sTCP:LISTEN \
    >/dev/null 2>&1; then
    echo "พอร์ต ${api_port} ถูกใช้งานอยู่ กรุณาปิด API เดิมก่อน" >&2
    exit 1
fi

if [[ ! -r "${stripe_config_path}" ]] &&
    { [[ -z "${STRIPE_SECRET_KEY:-}" ]] ||
      [[ -z "${STRIPE_PUBLISHABLE_KEY:-}" ]]; }; then
    echo "กรุณา login Stripe CLI หรือกำหนด STRIPE_SECRET_KEY และ STRIPE_PUBLISHABLE_KEY" >&2
    exit 1
fi

stripe_secret_key="${STRIPE_SECRET_KEY:-}"
stripe_publishable_key="${STRIPE_PUBLISHABLE_KEY:-}"
if [[ -z "${stripe_secret_key}" ]]; then
    stripe_secret_key="$(read_stripe_config_value test_mode_api_key)"
fi
if [[ -z "${stripe_publishable_key}" ]]; then
    stripe_publishable_key="$(read_stripe_config_value test_mode_pub_key)"
fi
if [[ "${stripe_secret_key}" != sk_test_* ||
      "${stripe_publishable_key}" != pk_test_* ]]; then
    echo "คำสั่งนี้รับเฉพาะ Stripe Test Mode keys เท่านั้น" >&2
    exit 1
fi

stripe_webhook_secret="$(stripe listen --print-secret)"
if [[ "${stripe_webhook_secret}" != whsec_* ]]; then
    echo "Stripe CLI ไม่ได้ส่ง webhook signing secret ที่ถูกต้อง" >&2
    exit 1
fi

stripe listen \
    --events payment_intent.succeeded,refund.updated \
    --forward-to "${api_base_url}/api/webhooks/stripe" \
    >"${temporary_directory}/stripe-listener.log" 2>&1 &
listener_pid="$!"
write_runtime_pid stripe-listener "${listener_pid}"

DOTNET_ENVIRONMENT=Development \
Stripe__Enabled=true \
Stripe__LiveMode=false \
Stripe__EnableDigitalGoods=false \
Stripe__PublishableKey="${stripe_publishable_key}" \
Stripe__SecretKey="${stripe_secret_key}" \
Stripe__WebhookSecret="${stripe_webhook_secret}" \
BuyerProtectionFee__Enabled=true \
BuyerProtectionFee__PolicyVersion=buyer-protection-v2 \
ShippingQuotes__Provider=Development \
BankPayout__Provider=Manual \
Reconciliation__SigningSecret=local-development-only-not-for-production \
dotnet run \
    --project "${repo_root}/src/Toklong.Worker/Toklong.Worker.csproj" \
    --no-launch-profile \
    >"${temporary_directory}/worker.log" 2>&1 &
worker_pid="$!"
write_runtime_pid worker "${worker_pid}"

echo "Toklong.Api + Worker + Stripe Test Mode: ${api_base_url}"
echo "กด Ctrl+C เพื่อปิด API, Worker และ Stripe webhook listener"

ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="${api_base_url}" \
Database__ApplyMigrations=true \
Logging__LogLevel__Microsoft=Warning \
Stripe__Enabled=true \
Stripe__LiveMode=false \
Stripe__EnableDigitalGoods=false \
Stripe__PublishableKey="${stripe_publishable_key}" \
Stripe__SecretKey="${stripe_secret_key}" \
Stripe__WebhookSecret="${stripe_webhook_secret}" \
BuyerProtectionFee__Enabled=true \
BuyerProtectionFee__PolicyVersion=buyer-protection-v2 \
ShippingQuotes__Provider=Development \
PublicUrls__WebBaseUrl=https://toklong.co.th \
BankPayout__Provider=Manual \
DevelopmentDemoSimulation__Enabled=true \
DevelopmentDemoSimulation__StepIntervalSeconds=3 \
Reconciliation__SigningSecret=local-development-only-not-for-production \
dotnet run \
    --project "${repo_root}/src/Toklong.Api/Toklong.Api.csproj" \
    --no-launch-profile &
api_pid="$!"
write_runtime_pid api "${api_pid}"
wait "${api_pid}"
