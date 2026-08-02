#!/usr/bin/env bash
set -euo pipefail

mode="${1:-parcel-protection}"
case "${mode}" in
  parcel-protection)
    test_filter="FullyQualifiedName~Protection_quote_and_booking_preserve_exact_values"
    ;;
  full-lifecycle)
    test_filter="FullyQualifiedName~Full_lifecycle_calls_every_current_endpoint_and_cleans_up"
    ;;
  counter-qr-observe)
    test_filter="FullyQualifiedName~Observe_booking_and_confirm_for_counter_qr_candidate"
    ;;
  *)
    echo "Usage: ./scripts/shippop-certify.sh [parcel-protection|full-lifecycle|counter-qr-observe]" >&2
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

if [[ "${SHIPPOP_BASE_URL}" != \
      "https://mkpservice.shippop.dev" &&
      "${SHIPPOP_BASE_URL}" != \
      "https://mkpservice.shippop.dev/" ]]; then
  echo "SHIPPOP_BASE_URL must be the approved HTTPS Sandbox endpoint." >&2
  exit 2
fi

if [[ "${mode}" == "full-lifecycle" &&
      "${SHIPPOP_CERTIFY_MUTATIONS:-}" != "1" ]]; then
  echo "Set SHIPPOP_CERTIFY_MUTATIONS=1 for the synthetic lifecycle." >&2
  exit 2
fi

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

export SHIPPOP_CERTIFY=1

dotnet test \
  tests/Toklong.Shippop.Certification/Toklong.Shippop.Certification.csproj \
  --filter "${test_filter}" \
  --logger "console;verbosity=minimal"
