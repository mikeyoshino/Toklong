# TOKLONG Goods MVP Project Pack

> Working name: **TOKLONG**. Replace it after brand and trademark checks are complete.

TOKLONG is a **buyer-first protected payment-link layer for off-platform social commerce**. A seller and buyer may meet in a Facebook group, Facebook Marketplace, LINE, Messenger, Instagram, or another chat. The buyer signs in with a phone number, supplies first and last name, and creates one private offer; the seller authenticates and confirms the final details, and only then may the buyer pay. The seller ships only after confirmed payment, and payout is released after the buyer confirms the item is satisfactory or the 72-hour inspection and payout-hold window closes without an open dispute.

The product is intentionally **not a marketplace** in the MVP. It does not provide product discovery, bidding, social chat, storefronts, or service-work contracts.

## User-facing flow

1. Buyer signs in with a phone number, creates one complete private offer, and sends the seller invitation link.
2. Seller reviews the read-only product details, photos, terms, eligibility,
   and payout conditions; for physical goods the seller also supplies a
   saved-or-new origin and package measurements and selects a shipping quote.
3. Seller confirms or declines the buyer-specified offer; after confirmation,
   the buyer reviews item price, shipping charge, buyer total, and the unchanged
   transaction details before paying through a payment partner.
4. After provider-confirmed payment, the shipping worker confirms the reserved
   SHIPPOP shipment, exposes the carrier tracking number and 4×6 label, and the
   seller hands the parcel to that carrier before the exact ship-by deadline.
5. When the trusted carrier confirms successful delivery, the 72-hour inspection and payout-hold window starts.
6. Payout is initiated when the buyer confirms after inspection, or the window expires with no open dispute.

The landing page presents this as four simple stages:

> Propose + seller confirms → Pay → Fulfill → Receive / payout

## What is included

- `AGENTS.md` — binding build instructions for an AI coding agent.
- `docs/00_PRODUCT_BRIEF.md` — positioning, users, goals, and strict MVP scope.
- `docs/01_USER_FLOWS_AND_STATE_MACHINE.md` — primary journeys, exception journeys, and state transitions.
- `docs/02_UI_UX_AND_CONTENT_SPEC.md` — mobile-first screen requirements, landing animation, copy, and notifications.
- `docs/03_BACKEND_TRANSACTION_RECORD.md` — immutable sale snapshot, audit events, permissions, and reference data model.
- `docs/04_PAYMENT_SHIPPING_AND_DISPUTE_RULES.md` — payment, shipment, 72-hour physical release, dispute, refund, and payout rules.
- `docs/05_ACCEPTANCE_TESTS.md` — executable-style product and domain scenarios.
- `docs/06_OPEN_DECISIONS.md` — unresolved product, operations, payment, and legal decisions.
- `docs/07_REGULATORY_SOURCE_NOTES.md` — dated notes from official Thai sources; not legal advice.
- `docs/08_IMPLEMENTATION.md` — current implemented architecture and explicit boundaries.
- `docs/09_BUYER_INITIATED_OFFER_BENCHMARK.md` — international benchmark and buyer-first flow rationale.
- `docs/10_MOBILE_APP_SPEC.md` — native Android/iOS UX, multiple-transaction workspace, and Stripe boundary.
- `docs/11_SELLER_VALUE_PROPOSITION_TH.md` — Thai seller value proposition, evidence strategy, copy rules, and roadmap boundaries.
- `config/product-rules.example.json` — machine-readable default business rules.
- `landing.html` — responsive single-file landing page with a four-scene animated mobile UI.

## Strict MVP boundaries

Included:

- Physical goods or allow-listed transferable digital items/rights already in the seller's possession or control.
- One item or one fixed bundle per transaction.
- One buyer, one seller, one payment, one shipment, one payout.
- Domestic shipping with a carrier and tracking status that can be verified.
- Seller identity and payout onboarding appropriate to the selected payment partner.
- Buyer verification, checkout, address, receipt confirmation, and dispute initiation.
- A 72-hour inspection and payout-hold window beginning from trusted carrier-confirmed delivery.

