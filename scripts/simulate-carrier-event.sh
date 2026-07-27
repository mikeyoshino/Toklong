#!/usr/bin/env bash
set -euo pipefail

transaction_id="${1:-}"
carrier_code="${2:-}"
tracking_number="${3:-}"
event_type="${4:-in_transit}"
base_url="${TOKLONG_API_BASE_URL:-http://127.0.0.1:5181}"
signing_secret="${TOKLONG_SIGNING_SECRET:-local-development-only-not-for-production}"

if [[ -z "$transaction_id" || -z "$carrier_code" || -z "$tracking_number" ]]; then
  echo "Usage: $0 TRANSACTION_ID CARRIER_CODE TRACKING_NUMBER [in_transit|delivered|unverified]"
  exit 2
fi
if [[ "$event_type" != "in_transit" && "$event_type" != "delivered" && "$event_type" != "unverified" ]]; then
  echo "Event type must be in_transit, delivered, or unverified"
  exit 2
fi

carrier_code="$(printf '%s' "$carrier_code" | tr '[:lower:]' '[:upper:]')"
tracking_number="$(printf '%s' "$tracking_number" | tr -cd '[:alnum:]' | tr '[:lower:]' '[:upper:]')"
compact_id="${transaction_id//-/}"
requested_at="$(date -u +'%Y-%m-%dT%H:%M:%SZ')"
requested_epoch="$(date -u +'%s')"
event_id="local-${event_type}-$(date -u +'%Y%m%d%H%M%S')"
payload="carrier|${compact_id}|${event_id}|${event_type}|${carrier_code}|${tracking_number}|${requested_epoch}"
signature="$(printf '%s' "$payload" | openssl dgst -sha256 -hmac "$signing_secret" -hex | awk '{print $NF}')"
body="{\"eventId\":\"${event_id}\",\"eventType\":\"${event_type}\",\"occurredAt\":\"${requested_at}\",\"requestedAt\":\"${requested_at}\",\"carrierCode\":\"${carrier_code}\",\"trackingNumber\":\"${tracking_number}\"}"

curl --fail-with-body \
  --request POST \
  --header "Content-Type: application/json" \
  --header "X-Toklong-Signature: ${signature}" \
  --data "$body" \
  "${base_url}/api/internal/transactions/${transaction_id}/carrier-events"
echo
