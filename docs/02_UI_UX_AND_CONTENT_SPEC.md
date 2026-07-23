# 02 — UI, UX, and Content Specification

## Design direction

- Mobile first.
- White and pale-blue surfaces.
- Soft radial spotlight in the hero.
- Rounded cards and restrained glass effects.
- Friendly Thai copy with precise money and deadline language.
- Product animation shown inside one mobile device, not a complex desktop dashboard.
- One primary action per transaction state.

## Landing page information architecture

1. **Hero**
   - Positioning: payment link for deals originating from Facebook, Marketplace, or chat.
   - Primary buyer CTA: `สร้างข้อเสนอซื้อ`.
   - Seller CTA: `สร้างลิงก์ข้อตกลง`.
   - Secondary CTA: play the mobile walkthrough.
   - Animated mobile UI with four scenes.
2. **Trust strip**
   - Provider-confirmed payment before shipping.
   - Tracking as the shared physical-delivery record; explicit confirmation/manual review for digital handoff.
   - Dispute blocks payout.
3. **Seller and buyer simplicity**
   - Seller: create a link or accept a buyer offer, fulfill, receive payout.
   - Buyer: create an offer or review/pay a seller link, track or review handoff, confirm/report.
4. **Four-step flow**
   - Create link → Pay → Fulfill → Confirm/payout.
5. **Behind-the-scenes record**
   - Explain that transaction terms are recorded without a visible contract-signing step.
6. **Focused use cases**
   - Used goods, community/group sales, private chat deals.
7. **Protection rules**
   - Exact start/end of seven-day window.
   - Reminder before payout.
   - Dispute pause.
8. **FAQ**
   - Not a marketplace.
   - Payout timing.
   - Missed shipment deadline.
   - Item not as described.
   - No separate contract signing step.
   - Unsupported goods.
9. **Final CTA**

## Hero mobile animation

### Scene 1 — Buyer creates an offer / seller accepts

Show:

- Proposed item, fulfillment type, price, shipping, and fulfillment rule.
- Primary buyer action `สร้างข้อเสนอซื้อ`.
- Generated private URL and `คัดลอกแล้ว`.
- Seller view of the same link with expected net payout, offer-expiry time, and primary action `ยอมรับข้อเสนอ`.
- Plain-language status `ผู้ซื้อพร้อมชำระเมื่อคุณยอมรับ`; never imply payment is complete.

### Scene 2 — Buyer reviews and pays

Show:

- Same frozen product details.
- Seller identity signal.
- Price breakdown and total.
- Plain-language payout condition.
- Primary action `ชำระ ฿X`.

### Scene 3 — Seller ships

Show:

- Success banner `ผู้ซื้อชำระสำเร็จ`.
- Plain-language note that money is waiting for release through the payment partner.
- Exact fulfillment deadline.
- Two clear examples: physical uses carrier/tracking; digital uses the agreed external channel and never stores credentials.
- Physical primary action `บันทึกหมายเลขติดตาม`.
- Digital primary action `แจ้งว่าส่งมอบแล้ว`, with copy that this does not release payout.

### Scene 4 — Buyer inspects / payout

Show:

- Physical example: carrier-confirmed delivered timestamp, exact dispute deadline, and countdown.
- Digital example: no countdown; buyer confirmation or authorized manual review is required.
- Primary action `ได้รับสินค้าแล้ว` or `ได้รับรายการดิจิทัลแล้ว`.
- Secondary action `แจ้งปัญหา`.
- Final success overlay `จ่ายเงินให้ผู้ขายแล้ว`.

### Animation behavior

- Autoplay one scene every approximately 4–5 seconds.
- Provide previous, pause/play, and next controls.
- Provide four clickable progress dots.
- Replay from the CTA.
- Pause or disable autoplay for `prefers-reduced-motion`.
- Announce scene changes with an `aria-live` region.
- Do not rely on animation alone; all four steps also appear as static content below.

## Seller application screens

### Seller home

Primary elements:

