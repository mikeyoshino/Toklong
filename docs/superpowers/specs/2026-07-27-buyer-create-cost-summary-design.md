# Buyer Create-Offer Cost Summary Design

## Status

Approved by the product owner on 2026-07-27 after reviewing the Superpowers
Visual Companion mockups. This specification covers only the cost preview
during buyer offer creation. The accepted direction is the sticky summary
variant B with the bottom-sheet detail interaction B1.

## Goal

Show the Buyer Protection fee as soon as the buyer enters a valid item price,
without presenting an incomplete amount as the final payment total. Make it
unmistakable that no payment is collected while creating the offer.

## Scope

This slice adds:

- a server-calculated Buyer Protection preview for an authenticated buyer;
- a compact sticky cost bar on the create-offer screen;
- a cost-detail bottom sheet opened from that bar;
- loading, invalid-price, stale-response, and service-error handling;
- accessibility and layout tests for the new controls.

This slice does not change seller quote selection, checkout, Stripe
PaymentSheet, transaction states, paid snapshots, pricing tiers, or fee-policy
configuration. The separate exact checkout breakdown remains a later design
and implementation slice.

## Approved interaction

### Visibility

The sticky summary is hidden when:

- the price is empty;
- the value cannot be represented as integer satang;
- the price is outside the active server policy range;
- no current server preview is available.

After a valid price remains unchanged for a short debounce interval, the app
requests a server preview. The bar appears only after the matching response is
received. A response for an older price must never replace the current
preview.

### Sticky summary bar

The bar is fixed above the bottom safe area while the create-offer form
scrolls. Content receives enough bottom inset that the bar never covers a
field, validation message, or primary action.

The bar shows:

- label `ยอดก่อนค่าจัดส่ง`;
- exact item price plus Buyer Protection fee;
- action `ดูรายละเอียด`;
- reassurance `ยังไม่มีการเรียกเก็บเงิน`.

The bar is a single accessible button with a minimum 44-point touch target.
Its semantic description includes the amount and states that shipping is not
yet included.

### Bottom sheet

Tapping the bar opens a modal-style bottom sheet over a dimmed scrim. It uses:

- heading `ค่าใช้จ่ายเบื้องต้น`;
- `ราคาสินค้า`;
- `ค่าจัดส่ง` with status `รอผู้ขายเลือก` for physical goods;
- `ค่าจัดส่ง` with value `ไม่มีค่าจัดส่ง` for digital goods;
- `ค่าคุ้มครองผู้ซื้อ`;
- `ยอดก่อนค่าจัดส่ง` for physical goods;
- `ยอดเมื่อผู้ขายตอบรับ` for digital goods;
- a short description of Buyer Protection;
- guidance that the buyer will see and review the exact final total before
  payment;
- reassurance `ยังไม่มีการเรียกเก็บเงินในขั้นตอนนี้`.

The sheet closes through its close button, tapping the scrim, or the platform
back action. Focus moves into the sheet when opened and returns to the summary
bar when closed.

## Visual system

Reuse existing `Toklong.Mobile/App.xaml` tokens and type roles. Do not introduce
a parallel color or typography system.

- Main text: `Ink` (`#101828`)
- Secondary text: `InkSoft` / `Muted`
- Lines: `Line` (`#E4EAF1`)
- Primary blue: `BrandBlue` (`#2B7FFF`)
- Deep blue: `BrandBlueDeep` (`#145FC7`)
- Pale blue surfaces: the existing `#EAF4FF` family
- Sheet surface: white with the existing 24-point rounded-sheet treatment
- Scrim: the existing `#730F172A`

Normal row labels use the existing 12–13 point text roles without bold.
Amounts use bold only where the current app uses amount emphasis. The total is
14–16 points and must not be visually heavier than current primary amount
labels. The bar amount may use the existing emphasized amount role, capped at
20 points. Dynamic Type must wrap without clipping.

## Pricing boundary and data flow

The mobile client must not own or duplicate the marginal-tier formula.

1. The buyer enters a THB price.
2. The app converts the value to integer satang using the existing validated
   parser.
3. After debounce, the authenticated mobile client requests a pricing preview.
4. The API calls the existing `IPaymentFeePolicy`.
5. The policy validates the active range and returns the exact Buyer
   Protection fee, seller platform fee, expected seller net, and policy
   version.
6. The API returns only integer-satang values and the ISO currency code.
7. The mobile app presents the item price, Buyer Protection fee, and their
   exact sum. It does not persist this preview or use it as payment authority.
8. Offer creation, seller acceptance, and checkout continue to recalculate and
   validate pricing independently on the server.

Preview requests are read-only. They create no transaction, payment,
notification, acceptance, snapshot, or audit state transition.

## Error handling

- Empty, incomplete, or locally invalid input hides the preview without a
  network request.
- An active-range rejection keeps the bar hidden and lets the existing form
  validation present the price-range message.
- A transient preview failure must not block editing or erase other form
  fields. The bar stays hidden and the app may retry only after another price
  change or an explicit review action.
- Cancellation caused by a newer price is silent.
- The final review action still performs ordinary validation and server-side
  offer creation checks even when the preview succeeded.

## Reusable boundaries

- A small mobile-core cost-preview record owns integer-satang values and
  presentation labels.
- `ITransactionService` exposes the read-only preview operation.
- `CreateOfferViewModel` owns debounce, cancellation, stale-response
  protection, sheet visibility, and binding properties.
- `CreateOfferPage.xaml` owns only layout and visual states, using existing
  resources.
- The API endpoint owns authentication and delegates fee calculation to
  `IPaymentFeePolicy`.

## Tests

### API and application

- a valid 1,000 THB preview returns a 59 THB Buyer Protection fee;
- 5,000, 15,000, and 30,000 THB return the approved policy values;
- values outside the active range are rejected;
- values are integer satang with `THB`;
- the endpoint requires an authenticated buyer;
- the preview creates no transaction or financial state.

### Mobile core and presentation

- the summary total equals item price plus Buyer Protection fee using checked
  integer arithmetic;
- physical copy uses `ยอดก่อนค่าจัดส่ง` and `รอผู้ขายเลือก`;
- digital copy uses `ไม่มีค่าจัดส่ง`;
- stale responses cannot replace the preview for a newer price;
- clearing or invalidating the price hides the bar and closes the sheet;
- the sheet exposes separate rows for item price, shipping, and protection;
- the sticky control and close action meet the 44-point minimum;
- the form reserves enough bottom space to keep the existing review action
  reachable;
- colors, font sizes, and font weights reference existing app resources where
  available.

## Acceptance criteria

1. Entering a valid price eventually shows the sticky summary without leaving
   the form.
2. Entering `10,000.00` under `buyer-protection-v2` displays `฿10,375.00`
   before shipping.
3. Tapping `ดูรายละเอียด` opens B1 and displays the three cost rows.
4. Both surfaces state that payment has not been collected.
5. No client-side tier table or floating-point money calculation is added.
6. No offer, payment, snapshot, notification, or audit event is created by a
   preview.
7. Existing create-offer validation and the final
   `ตรวจข้อมูลก่อนส่ง` action continue to work.
