# 02 — UI, UX, and Content Specification

## Design direction

- Mobile first.
- White surfaces with pale-blue buyer accents and pale-purple seller accents.
- Soft radial spotlight in the hero.
- Rounded cards and restrained glass effects.
- Friendly Thai copy with precise money and deadline language.
- Product animation shown inside one mobile device, not a complex desktop dashboard.
- One primary action per transaction state.
- Keep buyer and seller transaction-detail screens visibly distinct: blue for
  `ซื้อ`, purple for `ขาย`, plus explicit role labels and role-specific status
  copy. Never rely on color alone.
- In the three-step progress card, use Connected Tokens: three `48 × 48`
  circular milestones joined by two rounded connectors. Completed buyer tokens,
  labels, and destination connectors use Buyer Blue (`#145FC7`) on
  `#EAF4FF`; completed seller tokens, labels, and destination connectors use
  Seller Purple (`#6548C7`) on `#F1ECFF`. Active-but-incomplete and future
  tokens remain neutral (`#98A2B3`, `#E4EAF1`, and white) because the main
  status card communicates the current action. Use the TOKLONG Rail Morph
  family with role-specific completed artwork and distinct physical/digital
  fulfillment glyphs. Do not show floating number/check badges, tap behavior,
  or progress animation.

## Mobile startup brand motion

The mobile identity is the TOKLONG Transaction Rail: two rounded rails approach
from opposite sides and form one compact transaction path. The Mint node is a
brand confirmation beat only; it never represents provider-confirmed payment,
refund, payout, delivery, or any transaction state.

On a normal cold launch, the in-app reveal plays once in exactly 1.2 seconds:

1. the separated rails begin their arrival over 250 ms;
2. the rails connect over 400 ms;
3. the Mint confirmation node pulses once over 200 ms; and
4. the TOKLONG wordmark enters over 350 ms.

Authentication lookup runs concurrently with this reveal. The native OS launch
screen remains static and uses the separated first frame so handoff into the
app does not imply a completed animation before the app can render. The intro
is a temporary root page, is not added to Shell history, and does not replay
when the app returns from the background.

When the platform requests Reduced Motion, show the completed Transaction Rail
and wordmark immediately with no animation-duration delay. Routing and
authentication behavior remain identical.

## Landing page information architecture

1. **Hero**
   - Seller-led positioning: help an honest seller close a deal with a new
     customer without checking slips manually.
   - Headline: `ลูกค้าไม่กล้าโอน ก็ปิดการขายได้`.
   - Explain the buyer-first truth: the buyer creates the offer, the seller
     confirms it, and fulfillment appears only after provider-confirmed payment.
   - Single primary CTA: `สร้างข้อเสนอซื้อ`.
   - Secondary CTA: jump to seller value; the header may separately play the
     mobile walkthrough.
   - Animated mobile UI with four scenes.
2. **Trust strip**
   - No manual slip checking.
   - Fulfill only after provider-confirmed payment.
   - Agreed details, timestamps, and tracking in one transaction.
3. **Seller value**
   - Help hesitant customers proceed.
   - Reduce payment and follow-up work.
   - Preserve evidence for both parties.
   - Clearly show why money is waiting and when payout can start.
4. **Seller and buyer simplicity**
   - Seller: accept a buyer offer, fulfill, receive payout.
   - Buyer: create an offer, review/pay after acceptance, track or review handoff, confirm/report.
5. **Four-step flow**
   - Buyer proposes / seller confirms → Pay → Fulfill → Confirm/payout.
6. **Behind-the-scenes record**
   - Explain that transaction terms are recorded without a visible contract-signing step.
7. **Focused use cases**
   - Used goods, community/group sales, private chat deals.
8. **Protection rules**
   - Exact start/end of the 72-hour physical inspection and payout-hold window.
   - Reminder before payout.
   - Dispute pause.
9. **FAQ**
   - Not a marketplace.
   - Payout timing.
   - Missed shipment deadline.
   - Item not as described.
   - No separate contract signing step.
   - Unsupported goods.
10. **Final CTA**

Do not present Trust Profile, verified payout-account name, video-before-pack,
shipping compensation, or automated bank payout as live capabilities until
their respective provider, operations, privacy, and implementation work is
complete. The complete seller positioning and roadmap are in
`docs/11_SELLER_VALUE_PROPOSITION_TH.md`.

## Hero mobile animation

