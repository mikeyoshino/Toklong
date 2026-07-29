# Seller Proof Status Icons Design

## Goal

Make the seller's three-step transaction status immediately recognizable
without relying on the labels alone. The approved direction is **B — Proof
Icons**.

## Scope

The design applies to the seller transaction progress:

1. `ยอมรับข้อตกลง`
2. `ส่งของ`
3. `รับเงิน`

Buyer progress icons and shipping-progress icons remain unchanged. Transaction
state mapping, authorization, fulfillment, payout rules, and audit behavior
remain unchanged.

## Approved Icon Language

All icons use a 26-by-26 drawing area, approximately 2.1-point strokes, rounded
line caps, rounded joins, and no solid fill.

### Agreement proof

A document with a folded corner and a check mark. It replaces the handshake
for the seller view and communicates that the agreement has been accepted and
recorded.

### Shipment proof

A compact delivery vehicle with an outgoing arrow. The arrow distinguishes the
seller's handoff action from a generic in-transit carrier status.

### Payout proof

A transaction record with a check mark. It communicates a completed payout
without a coin, banknote, currency symbol, or wallet metaphor.

## Progress State Styling

- Completed seller steps use graphite `#3B5266` on seller surface `#EDF2F5`.
- The current incomplete seller step uses teal `#087C68` on `#EAFBF7`.
- Later seller steps use muted `#98A2B3` with border `#E4EAF1` on white.
- Completed connectors remain graphite. Incomplete connectors remain
  `#E4EAF1`.
- Buyer styling remains unchanged.

When fewer than three steps are complete, the current seller step is the next
incomplete step. When all three steps are complete, no step receives the
current styling.

## Accessibility

The custom drawing remains excluded from the accessibility tree. Each token
continues to expose one semantic description through its surrounding border:
the Thai step label followed by whether it is complete, current, or not yet
complete. Meaning must not depend on color alone.

## Implementation Boundary

Add seller-specific glyph enum values and drawing functions instead of
changing glyphs shared by the buyer. Select those glyphs only when
`AppTransaction.Role` is `Seller`.

Do not add image assets or a new icon dependency. Continue drawing through the
existing `TransactionProgressIconView` graphics view.

## Verification

- Presentation tests verify seller-only glyph selection and current/completed
  colors.
- Buyer presentation tests verify existing buyer glyphs and colors remain
  unchanged.
- UI layout tests verify the same three accessible tokens and label placement.
- Build the iOS simulator target and visually inspect the same seller
  transaction used during brainstorming.

