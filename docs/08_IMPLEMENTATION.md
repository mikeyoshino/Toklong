# 08 — MVP Implementation

## Architecture

The implementation targets .NET 10 and uses a Blazor Web App with interactive server rendering.

- `Toklong.Domain`: transaction aggregate, allow-listed transitions, authorization checks, product policy, immutable audit and external events.
- `Toklong.Application`: feature folders containing MediatR commands, queries, and handlers.
- `Toklong.Infrastructure`: EF Core, PostgreSQL, repositories, HMAC verification, and manual payout adapter.
- `Toklong.Web`: Thai mobile-first pages, reusable Razor form components, signed internal webhook endpoints, and the deadline worker.
- `tests`: domain transition tests and application/infrastructure tests.

Dependencies point inward: Web and Infrastructure depend on Application; Application depends on Domain.

## Start locally

PostgreSQL is the only Docker service:

```bash
docker compose up -d postgres
dotnet run --project src/Toklong.Web/Toklong.Web.csproj
```

The app applies committed EF Core migrations on startup. The launch profile prints the local URL.

Before accepting a real transfer, configure these values through environment variables or a local uncommitted settings file:

```text
ManualBank__BankName
ManualBank__AccountName
ManualBank__AccountNumber
Reconciliation__SigningSecret
```

The committed bank values are intentionally placeholders. Do not put a real signing secret or payment credential in source control.

## Manual reconciliation without an admin page

The app never accepts a slip, browser redirect, or ordinary client action as proof of payment, delivery, or payout. In development, an authorized operator can send signed events:

```bash
./scripts/reconcile-event.sh payment TRANSACTION_ID bank-event-001
./scripts/reconcile-event.sh carrier TRANSACTION_ID carrier-event-001 in_transit
./scripts/reconcile-event.sh carrier TRANSACTION_ID carrier-event-002 delivered
./scripts/reconcile-event.sh payout TRANSACTION_ID payout-event-001
```

Set `TOKLONG_SIGNING_SECRET` when it differs from the development-only value. Every endpoint verifies HMAC-SHA256, persists a unique provider/event ID, and safely returns `AlreadyProcessed` for replayed events.

This mechanism is for the first manual operation only. Before production, replace it with an approved bank/payment-provider reconciliation source and a trusted carrier integration.

## Reusable UI components

All main forms use the shared components under `Components/Shared`:

- `TextField`, `TextAreaField`, `SelectField`, `MoneyField`, `CheckboxField`
- `PrimaryButton`, `StatusBadge`, `AlertMessage`
- `TransactionSummary`, `TransactionDetails`, seller and buyer state cards

Money inputs remain strings in the form and are parsed to integer satang at the command boundary. No floating-point money enters the domain.

## Current boundaries

- One fixed physical item, buyer, seller, payment, shipment, and payout in THB.
- The buyer-initiated offer states specified in `docs/01_USER_FLOWS_AND_STATE_MACHINE.md` and benchmarked in `docs/09_BUYER_INITIATED_OFFER_BENCHMARK.md` are product direction only and are not implemented in the current slice.
- Opaque seller and buyer access links are temporary MVP authorization, not final phone/KYC onboarding.
- Product photos are URL-based in this slice. Secure upload, scanning, private storage, and signed asset URLs remain a later slice.
- No admin UI. Dispute resolution, refund, identity onboarding, notifications, and real provider/carrier adapters are not invented while their policy/provider decisions remain open.
