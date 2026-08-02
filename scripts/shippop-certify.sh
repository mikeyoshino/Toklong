#!/usr/bin/env bash
set -euo pipefail

mode="${1:-parcel-protection}"
case "${mode}" in
  parcel-protection|full-lifecycle)
    ;;
  *)
    echo "Usage: ./scripts/shippop-certify.sh [parcel-protection|full-lifecycle]" >&2
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

export SHIPPOP_CERTIFY=1

case "${mode}" in
  parcel-protection)
    filter="FullyQualifiedName~Protection_quote_and_booking_preserve_exact_values"
    ;;
  full-lifecycle)
    filter="FullyQualifiedName~Full_lifecycle_calls_every_current_endpoint_and_cleans_up"
    ;;
esac

dotnet test \
  tests/Toklong.Shippop.Certification/Toklong.Shippop.Certification.csproj \
  --filter "${filter}" \
  --logger "console;verbosity=minimal"
