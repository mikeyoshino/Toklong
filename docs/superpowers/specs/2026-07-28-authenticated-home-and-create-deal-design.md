# Authenticated Home and Create-Deal Design

**Date:** 2026-07-28  
**Status:** Approved in brainstorming; awaiting written-spec review

## Objective

Replace the post-startup login-first welcome experience with a clear
authenticated entry page, then simplify buyer offer creation without changing
TOKLONG's buyer-first transaction model.

The resulting mobile flow is:

```text
Open app
  → verify registered Thai mobile number
  → authenticated role home
      → ซื้อ → existing transaction root in buying mode
      → ขาย → existing transaction root in selling mode
      → รายการของฉัน → existing transaction root

Buying mode
  → + สร้างดีลซื้อ
  → three-step buyer offer wizard
  → seller reviews the buyer-created offer
```

The `ซื้อ` and `ขาย` choices are navigation choices, not permanent account
roles. One authenticated account may have both buyer and seller transactions.

## Product-model decision

This design follows the current buyer-first lifecycle in the Product Brief and
User Flows:

- The buyer creates one private offer for an intended seller's verified Thai
  mobile number.
- The offer creates an unguessable seller-routing link as a delivery
  convenience.
- Possessing the link does not authorize access or acceptance.
- The matching authenticated seller reviews and accepts or declines the
  buyer-created offer.
- The seller does not create a marketplace listing or a seller-first sales
  link in this MVP.
- The buyer reviews the accepted final terms before payment.

The UI must not use copy that implies a seller-created listing or sales-link
workflow.

## Authenticated role home

### Entry behavior

- A returning user without a valid session verifies the registered phone
  number before reaching this page.
- A valid session routes directly to this page after startup motion.
- A pending new-account registration still resumes profile completion before
  this page.
- The page never repeats a login or registration action.

### Visual hierarchy

The page uses the real TOKLONG transaction-rail mark and wordmark centered near
the top. Below the brand:

- Heading: `เริ่มดีลอย่างมั่นใจ`
- Supporting copy: `สร้างข้อเสนอซื้อ หรือจัดการรายการขาย`

Two full-width, independently accessible action cards follow:

1. `ซื้อ`
   - Description:
     `สร้างข้อเสนอ ตรวจรายละเอียด และติดตามรายการ`
   - Blue role color.
   - Routes to the existing `TransactionsPage` with buying mode selected.
2. `ขาย`
   - Description:
     `ตรวจข้อเสนอ ส่งสินค้า และติดตามยอดรับ`
   - Purple role color.
   - Routes to the existing `TransactionsPage` with selling mode selected.

A lower-emphasis `รายการของฉัน` action routes to the existing transaction
root. It does not compete visually with the two role cards.

### Accessibility

- The brand is announced once as `โลโก้ TOKLONG`.
- Each action card is one focusable control; nested labels and arrows are not
  separate focus targets.
- Semantic descriptions state the result of the action, for example
  `เปิดรายการที่คุณเป็นผู้ซื้อ`.
- Role is never communicated by color alone; visible `ซื้อ` and `ขาย` labels
  are required.
- Cards retain the existing minimum touch target and support Dynamic Type
  without clipping the descriptions.

## Existing transaction root

The authenticated home does not replace `TransactionsPage`. It makes the
existing root easier to enter in the intended mode.

### Buying mode

- Keep `รายการของคุณ`, the `ซื้อ | ขาย` switch, buyer action spotlight,
  filters, and buyer transaction list.
- Select `ซื้อ` when navigation originated from the home `ซื้อ` card.
- Keep the existing `+ สร้างดีลซื้อ` action.
- Notification and deep-link navigation still opens the exact transaction
  directly.
- Do not add a generic clipboard/paste-link control.

### Selling mode

- Keep `รายการของคุณ`, the `ซื้อ | ขาย` switch, seller action spotlight,
  seller filters, and seller transaction list.
- Select `ขาย` when navigation originated from the home `ขาย` card.
- Retain seller filters for `ต้องตอบ`, `ต้องส่ง`, `รอรับเงิน`, and
  `เสร็จแล้ว`.
- Do not add `สร้างลิงก์ขาย` or any seller-created listing action.
- Incoming buyer-created offers remain the seller's entry into a new sale.

The root continues remembering its last selected mode for ordinary navigation.
An explicit role-card navigation overrides the remembered mode for that visit.

## Create-deal information architecture

The existing `CreateOfferPage` remains the owner of buyer offer creation. Its
long quick form and review bottom sheet become a three-step, full-page wizard.
The backend request and domain lifecycle do not change.

### Shared wizard chrome

- Shell title: `สร้างข้อเสนอ`
- Back action with a plain-language semantic label.
- Three-segment progress indicator.
- Visible step name and `ขั้นที่ N จาก 3`.
- One primary action at the bottom of each step.
- Values stay in one `CreateOfferViewModel` while navigating between steps.
- No transaction, product snapshot, notification, or payment record is created
  before the final submit succeeds.

### Step 1 — ข้อมูลดีล

Purpose: capture only what the parties already agreed in the external chat.

Required, in order:

1. `เบอร์ผู้ขาย`
2. `ชื่อสินค้า`
3. `ราคาที่ตกลง`

Optional actions below the required fields:

- `รูปสินค้า (ไม่บังคับ)`
- `รายละเอียดเพิ่มเติม`
- `ให้ AI ช่วยกรอกจากแชต`

AI remains optional and opens the existing isolated assistant sheet. It fills
only blank fields, never advances the wizard, bypasses validation, or submits
an offer.

Primary action: `ถัดไป: การรับสินค้า`

