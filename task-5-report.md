# Task 5 Report — Deferred Shipping Booking and Seller Privacy

## Implementation commit

`bb95f60 feat: defer shipment booking until buyer checkout`

## What changed

- Seller acceptance now validates and freezes the selected delivery service and
  disclosed shipping charge, then transitions directly to
  `SellerAcceptedAwaitingPayment`.
- It creates no managed shipment, shipping operation, provider reservation, or
  client-driven payment state.
- Seller request, quote, invitation, mobile-service, and view-model contracts
  no longer include Buyer Protection or parcel-insurance election values.
- Seller transaction serialization omits Buyer Protection, parcel-insurance,
  declared-value, fee-policy, terms-version, agreement-hash, and internal
  shipping-operation fields. Buyer serialization retains the intended
  buyer-facing values.
- Direct legacy managed-shipment setup remains compatible for existing
  historical/worker tests; it is not reachable from the seller acceptance
  handler.

## Tests and verification

- RED: `BuyerOfferFlowTests` failed because acceptance required seller-supplied
  insurance data; `MobileSellerOfferApiTests` failed because seller JSON
  contained `shippingDeclaredValueSatang`.
- `dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj --filter "FullyQualifiedName~BuyerOfferFlowTests" --no-restore`
  — 6 passed.
- `dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj --filter "FullyQualifiedName~MobileSellerOfferApiTests" --no-restore`
  — 7 passed.
- `dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter "FullyQualifiedName~UiLayoutConsistencyTests" --no-restore`
  — 41 passed.
- Domain suite — 151 passed.
- Application suite — 286 passed, 1 PostgreSQL migration test skipped.
- API suite — 65 passed.
- Mobile Core suite — 369 passed.
- CRM suite — 47 passed.
- SHIPPOP certification suite — 1 expected certification skip.
- `dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj -f net10.0-ios --no-restore --disable-build-servers`
  — succeeded with 6 existing duplicate Stripe bundle-resource warnings.

The solution-wide test command cannot build Android and MacCatalyst in this
environment because those .NET workloads are not installed.

## Assumptions and open work

- The signed shipping quote reference remains seller-visible because the Task 5
  contract explicitly requires it for the seller's selected service; provider
  insurance/cost/code fields do not cross the seller boundary.
- Buyer Protection fee policy remains server-calculated at acceptance as part
  of the immutable pre-payment agreement; this does not change payment state.
- Buyer parcel-protection election/checkout-annex work is outside Task 5.
- The next smallest vertical slice is the buyer-only parcel-protection election
  and checkout-annex flow, if approved.