The landing-page animation must be a compact representation of the current
`Toklong.Mobile` buyer-first flow, copy, role colors, and available actions. It
must not show an invented seller-first link flow or controls that the mobile app
does not provide. Use a physical-item example in the hero because payment,
shipping, tracking, and payout conditions communicate the end-to-end seller
value most clearly. The supported digital path remains documented and shown in
the static product content. The phone chrome must use the same centered Shell
navigation title and back affordance as the app; do not add a TOKLONG logo bar
or a role badge that does not exist on these screens.

The visual source of truth for each scene is:

1. `src/Toklong.Mobile/Pages/CreateOfferPage.xaml`
2. `src/Toklong.Mobile/Pages/SellerOfferPage.xaml`
3. `src/Toklong.Mobile/Pages/TransactionDetailPage.xaml` in the buyer payment state
4. `src/Toklong.Mobile/Pages/TransactionDetailPage.xaml` in the seller physical-fulfillment state

On the mobile home screen, `ต้องทำตอนนี้` shows only a transaction whose
current state requires an action from the signed-in user. It must never fall
back to an in-progress or completed transaction. When no action is required,
show the single-line empty state `ยังไม่มีรายการ`; keep completed transactions
in the status-filtered list below. The empty state reserves the same minimum
vertical footprint as the populated spotlight card so switching mode or
refreshing data does not move the filters, while still allowing both states to
grow for accessibility text sizes.

The animation may crop lower content to fit the device viewport, but it must
preserve the visible hierarchy, card shapes, field layout, button arrangement,
role colors, and current consumer copy from those XAML screens.

### Scene 1 — Buyer creates a targeted offer

Show:

- Shell title and heading `สร้างข้อเสนอ`.
- Show the current step name and `ขั้นที่ N จาก 3` with a three-segment
  progress indicator.
- Start directly with that heading without the redundant
  `ดีลซื้อขายส่วนตัว` badge. Keep the main form on the page background rather
  than inside one outer bordered card so mobile inputs retain the available
  width; individual input boundaries and section spacing remain visible.
- Do not add a separate blue informational card below the form repeating that
  the buyer reviews again before payment or that fulfillment is due in three
  days; those rules remain visible in the review and transaction states where
  they are actionable.
- Step `ข้อมูลดีล` contains seller phone, product name, item price, optional
  product photo, optional details, and the optional AI helper. Label the photo
  `รูปสินค้า (ไม่บังคับ)`.
- Step `การรับสินค้า` contains the physical/digital selection and the
  applicable delivery address. Physical remains the default.
- Under the physical item-price field, say that shipping will be calculated
  from the seller's origin and parcel size before the buyer pays. Do not ask the
  buyer to guess or include shipping in the item price.
- Keep description and included items behind one optional-details disclosure.
  AI remains independent from this disclosure and is never required to finish
  the ordinary form.
- Step `ตรวจและส่ง` is a full page, not a sheet or contract-signing step. It
  shows the entered summary and server-priced cost preview, asks for condition
  with three compact choices, reveals a defect description only when
  `มีตำหนิ` is selected, and contains the only create action
  `ส่งข้อเสนอให้ผู้ขาย`.
- When no optional description is supplied, the transaction record uses the
  product name as its explicit description. `ใหม่` and `มือสอง สภาพดี` record
  `ไม่มีตำหนิที่ผู้ซื้อระบุ`; `มีตำหนิ` requires explicit defect text. This
  keeps the transaction record complete without showing redundant fields in
  the quick form.
- The system-fixed physical fulfillment rule, not an editable shipping-duration
  field.
- One secondary action `ให้ AI ช่วยกรอก` below the heading. It opens a
  bottom-sheet overlay only on demand, accepts one image/chat screenshot or
  pasted chat text, previews the extracted draft, and fills only blank fields
  after the buyer confirms. Its icon is the TOKLONG Transaction Rail inside
  scan corners with the Mint confirmation node; do not use a generic sparkle
  or marketplace icon. The source image is not a product-evidence photo.
- Plain-language notice that only the account verified with the specified
  seller phone can respond.

The AI helper must remain optional and collapsed by default. It must say that
the result is a draft requiring buyer review and must not request OTP,
passwords, card details, bank details, or reusable credentials. An AI source
image is never promoted to an optional product-evidence photo without the
buyer's separate selection. Existing buyer-entered values are never overwritten
by applying an AI draft.

### Scene 2 — Seller prepares the sale

Show:

- Shell title and page heading `เตรียมขาย`.
- Use the current neutral white/blue `SellerOfferPage` presentation. Do not
  apply the purple transaction-detail theme to this screen.