- `สร้างลิงก์ข้อตกลง` CTA.
- Incoming buyer-created offers grouped separately from funded sales.
- Active sales grouped by state.
- Clear amount and next action.

Suggested state cards:

- `ข้อเสนอใหม่ · ตอบภายใน 24 ก.ค. 18:00`
- `รอผู้ซื้อชำระ`
- `ชำระแล้ว · ส่งภายใน 18 ก.ค. 18:00`
- `กำลังจัดส่ง`
- `พัสดุถึงแล้ว · จ่ายเงินวันที่ 27 ก.ค. 14:18 หากไม่มีปัญหา`
- `พักการจ่ายระหว่างตรวจสอบ`
- `โอนเงินแล้ว`

### Create agreement link

The page must state that it records an agreement already made elsewhere and does not publish a marketplace listing. Keep the main form short:

1. `รายการที่ตกลงซื้อขาย`.
2. `รูปแบบการส่งมอบ` — physical shipment or supported digital handoff.
3. `รายละเอียดข้อตกลง` — one combined field for included items/rights, condition, functionality, known defects/limitations, ownership-transfer details, and other material facts.
4. `รูปประกอบข้อตกลง` — capture or upload directly; never require users to paste a raw image URL.
5. Price, and shipping fee only for physical goods.
6. Fulfillment duration.
7. Saved payout account.
8. Possession/control, right-to-transfer, prohibited-goods, and terms confirmations.

Category and normalized condition are internal policy/snapshot fields. AI or source-link import may suggest them, but the seller must review all material text. Ask a focused follow-up only when policy classification cannot be completed safely. AI assistance is optional; uploading a photo must work without AI configuration.

Digital copy must explicitly say:

- No tracking or automatic time-based payout applies.
- The seller's “ส่งมอบแล้ว” action does not release money.
- The buyer must confirm receipt, otherwise payout remains blocked for authorized manual review.
- Never ask either party to paste passwords, recovery codes, private keys, or reusable credentials into TOKLONG.

Avoid marketplace language such as `ลงประกาศ`, `ลงสินค้า`, or copy that implies public product discovery. Prefer `สร้างลิงก์ข้อตกลง`, `รายการที่ตกลงซื้อขาย`, `รายละเอียดข้อตกลง`, and `รูปประกอบข้อตกลง`.

### Seller offer acceptance

Before login, the unguessable link may show the non-sensitive proposed transaction summary. Before acceptance, require authentication and show:

- Proposed product and amount.
- Expected seller net.
- Exact offer-expiry date/time.
- Exact shipping/handoff expectation.
- Payout trigger and dispute rule.
- Required seller additions or confirmations.

Primary action: `ยอมรับข้อเสนอ`. Secondary action: `ปฏิเสธ`. If material details are revised, label them clearly for the buyer's later review.

### Seller transaction detail

Primary action depends on state:

- Unpaid: `คัดลอกลิงก์`.
- Buyer-created offer awaiting seller: `ยอมรับข้อเสนอ`.
- Paid: `แจ้งส่งสินค้า`.
- Digital paid: `แจ้งว่าส่งมอบแล้ว`.
- Tracking issue: `แก้ไข Tracking`.
- Disputed: `ส่งหลักฐาน`.
- Payout processing: no primary destructive action; show expected status.

## Buyer application screens

### Create buyer offer

The page must explain that this is a private proposal for a seller already known from another channel, not a public request or marketplace bid. Keep it short:

1. `รายการที่ต้องการซื้อ`.
2. Physical shipment or supported digital handoff.
3. Proposed agreement description and any seller-provided photos already available.
4. Price and physical shipping fee.
5. Expected fulfillment duration.
6. Buyer email for receipts and PromptPay refund instructions.
7. Confirmation that the buyer will review the seller-confirmed final terms before payment.

Success copy:

> สร้างข้อเสนอแล้ว ส่งลิงก์นี้ให้ผู้ขายยืนยัน เมื่อผู้ขายยอมรับ เราจะแจ้งให้คุณตรวจรายละเอียดและชำระ

