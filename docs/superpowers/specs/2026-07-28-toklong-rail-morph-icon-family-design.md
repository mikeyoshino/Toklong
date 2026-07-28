# TOKLONG Rail Morph Icon Family Design

Date: 2026-07-28  
Status: Approved visual direction; awaiting written-spec review

## Goal

Replace the transaction progress pictograms with a distinctive TOKLONG-owned
icon family. Every icon must feel derived from the Transaction Rail logo,
remain understandable beside its Thai milestone label, and adapt to the buyer
or seller color system already used by the transaction-detail page.

This slice changes presentation only. It does not change transaction states,
completion mappings, payment or webhook truth, fulfillment eligibility,
dispute behavior, payout eligibility, deadlines, or audit events.

## Selected direction

Use **Rail + Negative Space** with **Role Adaptive** color:

- every glyph is constructed from two rounded rail strokes and one internal
  node or negative-space junction;
- the rails bend differently for agreement, payment, physical handoff,
  physical receipt, digital handoff, and payout;
- glyphs are semi-abstract rather than stock depictions of a document, card,
  banknote, truck, parcel, wallet, or bank;
- completed buyer milestones use Buyer Blue;
- completed seller milestones use Seller Purple;
- incomplete milestones use the shared neutral gray system;
- Mint is an internal brand accent and never acts alone as evidence of payment,
  delivery, refund, or payout.

The approved Visual Companion references are `Rail + Negative Space` and
`Role Adaptive`.

## Visual grammar

### Canvas and geometry

- Source assets use a `0 0 48 48` view box.
- The useful drawing area stays within `6..42` on both axes.
- Primary rail strokes are `3` units.
- Secondary rail strokes are between `2.5` and `3` units.
- All open paths use round line caps and round line joins.
- Glyphs share an optical center at `(24, 24)`.
- The internal node is between `3.5` and `4.5` units in radius.
- No glyph contains text, numerals, currency symbols, brand-network marks,
  detailed perspective, or strokes wider than `3`.

### Semantic shapes

The family has six semantic glyphs:

1. **Agreement** — opposed rails interlock around the central node.
2. **Payment** — two rails pass through a confirmation junction from left to
   right; it must not resemble a plus sign or a bank card.
3. **Physical handoff** — two rails open outward around a protected center,
   suggesting an item leaving the seller without drawing a truck or box.
4. **Physical receipt** — the rails close around the center, suggesting the
   buyer has received and can inspect the item.
5. **Digital handoff** — mirrored rails exchange positions around the node;
   it must not show a password, key, recovery code, wallet secret, or reusable
   credential.
6. **Payout** — the rails converge on a destination node; it must not depict a
   banknote, wallet, stored value, or claim that TOKLONG itself holds funds.

Labels remain the primary literal explanation. The glyphs reinforce progression
and brand recognition without replacing the approved Thai copy.

## Role-adaptive color system

Each semantic glyph has exactly three local SVG variants:

### Buyer completed

- primary rail: `#145FC7`;
- secondary rail: `#2B7FFF`;
- accent node: `#65D6BF`;
- optional node outline: white.

### Seller completed

- primary rail: `#6548C7`;
- secondary rail: `#8067DE`;
- accent node: `#65D6BF`;
- optional node outline: white.

### Disabled

- both rails: `#98A2B3`;
- node: `#D6DCE5`;
- no Buyer Blue, Seller Purple, Success Green, or Mint.

The token surface and connector follow the same role:

- buyer completed token fill `#EAF4FF`, outline/label/connector `#145FC7`;
- seller completed token fill `#F1ECFF`, outline/label/connector `#6548C7`;
- incomplete token fill white, outline/connector `#E4EAF1`, label `#98A2B3`.

A connector becomes role-colored only when the destination milestone is
completed. An active but incomplete milestone remains neutral. The main status
card continues to explain the current action.

The role-specific label and page treatment remain present, so the design does
not rely on color alone.

## Asset contract

