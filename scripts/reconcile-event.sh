#!/usr/bin/env bash
set -euo pipefail

event_kind="${1:-}"
transaction_id="${2:-}"
event_id="${3:-}"
event_type="${4:-}"
base_url="${TOKLONG_BASE_URL:-http://127.0.0.1:5180}"
signing_secret="${TOKLONG_SIGNING_SECRET:-local-development-only-not-for-production}"

if [[ -z "$event_kind" || -z "$transaction_id" || -z "$event_id" ]]; then
  echo "Usage: $0 payment|carrier|payout TRANSACTION_ID EVENT_ID [in_transit|delivered|unverified]"
  exit 2
fi

timestamp="$(date -u +'%Y-%m-%dT%H:%M:%SZ')"
epoch="$(date -u +'%s')"
compact_id="${transaction_id//-/}"

case "$event_kind" in
  payment)
    endpoint="manual-payment"
    payload="payment|${compact_id}|${event_id}|${epoch}"
    body="{\"transactionId\":\"${transaction_id}\",\"eventId\":\"${event_id}\",\"confirmedAt\":\"${timestamp}\"}"
    ;;
  carrier)
    if [[ "$event_type" != "in_transit" && "$event_type" != "delivered" && "$event_type" != "unverified" ]]; then
      echo "Carrier event type must be in_transit, delivered, or unverified"
      exit 2
    fi
    endpoint="carrier"
    payload="carrier|${compact_id}|${event_id}|${event_type}|${epoch}"
    body="{\"transactionId\":\"${transaction_id}\",\"eventId\":\"${event_id}\",\"eventType\":\"${event_type}\",\"occurredAt\":\"${timestamp}\"}"
    ;;
  payout)
    endpoint="manual-payout"
    payload="payout|${compact_id}|${event_id}|${epoch}"
    body="{\"transactionId\":\"${transaction_id}\",\"eventId\":\"${event_id}\",\"confirmedAt\":\"${timestamp}\"}"
    ;;
  *)
    echo "Event kind must be payment, carrier, or payout"
    exit 2
    ;;
esac

signature="$(printf '%s' "$payload" | openssl dgst -sha256 -hmac "$signing_secret" -hex | awk '{print $NF}')"

curl --fail-with-body \
  --request POST \
  --header "Content-Type: application/json" \
  --header "X-Toklong-Signature: ${signature}" \
  --data "$body" \
  "${base_url}/api/webhooks/${endpoint}"
echo
