#!/usr/bin/env bash

set -euo pipefail
umask 077

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
api_port="${TOKLONG_STRIPE_TEST_PORT:-5182}"
api_base_url="http://127.0.0.1:${api_port}"
item_price_satang=100000
expected_buyer_protection_fee_satang=5900
run_refund_flow="${TOKLONG_STRIPE_TEST_REFUND:-false}"
stripe_config_path="${STRIPE_CONFIG_PATH:-${HOME}/.config/stripe/config.toml}"
temporary_directory="$(mktemp -d "${TMPDIR:-/tmp}/toklong-stripe-test.XXXXXX")"
api_pid=""
listener_pid=""
worker_pid=""
payment_intent_id=""

cleanup() {
    if [[ -n "${api_pid}" ]] && kill -0 "${api_pid}" 2>/dev/null; then
        kill "${api_pid}" 2>/dev/null || true
        wait "${api_pid}" 2>/dev/null || true
    fi
    if [[ -n "${listener_pid}" ]] &&
        kill -0 "${listener_pid}" 2>/dev/null; then
        kill "${listener_pid}" 2>/dev/null || true
        wait "${listener_pid}" 2>/dev/null || true
    fi
    if [[ -n "${worker_pid}" ]] &&
        kill -0 "${worker_pid}" 2>/dev/null; then
        kill "${worker_pid}" 2>/dev/null || true
        wait "${worker_pid}" 2>/dev/null || true
    fi
    if [[ -n "${payment_intent_id}" ]] &&
        command -v stripe >/dev/null 2>&1; then
        stripe payment_intents cancel "${payment_intent_id}" \
            >/dev/null 2>&1 || true
    fi
    rm -r -- "${temporary_directory}"
}
trap cleanup EXIT INT TERM

report_error() {
    local exit_code="$?"
    if [[ -s "${temporary_directory}/api.log" ]]; then
        echo "บันทึกข้อผิดพลาดล่าสุดจาก Toklong.Api:" >&2
        tail -30 "${temporary_directory}/api.log" >&2
    fi
    exit "${exit_code}"
}
trap report_error ERR

if [[ "${run_refund_flow}" != "true" &&
      "${run_refund_flow}" != "false" ]]; then
    echo "TOKLONG_STRIPE_TEST_REFUND ต้องเป็น true หรือ false" >&2
    exit 1
fi

require_command() {
    if ! command -v "$1" >/dev/null 2>&1; then
        echo "ต้องติดตั้งคำสั่ง '$1' ก่อนรันการทดสอบ" >&2
        exit 1
    fi
}

api_request() {
    local response_file="${temporary_directory}/last-api-response.json"
    local status_code
    status_code="$(curl -sS \
        -o "${response_file}" \
        -w '%{http_code}' \
        "$@")"
    if [[ "${status_code}" -lt 200 || "${status_code}" -ge 300 ]]; then
        echo "Toklong.Api ตอบ HTTP ${status_code}" >&2
        jq -r '.detail // .title // .' \
            "${response_file}" >&2 2>/dev/null ||
            echo "ไม่สามารถอ่านรายละเอียดข้อผิดพลาด" >&2
        return 1
    fi
    cat "${response_file}"
}

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

wait_for_api() {
    local attempt
    for attempt in {1..60}; do
        if curl -fsS "${api_base_url}/health/ready" \
            >/dev/null 2>&1; then
            return
        fi
        if ! kill -0 "${api_pid}" 2>/dev/null; then
            echo "Toklong.Api เริ่มทำงานไม่สำเร็จ" >&2
            tail -40 "${temporary_directory}/api.log" >&2
            exit 1
        fi
        sleep 1
    done
    echo "Toklong.Api ไม่พร้อมภายใน 60 วินาที" >&2
    tail -40 "${temporary_directory}/api.log" >&2
    exit 1
}

