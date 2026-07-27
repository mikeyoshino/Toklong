#!/usr/bin/env bash
set -euo pipefail

event_kind="${1:-}"
transaction_id="${2:-}"
event_id="${3:-}"
event_type="${4:-}"
carrier_code="${5:-}"
tracking_number="${6:-}"
base_url="${TOKLONG_BASE_URL:-http://127.0.0.1:5180}"
signing_secret="${TOKLONG_SIGNING_SECRET:-local-development-only-not-for-production}"

if [[ -z "$event_kind" || -z "$transaction_id" || -z "$event_id" ]]; then
  echo "Usage: $0 payment|carrier|payout TRANSACTION_ID EVENT_ID [EVENT_TYPE CARRIER_CODE TRACKING_NUMBER]"
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
    if [[ -z "$carrier_code" || -z "$tracking_number" ]]; then
      echo "Carrier events require CARRIER_CODE and TRACKING_NUMBER"
      exit 2
    fi
    carrier_code="$(printf '%s' "$carrier_code" | tr '[:lower:]' '[:upper:]')"
    tracking_number="$(printf '%s' "$tracking_number" | tr -cd '[:alnum:]' | tr '[:lower:]' '[:upper:]')"
    endpoint="carrier"
    payload="carrier|${compact_id}|${event_id}|${event_type}|${carrier_code}|${tracking_number}|${epoch}"
    body="{\"transactionId\":\"${transaction_id}\",\"eventId\":\"${event_id}\",\"eventType\":\"${event_type}\",\"occurredAt\":\"${timestamp}\",\"carrierCode\":\"${carrier_code}\",\"trackingNumber\":\"${tracking_number}\"}"
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