- Supporting copy `ตรวจรายละเอียดและเตรียมข้อมูลให้ครบก่อนเปิดให้ผู้ซื้อชำระ`.
- The same offer as read-only, its exact response deadline, item price,
  destination province/postal code, fixed fulfillment rule, applicable
  shipping charge, and expected seller net. Do not show the Buyer Protection
  amount, parcel-protection choice/price/limit, provider option, or buyer total
  to the seller.
- For a physical offer, one `เตรียมการจัดส่ง` section before readiness confirmation:
  saved-origin summary or complete origin editor, `จำต้นทางนี้ไว้`, parcel
  weight and width/length/height, `ดูค่าจัดส่ง`, selectable quote rows, and the
  item-price/shipping breakdown. Changing origin or measurements clears the
  selected quote.
- For a digital offer, show `เตรียมส่งมอบไอดีเกม`, keep all shipping inputs
  absent, and warn never to enter passwords, OTPs, recovery codes, login QR
  data, or other reusable secrets in TOKLONG.
- Primary action `ยืนยันพร้อมขาย`. This user action records the existing
  seller acceptance evidence and moves the unchanged internal state to
  `SELLER_ACCEPTED_AWAITING_PAYMENT`.
- Secondary action `ปฏิเสธรายการ`, placed below the full-width primary action
  as in the app.
- If anything is incorrect, instruct the seller to reject it and ask the buyer
  to create a new offer. Do not imply the seller can edit buyer-entered terms.

### Scene 3 — Buyer reviews and pays

Show:

- Shell title `รายการซื้อ` and the current blue buyer transaction header.
- The seller-ready product snapshot and a clear statement that paid details
  cannot be edited.
- Allowed seller identity signals, the amount breakdown, and the exact payment
  deadline.
- Show the complete locked delivery address once in the agreement details.
- Before payment preparation, obtain the buyer-only parcel-protection outcome.
  When an add-on is available and the item exceeds the verified included limit,
  show one accessible checkout row with `เพิ่มความคุ้มครองพัสดุ` on the left
  and a switch on the right. The switch is off when no paid add-on is saved.
  Turning it on, or turning off an already-saved add-on, opens a confirmation
  modal with explicit `ตกลง` and `ยกเลิก` actions. The add-on confirmation
  discloses its maximum and one combined price. Cancel restores the last
  server-saved choice and total without submitting a change. Do not show a
  provider name, internal cost split, package label, or coverage gap. Included
  coverage skips this control and adds no charge; unavailable add-on status
  lets the buyer continue only with the verified included-coverage outcome,
  which may be zero, without claiming coverage beyond that verified amount.
  Once checkout has started, keep the applicable row visible but disable the
  switch and state that the saved choice can no longer be changed.
- Show the exact item price, Buyer Protection fee, shipping charge, final
  `ค่าความคุ้มครองพัสดุ` and total in one buyer-only breakdown only when the
  buyer accepted a paid add-on. Omit that row for declined, unavailable,
  included-within-limit, and no-add-on outcomes. The accepted combined price
  remains visible in this summary; the maximum is not repeated outside the
  choice or details surface. Place the passive consent sentence and exact-total
  payment button directly below it; do not add a standalone buyer acceptance
  checkbox.
- Plain-language payout condition.
- Primary action `ยืนยันและชำระ <ยอดทั้งหมด>`. Activating this authenticated
  action records buyer acceptance and starts provider checkout together.

### Scene 4 — Seller fulfills and tracks payout

Show:

- Shell title `รายการขาย` and the current purple seller transaction header.
- Provider-confirmed status `ส่งสินค้าได้` plus the guidance
  `ผู้ซื้อจ่ายแล้ว เปิดใบปะหน้าและส่งกับขนส่งที่เลือกไว้`; never infer
  payment from a slip or client redirect.
- Exact ship-by date and time.
- Read-only selected carrier and the tracking number issued through the
  selected certified delivery service/provider.
- Primary action `เปิดใบปะหน้า`.
- The transaction detail shows one compact 4×6 label preview. Tapping either
  the preview or `แตะเพื่อดูใบปะหน้าเต็มจอ` opens a dedicated seller-only
  viewer. The viewer keeps the screen awake, allows pinch zoom, blocks scripts
  and top-level navigation from provider HTML, and provides
  `บันทึกลงเครื่อง` plus `แชร์หรือพิมพ์` through the native file/share sheet.