sign_up() {
    local phone_number="$1"
    local full_name="$2"
    local email="${phone_number}@example.com"
    local request_body
    local challenge_response
    local challenge_id
    local development_code
    local verification_body

    request_body="$(jq -nc \
        --arg phoneNumber "${phone_number}" \
        --arg fullName "${full_name}" \
        --arg email "${email}" \
        '{
            phoneNumber: $phoneNumber,
            mode: "SignUp",
            fullName: $fullName,
            email: $email
        }')"
    challenge_response="$(api_request \
        -H "Content-Type: application/json" \
        -d "${request_body}" \
        "${api_base_url}/api/mobile/auth/otp/request")"
    challenge_id="$(jq -er '.challengeId' <<<"${challenge_response}")"
    development_code="$(
        jq -er '.developmentCode' <<<"${challenge_response}"
    )"
    verification_body="$(jq -nc \
        --arg challengeId "${challenge_id}" \
        --arg code "${development_code}" \
        --arg fullName "${full_name}" \
        --arg email "${email}" \
        '{
            challengeId: $challengeId,
            code: $code,
            mode: "SignUp",
            fullName: $fullName,
            email: $email
        }')"
    api_request \
        -H "Content-Type: application/json" \
        -d "${verification_body}" \
        "${api_base_url}/api/mobile/auth/otp/verify"
}

for command_name in curl jq stripe dotnet docker lsof base64; do
    require_command "${command_name}"
done

if lsof -nP -iTCP:"${api_port}" -sTCP:LISTEN \
    >/dev/null 2>&1; then
    echo "พอร์ต ${api_port} ถูกใช้งานอยู่ ตั้งพอร์ตอื่นด้วย TOKLONG_STRIPE_TEST_PORT" >&2
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
    echo "สคริปต์นี้รับเฉพาะ Stripe Test Mode keys เท่านั้น" >&2
    exit 1
fi

echo "Building Toklong.Api..."
dotnet build \
    "${repo_root}/src/Toklong.Api/Toklong.Api.csproj" \
    --nologo \
    >"${temporary_directory}/build.log"
if [[ "${run_refund_flow}" == "true" ]]; then
    dotnet build \
        "${repo_root}/src/Toklong.Crm/Toklong.Crm.csproj" \
        --nologo \
        >>"${temporary_directory}/build.log"
    dotnet build \
        "${repo_root}/src/Toklong.Worker/Toklong.Worker.csproj" \
        --nologo \
        >>"${temporary_directory}/build.log"
fi

docker compose \
    --file "${repo_root}/compose.yml" \
    up -d postgres \
    >"${temporary_directory}/docker.log"

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

ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="${api_base_url}" \
Database__ApplyMigrations=true \
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
DevelopmentDemoSimulation__Enabled="${run_refund_flow}" \
DevelopmentDemoSimulation__StepIntervalSeconds=1 \
dotnet run \
    --project "${repo_root}/src/Toklong.Api/Toklong.Api.csproj" \
    --no-build \
    --no-launch-profile \
    >"${temporary_directory}/api.log" 2>&1 &
api_pid="$!"
wait_for_api

phone_seed="$(( $(date +%s) % 89999998 + 10000000 ))"
buyer_phone="08${phone_seed}"
seller_suffix="$(( phone_seed + 1 ))"
seller_phone="09${seller_suffix}"
echo "Creating isolated buyer and seller test accounts..."
buyer_session="$(sign_up "${buyer_phone}" "ผู้ซื้อ ทดสอบ")"
seller_session="$(sign_up "${seller_phone}" "ผู้ขาย ทดสอบ")"
buyer_access_token="$(jq -er '.accessToken' <<<"${buyer_session}")"
seller_access_token="$(jq -er '.accessToken' <<<"${seller_session}")"
province_id="$(api_request \
    -H "Authorization: Bearer ${buyer_access_token}" \
    "${api_base_url}/api/mobile/addresses/provinces" \
    | jq -er '.[0].id')"
