# Clean Ledger App Redesign

**Date:** 2026-08-03  
**Status:** Approved in brainstorming; awaiting written-spec review

## Objective

Give the authenticated Toklong mobile app a calmer, more polished transaction-
finance experience inspired by the supplied reference without copying that
product's branding or banking metaphors.

The approved direction is **Clean Ledger**:

- `ซื้อ` on the left of the authenticated bottom action bar;
- a raised `+` action labelled `สร้างดีล` in the center;
- `ขาย` on the right;
- Activity and Account actions at the top right;
- quiet mist-gray page backgrounds, white cards, compact spacing, and restrained
  role gradients;
- buyer blue and seller indigo role treatments; and
- one clear, state-appropriate action with an exact deadline when applicable.

This visual system applies across authenticated roots, deal creation, buyer and
seller transaction detail, Activity, and Account. Authentication screens retain
their existing focused information architecture but adopt the same tokens and
surface language where doing so does not disturb the approved welcome and
verification layouts.

## Product boundaries

The redesign does not change Toklong's buyer-first transaction model.

- The center action always starts a buyer-created private offer for a seller the
  buyer already knows from another channel.
- `สร้างดีล` is the compact navigation label. Its accessible label is
  `สร้างข้อเสนอซื้อ`, and its destination heading is `สร้างดีลซื้อ`.
- The seller does not gain a create-listing, sales-link, storefront, discovery,
  counteroffer, bidding, or chat workflow.
- Buy and Sell are workspaces, not permanent account roles. A single account may
  participate in both.
- The contextual guide is one-way presentation copy. It is not chat, does not
  accept user prompts, and does not use AI to decide or infer transaction truth.
- No navigation or visual action may mark payment, delivery, refund, or payout
  successful.
- Existing immutable snapshots, authorization, provider verification,
  fulfillment, dispute, refund, and payout rules remain authoritative.

This specification supersedes the bottom-destination portion of
`2026-08-01-buy-sell-bottom-navigation-design.md`. The prior separation of Buy
and Sell workspaces remains valid, while Account moves from the bottom bar to a
top-right action and the center create action becomes part of the authenticated
root frame.

## Approved visual direction

### Design character

Clean Ledger should feel dependable, modern, and focused rather than playful or
dense. The design uses financial-app clarity without presenting a wallet,
balance, stored value, or money held by Toklong.

Root summaries therefore use labels such as `กำลังดำเนินการ 2 ดีล`, never
`ยอดคงเหลือ`, `เงินของคุณ`, or another balance metaphor. Money appears only as
transaction-specific item price, buyer total, seller expected net, refund, or
payout status supported by the current transaction projection.

### Color tokens

The implementation centralizes semantic colors in application resources and
does not scatter raw role colors through pages.

| Token | Value | Use |
|---|---:|---|
| Mist Background | `#F6F8FA` | Authenticated page background |
| Trust Navy | `#12364F` | Primary actions and neutral trust emphasis |
| Buyer Blue | `#1988D3` | Buy workspace and buyer transaction identity |
| Buyer Blue Soft | `#E9F6FF` | Buyer chips and quiet buyer surfaces |
| Seller Indigo | `#55508A` | Sell workspace and seller transaction identity |
| Seller Indigo Soft | `#EFEDFB` | Seller chips and quiet seller surfaces |
| Verified Mint | `#65C8B4` | Guidance accents or an explicitly verified positive fact |
| Deadline Rust | `#BD563A` | Exact urgent deadlines and expiring actions |
| Ink | `#112337` | Primary text |
| Muted Ink | `#647589` | Secondary text |
| Line | `#DCE5EC` | Borders and separators |
| Surface | `#FFFFFF` | Cards, inputs, and controls |

Verified Mint is never sufficient by itself to communicate payment, delivery,
refund, or payout truth. Visible status text and the authorized server-backed
projection remain required. Danger, dispute, refund, and review states retain
their existing semantic colors rather than being recolored as positive.

### Surfaces and typography

- Page backgrounds use Mist Background with no ambient multicolor wash.
- White cards use a one-pixel Line border, 16–20 point corner radius, and either
  no shadow or one low-opacity shadow on a primary summary surface.
- Gradients are reserved for the workspace summary and transaction role header.
  Buyer gradients run from Trust Navy toward Buyer Blue; seller gradients run
  from deep indigo toward Seller Indigo.
- Primary actions use Trust Navy unless a role-specific action requires the
  established buyer or seller treatment.
- Noto Sans Thai remains the application font.
- Page titles use the existing large title role, while transaction values and
  exact deadlines use compact, high-contrast roles.