The six semantics multiplied by three variants produce eighteen repository-owned
SVG assets:

```text
progress_<semantic>_buyer_completed.svg
progress_<semantic>_seller_completed.svg
progress_<semantic>_disabled.svg
```

where `<semantic>` is one of:

```text
agreement
payment
physical_handoff
physical_receipt
digital_handoff
payout
```

MAUI continues referencing the compiled runtime resource with the `.png`
extension. The old generic `_completed` assets are removed after every
presentation mapping and XAML reference has migrated.

## Presentation mapping

`AppTransaction` continues deriving milestone completion from the existing
`ProgressCompletedThrough` value.

For each completed milestone:

- buyer role selects `_buyer_completed`;
- seller role selects `_seller_completed`.

For each incomplete milestone:

- both roles select `_disabled`.

Semantic mapping remains:

| Role | Step 1 | Step 2 | Step 3 |
| --- | --- | --- | --- |
| Buyer | agreement | payment | physical receipt or digital handoff |
| Seller | agreement | physical handoff or digital handoff | payout |

The same presentation layer supplies token background, outline, label, and
connector colors. No view code infers transaction state.

## Component behavior

`TransactionProgressView` retains three circular `48 × 48` tokens and two
rounded connectors. It remains read-only and contains no tap commands,
navigation, or animation.

The image optical size remains `30 × 30`. If a glyph appears smaller because of
its negative space, adjust only its SVG geometry; do not give individual images
different XAML sizes.

The title remains `15` and milestone labels remain `12`.

## Accessibility

- Each token retains a Thai semantic description containing the milestone label
  and `เสร็จแล้ว` or `ยังไม่เสร็จ`.
- Decorative images and connectors remain outside the accessibility tree.
- Role labels and milestone labels remain visible, so Buyer Blue and Seller
  Purple are not the only role indicators.
- Disabled and completed states differ in fill, outline, rail color, label
  color, and semantic description.
- The component has no gesture recognizers and does not imply that users can
  skip transaction states.

## Error handling

All icons are local build-time resources. Missing, malformed, wrongly named, or
wrong-palette assets fail asset-contract tests or the iOS build. The app does
not download icons, substitute remote artwork, or silently fall back to a stock
system symbol.

## Testing

Add or update tests that verify:

- all eighteen assets exist and parse as SVG;
- every asset has view box `0 0 48 48`;
- every open rail path uses round caps and joins;
- no stroke exceeds `3`;
- buyer-completed assets contain Buyer Blue and Mint, but no Seller Purple;
- seller-completed assets contain Seller Purple and Mint, but no Buyer Blue;
- disabled assets contain neutral gray and no Buyer Blue, Seller Purple, or
  Mint;
- agreement, payment, handoff, receipt, digital handoff, and payout each map to
  the correct role/fulfillment milestone;
- completed token, label, and destination connector colors follow the
  transaction role;
- active but incomplete milestones remain neutral;
- Thai semantic descriptions still communicate completion independently of
  color;
- the progress component retains three equal tokens, two connectors, no badge,
  no tap behavior, title size `15`, label size `12`, and image size `30`;
- the full mobile-core suite and iOS simulator build pass.

Visual verification covers at least:

- one buyer physical transaction;
- one seller physical transaction;
- one buyer or seller digital transaction;
- completed and incomplete milestones in the same card;
- Thai labels at the default text size and one accessibility text size.

## Assumptions

- The approved three-step buyer and seller milestone labels remain unchanged.
- Existing transaction completion mappings remain correct.
- The current Buyer Blue, Seller Purple, Mint, and neutral palette remain the
  app color source of truth.
- The status card, not the progress glyph alone, remains the source of current
  action and deadline information.

## Out of scope

- Changing transaction states or business rules.
- Adding icon animation.
- Making progress tokens interactive.
- Redesigning non-progress icons elsewhere in the app.
- Changing transaction-detail spacing beyond what is needed to preserve the
  existing `48 × 48` token and `30 × 30` image sizes.