- Scan-from-screen guidance is conditional: say
  `หากจุดบริการรองรับการสแกนจากหน้าจอ`. Do not copy a marketplace counter QR
  or claim drop-off/pickup behavior unless the selected provider service
  explicitly supplies that capability.
- No manual carrier/tracking form for a provider-managed shipment.
- Next status before the first carrier scan `นำพัสดุส่งภายใน [exact time]`;
  after the scan, `ขนส่งรับพัสดุแล้ว`.
- Plain-language payout condition. A presentation-only overlay may show
  `ครบเงื่อนไขการจ่ายแล้ว` and
  `ระบบเริ่มจ่ายเงินให้ผู้ขายผ่านพาร์ทเนอร์`; it must not claim payout
  completion before provider confirmation.

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

- Incoming buyer-created offers grouped separately from funded sales.
- Active sales grouped by state.
- Clear amount and next action.

Suggested state cards:

- `มีรายการรอเตรียมขาย`
- `ผู้ขายพร้อมขายแล้ว · รอผู้ซื้อชำระ`
- `รอผู้ซื้อจ่ายถึง 18:00`
- `ผู้ซื้อไม่ได้จ่าย · รายการปิดแล้ว`
- `จ่ายแล้ว · ส่งภายใน 18 ก.ค. 18:00`
- `กำลังจัดส่ง`
- `ขนส่งรับพัสดุทันเวลาแล้ว · กำลังตรวจปัญหาการนำส่ง`
- `พัสดุถึงแล้ว · จ่ายเงินวันที่ 27 ก.ค. 14:18 หากไม่มีปัญหา`
- `หยุดจ่ายเงินชั่วคราว`
- `โอนเงินแล้ว`

### Seller prepare-sale screen

The unguessable invitation requires seller phone authentication. Show:

- Proposed product and item price.
- For physical goods, seller origin, parcel weight/dimensions, selected carrier
  service, shipping charge, and expected seller net.
- Do not show Buyer Protection or parcel-protection values, coverage limits,
  provider option details, or buyer total to the seller. For
  `buyer-protection-v2`, seller platform fee is zero and exact seller net is the
  item price; buyer-paid shipping and buyer-only protection charges are not seller
  proceeds.
- Exact shipping/handoff expectation.
- Payout trigger and dispute rule.
- A read-only offer record and required seller confirmations.

Show the buyer-specified material description, condition, item price, and any
supplied managed product photo as read-only; the selected shipping charge and
expected seller net are also read-only.
When no photo was supplied, use
the normal item-type placeholder without implying an error. Show the
system-fixed rule `ส่งภายใน 3 วันหลังยืนยันยอดชำระ`; neither party can edit it.
Require an owned payout account, transfer-rights/prohibited-goods attestation,
and seller terms. Primary action: `ยืนยันพร้อมขาย`. Secondary action:
`ปฏิเสธรายการ`. If anything is incorrect, instruct the seller to decline and ask the
buyer to create a new offer.

`ยืนยันพร้อมขาย` combines consumer-facing readiness and acceptance; it does
not remove seller consent evidence, the agreement hash, authorization, audit,
or the internal acceptance transition. The seller still sees no fulfillment
action until provider-confirmed payment.

The origin selector mirrors the buyer's saved-address behavior but stores only
one seller origin: use it by default, allow `เปลี่ยนต้นทาง`, and replace it only
after explicit `จำต้นทางนี้ไว้`. Parcel measurements remain transaction
specific and are not silently copied into a later offer.

Digital copy must explicitly say there is no tracking or time-based automatic payout, seller assertion does not release money, and secrets must be sent only through the agreed external channel.

### Seller transaction detail

Primary action depends on state:

- Buyer-created offer awaiting seller: `เตรียมขาย`.
- Paid: `แจ้งส่งสินค้า`.
- Digital paid: `แจ้งว่าส่งมอบแล้ว`.
- Tracking issue: `แก้ไข Tracking`.
- Provider-managed carrier exception after a timely trusted scan: `ดูสถานะ`;
  explain that the seller handoff was confirmed, payout remains paused, and
  Seller Protection does not itself promise payout completion.
- Disputed: `ส่งหลักฐาน`.
- Payout processing: no primary destructive action; show expected status.

## Buyer application screens

### Create buyer offer

An unauthenticated app launch first shows the approved centered TOKLONG mark,
the headline `ซื้อขายออนไลน์ ง่ายขึ้น`, and two unambiguous actions:
`เข้าสู่ระบบ` and `สมัครสมาชิก`. The welcome screen does not use shield,
truck, payment-status, country, or currency artwork. It preserves the existing
blue-to-purple brand tile and exact mark and must not advertise social sign-in
that is not implemented.