- Cards do not imitate bank balances, pockets, wallets, or investment accounts.

### Motion

- Selecting Buy or Sell uses a 180 ms opacity transition with at most 6 points
  of vertical movement in newly presented root content.
- Pressing the raised create action uses a 120 ms scale response from `1.0` to
  `0.96` and back.
- No root dashboard autoplays or rotates content.
- With Reduced Motion enabled, both effects are removed and navigation occurs
  without an artificial delay.
- The existing separately approved cold-start Transaction Rail motion remains
  unchanged.

## Authenticated root frame

### Layout

Buy and Sell use one shared authenticated root frame:

```text
safe-area header
  → TOKLONG identity on the left
  → Activity and Account actions on the right

role-specific root content
  → workspace label and title
  → role summary
  → action/status filters
  → one action-required spotlight or fixed-height empty state
  → role-specific transaction list

fixed bottom action bar
  → ซื้อ | + สร้างดีล | ขาย
```

The bottom action bar appears only on the Buy and Sell authenticated roots. It
is hidden on create flow pages, transaction detail, Activity, Account,
authentication, Counter QR, label handoff, and other pushed pages.

The raised center control has a minimum 64-by-64 point visual button, a white
separation ring, Trust Navy fill, Mint plus sign, and a visible `สร้างดีล`
label. The whole labelled control is one accessibility element with the name
`สร้างข้อเสนอซื้อ` and a hint that it starts a private buyer offer.

### Navigation behavior

- Ordinary authenticated startup, sign-in, and registration completion open
  Buy.
- Buy and Sell switch between independent root pages. They never combine roles
  in one `ทั้งหมด` list.
- Authorized transaction, notification, and invitation deep links still open
  their exact destinations and take precedence over the default root.
- The center action always opens the existing product-type selection followed
  by the three-step buyer offer wizard.
- Activity and Account open as pushed pages with visible Back navigation.
- Returning from a pushed page restores the originating root, filters, loaded
  collection, and scroll position when still valid for the authenticated
  session.
- Logout or account switching clears both root presentations and any pending
  navigation payload before another account can render.
- The existing stored preferred-role behavior is retired because ordinary
  authenticated entry is required to default to Buy.

### Header actions

Activity and Account each have a minimum 44-by-44 point target. Activity uses
the existing bell artwork; Account uses the user's initials or the established
account glyph. Neither shows an unread, verification, or warning badge without
authoritative backing data.

### Buy workspace

The Buy root contains buyer records only:

1. `พื้นที่ของผู้ซื้อ` and `รายการซื้อ` identity;
2. an active-deal count derived from the loaded buyer projection;
3. compact existing buyer status filters;
4. `ต้องทำตอนนี้` with only an action-required buyer transaction;
5. the existing fixed-height `ยังไม่มีรายการ` spotlight state when no buyer
   action is required; and
6. the buyer transaction list ordered newest first inside its selected filter.

Counts and spotlight copy come from existing presentation classifiers. XAML
must not classify raw transaction states independently.

### Sell workspace

The Sell root contains seller records only:

1. `พื้นที่ของผู้ขาย` and `รายการขาย` identity;
2. an active-sale count derived from the loaded seller projection;
3. actionable summaries for `ต้องตอบ`, `ต้องส่ง`, and `รอรับเงิน`;
4. the existing seller filters, including `เสร็จแล้ว`;
5. `ต้องทำตอนนี้` with only an action-required seller transaction; and
6. the seller transaction list ordered newest first inside its selected filter.

The Sell workspace has no seller creation semantics. The globally visible
center action is explicitly the buyer-offer entry point.

## App-wide component system

The redesign introduces small, bounded presentation components instead of
duplicating styles and state logic across pages.

### `AuthenticatedRootFrame`

Owns the safe-area root layout, top actions, role content slot, and raised
bottom action bar. It receives the selected role and navigation commands. It
does not load transactions or interpret transaction state.

### `WorkspaceSummaryCard`

Renders a role title, authoritative count, and compact summary values supplied
by the owning ViewModel. It never calculates money or transaction state.

### `ActionSpotlightCard`

Renders the existing state-presenter output for the one action-required
transaction. It accepts visible role, title, status, exact deadline, amount,
and command. It does not fall back to in-progress or completed records.

### `DealGuidanceCard`

Renders short contextual guidance under the current status or summary. Its
input is a presentation model generated by the existing centralized
`TransactionStatePresenter` family. It has no input field, conversation
history, network call, or state-changing command.

The presenter may explain:

- what the current authoritative status means;
- the next safe action;
- why an action is unavailable; and
- which exact deadline applies.

It must not:

- infer a payment, carrier, refund, or payout result;
- recommend a binding dispute outcome;
- expose internal state names, provider terminology, hashes, or webhook copy;
- claim Toklong holds money or guarantees an outcome; or
- replace the primary action or mandatory pre-payment disclosure.

### `RoleTransactionHeader`

Provides the compact buyer-blue or seller-indigo header on transaction detail.
It always includes a visible `รายการซื้อ` or `รายการขาย` label so role is not
communicated through color alone.

### `LedgerSectionCard`

Provides the shared white surface for amount breakdowns, shipping data,
addresses, account information, and transaction details. Money rows accept
already formatted integer-satang values and never perform arithmetic.

## Screen migration

### Deal creation

The existing type selection and three-step buyer wizard retain their current
fields, validation, privacy boundaries, cost preview, dirty-exit warning, and
final-only create behavior.

The redesign changes presentation only:

- compact transparent Back header;
- Clean Ledger progress segments;
- white individual fields on Mist Background rather than one outer form card;
- one Trust Navy primary action per step; and
- optional AI draft assistance presented as a Mint-accent helper, still
  collapsed by default and subject to all existing draft-safety rules.

The AI draft helper remains different from `DealGuidanceCard`: the former is an
explicit, optional extraction tool in offer creation; the latter is static
state guidance and makes no AI request.

### Transaction detail

- Buyer detail uses a Buyer Blue role header and seller detail uses Seller
  Indigo.
- The existing three-step transaction progress remains centralized and keeps
  its current fulfillment-specific semantics.
- The primary status card, exact deadline, and current action stay above
  supporting details.
- Amounts use `LedgerSectionCard` rows and remain role-shaped. Seller surfaces
  continue to exclude buyer-only protection values and buyer total.
- Physical delivery, Counter QR, label download, tracking, digital handoff,
  dispute, refund, and payout copy retain their current authoritative gates.
- The contextual guide may clarify a gate but never creates a new gate or
  transition.

### Activity and Account

- Both become pushed pages with Back navigation and no bottom action bar.
- Activity retains the existing feed ordering, authorization, loading, retry,
  and deep-link behavior.
- Account retains current name, email, payout, address, support, privacy, and
  logout behavior.
- Account cards adopt the shared Clean Ledger surface and spacing tokens.
- No badge or status is inferred from missing provider capability.

### Authentication

Welcome, sign-in, registration, and six-digit verification preserve their
approved layouts, wording, single-input verification behavior, and startup
routing. They adopt only compatible background, border, type, and button
tokens. The authenticated bottom action bar never appears during
authentication or pending registration.

## Data and event flow

```text
ordinary authenticated start
  → Buy root
  → buyer-only transaction request
  → centralized presentation models
  → summary, filters, spotlight, list, and optional guidance

tap Sell
  → independent Sell root and ViewModel
  → seller-only transaction request
  → centralized presentation models

tap + สร้างดีล
  → product-type selection
  → existing three-step in-memory wizard
  → server cost preview
  → final ส่งข้อเสนอให้ผู้ขาย
  → existing buyer-offer command and audit behavior

tap Activity or Account
  → pushed route
  → existing authorized service and ViewModel
  → Back to originating root
```

No root navigation event writes a transaction audit event. Appropriate
non-sensitive analytics are:

- `workspace_opened` with `role = buying | selling` and
  `source = startup | bottom_action | deep_link`; and
- `create_offer_started` with `source_role = buying | selling`.

These events contain no transaction ID, phone, counterparty, product, address,
amount, provider reference, or credential-shaped value. They do not imply an
offer was submitted.

## Loading, errors, and session safety

- Initial root loading uses quiet skeleton geometry matching the summary,
  spotlight, and first list cards.
- A root failure stays in the selected role and offers inline `ลองอีกครั้ง`.
  It never falls through to the opposite workspace.
- A refresh failure preserves already loaded authorized content where the
  current behavior permits and shows a non-blocking retry message.
- Switching roots or accounts cancels or generations-bounds in-flight work so a
  late response cannot populate another role or session.
- The raised center action is single-flight. Repeated taps cannot open stacked
  type-selection pages.
- Create preview and submit failures preserve the existing in-memory wizard
  values and never claim the offer was created.
- Missing or invalid exact deadlines fail to neutral consumer-safe copy and do
  not enable an action. Clients never synthesize or extend deadlines.
- Guidance is omitted when the authoritative presenter cannot produce safe
  copy. Missing guidance never blocks the underlying transaction action.
