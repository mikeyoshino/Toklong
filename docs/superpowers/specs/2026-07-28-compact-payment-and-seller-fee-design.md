# Compact Payment and Seller Fee Design

**Date:** 2026-07-28  
**Status:** Approved in brainstorming; awaiting written-spec review

## Objective

Reduce repeated information in the accepted-offer payment state while keeping
the buyer's exact pre-payment disclosures intact. Remove the buyer-protection
fee amount from seller-facing agreement views because it is not part of the
seller's payout.

This is a mobile presentation change only. It does not change fee
calculation, payment eligibility, transaction snapshots, API contracts, or
domain transitions.

## Visibility rules

### Buyer

The buyer continues to see the buyer-protection fee:

- in the buyer offer review before the offer is sent;
- in the accepted-offer cost breakdown immediately before payment; and
- in advanced transaction details where the immutable paid breakdown is
  required.

The accepted-offer payment state must show the exact item price, buyer
protection fee, shipping charge, and total before enabling payment. This
preserves the requirement that fees and exact payment terms are visible before
payment.

### Seller

Seller-facing offer acceptance and agreement summaries must not show the
amount labelled `ค่าคุ้มครองที่ผู้ซื้อจ่าย` or
`ค่าคุ้มครองผู้ซื้อ`.

The seller continues to see:

- item price;
- applicable shipping information or charge;
- any seller-side fee that materially affects payout, if introduced under an
  approved fee policy; and
- the exact expected seller net amount.

The existing buyer-protection amount may remain in the API model and acceptance
request for integrity checks. Hiding it from seller presentation does not
authorize changing or omitting server-side validation.

## Accepted-offer buyer payment layout

The affected state is the buyer transaction detail after the seller accepts
the offer and before provider-confirmed payment.

### Remove the separate pre-payment card

Remove the complete `เช็กให้ครบก่อนจ่าย` card, including:

- its heading and shield icon;
- the receipt-email helper copy;
- its repeated delivery-address block; and
- the extra card border and vertical spacing.

Do not remove the buyer's confirmation or payment action. Move these controls
directly below the existing exact cost breakdown:

1. the required confirmation checkbox;
2. concise confirmation copy covering the product, price, ship-by deadline,
   dispute deadline, and applicable terms; and
3. one primary button labelled with the exact total, for example
   `ชำระ ฿10,635`.

The primary action remains disabled or fails closed until the required
confirmation is selected. Existing payment-command error handling remains in
place.

### Delivery address

During the accepted-offer payment state:

- show the delivery address once in the main agreement/product details;
- do not repeat it beside the payment controls;
- retain the immutable-address explanation under advanced
  `รายละเอียดรายการ`, rather than in the primary payment path; and
- do not add a second address-edit action after seller acceptance.

After payment, the full address appears only where it is operationally useful:

- the applicable shipping/tracking section; and
- advanced `รายละเอียดรายการ`.

Pages or states that do not require the full address should show only the
delivery region or omit the address entirely.

## Layout and spacing

- The exact cost breakdown and payment controls form one continuous action
  section.
- Use the existing transaction-detail content width and spacing tokens.
- Do not introduce a new nested surface around the checkbox and payment
  button.
- Keep one primary action in the state.
- The checkbox copy may wrap, but the total and payment button label must not
  truncate at supported mobile widths.
- Removing the card must also remove its reserved margins so it does not leave
  an artificial gap.

## Accessibility

- The confirmation checkbox retains its semantic description.
- The payment button announces the exact total.
- Removing the repeated address must not remove the only accessible address;
  the remaining address in the agreement details is readable as one grouped
  block.
- Seller screen readers must not announce the buyer-protection fee amount.
- Dynamic Type must preserve reading order: cost breakdown, confirmation,
  payment action.

## Data and state behavior

No backend or state-machine behavior changes:

- the accepted offer and paid transaction keep the existing immutable monetary
  snapshot;
- buyer-protection fee remains integer satang plus ISO currency;
- payment remains pending until a signature-verified provider webhook or
  authorized reconciliation confirms it;
- client action never marks payment successful;
- seller fulfillment remains unavailable until provider-confirmed payment;
  and
- open disputes continue to block payout.

## Test coverage

Update mobile UI and presentation tests to prove:

1. Buyer offer review still contains the buyer-protection fee.
2. Seller offer acceptance does not contain either seller-facing
   buyer-protection fee label.
3. Seller transaction agreement summaries do not render the buyer-protection
   fee amount.
4. The accepted-offer buyer payment state contains one delivery-address
   presentation, not two.
5. The `เช็กให้ครบก่อนจ่าย` card heading, receipt-email helper, and repeated
   address block are absent.
6. Confirmation and payment action remain present below the exact buyer cost
   breakdown.
7. Payment cannot start without confirmation.
8. Accessibility semantics and changed-page layout checks pass.

Existing domain, authorization, payment-webhook signature, idempotency,
replay, and payout-blocking tests remain unchanged and must continue to pass.

## Assumptions and exclusions

- The current buyer-protection fee policy and payer do not change.
- Seller payout remains based on the existing server-calculated net amount.
- This design does not redesign post-payment tracking, account address
  management, Stripe PaymentSheet, or the advanced transaction record.
- No marketplace, chat, wallet, stored value, or seller-created listing is
  introduced.