All authentication phone fields are local Thai-mobile fields labelled
`เบอร์มือถือไทย`. They use the thin modern smartphone icon and the helper
`กรอกเบอร์ 10 หลัก เช่น 081-234-5678`. Do not show `+66`, a country flag,
country picker, email-login framing, or an old handset icon. Returning buyers
see `เข้าสู่ระบบด้วยเบอร์มือถือ` and the primary action
`ส่งรหัสทาง SMS`.

First-time registration is three steps:

1. enter only the Thai mobile number and request the SMS code;
2. verify the same six-digit experience used by sign-in;
3. after verified proof, enter first and last name and the required
   receipt/refund contact email, review tappable Terms and Privacy links, and
   press `สร้างบัญชีและเริ่มใช้งาน`.

The final sentence immediately before that button states that pressing it
records acceptance; no separate checkbox is added. Email is not a login
identifier and is not requested or overwritten during later sign-in.

Both paths continue to a `รหัสยืนยัน 6 หลัก` screen. The six digits appear over
six underlines but are backed by one numeric input so paste, deletion,
accessibility, and iOS one-time-code AutoFill continue to work. OTP remains an
internal technical term and is not used in normal consumer copy. After
authentication, the create-offer page must explain that this is a private
proposal for a seller already known from another channel, not a public request
or marketplace bid. Keep it short:

The server-side proof for a new registration expires after 15 minutes and is
bound to the app installation. The app resumes a still-valid profile-completion
step after backgrounding or restart. Startup priority is: valid authenticated
session to the authenticated role home; otherwise valid pending registration to
profile completion; otherwise Welcome. The unsigned DEBUG iOS simulator uses
in-memory authentication storage, so cold-process resume must be verified on a
signed physical build. Consumer screens and logs must not expose internal
registration tokens, hashes, idempotency values, or one-time codes.

Phone fields accept only ASCII digits, stop at 10 digits, and display the value
as `092-103-1202` while the user types. The separators are visual formatting and
are removed before the API request. The number must begin with `06`, `08`, or
`09`. Invalid input must be rejected before requesting a verification code, and
the API must independently enforce the same rule.

The verification screen provides `ขอรหัสใหม่`. A rejected one-time code must
say that it may be incorrect, already used, or expired and direct the user to
request another code instead of presenting expiration as the only cause.
If the user requests another code during the resend cooldown, show the actual
remaining wait in seconds. Do not describe a normal cooldown as a service
outage.

### Account name change

The blue profile card on `บัญชี` always shows `แก้ไข` beside the current name.
It never proactively displays the last change, remaining first-change
entitlement, or next allowed date. Selecting `แก้ไข` asks the server. When
blocked, remain on the current page and show:

- title `ยังเปลี่ยนชื่อไม่ได้`;
- `เพื่อความปลอดภัย ชื่อบัญชีเปลี่ยนได้ทุก 2 เดือน`;
- `คุณจะเปลี่ยนได้อีกครั้งวันที่ {exact Bangkok date and time}`; and
- one action, `เข้าใจแล้ว`.

An eligible flow has two mobile-first pages. Step 1 shows separate required
`ชื่อ` and `นามสกุล` fields, prefilled from the current profile, and one primary
action `ส่งรหัสยืนยัน`. Step 2 shows the masked existing phone, pending name,
and the existing `OtpVerificationFormView`; it must not add another
`OtpCodeInput`. Resend, expiry, incorrect-attempt, paste, deletion, VoiceOver,
and iOS one-time-code behavior remain shared. Cooldown and daily-send-limit
outcomes use action modals rather than proactive account copy. Success returns
to `บัญชี`, refreshes the authoritative profile, and says
`เปลี่ยนชื่อเรียบร้อยแล้ว ชื่อใหม่จะใช้กับรายการใหม่` once.

Sign-out or account switching immediately clears names, masked phone, pending
challenge presentation, code, timers, errors, and route payloads. Navigation
payloads are session-generation bound so a page created after a reset reloads
the new account instead of rendering the previous account's data.

### Authenticated root navigation

The native bottom bar contains `ซื้อ`, `ขาย`, and `บัญชี` in that order.
`กิจกรรม` is a top-right action on all three roots and opens as a pushed page
with Back navigation. The transaction roots do not render another `ซื้อ | ขาย`
switch.