district_id="$(api_request \
    -H "Authorization: Bearer ${buyer_access_token}" \
    "${api_base_url}/api/mobile/addresses/districts/${province_id}" \
    | jq -er '.[0].id')"
subdistrict_id="$(api_request \
    -H "Authorization: Bearer ${buyer_access_token}" \
    "${api_base_url}/api/mobile/addresses/subdistricts/${district_id}" \
    | jq -er '.[0].id')"

printf '%s' \
    'iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAIAAACQkWg2AAAACXBIWXMAAAABAAAAAQBPJcTWAAAAGUlEQVR4nGPUq3vDQApgIUn1qIZRDUNKAwA/3QHWYsxWOgAAAABJRU5ErkJggg==' \
    | base64 --decode >"${temporary_directory}/product.png"

echo "Creating a physical-item offer..."
offer_response="$(api_request \
    -H "Authorization: Bearer ${buyer_access_token}" \
    -F "sellerPhoneNumber=${seller_phone}" \
    -F "fulfillmentType=PhysicalShipment" \
    -F "productName=กล้องทดสอบ Stripe" \
    -F "agreementDetails=กล้องทดสอบ Stripe Test Mode พร้อมอุปกรณ์ตามภาพ" \
    -F "condition=UsedGood" \
    -F "knownDefects=มีรอยใช้งานเล็กน้อย" \
    -F "amountSatang=${item_price_satang}" \
    -F "useSavedAddress=false" \
    -F "addressLine=99 อาคารทดสอบ Stripe" \
    -F "provinceId=${province_id}" \
    -F "districtId=${district_id}" \
    -F "subdistrictId=${subdistrict_id}" \
    -F "rememberAddress=false" \
    -F "photo=@${temporary_directory}/product.png;type=image/png" \
    "${api_base_url}/api/mobile/offers")"
transaction_id="$(jq -er '.id' <<<"${offer_response}")"
seller_invitation_url="$(jq -er '.sellerInvitationUrl' <<<"${offer_response}")"
seller_offer_token="${seller_invitation_url%/}"
seller_offer_token="${seller_offer_token##*/}"

echo "Accepting the offer as the seller..."
seller_offer="$(api_request \
    -H "Authorization: Bearer ${seller_access_token}" \
    "${api_base_url}/api/mobile/seller-offers/${seller_offer_token}")"
buyer_protection_fee_satang="$(
    jq -er '.buyerProtectionFeeSatang' <<<"${seller_offer}"
)"
platform_fee_satang="$(
    jq -er '.platformFeeSatang' <<<"${seller_offer}"
)"
seller_expected_net_satang="$(
    jq -er '.sellerExpectedNetSatang' <<<"${seller_offer}"
)"
fee_policy_version="$(
    jq -er '.feePolicyVersion' <<<"${seller_offer}"
)"
if (( buyer_protection_fee_satang !=
      expected_buyer_protection_fee_satang ||
      platform_fee_satang != 0 ||
      seller_expected_net_satang != item_price_satang )) ||
   [[ "${fee_policy_version}" != "buyer-protection-v2" ]]; then
    echo "ราคา Buyer Protection ที่เปิดเผยไม่ตรงกับ buyer-protection-v2" >&2
    exit 1
fi

payout_request="$(jq -nc \
    '{
        accountId: null,
        bankCode: "TESTBANK",
        accountName: "ผู้ขาย ทดสอบ",
        accountNumber: "1234567890"
    }')"
payout_response="$(api_request \
    -X PUT \
    -H "Authorization: Bearer ${seller_access_token}" \
    -H "Content-Type: application/json" \
    -d "${payout_request}" \
    "${api_base_url}/api/mobile/seller/payout-account")"
seller_access_token="$(
    jq -er '.session.accessToken' <<<"${payout_response}"
)"
payout_account_id="$(
    jq -er '.payoutAccounts[0].id' <<<"${payout_response}"
)"

