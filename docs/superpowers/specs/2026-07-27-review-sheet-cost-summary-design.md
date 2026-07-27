# Review-Sheet Cost Summary Design

## Goal

Show the Buyer Protection price breakdown only inside the existing
`ตรวจข้อมูลก่อนส่ง` review sheet. Remove the separate sticky price bar,
standalone price bottom sheet, and the `กำหนดส่งสินค้า` card.

## User flow

1. The buyer completes the create-offer form.
2. The buyer taps `ตรวจข้อมูลก่อนส่ง`.
3. The app validates the ordinary required fields and item price.
4. The app requests a fresh Buyer Protection preview from the authenticated
   server endpoint for the exact current item price.
5. The review sheet opens only after that matching server response succeeds.
6. The buyer reviews the agreement, selects the condition, reviews the cost
   breakdown, and chooses `ส่งข้อเสนอให้ผู้ขาย` or `กลับไปแก้ไข`.

If pricing fails, the review sheet remains closed and the form shows a
retryable message. No locally calculated fee or stale preview is shown.

## Review-sheet layout

The review sheet keeps its current heading, agreement summary, condition
selection, defect input, validation message, and final actions.

Add one `สรุปค่าใช้จ่าย` section after the agreement summary and before the
condition selection. It contains:

- `ราคาสินค้า`
- `ค่าคุ้มครองผู้ซื้อ`
- `ค่าจัดส่ง`
- the applicable total label and amount
- `ยังไม่ตัดเงินในขั้นตอนนี้`
- a reminder that the buyer will review the final amount again before payment

For physical fulfillment:

- total label: `ยอดก่อนค่าจัดส่ง`
- shipping value: `รอผู้ขายเลือก`

For digital fulfillment:

- total label: `ยอดเมื่อผู้ขายตอบรับ`
- shipping value: `ไม่มีค่าจัดส่ง`

Use existing TOKLONG colors, spacing, type sizes, and normal label weights.
The cost section must remain readable inside the existing scrollable sheet.

Remove:

- `BuyerCostPreviewSummary`
- `BuyerCostPreviewSheet`
- `BuyerCostPreviewFormSpacer`
- the physical `กำหนดส่งสินค้า` card and its three-day copy

## Pricing and state behavior

- The server remains the only owner of fee tiers and calculation.
- The client sends integer satang and displays the returned integer-satang
  breakdown.
- `ReviewCommand` becomes asynchronous and obtains a fresh preview for the
  current validated price.
- A response is accepted only when its item price still matches the form.
- Changing the item price, fulfillment type, or closing the review invalidates
  the displayed preview.
- Reviewing price creates no transaction, snapshot, acceptance, notification,
  payment, refund, payout, or audit event.
- Offer creation and later checkout remain independently authoritative.

## Error handling

- Invalid form input uses the existing validation messages.
- Cancellation caused by editing or leaving the page is silent.
- A pricing network or server failure keeps the review closed and shows a
  concise retryable message.
- The review action is disabled while its pricing request is running to prevent
  duplicate requests.

## Verification

- API integration tests continue to verify authenticated server pricing and
  no transaction creation.
- Mobile core tests continue to verify money formatting and physical/digital
  copy.
- XAML consistency tests verify that the review sheet contains the price
  section and no longer contains the sticky bar, standalone price sheet,
  spacer, or shipment-deadline card.
- The iOS simulator verifies that tapping `ตรวจข้อมูลก่อนส่ง` opens one
  scrollable review sheet containing the correct server-priced total.

## Approved Create Offer visual design

The form follows the approved Superdesign draft
`TOKLONG - สร้างข้อเสนอ (Price Section Refinement)`.

- Replace the native navigation/title treatment with a white rounded header
  containing a pale-blue back action, `สร้างข้อเสนอ`, the subtitle
  `ส่งให้ผู้ขายตรวจและตอบรับ`, and a two-segment progress indicator.
- Show the essential fields first: seller phone, product name, item price, and
  physical delivery address.
- Keep the product-price section directly on the page background. It has no
  outer card border, white container, shadow, or outer card padding. The amount
  input keeps one blue rounded border.
- Keep the delivery-address summary as a white rounded bordered card. The
  address editor remains available through the existing change action.
- Move fulfillment type, optional product photo, optional agreement details,
  and AI assistance below the essential fields as progressive-disclosure
  actions without deleting behavior.
- Use Noto Sans Thai Medium 500 for form labels and Regular 400 for helper
  descriptions, placeholders, entered values, picker values, and fee-row
  labels. Reserve Bold 700 for page titles, primary actions, the large amount,
  and final totals.
- Focus is shown by the active input border only. The page heading never
  receives a visible focus border.
- Existing automation IDs, semantic descriptions, physical/digital behavior,
  address editing, photo handling, AI drafting, validation, condition/defect
  selection, and submission logic remain intact.