First authenticated use opens `ซื้อ`. Later ordinary launches restore the last
explicitly selected Buy/Sell root. `บัญชี` does not replace that preference,
explicit logout clears it, and deep links take precedence without overwriting it.

### Three-step buyer offer creation

1. `ข้อมูลดีล`: required intended-seller Thai phone, product name, agreed item
   price, and optional AI/photo/details.
2. `การรับสินค้า`: physical/digital choice and the applicable delivery
   address. Physical is the default. A saved address is compact with one
   `เปลี่ยน` action; Digital hides address and shipping content.
3. `ตรวจและส่ง`: masked seller phone, offer summary, condition, conditional
   defect text, server cost preview, and the only create action
   `ส่งข้อเสนอให้ผู้ขาย`.

The three steps live in one `CreateOfferViewModel`. Current-step validation is
inline and moves focus to the first invalid field. Going backward preserves
values. Editing price or fulfillment invalidates an older preview. A preview,
AI draft, or step transition creates no transaction, snapshot, notification,
payment, or audit event.

The wizard keeps values only while the page is open. A dirty exit says
`ยังสร้างข้อเสนอไม่เสร็จ`,
`ถ้าออกตอนนี้ ข้อมูลที่กรอกไว้จะหาย`,
`กลับไปกรอกต่อ`, and `ออกจากหน้านี้`.
No draft is persisted locally or on the server.

The physical page explains that the seller sees only province and postal code
before provider-confirmed payment, selects an authoritative supported shipping
service later, and that the buyer must not include shipping in the item price.
Do not render a buyer-editable fulfillment duration.

Success copy:

> ส่งข้อเสนอให้ผู้ขายแล้ว ระบบจะแจ้งบัญชีที่ยืนยันด้วยเบอร์นี้ เมื่อผู้ขายยอมรับ เราจะแจ้งให้คุณตรวจรายละเอียดและชำระ

The in-app and push notification for the initial invitation uses:

- Title: `ได้รับข้อเสนอซื้อ`
- Body: `[ชื่อสินค้า] · ฿[ราคาสินค้า]` before a shipping quote exists
- Tap destination: the intended seller offer.

Never put full addresses, phone numbers, payout details, or other sensitive
transaction evidence in an OS notification.

The wait page shows `ผู้ขายตอบได้ถึง [exact date/time]` and
`ยังไม่มีการเก็บเงิน`. It may show an optional invitation URL with
`คัดลอกลิงก์` and `แชร์ให้ผู้ขาย`, while stating that the system already
notified the phone-targeted seller. The URL is only a delivery channel;
authorization still comes from the verified seller phone. The transaction-list
root does not expose a generic clipboard-open action.
After seller acceptance, buyer copy says
`จ่ายภายใน [exact date/time] ไม่เช่นนั้นรายการจะปิด`.

### Buy and Sell workspaces

The `ซื้อ` and `ขาย` roots are independent workspaces and never mix roles in
an `ทั้งหมด` view. Notification and deep-link navigation still opens the exact
authorized transaction directly. Each root starts with its role title and the
global Activity action; there is no second role chooser.

- `ซื้อ`: buyer-only spotlight/list, `+ สร้างดีลซื้อ`, and buyer status
  filters.
- `ขาย`: seller-only spotlight/list with `ต้องตอบ`, `ต้องส่ง`, `รอรับเงิน`,
  and `เสร็จแล้ว` filters. It has no create or clipboard-link action.
- Buyer Blue and Seller Graphite/Navy support the visible `ซื้อ` and `ขาย`
  labels; role is never communicated by color alone.
- Within every buyer or seller filter, order transactions by creation time from
  newest to oldest. Status bucket and action deadline must not move an older
  transaction above a newer one.

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

### Physical address at offer creation

- A physical offer requires province, district, and sub-district before it can
  be sent to the seller. The seller review shows the resolved destination
  province and postal code before `ยืนยันพร้อมขาย`; it never shows the full
  street address at this stage.
- Delivery address is shown only for physical agreements and separates address line, province, district, and sub-district.
- Province, district, sub-district, and postal-code options come from the versioned dataset bundled with the server application and loaded once at startup; do not make the buyer's browser download the nationwide dataset.
- The buyer may check `จำที่อยู่นี้ไว้`. A buyer profile has at most one saved address; a later save replaces it.
- When a saved address exists, select `ใช้ที่อยู่ที่บันทึกไว้` by default and allow the buyer to switch to an editor.
- The full address is locked when the offer is created. Changing it requires a
  new offer.

### Checkout

