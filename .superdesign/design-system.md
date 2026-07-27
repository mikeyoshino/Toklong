# TOKLONG mobile design system

## Product and UX

TOKLONG is a mobile-first transaction-trust product for a single physical shippable item/bundle or allow-listed transferable digital item/right. The Create Offer screen lets a buyer prepare an offer for a seller. It must remain simple, trustworthy, and explicit about price and fulfillment without claiming that TOKLONG itself is legal escrow.

One primary action per state. Consumer copy is plain Thai. Terms, fees, deadlines, dispute actions, and the exact payout trigger must be clear before payment. Advanced details are progressive disclosure.

## Visual language

- Typeface: Noto Sans Thai only.
- Page background: cool near-white `#FBFDFF`, with restrained pale-blue depth.
- Primary ink: `#101828`; secondary `#475467`; muted `#667085`.
- Primary brand: `#2B7FFF`; pressed/deep `#145FC7`; soft blue `#EEF7FF`.
- Dividers and input strokes: `#E4EAF1`; focused/amount stroke `#8DBEFF` to `#B8D9FF`.
- Danger: `#C52F4D`; success: `#087C68`.
- Surfaces: white, rounded, low/no shadow. Avoid gradients except extremely subtle background depth.
- Spacing: 4/8/12/20/28 scale. Mobile horizontal gutter 20px.
- Input height 52–58px; rounded corners 14–18px.
- Cards use 18–22px radius and 1px pale border.
- Primary buttons use brand blue, white 15–16px semibold text, 52–56px height, 16px radius.
- Type: title 28–30px bold; section 18px semibold; form label 14–16px medium (500); input 16–18px regular (400); helper/description 13px regular (400).
- Avoid synthesized bold on Thai form labels and descriptions. Reserve bold (700) for page titles, primary actions, totals, and the large monetary amount.
- Do not make field values bolder than select/dropdown values. Labels may be semibold; entered values stay regular except the large monetary amount.
- Focus must be visible through the field border only; never draw a focus rectangle around an H1/title.
- Dropdown/picker menus visually open below their field and match the field typography and borders.

## Create Offer reference direction

Use the supplied mobile reference as the layout target:

- A white rounded top header contains a pale-blue square back button, title `สร้างข้อเสนอ`, subtitle `ส่งให้ผู้ขายตรวจและตอบรับ`, and a two-step progress line.
- The form sits on a very light cool background.
- Seller phone and item name are simple large rounded fields.
- Item price is a borderless, backgroundless form section on the page surface with icon + label, then one prominent blue-bordered amount entry and helper copy. Do not nest the input inside another visible card.
- Delivery address is a rounded white summary card with a bold location summary and muted privacy/helper line.
- Existing TOKLONG logic remains: physical/digital switch, optional product photo, optional details, AI assistance, address editor, validation, and review before submission.
- Keep less common inputs below the essential fields using progressive disclosure, so the first viewport closely follows the reference without deleting behavior.
- Buyer protection cost details appear only after the buyer taps `ตรวจข้อมูลก่อนส่ง`, inside the review sheet. Do not show a sticky or standalone fee preview on the form.
- Do not show the old `กำหนดส่งสินค้า` card in the review sheet.

## Motion and accessibility

- Use short 150–220ms ease-out transitions for sheets, disclosures, and focus.
- Minimum touch target 44px.
- Preserve semantic labels and automation identifiers.
- Maintain readable contrast and Dynamic Type-friendly layout.
- The form and sheets must scroll safely above the soft keyboard.