### Public transaction page

Must show before payment:

- Product photos.
- Product snapshot and known defects.
- Seller identity signals allowed by policy.
- Total amount and fee breakdown.
- Ship-by deadline.
- For digital agreements, show the handoff deadline and the no-auto-release rule instead of shipping/tracking terms.
- Release and dispute rule.
- Prohibited-item/report link.

### Checkout

- Delivery address.
- Delivery address is shown only for physical agreements.
- Contact verification.
- Terms acceptance.
- Approved provider checkout.
- No hidden fee added after the final review screen.
- For buyer-created offers, checkout is unavailable until the seller has accepted the final terms.
- PromptPay checkout requires an email that Stripe can use for refund instructions.

### Buyer order detail

Primary action depends on state:

- Paid before shipping: no action; show deadline.
- In transit: `ติดตามพัสดุ`.
- Delivered window: `ได้รับสินค้าแล้ว` plus visible `แจ้งปัญหา`.
- Disputed: `เพิ่มหลักฐาน`.
- Refunded/closed: download summary.

## Transaction details / terms

Do not create a separate “contract workflow.” Use a compact section called:

- `รายละเอียดรายการ`
- `ข้อตกลงของรายการ`
- `ประวัติและหลักฐาน`

Show:

- Product snapshot.
- Price and shipping.
- Buyer and seller identifiers appropriate to privacy policy.
- Terms version.
- Acceptance timestamps.
- Payment reference.
- Carrier and tracking.
- Delivery timestamp.
- Dispute deadline.
- Payout/refund status.

## Notification copy

### Buyer offer received — seller

> คุณได้รับข้อเสนอซื้อ ฿10,100 ตรวจรายละเอียดและตอบรับภายใน 24 ก.ค. เวลา 18:00 น. ผู้ซื้อจะชำระหลังคุณยอมรับ

### Seller accepted — buyer

> ผู้ขายยืนยันข้อเสนอแล้ว กรุณาตรวจรายละเอียดสุดท้ายและชำระภายใน 24 ก.ค. เวลา 18:30 น.

### Payment confirmed — seller

> ผู้ซื้อชำระแล้ว ส่งสินค้าได้ภายใน 18 ก.ค. เวลา 18:00 น. เพิ่มหมายเลขติดตามหลังจัดส่ง

### Tracking added — buyer

> ผู้ขายส่งสินค้าแล้ว ติดตามพัสดุ TH240719883 ได้จากรายการนี้

### Delivered — buyer

> ขนส่งแจ้งว่านำส่งสินค้าแล้ว กรุณาตรวจสินค้าและแจ้งปัญหาภายใน 27 ก.ค. เวลา 14:18 น.

### 24 hours before payout — buyer

> รายการนี้จะเข้าสู่การจ่ายเงินให้ผู้ขายในวันที่ 27 ก.ค. เวลา 14:18 น. หากสินค้ามีปัญหา กรุณาแจ้งก่อนเวลานี้

### Dispute opened — both

> การจ่ายเงินถูกพักไว้ระหว่างตรวจสอบ กรุณาส่งข้อมูลผ่านหน้ารายการนี้

### Payout confirmed — seller

> ผู้ให้บริการยืนยันการโอนเงิน ฿4,560 ให้คุณแล้ว

## Copy rules

Use:

- `ชำระผ่านพาร์ทเนอร์`
- `เงินรอการจ่ายตามเงื่อนไข`
- `ขนส่งยืนยันการนำส่ง`
- `พักการจ่ายระหว่างตรวจสอบ`
- `ผู้ซื้อพร้อมชำระเมื่อคุณยอมรับ`

Avoid until legally and operationally approved:

- `เราเก็บเงินไว้ให้`
- `เงินอยู่กับ TOKLONG`
- `escrow` or `เอสโครว์`
- `ปลอดภัย 100%`
- `รับประกันคืนเงินทุกกรณี`