- For a physical agreement, show the complete locked address exactly once for
  review. The payment action must not repeat it.
- Show separate rows for `ราคาสินค้า`, `ค่าคุ้มครองผู้ซื้อ`, `ค่าจัดส่ง`, and
  `ยอดชำระทั้งหมด`, plus the selected carrier service. Insert
  `ค่าความคุ้มครองพัสดุ` only for an accepted paid add-on; it is the buyer's
  final combined price. Omit that row for declined, unavailable,
  included-within-limit, and no-add-on outcomes. The payment intent amount
  equals the displayed total.
- Do not show an address editor, saved-address selector, or
  `จำที่อยู่นี้ไว้` control in checkout.
- The seller may see the complete address only after provider-confirmed payment
  unlocks fulfillment. Before that, seller surfaces show province and postal
  code only.
- Contact verification.
- Passive consent copy immediately above the payment action:
  `เมื่อกดชำระ คุณยืนยันว่าได้ตรวจรายละเอียดและยอมรับข้อตกลงแล้ว`.
- Approved provider checkout.
- No hidden fee added after the final review screen.
- For buyer-created offers, checkout is unavailable until the seller has
  confirmed readiness, which preserves the existing seller acceptance record.
- For physical offers, PaymentIntent creation is also unavailable until the
  buyer election is recorded and the payment action has synchronously committed
  the exact unconfirmed booking. While that request is active, show
  `กำลังเตรียมการจัดส่ง…`; do not poll a background booking worker. A retryable
  failure says `เตรียมการจัดส่งไม่สำเร็จ` and
  `ยังไม่มีการชำระเงิน กรุณาลองอีกครั้ง`. Retrying does not write a second
  election. A changed price, limit, expiry, or terms
  requires fresh buyer confirmation, while the original payment deadline stays
  unchanged.
- The final buyer action is `ยืนยันและชำระ <ยอดทั้งหมด>`. It records electronic
  acceptance of the same agreement-core hash already accepted by the seller;
  do not show a separate acceptance checkbox and do not label it as a
  certificate-backed digital signature.
- PromptPay checkout uses the email already saved on the authenticated buyer
  profile for receipts and refund instructions.
- Checkout has no editable email field. Existing pre-migration accounts without
  an email must add one from the account screen before payment.
- Evidence remains retained and available through the authenticated export
  capability, but it is not rendered as a card in the normal transaction
  screen. A future dedicated export/support surface may expose the download;
  the everyday screen never renders a raw hash or describes schemas,
  signatures, or internal verification.

### Buyer order detail

Primary action depends on state:

- Paid before shipping: no action; show deadline.
- In transit: `ติดตามพัสดุ`.
- Delivered physical window: show `ตรวจสินค้าให้เรียบร้อย`, the exact trusted
  inspection deadline, primary action `ยืนยันว่าได้รับของเรียบร้อย`, and
  neutral secondary action `พบปัญหากับรายการนี้`.
- Digital handoff submitted: show `ตรวจรายการที่ได้รับ`, no automatic deadline,
  primary action `ยืนยันว่าได้รับเรียบร้อย`, and neutral secondary action
  `พบปัญหากับรายการนี้`.
- Disputed: `เพิ่มหลักฐาน`.
- Refunded/closed: download summary.

The problem form is collapsed initially and expands only after the secondary
action. Before accepting the primary action, show the fulfillment-specific
confirmation disclosure that confirmation can begin seller payout.

For physical fulfillment, show:

> คุณตรวจสินค้าแล้วและไม่พบปัญหา เมื่อยืนยัน ระบบจะเริ่มขั้นตอนจ่ายเงินให้ผู้ขาย

The final physical confirmation actions remain `ยืนยันและเริ่มจ่ายให้ผู้ขาย`
and `กลับไปตรวจสินค้า`. Do not use a bare `ได้รับสินค้าแล้ว` as the release
action, because receipt alone does not clearly mean the buyer has inspected
and accepted the item.

### Provider-managed shipping status

Keep the role-specific three-step transaction progress unchanged. Inside the
transaction detail, show a separate shipping card with four connected
milestones:

1. `เตรียมจัดส่ง` — provider booking is confirmed and the label is available;
   no carrier scan exists.
2. `ขนส่งรับพัสดุแล้ว` — the first trusted matching carrier scan exists.
3. `กำลังจัดส่ง` — trusted in-transit events exist.
4. `ส่งถึงแล้ว` — the selected certified delivery service/provider reports
   completion with a trusted carrier delivery timestamp.

