#!/usr/bin/env bash
set -euo pipefail

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

export SHIPPOP_CERTIFY=1
dotnet test \
  tests/Toklong.Shippop.Certification/Toklong.Shippop.Certification.csproj \
  --filter FullyQualifiedName~Certified_service_returns_full_value_insured_quote \
  --logger "console;verbosity=minimal"