shipping_quote_request="$(jq -nc \
    --arg addressLine "88 ต้นทางทดสอบ Stripe" \
    --argjson provinceId "${province_id}" \
    --argjson districtId "${district_id}" \
    --argjson subdistrictId "${subdistrict_id}" \
    '{
        useSavedOrigin: false,
        addressLine: $addressLine,
        provinceId: $provinceId,
        districtId: $districtId,
        subdistrictId: $subdistrictId,
        weightGrams: 1200,
        widthCentimeters: 20,
        lengthCentimeters: 30,
        heightCentimeters: 15
    }')"
shipping_quotes="$(api_request \
    -H "Authorization: Bearer ${seller_access_token}" \
    -H "Content-Type: application/json" \
    -d "${shipping_quote_request}" \
    "${api_base_url}/api/mobile/seller-offers/${seller_offer_token}/shipping-quotes")"
shipping_quote_reference="$(
    jq -er '.[0].quoteReference' <<<"${shipping_quotes}"
)"
shipping_fee_satang="$(
    jq -er '.[0].feeSatang' <<<"${shipping_quotes}"
)"

accept_request="$(jq -nc \
    --arg payoutAccountId "${payout_account_id}" \
    --argjson buyerProtectionFeeSatang \
        "${buyer_protection_fee_satang}" \
    --argjson platformFeeSatang \
        "$(jq '.platformFeeSatang' <<<"${seller_offer}")" \
    --argjson sellerExpectedNetSatang \
        "$(jq '.sellerExpectedNetSatang' <<<"${seller_offer}")" \
    --arg feePolicyVersion \
        "$(jq -r '.feePolicyVersion' <<<"${seller_offer}")" \
    --arg addressLine "88 ต้นทางทดสอบ Stripe" \
    --argjson provinceId "${province_id}" \
    --argjson districtId "${district_id}" \
    --argjson subdistrictId "${subdistrict_id}" \
    --arg quoteReference "${shipping_quote_reference}" \
    --argjson shippingFeeSatang "${shipping_fee_satang}" \
    '{
        payoutAccountId: $payoutAccountId,
        transferRightsAttested: true,
        sellerAcceptedTerms: true,
        disclosedBuyerProtectionFeeSatang:
            $buyerProtectionFeeSatang,
        disclosedPlatformFeeSatang: $platformFeeSatang,
        disclosedSellerExpectedNetSatang: $sellerExpectedNetSatang,
        disclosedFeePolicyVersion: $feePolicyVersion,
        shipping: {
            useSavedOrigin: false,
            addressLine: $addressLine,
            provinceId: $provinceId,
            districtId: $districtId,
            subdistrictId: $subdistrictId,
            rememberOrigin: true,
            weightGrams: 1200,
            widthCentimeters: 20,
            lengthCentimeters: 30,
            heightCentimeters: 15,
            quoteReference: $quoteReference,
            disclosedShippingFeeSatang: $shippingFeeSatang
        }
    }')"
api_request \
    -H "Authorization: Bearer ${seller_access_token}" \
    -H "Content-Type: application/json" \
    -d "${accept_request}" \
    "${api_base_url}/api/mobile/seller-offers/${seller_offer_token}/accept" \
    >/dev/null

payment_request='{"acceptedTerms":true}'
echo "Creating a Stripe PaymentIntent..."
payment_sheet="$(api_request \
    -H "Authorization: Bearer ${buyer_access_token}" \
    -H "Content-Type: application/json" \
    -d "${payment_request}" \
    "${api_base_url}/api/mobile/transactions/${transaction_id}/payment-sheet")"
client_secret="$(jq -er '.clientSecret' <<<"${payment_sheet}")"
payment_intent_id="${client_secret%%_secret_*}"
if [[ "${payment_intent_id}" != pi_* ]]; then
    echo "API ไม่ได้ส่ง PaymentIntent client secret ที่ถูกต้อง" >&2
    exit 1
