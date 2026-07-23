# TOKLONG Goods MVP Project Pack

> Working name: **TOKLONG**. Replace it after brand and trademark checks are complete.

TOKLONG is a **protected payment-link layer for off-platform social commerce**. A seller and buyer may meet in a Facebook group, Facebook Marketplace, LINE, Messenger, Instagram, or another chat. Either party can start a private transaction link. For a buyer-created offer, the seller confirms the final details before the buyer pays. The seller ships only after confirmed payment, and payout is released after receipt is confirmed or the seven-day dispute window closes without an open dispute.

The product is intentionally **not a marketplace** in the MVP. It does not provide product discovery, bidding, social chat, storefronts, or service-work contracts.

## User-facing flow

1. Seller creates an agreement link, or buyer creates a proposed offer link for the seller.
2. Seller confirms the material product details, photos, terms, eligibility, and payout conditions before checkout.
3. Buyer reviews the final seller-confirmed transaction details and pays through a payment partner.
4. Seller ships only after provider-confirmed payment and enters a verifiable tracking number.
5. When the carrier confirms delivery, the seven-day dispute window starts.
6. Payout is initiated when the buyer confirms receipt early, or the window expires with no open dispute.

The landing page presents this as four simple stages:

> Create link → Pay → Ship + tracking → Receive / payout

## What is included

- `AGENTS.md` — binding build instructions for an AI coding agent.
- `docs/00_PRODUCT_BRIEF.md` — positioning, users, goals, and strict MVP scope.
- `docs/01_USER_FLOWS_AND_STATE_MACHINE.md` — primary journeys, exception journeys, and state transitions.
- `docs/02_UI_UX_AND_CONTENT_SPEC.md` — mobile-first screen requirements, landing animation, copy, and notifications.
- `docs/03_BACKEND_TRANSACTION_RECORD.md` — immutable sale snapshot, audit events, permissions, and reference data model.
- `docs/04_PAYMENT_SHIPPING_AND_DISPUTE_RULES.md` — payment, shipment, seven-day release, dispute, refund, and payout rules.
- `docs/05_ACCEPTANCE_TESTS.md` — executable-style product and domain scenarios.
- `docs/06_OPEN_DECISIONS.md` — unresolved product, operations, payment, and legal decisions.
- `docs/07_REGULATORY_SOURCE_NOTES.md` — dated notes from official Thai sources; not legal advice.
- `docs/08_IMPLEMENTATION.md` — current implemented architecture and explicit boundaries.
- `docs/09_BUYER_INITIATED_OFFER_BENCHMARK.md` — international benchmark and recommended dual-entry flow.
- `config/product-rules.example.json` — machine-readable default business rules.
- `landing.html` — responsive single-file landing page with a four-scene animated mobile UI.

## Strict MVP boundaries

Included:

- Physical goods already in the seller's possession.
- One item or one fixed bundle per transaction.
- One buyer, one seller, one payment, one shipment, one payout.
- Domestic shipping with a carrier and tracking status that can be verified.
- Seller identity and payout onboarding appropriate to the selected payment partner.
- Buyer verification, checkout, address, receipt confirmation, and dispute initiation.
- A seven-day dispute window beginning from carrier-confirmed delivery.

Not included:

- Marketplace search or recommendations.
- In-app buyer/seller chat.
- Services, freelance work, milestones, subscriptions, rentals, digital goods, or preorders.
- Split shipments, partial delivery, partial payout, multiple sellers, or multiple currencies.
- Platform wallet, stored-value balance, crypto, or direct custody of customer funds.
- AI deciding refund or payout outcomes.

## Contract and transaction record approach

There is no user-facing “draft contract and sign” step. At checkout, the system stores an immutable transaction snapshot containing the product details, condition, photos, price, shipping charge, ship-by deadline, applicable terms version, identities, and acceptance timestamps. Users can view or download this record from “Transaction details.”

Any material change after payment requires cancellation and a new payment link. The paid transaction snapshot must never be silently edited.

## Payment and payout model

The application creates checkout and payout instructions, while the selected payment partner performs the actual payment, safeguarding/settlement, refund, and seller transfer functions supported by its approved product model.

Never display `PAID`, `REFUNDED`, or `PAID_OUT` based only on a browser callback, screenshot, slip, or client request. These states require a verified provider webhook or an authorized reconciliation process.

## Start the working MVP

The repository now includes a .NET 10 Blazor Web App using MediatR, Clean Architecture, feature folders, EF Core, and PostgreSQL.

```bash
docker compose up -d postgres
dotnet run --project src/Toklong.Web/Toklong.Web.csproj
```

Only PostgreSQL runs in Docker. The web app runs directly with `dotnet run` and applies committed migrations at startup. Bank-account values are placeholders until configured locally; do not commit real secrets or payment credentials.

See `docs/08_IMPLEMENTATION.md` for architecture, configuration, and signed manual reconciliation commands. `landing.html` remains the original visual reference.

## Start implementation with an AI agent

Give the agent this instruction:

> Read `AGENTS.md`, then every file in `docs/` in numeric order. Build only the smallest complete vertical slice. Treat the state machine, immutable paid snapshot, provider-confirmed money states, carrier-confirmed delivery time, seven-day dispute deadline, and dispute payout block as hard constraints. Do not add marketplace or service-work features.

## Important disclaimer

This package is a product and engineering specification, not legal, payment-licensing, tax, accounting, consumer-protection, or regulatory advice. Obtain review from Thai counsel and the selected licensed payment and logistics partners before production use.
