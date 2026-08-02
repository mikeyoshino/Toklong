#!/usr/bin/env bash

toklong_validate_local_shipping_mode() {
    local mode="${TOKLONG_SHIPPING_MODE:-Development}"
    case "${mode}" in
        Development)
            case "${TOKLONG_DEVELOPMENT_AUTO_ADVANCE:-1}" in
                0|1)
                    ;;
                *)
                    echo "ค่า TOKLONG_DEVELOPMENT_AUTO_ADVANCE ต้องเป็น 0 หรือ 1" >&2
                    return 2
                    ;;
            esac
            return 0
            ;;
        ShippopSandbox)
            ;;
        *)
            echo "ค่า TOKLONG_SHIPPING_MODE ไม่ถูกต้อง" >&2
            return 2
            ;;
    esac

    local required_name
    for required_name in \
        SHIPPOP_API_KEY \
        SHIPPOP_ACCOUNT_EMAIL \
        SHIPPOP_SERVICE_CODE \
        SHIPPOP_QUOTE_SIGNING_SECRET; do
        if [[ -z "${!required_name:-}" ]]; then
            echo "กรุณากำหนด ${required_name}" >&2
            return 2
        fi
    done

    case "${SHIPPOP_SERVICE_CODE}" in
        EMST|FLE|KRYX|KRYS)
            ;;
        *)
            echo "ค่า SHIPPOP_SERVICE_CODE ไม่ถูกต้อง" >&2
            return 2
            ;;
    esac

    if [[ "${#SHIPPOP_QUOTE_SIGNING_SECRET}" -lt 32 ]]; then
        echo "SHIPPOP_QUOTE_SIGNING_SECRET ต้องมีอย่างน้อย 32 ตัวอักษร" >&2
        return 2
    fi
    if [[ "${SHIPPOP_API_KEY}" == \
          "${SHIPPOP_QUOTE_SIGNING_SECRET}" ]]; then
        echo "SHIPPOP_QUOTE_SIGNING_SECRET ต้องไม่ซ้ำกับ SHIPPOP_API_KEY" >&2
        return 2
    fi
}

toklong_apply_local_shipping_mode() {
    local mode="${TOKLONG_SHIPPING_MODE:-Development}"
    local keys_path="${1:-}"
    local api_key="${2:-}"
    local account_email="${3:-}"
    local service_code="${4:-}"
    local quote_signing_secret="${5:-}"

    if [[ "${mode}" == "Development" ]]; then
        case "${keys_path}" in
            /*)
                ;;
            *)
                echo "DataProtection key path ต้องเป็น absolute path" >&2
                return 2
                ;;
        esac
        case "${TOKLONG_DEVELOPMENT_AUTO_ADVANCE:-1}" in
            0)
                export DevelopmentDemoSimulation__Enabled=false
                ;;
            1)
                export DevelopmentDemoSimulation__Enabled=true
                ;;
            *)
                echo "ค่า TOKLONG_DEVELOPMENT_AUTO_ADVANCE ต้องเป็น 0 หรือ 1" >&2
                return 2
                ;;
        esac
        mkdir -p -- "${keys_path}"
        chmod 700 "${keys_path}"
        export ShippingQuotes__Provider=Development
        export DataProtection__KeysPath="${keys_path}"
        return 0
    fi
    if [[ "${mode}" != "ShippopSandbox" ]]; then
        echo "ค่า TOKLONG_SHIPPING_MODE ไม่ถูกต้อง" >&2
        return 2
    fi
    case "${keys_path}" in
        /*)
            ;;
        *)
            echo "DataProtection key path ต้องเป็น absolute path" >&2
            return 2
            ;;
    esac
    case "${service_code}" in
        EMST|FLE|KRYX|KRYS)
            ;;
        *)
            echo "ค่า SHIPPOP_SERVICE_CODE ไม่ถูกต้อง" >&2
            return 2
            ;;
    esac
    if [[ -z "${api_key}" || -z "${account_email}" ||
          "${#quote_signing_secret}" -lt 32 ||
          "${api_key}" == "${quote_signing_secret}" ]]; then
        echo "การตั้งค่า SHIPPOP Sandbox ไม่ครบ" >&2
        return 2
    fi

    mkdir -p -- "${keys_path}"
    chmod 700 "${keys_path}"

    export ShippingQuotes__Provider=Shippop
    export DevelopmentDemoSimulation__Enabled=false
    export DataProtection__KeysPath="${keys_path}"
    export Shippop__BaseUrl=https://mkpservice.shippop.dev
    export Shippop__AllowInsecureHttp=false
    export Shippop__ApiKey="${api_key}"
    export Shippop__AccountEmail="${account_email}"
    export Shippop__QuoteSigningSecret="${quote_signing_secret}"
    export Shippop__DirectBookingEnabled=true
    export Shippop__DirectBookingCertificationReference=development-shippop-sandbox-not-production-certified
    export Shippop__ServiceCodes__0="${service_code}"
    unset Shippop__ServiceCodes__1 \
        Shippop__ServiceCodes__2 \
        Shippop__ServiceCodes__3

    local code
    for code in EMST FLE KRYX KRYS; do
        export "Shippop__Services__${code}__QuoteEnabled=false"
        export "Shippop__Services__${code}__BookOutboundEnabled=false"
        export "Shippop__Services__${code}__ConfirmEnabled=false"
        export "Shippop__Services__${code}__ReturnEnabled=false"
        export "Shippop__Services__${code}__InsuranceEnabled=false"
        export "Shippop__Services__${code}__OptionalProtectionEnabled=false"
        export "Shippop__Services__${code}__OperationLookupEnabled=false"
        export "Shippop__Services__${code}__HandoffMode=DropOff"
        export "Shippop__Services__${code}__MaximumCoverageSatang=0"
        export "Shippop__Services__${code}__IncludedCoverageSatang=0"
        export "Shippop__Services__${code}__CertificationReference="
    done

    export "Shippop__Services__${service_code}__QuoteEnabled=true"
    export "Shippop__Services__${service_code}__BookOutboundEnabled=true"
    export "Shippop__Services__${service_code}__ConfirmEnabled=true"
    export "Shippop__Services__${service_code}__OperationLookupEnabled=true"
}