fi
payment_pending_transaction="$(api_request \
    -H "Authorization: Bearer ${buyer_access_token}" \
    "${api_base_url}/api/mobile/transactions/${transaction_id}")"
expected_buyer_total_satang=$((
    item_price_satang +
    shipping_fee_satang +
    expected_buyer_protection_fee_satang
))
payment_pending_state="$(
    jq -er '.state' <<<"${payment_pending_transaction}"
)"
stored_item_price_satang="$(
    jq -er '.itemPriceSatang' <<<"${payment_pending_transaction}"
)"
stored_shipping_fee_satang="$(
    jq -er '.shippingFeeSatang' <<<"${payment_pending_transaction}"
)"
stored_buyer_protection_fee_satang="$(
    jq -er '.buyerProtectionFeeSatang' <<<"${payment_pending_transaction}"
)"
stored_buyer_total_satang="$(
    jq -er '.amountSatang' <<<"${payment_pending_transaction}"
)"
if [[ "${payment_pending_state}" != "PaymentPending" ]] ||
   (( stored_item_price_satang != item_price_satang ||
      stored_shipping_fee_satang != shipping_fee_satang ||
      stored_buyer_protection_fee_satang !=
          expected_buyer_protection_fee_satang ||
      stored_buyer_total_satang != expected_buyer_total_satang )); then
    echo "ยอด immutable ก่อนส่งให้ Stripe ไม่ตรงกับรายการ" >&2
    exit 1
fi

echo "Confirming the PaymentIntent with Stripe test card..."
stripe payment_intents confirm "${payment_intent_id}" \
    --confirm \
    --payment-method pm_card_visa \
    --return-url https://toklong.co.th/payment/return \
    >"${temporary_directory}/stripe-confirm.json"

echo "Waiting for the signature-verified Stripe webhook..."
final_state=""
for attempt in {1..30}; do
    final_state="$(api_request \
        -H "Authorization: Bearer ${buyer_access_token}" \
        "${api_base_url}/api/mobile/transactions/${transaction_id}" \
        | jq -er '.state')"
    if [[ "${final_state}" == "PaidAwaitingShipment" ||
          "${final_state}" == "TrackingSubmitted" ||
          "${final_state}" == "InTransit" ||
          "${final_state}" == "DeliveredDisputeWindow" ]]; then
        break
    fi
    sleep 1
done

if [[ "${final_state}" != "PaidAwaitingShipment" &&
      "${final_state}" != "TrackingSubmitted" &&
      "${final_state}" != "InTransit" &&
      "${final_state}" != "DeliveredDisputeWindow" ]]; then
    echo "Stripe รับการชำระแล้ว แต่ webhook ยังไม่เปลี่ยนสถานะรายการ" >&2
    tail -40 "${temporary_directory}/api.log" >&2
    exit 1
fi

