# AGENTS.md — Binding Instructions

These rules apply to every AI coding agent working in this repository.

## Mission

Build the smallest reliable mobile-first transaction-trust product that lets a seller create a protected agreement link for one physical or supported transferable digital item, lets a buyer pay, verifies the applicable fulfillment path, and initiates seller payout only when release conditions are satisfied.

## Read before changing code

Read these files in order:

1. `docs/00_PRODUCT_BRIEF.md`
2. `docs/01_USER_FLOWS_AND_STATE_MACHINE.md`
3. `docs/02_UI_UX_AND_CONTENT_SPEC.md`
4. `docs/04_PAYMENT_SHIPPING_AND_DISPUTE_RULES.md`
5. `docs/03_BACKEND_TRANSACTION_RECORD.md`
6. `docs/05_ACCEPTANCE_TESTS.md`
7. `docs/06_OPEN_DECISIONS.md`
8. `docs/07_REGULATORY_SOURCE_NOTES.md`

## Non-negotiable domain rules

1. MVP supports one physical shippable item/bundle or one allow-listed transferable digital item/right. The seller must already possess or control it and have the right to transfer it.
2. Do not add marketplace discovery, bidding, storefronts, in-app chat, services, milestones, wallets, crypto, stored-value instruments, subscriptions, split fulfillment, or multi-currency.
3. A paid transaction contains an immutable snapshot of product details, any supplied photos, condition, amount, shipping charge, deadline, and terms version. A product photo is optional; when supplied, it becomes part of the immutable snapshot.
4. Any material change after payment requires cancellation/refund handling and a new link. Never mutate the paid snapshot.
5. Monetary values are integer satang plus an ISO currency code. Never use floating-point arithmetic for money.
6. Never mark payment, refund, or payout success from a client request, redirect, slip, screenshot, or database assumption. Require a verified payment-provider webhook or authorized reconciliation job.
7. The seller sees the applicable fulfillment action only after provider-confirmed payment.
8. Physical shipment requires carrier and tracking number. Tracking events must be idempotently ingested and retained.
9. The physical inspection and payout-hold deadline is `carrier_confirmed_delivered_at + 72 hours` for the current MVP default.
10. Do not start the 72-hour clock from payment time, shipment creation, an in-transit status, seller-entered status, or an unverified tracking event.
11. If delivery cannot be verified, automatic payout is blocked. Require buyer confirmation or authorized manual review.
11.1. Digital fulfillment never uses a seller-entered delivery claim or elapsed time as automatic payout evidence. It requires explicit buyer confirmation or authorized manual review.
11.2. Never store account passwords, recovery codes, private keys, wallet secrets, or reusable digital credentials as normal transaction fields or logs.
12. Buyer confirmation may release early only if no dispute is open and payment is eligible for payout.
13. Any open dispute blocks payout immediately.
14. AI may classify, summarize, translate, and organize evidence. AI must not make the binding refund or payout decision.
15. Payout completion requires provider confirmation.
16. Every state transition uses the domain transition service, enforces an allow-list, checks role/authorization, and writes an immutable audit event.
17. Every external webhook is signature-verified, idempotent, replay-safe, and reconcilable.
18. Prohibited or unsupported goods must be blocked before link activation.
19. Product copy must not claim that the platform itself holds money or provides legal escrow unless explicitly approved after provider and legal review.
20. Terms, fees, deadlines, dispute actions, and the exact payout trigger must be visible before buyer payment.

## UX rules

- Mobile first, one primary action per state.
- Seller actions: create link, fulfill (ship/add tracking or mark digital handoff), view payout.
- Buyer actions: review/pay, track or review digital handoff, confirm after inspection or report a problem.
- Do not add a separate contract drafting/signing step.
- Keep advanced transaction records under “รายละเอียดรายการ.”
- Always show an exact date and time for ship-by and dispute deadlines.
- Send a clear reminder before automatic payout.
- Avoid internal words such as webhook, state machine, settlement, hash, or provider-confirmed in normal consumer copy unless translated into plain language.

## Required checks for every pull request

- Type checking passes.
- Unit and integration tests pass.
- State-transition and authorization tests pass.
- Payment webhook signature, idempotency, and replay tests pass when payment code changes.
- Carrier webhook idempotency and delivery-time tests pass when tracking code changes.
- Dispute blocks payout in every relevant test path.
- No auto-release occurs without verified delivery or buyer confirmation.
- No digital auto-release occurs from seller assertion or elapsed time.
- Accessibility checks pass for changed pages.
- No secrets, personal data, raw payment credentials, or provider keys are committed.
- New behavior has an audit event and analytics event where appropriate.

## Agent completion report

At the end of each task, report:

1. What changed.
2. Which requirement or state transitions were implemented.
3. Which tests were added or updated.
4. Assumptions made.
5. Open decisions or blocked provider capabilities.
6. The next smallest vertical slice.
