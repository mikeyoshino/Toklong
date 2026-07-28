# Connected Status Tokens Design

Date: 2026-07-28  
Status: Approved visual direction; awaiting written-spec review

## Goal

Replace the current mixed-style transaction progress icons with one compact,
branded status system. The component must remain immediately understandable as
an agreement, payment or fulfillment flow while matching the rounded
Transaction Rail language of the TOKLONG logo.

This change is visual and presentational. It does not change transaction
states, transition rules, payment truth, fulfillment eligibility, payout
eligibility or deadlines.

## Selected direction

Use **Connected Tokens**:

- three circular milestone tokens connected by two horizontal segments;
- custom glyphs drawn with the same heavy, rounded strokes as the TOKLONG
  Transaction Rail;
- no floating number or check badges;
- no stock banknote, vintage phone or generic delivery-truck artwork;
- role-specific labels and glyphs for buyer and seller;
- completed milestones are green and incomplete milestones are gray;
- the separate main status card remains the source of truth for the current
  actionable state.

The visual reference approved in Visual Companion shows a buyer flow and a
seller flow side by side.

## Visual system

### Geometry

- Each token is `48 × 48` device-independent units.
- Tokens are circles, not rounded-square icon tiles.
- Each glyph uses a consistent rounded stroke and rounded joins.
- The inactive circle outline is `4` units.
- Each connector is `4` units high with round caps and sits behind the tokens.
- Labels remain centered below their token, support two lines, and retain a
  minimum touch/semantic target of `44 × 44` where the platform exposes one.

### Color and completion

- Completed token fill and label: `#087C68`.
- Completed glyph: white.
- The agreement glyph may use the brand Mint node `#65D6BF` as a small internal
  detail; Mint is decorative brand confirmation only and does not communicate
  provider-confirmed payment or delivery.
- Incomplete token fill: white.
- Incomplete outline and connectors: `#E4EAF1`.
- Incomplete glyph and label: `#98A2B3`.
- A connector becomes green only when the milestone at its destination is
  completed. Otherwise it remains gray.
- The active but incomplete milestone remains gray. The main status card and
  primary action explain what the user must do now.
- Do not use color alone: accessible milestone descriptions include the label
  and either `เสร็จแล้ว` or `ยังไม่เสร็จ`.

### Glyph construction

All glyphs are repository-owned SVG assets with a shared view box, stroke
weight, cap style and optical size.

- Agreement: the two opposed Transaction Rails and Mint confirmation node from
  the TOKLONG mark.
- Buyer payment: a simple confirmation junction formed from two rounded strokes,
  without currency signs, coins, banknotes or card-network imagery.
- Physical fulfillment: a minimal parcel made from rounded strokes. The seller
  variant includes an outward handoff cue; the buyer variant shows receipt.
- Digital fulfillment: a rounded rail handoff glyph rather than a parcel,
  truck, key, password or reusable credential.
- Seller payout: a destination rail and confirmation node, without a banknote,
  wallet or claim that TOKLONG itself holds funds.

Each semantic glyph has completed and incomplete variants so the existing
presentation model continues selecting local assets without runtime tinting.

## Role and fulfillment variants

Buyer labels remain:

1. `สร้างข้อตกลง`
2. `จ่ายเงิน`
3. `ได้รับของ`

Seller labels remain:

1. `ยอมรับข้อตกลง`
2. `ส่งของ`
3. `รับเงิน`

The labels are unchanged in this slice. The icon selected for fulfillment steps
must use `FulfillmentType`: a parcel for physical items and a rail-handoff glyph
for supported transferable digital items. This prevents a truck or parcel from
misrepresenting digital fulfillment.

## Component structure

The repeated milestone markup in `TransactionDetailPage.xaml` should become one
small reusable progress component or one bindable collection template. Each
milestone presentation item exposes:

- label;
- local icon asset;
- completed state;
- semantic description.

The progress presentation also exposes the two connector colors. It derives all
values from the existing role, fulfillment type and completed-through value.
No transaction state is mutated by this component.

Keeping the component isolated prevents the three nodes from drifting in size,
spacing or semantics and lets icon changes remain independent from transaction
actions.

## Data flow

1. The transaction state and authenticated role continue to produce
   `ProgressCompletedThrough` and `ProgressActiveStep`.
2. The presentation layer maps role and fulfillment type to the approved local
   glyph family.
3. The view renders three milestone items and two decorative connectors.
4. Screen readers announce each milestone label and completion state.
5. Missing or invalid assets fail during build or UI consistency tests; the
   component does not download or substitute remote artwork at runtime.

## Interaction and motion

The status card is read-only. Tokens do not navigate and are not independently
clickable. There is no new animation. This avoids implying that users can skip
states and remains safe under Reduced Motion.

## Testing

Update or add tests that verify:

- buyer and seller receive the correct semantic glyphs;
- physical and digital fulfillment select different appropriate glyphs;
- completed milestones use completed assets and green labels;
- active but incomplete milestones remain gray;
- connector one becomes green only when milestone two is completed;
- connector two becomes green only when milestone three is completed;
- every milestone exposes a Thai semantic completion description;
- the XAML contains three equal circular tokens and two connectors;
- floating number/check badges are removed;
- the progress component has no tap command;
- all referenced SVG assets exist and parse;
- changed-page accessibility and the full mobile-core test suite pass.

## Assumptions

- The existing three milestones and Thai labels remain product-approved.
- The main transaction status card continues to carry detailed current-state
  copy, deadlines and the single primary action.
- This design does not alter the existing definition of when a milestone is
  completed.

## Out of scope

- Adding or removing transaction states.
- Changing payment, refund, shipping, dispute or payout rules.
- Making the progress tokens interactive.
- Animating state changes.
- Rewriting the detailed status copy or milestone labels.
