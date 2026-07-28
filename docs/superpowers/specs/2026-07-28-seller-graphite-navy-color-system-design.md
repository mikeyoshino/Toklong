# Seller Graphite Navy Color System

Date: 2026-07-28  
Status: Approved in Visual Companion

## Goal

Replace the saturated purple seller treatment with a calmer Graphite Navy
system. Seller surfaces must feel professional and comfortable over large
areas while remaining distinguishable from the brighter blue buyer role.

This is a presentation-only change. It does not change transaction data,
classification, actions, state transitions, money, shipping, disputes,
analytics, or authorization.

## Approved direction

The seller role uses Graphite Navy with the existing mint brand accent.
Buyer surfaces retain the existing brand blue. Warning, progress, and problem
colors retain their existing semantic meanings.

The approved seller palette is:

| Token | Value | Use |
| --- | --- | --- |
| `SellerRole` | `#3B5266` | Seller labels, links, and white-surface actions |
| `SellerHeaderStart` | `#4B6073` | Start of large seller gradients |
| `SellerHeaderMiddle` | `#3D5163` | Middle of large seller gradients |
| `SellerHeaderEnd` | `#304354` | End of large seller gradients |
| `SellerSurface` | `#EDF2F5` | Pale seller background where a role tint is required |
| `SellerBorder` | `#C8D4DC` | Seller surface borders |
| `SellerSecondary` | `#DCE7EC` | Secondary text on Graphite Navy |
| `SellerBadgeSurface` | `#F3F7F9` | Seller badge background |
| `SellerAccent` | `#8DE8D2` | Small dots and brand accents |

Large seller gradients must be subtle:

```text
#4B6073 → #3D5163 → #304354
```

Do not introduce a saturated highlight or a large purple surface elsewhere to
replace the existing seller purple.

## Surface application

### Authenticated home

- Keep the buyer card unchanged.
- Change the seller card to the Graphite Navy gradient.
- Keep its primary and secondary copy white or `SellerSecondary`.
- Render the new-offer badge on `SellerBadgeSurface` with `SellerRole` text.
- Keep the actionable mint dot, using `SellerAccent`.
- Preserve the existing layout, counts, visibility rules, copy, tap behavior,
  and semantic description.

### Seller workspace

- Use `SellerRole` for the selected seller role label, seller links, and
  seller action labels shown on white.
- Use the Graphite Navy gradient for the seller spotlight.
- Use `SellerSecondary` for secondary spotlight copy.
- Use `SellerAccent` only as a small priority/accent marker.
- Keep seller item-price-only presentation. Do not expose buyer protection fee
  or buyer total.
- Keep SHIPPOP-managed records status-only; do not expose manual Add Tracking.

### Transaction details and compact seller cards

- Use Graphite Navy for seller headers and role accents.
- Keep exact deadlines wrapping without ellipsis.
- Preserve all content, actions, disclosure rules, and role authorization.
- Do not perform a global purple replacement. Change only colors whose existing
  purpose is the seller role; unrelated semantic colors remain unchanged.

## Seller summary tiles

The three summary tiles use the same white background. They are distinguished
by border, number, and label color rather than filled color blocks.

| Tile | Background | Border | Number and label |
| --- | --- | --- | --- |
| รอตอบ | `#FFFFFF` | `#DDB866` | `#8A5100` |
| ต้องส่ง | `#FFFFFF` | `#9BAEBC` | `#3B5266` |
| กำลังไปต่อ | `#FFFFFF` | `#9CC4EC` | `#145FC7` |

The selected tile must remain white. Selection is shown through:

- a thicker border in that tile's semantic color;
- a visible selection dot or equivalent non-color marker;
- a subtle shadow; and
- the existing selected-state semantic announcement.

Unselected tiles must not be dimmed. This preserves readable contrast and keeps
all three counts equally scannable. Selection must not rely on color alone.

The problem banner remains red and separate from the three tiles. It is not
converted to Graphite Navy.

## Semantic color boundaries

- Amber continues to mean a new offer awaiting seller response.
- Graphite Navy identifies the seller role and seller-owned fulfillment work.
- Blue continues to represent buyer role surfaces and in-progress work.
- Red continues to identify a problem or blocked payout.
- Mint is an accent, not a success-state replacement.

The role color must not override status colors inside badges, warnings, error
messages, or the problem banner.

## Accessibility

- Normal text must meet WCAG AA contrast against its actual background.
- Selected summary tiles must expose selected state through semantics and a
  non-color visual marker.
- Existing minimum tap targets remain unchanged.
- Exact dates and times must continue to wrap at narrow widths and
  accessibility text sizes.
- Seller card and spotlight copy must remain readable at Accessibility Large
  without clipping.
- Buyer and seller must remain identifiable by visible role labels, not color
  alone.

## Testing and verification

Automated checks must cover:

- the Graphite Navy seller role tokens;
- absence of the former seller purple on the owned home, workspace, spotlight,
  and seller-header surfaces;
- buyer color tokens remaining unchanged;
- all summary tiles using white backgrounds;
- semantic border/text colors for each summary tile;
- selected tiles remaining white and exposing a non-color selected marker;
- seller item-price-only bindings;
- SHIPPOP-managed seller records retaining status-only behavior; and
- XAML compilation.

Verification must include:

- the Mobile Core test suite;
- an exact iOS Simulator build;
- authenticated home and seller workspace at a normal text size;
- the same screens at Accessibility Large; and
- a narrow-width check for summary labels and exact deadlines.

Spoken VoiceOver and physical-device rendering remain separate manual
ceremonies when no physical iOS device is connected.

## Scope exclusions

- No layout restructuring beyond selected-tile styling required by this spec.
- No copy changes.
- No buyer palette changes.
- No transaction workflow or seller-work classification changes.
- No backend, API, database, webhook, payment, payout, shipping, or dispute
  changes.
- No new animation or iconography.

## Assumptions

- Graphite Navy is a role treatment, not a new transaction status.
- Existing amber, blue, red, and mint colors remain suitable for their current
  semantic roles.
- The Visual Companion mockup is a visual reference; this document defines the
  binding token values and accessibility behavior.

