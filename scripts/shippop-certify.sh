#!/usr/bin/env bash
set -euo pipefail

mode="${1:-parcel-protection}"
case "${mode}" in
  parcel-protection)
    test_filter="FullyQualifiedName~Protection_quote_and_booking_preserve_exact_values"
    ;;
  counter-qr-observe)
    test_filter="FullyQualifiedName~Observe_booking_and_confirm_for_counter_qr_candidate"
    ;;
  *)
    echo "Usage: ./scripts/shippop-certify.sh [parcel-protection|counter-qr-observe]" >&2
    exit 2
    ;;
esac

required=(
  SHIPPOP_BASE_URL
  SHIPPOP_API_KEY
  SHIPPOP_ACCOUNT_EMAIL
  SHIPPOP_SERVICE_CODE
  SHIPPOP_SYNTHETIC_ADDRESS_JSON
)

for name in "${required[@]}"; do
  if [[ -z "${!name:-}" ]]; then
    echo "Missing required environment variable: ${name}" >&2
    exit 2
  fi
done

if [[ "${mode}" == "counter-qr-observe" ]]; then
  if [[ -z "${SHIPPOP_EVIDENCE_DIRECTORY:-}" ]]; then
    echo "Missing required environment variable: SHIPPOP_EVIDENCE_DIRECTORY" >&2
    exit 2
  fi
  if [[ "${SHIPPOP_CERTIFY_MUTATIONS:-}" != "1" ]]; then
    echo "Counter QR observation requires SHIPPOP_CERTIFY_MUTATIONS=1." >&2
    exit 2
  fi
  umask 077
  export SHIPPOP_REPOSITORY_ROOT="$(pwd -P)"
fi

case "${SHIPPOP_BASE_URL}" in
  https://*)
    ;;
  http://*)
    if [[ "${SHIPPOP_ALLOW_INSECURE_HTTP:-}" != "1" ]]; then
      echo "HTTP requires SHIPPOP_ALLOW_INSECURE_HTTP=1 for explicit Dev certification opt-in." >&2
      exit 2
    fi
    ;;
  *)
    echo "SHIPPOP_BASE_URL must use http:// or https://." >&2
    exit 2
    ;;
esac

export SHIPPOP_CERTIFY=1
dotnet test \
  tests/Toklong.Shippop.Certification/Toklong.Shippop.Certification.csproj \
  --filter "${test_filter}" \
  --logger "console;verbosity=minimal"
