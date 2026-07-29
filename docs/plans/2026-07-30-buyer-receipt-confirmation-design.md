# Buyer Receipt Confirmation Card Design

Date: 2026-07-30  
Status: Approved direction A (`Clear & Calm`), pending implementation-plan review

## Problem

The current buyer confirmation card repeats the word `ตรวจ`, hides the exact
inspection deadline behind the vague phrase `ก่อนหมดเวลา`, and gives the
problem route similar visual weight without explaining when the payout process
can begin.

The redesign must make the safe next action obvious without encouraging a
premature confirmation or presenting the dispute form before the buyer asks
for it.

## Approved direction

Use one calm white surface card with:

1. A small blue-tinted package-check icon.
2. One concise heading.
3. One explanation of the consequence of confirmation.
4. A pale-blue exact-deadline notice.
5. One full-width blue primary action.
6. One neutral inline problem action beneath it.

Do not show the problem form until the buyer selects the problem action.

## Physical-item copy

- Heading: `ตรวจสินค้าให้เรียบร้อย`
- Supporting copy:
  `เช็กสินค้าและอุปกรณ์ให้ครบก่อนยืนยัน เมื่อยืนยันแล้ว ระบบจะเริ่มจ่ายเงินให้ผู้ขาย`
- Deadline:
  `แจ้งปัญหาได้ถึง {localized exact date and time}`
- Primary action: `ยืนยันว่าได้รับของเรียบร้อย`
- Secondary action: `พบปัญหากับรายการนี้`
- Expanded-form close action: `ปิดแบบฟอร์ม`

The deadline must come from the trusted carrier-delivery inspection window. It
must include an exact Thai-localized date and time and must never be calculated
from client time, payment time, shipment creation, polling time, or an
unverified delivery event.

## Digital-item copy

The same layout may be reused, but it must not mention a physical package,
accessories, or an automatic deadline:

- Heading: `ตรวจรายการที่ได้รับ`
- Supporting copy:
  `ตรวจรายการและการเข้าถึงให้เรียบร้อยก่อนยืนยัน เมื่อยืนยันแล้ว ระบบจะเริ่มจ่ายเงินให้ผู้ขาย`
- No automatic-payout deadline notice.
- Primary action: `ยืนยันว่าได้รับเรียบร้อย`
- Secondary action: `พบปัญหากับรายการนี้`

Digital elapsed time and seller assertion never create payout eligibility.

## Confirmation interaction

Tapping the primary button must not immediately complete the transition.
Present the existing final confirmation disclosure:

> คุณตรวจสินค้าแล้วและไม่พบปัญหา เมื่อยืนยัน ระบบจะเริ่มขั้นตอนจ่ายเงินให้ผู้ขาย

Actions:

- Primary: `ยืนยันและเริ่มจ่ายให้ผู้ขาย`
- Secondary: `กลับไปตรวจสินค้า`

Only an authenticated buyer may submit the confirmation. The domain transition
service remains authoritative, writes the immutable audit event, and evaluates
payout eligibility only when no dispute, refund, or hold exists.

## Problem interaction

`พบปัญหากับรายการนี้` is visually secondary, neutral in color, and remains
visible during the applicable inspection window. Selecting it expands the
existing problem form in place. The form is not rendered open by default and
does not replace the primary confirmation action.

Opening or closing the form does not alter transaction state. Submitting a
valid problem continues through the authorized dispute transition, which
blocks payout immediately.

## Visual specification

- Preserve the existing `SurfaceCard` geometry and buyer blue role color.
- Card padding: 18–20 device-independent pixels.
- Internal vertical spacing: 12–17 device-independent pixels.
- Icon container: 44 × 44, pale-blue background, minimum 2 px line icon.
- Heading: 20–21 px equivalent, semibold/bold.
- Supporting copy: 14 px equivalent with relaxed line height.
- Deadline notice: pale-blue background, clock icon, readable high-contrast
  graphite/blue text.
- Primary button: full width, minimum 48 px touch height, blue background,
  white text.
- Secondary action: full-width touch target with neutral text; no red warning
  styling before a problem has been selected.

Do not rely on color alone. The deadline notice includes a clock icon and the
problem action has an accessible semantic description.

## Accessibility

- Keep every interactive target at least 44 × 44 points.
- Expose the full deadline text to screen readers.
- Announce the expanded/collapsed state of the problem form.
- Preserve Dynamic Type without clipping the heading, deadline, or actions.
- Maintain WCAG AA contrast for body text and controls.
- Focus moves to the first problem-form field after expansion when supported.

## State coverage

The card is available only for:

- Physical: `DELIVERED_DISPUTE_WINDOW` backed by trusted delivery time.
- Digital: `DIGITAL_DELIVERY_SUBMITTED`.

It is hidden after buyer confirmation, dispute opening, refund/cancellation, or
any later payout state. A stale client submission must be rejected by the
server-side transition allow-list.

## Analytics and audit

- Existing buyer receipt confirmation audit/analytics events remain required.
- Expanding the problem form may emit a non-financial UI analytics event.
- Never log problem statements, private credentials, or reusable digital
  secrets in analytics.

## Acceptance checks

1. Physical card shows the trusted inspection deadline as an exact local date
   and time.
2. Digital card never shows an automatic deadline or implies time-based
   release.
3. Primary tap opens the confirmation disclosure before the state transition.
4. Cancelling the disclosure leaves state unchanged.
5. Confirming once records one transition and is idempotent on retry.
6. The problem form is collapsed initially and expands only from the secondary
   action.
7. Opening the problem form leaves state unchanged.
8. A submitted dispute blocks payout.
9. The changed layout passes accessibility and text-scaling checks.

## Non-goals

- No new dispute categories or evidence workflow.
- No change to payout, carrier, or digital-release rules.
- No change to the immutable paid agreement snapshot.
- No automatic confirmation from page viewing, delivery display, elapsed time,
  or client-side state.
