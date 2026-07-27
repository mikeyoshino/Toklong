#!/usr/bin/env bash
set -euo pipefail

operation="${1:-}"
base_url="${TOKLONG_API_BASE_URL:-http://127.0.0.1:5181}"
signing_secret="${TOKLONG_SIGNING_SECRET:-}"

if [[ -z "$signing_secret" ]]; then
  echo "Set TOKLONG_SIGNING_SECRET; no default is allowed"
  exit 2
fi
if ! command -v jq >/dev/null 2>&1; then
  echo "jq is required"
  exit 2
fi

requested_at="$(date -u +'%Y-%m-%dT%H:%M:%SZ')"
epoch="$(date -u +'%s')"

case "$operation" in
  preview)
    batch_size="${2:-100}"
    if ! [[ "$batch_size" =~ ^[0-9]+$ ]] ||
       (( batch_size < 1 || batch_size > 500 )); then
      echo "Batch size must be between 1 and 500"
      exit 2
    fi
    endpoint="/api/internal/retention/preview"
    payload="retention|preview|${batch_size}|${epoch}"
    body="$(jq -n \
      --argjson batchSize "$batch_size" \
      --arg requestedAt "$requested_at" \
      '{batchSize:$batchSize,requestedAt:$requestedAt}')"
    ;;
  hold)
    transaction_id="${2:-}"
    reference="${3:-}"
    reason="${4:-}"
    if [[ -z "$transaction_id" ||
          -z "$reference" ||
          -z "$reason" ||
          "$reference" == *"|"* ||
          "$reason" == *"|"* ]]; then
      echo "Usage: $0 hold TRANSACTION_ID REFERENCE REASON"
      exit 2
    fi
    compact_id="${transaction_id//-/}"
    endpoint="/api/internal/transactions/${transaction_id}/legal-hold"
    payload="legal-hold|place|${compact_id}|${reference}|${reason}|${epoch}"
    body="$(jq -n \
      --arg reference "$reference" \
      --arg reason "$reason" \
      --arg requestedAt "$requested_at" \
      '{reference:$reference,reason:$reason,requestedAt:$requestedAt}')"
    ;;
  release)
    transaction_id="${2:-}"
    reference="${3:-}"
    if [[ -z "$transaction_id" ||
          -z "$reference" ||
          "$reference" == *"|"* ]]; then
      echo "Usage: $0 release TRANSACTION_ID REFERENCE"
      exit 2
    fi
    compact_id="${transaction_id//-/}"
    endpoint="/api/internal/transactions/${transaction_id}/legal-hold/release"
    payload="legal-hold|release|${compact_id}|${reference}|${epoch}"
    body="$(jq -n \
      --arg reference "$reference" \
      --arg requestedAt "$requested_at" \
      '{reference:$reference,requestedAt:$requestedAt}')"
    ;;
  *)
    echo "Usage: $0 preview [BATCH_SIZE]"
    echo "       $0 hold TRANSACTION_ID REFERENCE REASON"
    echo "       $0 release TRANSACTION_ID REFERENCE"
    exit 2
    ;;
esac

signature="$(
  printf '%s' "$payload" |
    openssl dgst -sha256 \
      -hmac "$signing_secret" -hex |
    awk '{print $NF}'
)"

curl --fail-with-body \
  --request POST \
  --header "Content-Type: application/json" \
  --header "X-Toklong-Signature: ${signature}" \
  --data "$body" \
  "${base_url}${endpoint}"
echo