- Activity and Account load failures keep Back navigation available.

## Accessibility

- Every interactive target is at least 44 by 44 points; the raised create
  button is at least 64 by 64 points.
- The bottom action bar exposes three ordered accessibility elements:
  `ซื้อ`, `สร้างข้อเสนอซื้อ`, and `ขาย`.
- Buy and Sell expose selected state semantically and visibly through label,
  icon treatment, and color.
- Activity and Account have distinct accessible names and hints.
- Decorative gradients, shadows, and icons are excluded from the accessibility
  tree.
- Thai Dynamic Type may wrap titles, guidance, filters, and buttons without
  clipping, overlapping the bottom bar, or hiding the primary action.
- Root content reserves bottom inset equal to the raised action bar plus device
  safe area.
- Status, urgency, and role never rely on color alone.
- Reduced Motion behavior follows the exact rules above.

## Testing

### Navigation and ViewModel tests

- Ordinary authenticated entry opens Buy.
- Authorized deep links still open the exact destination.
- Buy and Sell use separate page and ViewModel instances.
- Switching roots cannot leak filters, records, spotlight, error, loading, or
  scroll state into the other role.
- The center action opens product-type selection exactly once from either root.
- The center action never invokes seller listing or seller-link behavior.
- Activity and Account open as pushed pages and Back restores the origin.
- Logout/account switch clears root and route payload presentation.

### Presentation tests

- Summary counts come only from the matching authorized role projection.
- Spotlight selects only an action-required role-matching transaction.
- Empty spotlight height remains stable.
- Guidance maps through the centralized presenter and contains no raw internal
  state, provider, hash, or escrow language.
- Guidance cannot enable an unavailable action or create a transition.
- Buyer and seller amount cards remain role-shaped.
- Every applicable deadline is exact and server-derived.

### Accessibility and visual tests

- The bottom action bar has the correct element order, selected state, names,
  hints, and minimum targets.
- Activity and Account remain reachable at supported text sizes.
- Representative small iPhone and Android widths show no overlap between root
  content and the raised center button.
- Dynamic Type and long Thai copy do not clip primary actions.
- Reduced Motion removes root and press animation delays.
- Changed pages pass the repository's available accessibility checks.

### Regression checks

- Type checking and all unit/integration tests pass.
- Existing state-transition and authorization tests pass unchanged.
- Payment signature, idempotency, and replay tests continue to pass.
- Carrier idempotency and delivery-time tests continue to pass.
- Dispute blocks payout in every relevant path.
- No physical auto-release occurs without trusted delivery or buyer
  confirmation.
- No digital auto-release occurs from seller assertion or elapsed time.
- No client navigation or guidance action can mark payment, refund, delivery,
  or payout complete.

## Documentation changes during implementation

Implementation updates the binding product documentation in the same change:

- `docs/02_UI_UX_AND_CONTENT_SPEC.md` for the new bottom action bar, Account
  route, visual tokens, and contextual guide rules;
- `docs/05_ACCEPTANCE_TESTS.md` for navigation, accessibility, role isolation,
  and guidance acceptance scenarios; and
- any superseded mobile navigation wording in `docs/10_MOBILE_APP_SPEC.md`.

No backend record, payment, shipping, dispute, refund, payout, or regulatory
document changes are expected unless implementation discovers an actual
behavioral dependency.

## Delivery sequence

The app-wide redesign is delivered in small, verifiable slices:

1. semantic tokens, shared root-frame components, and root navigation;
2. Buy and Sell root migration with role isolation and analytics;
3. deal creation and authentication token migration;
4. buyer and seller transaction-detail component migration;
5. Activity and Account migration; and
6. accessibility, Reduced Motion, visual regression, documentation, and full
   regression verification.

Each slice must leave the app buildable and preserve the existing domain flow.

## Assumptions and open decisions

- The approved user-facing compact center label is `สร้างดีล`; its destination
  and accessibility semantics continue to make buyer initiation explicit.
- The contextual guide is deterministic local presentation, not a new AI
  feature or in-app companion conversation.
- No new backend capability is required.
- Existing open provider, legal, carrier, KYC, payout, and category decisions
  remain blocked exactly as documented. The redesign must not imply those
  capabilities are approved or live.

## Success criteria

The redesign succeeds when a first-time authenticated user can immediately
identify Buy, Create Deal, and Sell; understand the next safe action and exact
deadline; distinguish buyer and seller contexts without relying on color; and
complete the existing buyer-first flow without any change to domain truth,
authorization, or financial release conditions.