### Step 2 — การรับสินค้า

Purpose: capture the applicable fulfillment path and locked buyer delivery
information.

- Physical is selected by default.
- The user may switch to an allow-listed transferable digital item/right.
- A physical offer requires the complete delivery address or the saved-address
  selection.
- The address card offers `เปลี่ยน` without expanding all address controls by
  default when a saved address exists.
- Privacy copy explains that the seller sees only province and postal code
  until provider-confirmed payment.
- Shipping copy explains that the seller later selects an authoritative service
  using origin and parcel measurements before the buyer pays.
- The buyer never guesses shipping or includes it in the item price.
- Digital selection hides address and shipping content.

Primary action: `ถัดไป: ตรวจข้อมูล`

### Step 3 — ตรวจและส่ง

Purpose: provide the complete review-before-submit step in a full page instead
of a constrained bottom sheet.

The page shows:

- Product name.
- Masked intended seller phone.
- Item price.
- Optional photo indicator and additional details when supplied.
- Applicable delivery summary.
- Compact required condition choices:
  - `ใหม่`
  - `มือสอง สภาพดี`
  - `มีตำหนิ`
- A defect description only when `มีตำหนิ` is selected.
- Server-priced Buyer Protection fee.
- Shipping as `รอผู้ขายเลือก` for physical offers.
- `ยอดก่อนค่าจัดส่ง`.
- Plain copy that no payment is collected now and the seller must accept before
  the buyer reviews the final total and pays.

Each summary group has an `แก้ไข` action that returns to the owning step without
clearing any values.

The only create action is `ส่งข้อเสนอให้ผู้ขาย`.

## Validation and navigation behavior

- `ถัดไป` validates only the current step.
- Inline validation appears next to the affected input.
- The page scrolls to and focuses the first invalid input.
- Going backward within the wizard preserves all ViewModel values.
- Changing physical to digital clears no unrelated Step 1 values.
- Returning from Step 3 to an earlier step invalidates the stale cost preview;
  the preview is requested again before final submit.
- The submit command is single-flight and uses the existing server-side
  idempotency behavior.

### Leaving an incomplete wizard

The MVP does not persist a create-offer draft across page closure or app
restart.

If the user has changed any offer value and attempts to leave the wizard, show:

- Title: `ยังสร้างข้อเสนอไม่เสร็จ`
- Body: `ถ้าออกตอนนี้ ข้อมูลที่กรอกไว้จะหาย`
- Primary safe action: `กลับไปกรอกต่อ`
- Destructive secondary action: `ออกจากหน้านี้`

The destructive action is visually distinct and must not be the default focus.
No confirmation is shown when the wizard remains pristine.

## Data flow

```text
Authenticated role home
  → TransactionsPage role selection
  → CreateOfferPage
      → Step 1 values held in CreateOfferViewModel
      → Step 2 values held in the same ViewModel
      → server cost preview
      → Step 3 review
      → existing create-offer API
      → BUYER_OFFER_DRAFT / AWAITING_SELLER_ACCEPTANCE
      → transaction detail
```

The UI step is presentation state only. It does not become a backend
transaction state or audit event. The existing domain transition and audit
events remain authoritative.

## Error and loading states

- Address-catalog loading failure stays on Step 2 and offers `ลองอีกครั้ง`.
- Cost-preview failure stays before or on Step 3, preserves entered data, and
  offers `ลองอีกครั้ง`.
- AI analysis failure leaves ordinary manual fields usable.
- Final submit failure preserves all values and shows a retry action without
  claiming the offer was sent.
- While preview or submit is running, the relevant primary action is disabled
  and announces its loading state.
- An API error must never silently advance a step or clear the form.

## Testing

### ViewModel and navigation tests

- Role-home `ซื้อ` opens buying mode regardless of remembered selling mode.
- Role-home `ขาย` opens selling mode regardless of remembered buying mode.
- Ordinary transaction-root navigation still uses the remembered mode.
- Step navigation follows `1 → 2 → 3` and supports backward navigation.
- Values survive backward and forward navigation in the same wizard.
- Current-step validation blocks advancement and identifies the first invalid
  field.
- Physical and digital modes expose only applicable controls.
- Editing an earlier step invalidates and refreshes the preview.
- Exit confirmation appears only for a dirty wizard.
- `กลับไปกรอกต่อ` preserves values; `ออกจากหน้านี้` discards the in-memory
  wizard.
- Submit remains single-flight and creates only one offer.

### Integration tests

- No offer exists before final submit.
- Preview failure creates no offer, notification, audit transition, or payment.
- Final submit produces the existing buyer-first state and notification.
- Seller authorization remains tied to the intended verified phone, not link
  possession.
- Seller mode contains no seller-created link action.

### UI and accessibility checks

- All three steps fit the supported mobile widths without horizontal overflow.
- Dynamic Type does not hide required fields or the primary action.
- Progress is announced as text, not by color alone.
- Error focus and screen-reader announcements identify the affected field.
- Tap targets retain the project minimum size.
- The exit dialog exposes the safe action first and labels the destructive
  action clearly.

## Non-goals

- Seller-created marketplace listings or sales links.
- Marketplace discovery or search.
- Persistent local or server-side offer drafts.
- New backend transaction states for wizard steps.
- A separate contract drafting or signing flow.
- Changes to payment, shipping, dispute, payout, or immutable snapshot rules.

## Assumptions

- Phone authentication and account completion are already available.
- The existing address, photo, AI extraction, preview, and create-offer services
  remain the source of behavior.
- The visual companion mockups define hierarchy and interaction direction, not
  pixel-perfect platform rendering.
