# SHIPPOP Quote Request and Mobile Feedback Design

Date: 2026-08-02  
Status: Approved direction; written-spec review pending

## Problem

On the seller prepare-sale page, selecting `ดูค่าจัดส่ง` appears to do
nothing. Runtime evidence shows that the mobile action reaches the TOKLONG API
and the API reaches SHIPPOP Sandbox, but SHIPPOP returns an unsuccessful
provider result. The current mobile page renders the shared error message only
after the confirmation and decline actions at the bottom of the page, outside
the context of the shipping-rate action.

The TOKLONG quote payload also differs from SHIPPOP's published Postman Check
Price example. The published request includes `showall: 1` on each shipment,
while `GetQuotesAsync` currently omits it. The rest of the documented address,
parcel, courier, and gram/centimeter fields match the existing request shape.

Official reference:
https://documenter.getpostman.com/view/10021496/Tzz8qwkE

## Goals

1. Send the SHIPPOP Check Price request using the documented shipment shape.
2. Make every quote-request state visible next to `ดูค่าจัดส่ง`.
3. Preserve server-authoritative shipping prices and the existing seller
   acceptance flow.
4. Fail closed when SHIPPOP is unavailable or rejects the request.

## Non-goals

- No simulated or deterministic quote fallback while SHIPPOP Sandbox mode is
  selected.
- No change to payment, seller acceptance, shipment booking, tracking, refund,
  dispute, or payout state transitions.
- No exposure of API keys, raw provider responses, addresses, phone numbers, or
  provider-internal errors in consumer UI or normal logs.
- No claim that the SHIPPOP account or `EMST` service is production-certified.

## Chosen design

### Provider request

`ShippopShippingProvider.GetQuotesAsync` will build each `pricelist/` shipment
with `showall: 1`, matching the published Check Price request. The existing
server-side validation, service allow-list, response parsing, integer-satang
conversion, signed quote reference, and quote expiry remain unchanged.

The application will not retry automatically and will not substitute a local
price. A provider rejection remains a failed quote request, so the seller
cannot accept an unverified or client-computed shipping charge.

### Mobile interaction

The shipping section will own a dedicated presentation state rather than
depending on the page-level message at the bottom:

- Idle: show the enabled `ดูค่าจัดส่ง` action with no status message.
- Loading: keep the button label `ดูค่าจัดส่ง`, disable repeat submission, and
  show a small blue activity indicator plus `กำลังดูค่าจัดส่ง…` in a status
  row immediately below the button.
- Success: show the selectable provider quotes and automatically select the
  first returned quote, as today.
- Empty: show `ยังไม่พบตัวเลือกจัดส่งสำหรับพัสดุนี้` directly below the
  action.
- Validation failure: show the existing specific package or origin guidance
  directly below the action.
- Provider/API failure: show
  `ยังดูค่าจัดส่งไม่ได้ กรุณาลองอีกครั้ง` directly below the action. Raw
  SHIPPOP response text is never forwarded to the app.

Status text uses the existing helper/validation styles, remains understandable
without color, wraps at accessibility text sizes, and is exposed to the screen
reader. The existing button style continues to provide at least a 44-point
touch target.

Editing the origin or any package measurement will continue to invalidate the
selected quote. It will also clear stale quote feedback so a previous success
or failure is not presented as applying to new inputs.

The page-level message remains available for unrelated loading, payout,
acceptance, and decline failures.

### Data flow

```text
Seller taps ดูค่าจัดส่ง
  -> mobile validates origin and parcel fields
  -> mobile shows local loading state
  -> authenticated TOKLONG quote endpoint
  -> SHIPPOP pricelist request with showall: 1
  -> validated provider quote options
     -> success: render/select quote
     -> empty/rejected/unavailable: render inline safe message
```

No step creates a booking, PaymentIntent, transaction transition, or immutable
paid snapshot. Seller acceptance continues to revalidate the selected signed
quote server-side.

## Error and privacy handling

- Provider error bodies remain server-side and are not rendered verbatim.
- The consumer receives only the existing sanitized API error contract.
- The mobile view does not show provider terminology such as webhook,
  reconciliation, or raw status values.
- Repeated taps during an active request do not create concurrent quote calls.
- A failed request leaves no selected quote and therefore cannot enable a
  physical seller acceptance with an unverified shipping fee.

## Testing

Tests will be written before production changes.

1. Provider contract test: the serialized `pricelist/` request contains
   `showall: 1` for the quote shipment while retaining the configured service,
   parcel measurements, and server-held API key.
2. Mobile ViewModel tests:
   - invalid parcel/origin input produces shipping-local feedback without an
     API call;
   - loading state is visible and prevents a concurrent request;
   - a provider/API exception becomes shipping-local retry feedback;
   - successful quotes clear feedback and select the first option;
   - editing shipping inputs clears stale quotes and feedback.
3. Regression tests: application and mobile-core test suites remain green.
4. Manual Sandbox verification: restart the dual-simulator flow in
   `ShippopSandbox` mode, open the seller offer, and verify that tapping
   `ดูค่าจัดส่ง` visibly loads and either renders a real SHIPPOP quote or an
   inline retryable error.

## Acceptance criteria

- The button never appears inert: loading, quote results, empty results, or an
  error is visible in the shipping section after every valid tap.
- SHIPPOP quote requests match the published Check Price shipment shape by
  including `showall: 1`.
- No local or client-computed price is used as a fallback.
- A failed quote cannot be selected and cannot satisfy seller acceptance.
- No transaction or financial state transition changes as part of this work.
- Existing theme, colors, typography, and seller-page structure are preserved.
- Quote loading and failure are communicated by text and accessibility
  semantics, not color alone.

## Assumptions and provider dependency

The published request shape makes `showall: 1` the smallest justified contract
correction. It does not prove that the current Sandbox key has access to
`EMST`. If the corrected request still fails, TOKLONG will show the failure
clearly and the remaining blocker is SHIPPOP account/service activation or an
account-specific provider contract difference; it must not be masked with a
simulated quote.