Not included:

- Marketplace search or recommendations.
- In-app buyer/seller chat.
- Services, freelance work, milestones, subscriptions, rentals, unrestricted/non-transferable digital access, or preorders.
- Split shipments, partial delivery, partial payout, multiple sellers, or multiple currencies.
- Platform wallet, stored-value balance, crypto, or direct custody of customer funds.
- AI deciding refund or payout outcomes.

## Contract and transaction record approach

There is no separate user-facing “draft contract and sign” step. Seller
acceptance creates an immutable agreement core and an append-only electronic
acceptance tied to the authenticated seller. For physical goods, the buyer
selects the complete delivery address while creating the offer. The private
address is locked to the transaction, while the seller sees and accepts only
its destination province and postal code in the core. Checkout is review-only:
the buyer accepts that same core hash and the pre-locked private fulfillment
annex; it cannot edit the address. Provider-confirmed payment then seals the
snapshot and unlocks the full address for seller fulfillment. “Transaction
details” shows the shared hash and both acceptance times without presenting
them as a
certificate-backed digital signature. Either transaction party may download a
JSON evidence file containing hashes and server acceptance times, or an
HTML version suitable for printing or saving as PDF.

Any material change after payment requires cancellation and a new payment link. The paid transaction snapshot must never be silently edited.

Terminal transaction evidence is retained for five years and is then purged by
the internal Worker unless an audited legal hold is active. The purge removes
the complete personal/evidence aggregate and leaves only a minimized,
non-party financial record until year seven. TOKLONG does not collect
IP/device evidence for agreement acceptance.

## Payment and payout model

The current working model collects buyer payment into TOKLONG's Stripe account, reconciles Stripe settlement into a dedicated bank account, and creates an external bank payout instruction only after release eligibility. The complete third-party-seller, delayed-payout, refund, and bank-transfer structure still requires written provider, bank, legal, tax, and accounting approval.

Never display `PAID`, `REFUNDED`, or `PAID_OUT` based only on a browser callback, screenshot, slip, or client request. These states require a verified provider webhook or an authorized reconciliation process.

## Start the working MVP

The repository includes a .NET 10 Blazor Web App, a separate authenticated
mobile API, and `Toklong.Mobile`, a native .NET MAUI XAML client for Android
and iOS with a Mac Catalyst target for desktop layout development.

```bash
docker compose up -d postgres
dotnet run --project src/Toklong.Web/Toklong.Web.csproj
dotnet run --project src/Toklong.Api/Toklong.Api.csproj
```

Only PostgreSQL runs in Docker. The API owns the mobile endpoints, Stripe
PaymentIntent creation, Stripe webhook, and database migration on startup.
Do not commit real secrets, payment credentials, or Data Protection keys.

For local mobile testing, run the API on the port expected by the Debug app:

```bash
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS=http://127.0.0.1:5181 \
dotnet run --project src/Toklong.Api/Toklong.Api.csproj
```

The Debug iOS/Mac Catalyst build uses `http://localhost:5181`; the Debug Android
emulator build uses `http://10.0.2.2:5181`. Release builds use
`https://api.toklong.co.th/`. Local HTTP is restricted to loopback/emulator
hosts and is never enabled as a general cleartext transport policy.

The native app contains no Stripe secret. It requests a server-created
PaymentIntent and presents Stripe PaymentSheet inside iOS or Android. A completed
sheet only changes the copy to “กำลังรอ Stripe ยืนยัน”; only the
signature-verified webhook may confirm payment and expose fulfillment.

Production configuration belongs in a secret manager or environment:

```text
ConnectionStrings__ToklongDatabase
Stripe__Enabled
Stripe__LiveMode
Stripe__EnableDigitalGoods
Stripe__PublishableKey
Stripe__SecretKey
Stripe__WebhookSecret
BuyerProtectionFee__Enabled
BuyerProtectionFee__MinimumFeeSatang
BuyerProtectionFee__MinimumItemPriceSatang
BuyerProtectionFee__MaximumItemPriceSatang
BuyerProtectionFee__PolicyVersion
BuyerProtectionFee__Tiers__0__UpToItemPriceSatang
BuyerProtectionFee__Tiers__0__RateBasisPoints
BuyerProtectionFee__Tiers__1__UpToItemPriceSatang
BuyerProtectionFee__Tiers__1__RateBasisPoints
BuyerProtectionFee__Tiers__2__UpToItemPriceSatang
BuyerProtectionFee__Tiers__2__RateBasisPoints
Otp__Provider
Otp__BaseUrl
Otp__ApiKey
Otp__ApiSecret
ShippingQuotes__Provider
Shippop__BaseUrl
Shippop__ApiKey
Shippop__AccountEmail
Shippop__QuoteSigningSecret
Shippop__QuoteLifetimeMinutes
Shippop__ServiceCodes__0
OpenAI__ApiKey
OpenAI__Model
DataProtection__KeysPath
PublicUrls__WebBaseUrl
```

`OpenAI__ApiKey` stays server-side. The optional mobile agreement-draft helper
uses `gpt-5.6-luna` by default for text/image extraction with structured output;
the app never receives the key. For local Development, store it with the API
project's .NET user-secrets rather than an appsettings file.

Payment remains disabled by default. The API refuses to create a PaymentIntent
until an approved, complete marginal-tier schedule and fee-policy version are
configured.

To verify the complete Stripe sandbox path without changing the API used by the
mobile simulator, first sign in to the Stripe CLI in Test Mode, then run:

```bash
./scripts/test-stripe-payment.sh
```

The script uses port `5182`, creates isolated buyer/seller test accounts and a
฿100 physical-item transaction, confirms it with Stripe's test card, forwards a
signature-verified webhook, and requires a paid physical fulfillment state.
The Development shipping provider then allocates deterministic tracking so the
same managed-shipping UI can be tested without calling SHIPPOP. It accepts only
`sk_test_*`/`pk_test_*` keys, keeps secrets outside the repository, and never
initiates seller payout. Seller payout remains a manual bank operation for this
phase.

To test native PaymentSheet from the mobile app, stop the ordinary local API
and run this foreground command instead:

```bash
./scripts/run-stripe-test-api.sh
```

It serves the same mobile API on port `5181`, enables only Stripe Test Mode,
forwards signed Stripe events, and cleans up its webhook listener on `Ctrl+C`.

See `docs/08_IMPLEMENTATION.md` for architecture, production configuration,
mobile seller-link claiming, provider boundaries, health checks, durable
notifications, and signed reconciliation commands. `landing.html` remains the
original visual reference.

## Deploy on one Linux host

The repository also includes a production-shaped single-host Compose topology:

- Caddy with automatic HTTPS.
- Separate Web, API, and background-worker containers.
- PostgreSQL on an internal-only network.
- A one-shot migration container.
- Persistent Data Protection keys, product images, PostgreSQL data, and TLS
  state.

Start with [deploy/README.md](deploy/README.md). The topology is suitable for
staging or a controlled closed beta after real provider configuration. It is
not high availability, and provider/legal/operations approval is still
required before accepting real money.

## Start implementation with an AI agent

Give the agent this instruction:

> Read `AGENTS.md`, then every file in `docs/` in numeric order. Build only the smallest complete vertical slice. Treat the state machine, immutable paid snapshot, provider-confirmed money states, carrier-confirmed delivery time, 72-hour physical inspection deadline, and dispute payout block as hard constraints. Do not add marketplace or service-work features.

## Important disclaimer

This package is a product and engineering specification, not legal, payment-licensing, tax, accounting, consumer-protection, or regulatory advice. Obtain review from Thai counsel and the selected licensed payment and logistics partners before production use.