The card includes carrier and appropriate tracking presentation plus a
collapsible `รายละเอียดการเดินทาง` list. Each list item uses a normalized
consumer description, location when supplied, and exact local date/time. Do
not show raw provider status values, provider identifiers, reconciliation
terminology, or poll timestamps as if they were carrier event times.

When completion lacks a trusted delivery timestamp or any provider
problem/invalid/return/mismatch occurs, replace positive completion treatment
with `การจัดส่งต้องตรวจสอบ`, explain that seller payout is paused, and provide
one primary action `ดูรายละเอียด`. Never show `สำเร็จ` merely because a parcel
was delivered; the transaction still has inspection, dispute, and payout
stages.

Before payment, show a buyer-only `ค่าความคุ้มครองพัสดุ` row only when the
buyer accepted a paid add-on. Never show its maximum, internal split, provider
identity, or option reference in the payment summary, and never expose any of
these values to the seller.

## Transaction details / terms

Do not create a separate “contract workflow.” Seller detail keeps one
single-line `รายละเอียดสินค้า` accordion collapsed until requested, while
status, amount, deadline, and the current action remain visible. Its title row
must stay the same visual height as its icon; do not add helper copy below the
title.

Show:

- Optional description only when it adds information beyond the product name.
- Condition in consumer Thai.
- Defect text only when the item was declared as having a defect.
- Seller fee and expected net.
- Consumer fulfillment method and the applicable delivery destination.

Never render raw hashes, schema versions, webhook/provider state, internal
identifiers, terms-version codes, acceptance audit rows, or certificate
terminology in the normal consumer view. Preserve those values server-side for
an authenticated evidence export when required.

## Notification copy

### Buyer offer received — seller

Title:

> ได้รับข้อเสนอซื้อ

Body:

> กล้อง Fujifilm X-T30 II · ฿10,100

The in-app detail may explain that the buyer pays only after seller acceptance;
keep the lock-screen notification compact and free of sensitive data.

### Seller ready — buyer

Title: `ผู้ขายพร้อมขายแล้ว`

Body:

> [ชื่อสินค้า] · ตรวจยอดและชำระภายใน [exact date/time]

### Seller response expired — buyer

> ผู้ขายไม่ได้ตอบภายในเวลาที่กำหนด ไม่มีการเก็บเงินจากรายการนี้

### Buyer payment expired — both

Buyer:

> หมดเวลาชำระ หากยังต้องการซื้อ ให้ส่งข้อเสนอใหม่

Seller:

> ผู้ซื้อไม่ได้จ่ายภายในเวลา คุณไม่ต้องจองหรือส่งสินค้าให้รายการนี้

### Payment confirmed — seller

> ผู้ซื้อชำระแล้ว เปิดใบปะหน้าและส่งพัสดุภายใน 18 ก.ค. เวลา 18:00 น.

### Tracking added — buyer

> ผู้ขายส่งสินค้าแล้ว ติดตามพัสดุ TH240719883 ได้จากรายการนี้

### Delivered — buyer

> ขนส่งแจ้งว่านำส่งสินค้าแล้ว กรุณาตรวจสินค้าและแจ้งปัญหาภายใน 23 ก.ค. เวลา 14:18 น.

### 24 hours before payout — buyer

> รายการนี้จะเข้าสู่การจ่ายเงินให้ผู้ขายในวันที่ 23 ก.ค. เวลา 14:18 น. หากสินค้ามีปัญหา กรุณาแจ้งก่อนเวลานี้

### Dispute opened — both

> การจ่ายเงินถูกพักไว้ระหว่างตรวจสอบ กรุณาส่งข้อมูลผ่านหน้ารายการนี้

### Payout confirmed — seller

> ผู้ให้บริการยืนยันการโอนเงิน ฿4,560 ให้คุณแล้ว

## Copy rules

Use:

- `จ่ายผ่านพาร์ทเนอร์`
- `เงินจะจ่ายเมื่อครบเงื่อนไข`
- `บริษัทขนส่งยืนยันว่าส่งถึงแล้ว`
- `หยุดจ่ายเงินชั่วคราว`
- `เมื่อคุณตกลง ผู้ซื้อจะจ่ายเงินได้`

Avoid until legally and operationally approved:

- `เราเก็บเงินไว้ให้`
- `เงินอยู่กับ TOKLONG`
- `escrow` or `เอสโครว์`
- `ปลอดภัย 100%`
- `รับประกันคืนเงินทุกกรณี`