if [[ "${run_refund_flow}" == "true" ]]; then
    echo "Waiting for verified Development delivery..."
    delivered_state=""
    for attempt in {1..30}; do
        delivered_state="$(api_request \
            -H "Authorization: Bearer ${buyer_access_token}" \
            "${api_base_url}/api/mobile/transactions/${transaction_id}" \
            | jq -er '.state')"
        if [[ "${delivered_state}" == "DeliveredDisputeWindow" ]]; then
            break
        fi
        sleep 1
    done
    if [[ "${delivered_state}" != "DeliveredDisputeWindow" ]]; then
        echo "รายการยังไม่ถึงสถานะที่ผู้ซื้อเปิดปัญหาได้" >&2
        exit 1
    fi

    echo "Opening a buyer dispute..."
    dispute_request="$(jq -nc \
        '{
            reason: "NotAsDescribed",
            statement:
                "สินค้าทดสอบมีสภาพไม่ตรงกับรายละเอียด ใช้สำหรับ Stripe refund sandbox"
        }')"
    disputed_transaction="$(api_request \
        -H "Authorization: Bearer ${buyer_access_token}" \
        -H "Content-Type: application/json" \
        -d "${dispute_request}" \
        "${api_base_url}/api/mobile/transactions/${transaction_id}/disputes")"
    if [[ "$(jq -er '.state' <<<"${disputed_transaction}")" != "Disputed" ]]; then
        echo "ผู้ซื้อเปิดข้อโต้แย้งไม่สำเร็จ" >&2
        exit 1
    fi

    echo "Applying three-person Local CRM full-refund approval..."
    ASPNETCORE_ENVIRONMENT=Development \
    Database__ApplyMigrations=true \
    DevelopmentAccess__Enabled=true \
    DevelopmentRefundTest__TransactionId="${transaction_id}" \
    dotnet run \
        --project "${repo_root}/src/Toklong.Crm/Toklong.Crm.csproj" \
        --no-build \
        --no-launch-profile \
        -- \
        --apply-demo-full-refund \
        >"${temporary_directory}/crm-apply.log"

    echo "Starting the refund Worker..."
    DOTNET_ENVIRONMENT=Development \
    ASPNETCORE_ENVIRONMENT=Development \
    Stripe__Enabled=true \
    Stripe__LiveMode=false \
    Stripe__EnableDigitalGoods=false \
    Stripe__PublishableKey="${stripe_publishable_key}" \
    Stripe__SecretKey="${stripe_secret_key}" \
    Stripe__WebhookSecret="${stripe_webhook_secret}" \
    BuyerProtectionFee__Enabled=true \
    BuyerProtectionFee__PolicyVersion=buyer-protection-v2 \
    ShippingQuotes__Provider=Development \
    dotnet run \
        --project "${repo_root}/src/Toklong.Worker/Toklong.Worker.csproj" \
        --no-build \
        --no-launch-profile \
        >"${temporary_directory}/worker.log" 2>&1 &
    worker_pid="$!"

    echo "Waiting for verified Stripe refund completion..."
    refund_state=""
    for attempt in {1..60}; do
        refund_state="$(api_request \
            -H "Authorization: Bearer ${buyer_access_token}" \
            "${api_base_url}/api/mobile/transactions/${transaction_id}" \
            | jq -er '.state')"
        if [[ "${refund_state}" == "Refunded" ]]; then
            break
        fi
        if ! kill -0 "${worker_pid}" 2>/dev/null; then
            echo "Toklong.Worker หยุดก่อนคืนเงินสำเร็จ" >&2
            tail -40 "${temporary_directory}/worker.log" >&2
            exit 1
        fi
        sleep 1
    done
    if [[ "${refund_state}" != "Refunded" ]]; then
        echo "Stripe refund ยังไม่เปลี่ยนรายการเป็น Refunded" >&2
        tail -40 "${temporary_directory}/worker.log" >&2
        exit 1
    fi

    echo "Verifying CRM, audit, external event, and notifications..."
    ASPNETCORE_ENVIRONMENT=Development \
    Database__ApplyMigrations=true \
    DevelopmentAccess__Enabled=true \
    DevelopmentRefundTest__TransactionId="${transaction_id}" \
    dotnet run \
        --project "${repo_root}/src/Toklong.Crm/Toklong.Crm.csproj" \
        --no-build \
        --no-launch-profile \
        -- \
        --verify-demo-full-refund \
        >"${temporary_directory}/crm-verify.log"
fi

echo "Stripe Test Mode ผ่านครบเส้นทาง"
echo "Transaction: ${transaction_id}"
echo "PaymentIntent: ${payment_intent_id}"
if [[ "${run_refund_flow}" == "true" ]]; then
    echo "State: ${refund_state}"
    echo "CRM approval, refund audit, and notifications: verified"
else
    echo "State: ${final_state}"
fi
echo "การโอนเงินให้ผู้ขายไม่ได้ถูกรัน และยังเป็นงาน manual ผ่านธนาคาร"
