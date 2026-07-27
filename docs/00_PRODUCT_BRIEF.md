# 00 — Product Brief

## Product statement

TOKLONG is a buyer-first protected agreement-and-payment-link service for a buyer and seller who already found each other elsewhere. The buyer records a first and last name plus a payment-contact email once when registering, then later signs in with only the registered phone number and verification code before creating one private offer for a named item and the seller's Thai mobile number. The private link routes the intended seller into the app or Web, but the matching authenticated phone—not possession of the link—authorizes review and response. The seller confirms the final item facts, and only then may the buyer review and pay. It turns an informal social-commerce conversation into a seller-accepted purchase record, provider-confirmed payment, verified physical shipment or acknowledged digital handoff, and conditional seller payout. It is the transaction trust layer, not a product-listing or discovery marketplace.

## Core problem

People frequently buy and sell goods through Facebook groups, Facebook Marketplace, LINE, Messenger, Instagram, and community chats. Discovery and negotiation work well in those channels, but the final transaction is fragmented:

- The buyer is asked to transfer money directly and trust that the seller will ship.
- The seller may be shown a fake slip or ambiguous transfer status.
- Product details can be edited or disappear after payment.
- Shipment tracking, receipt confirmation, and evidence live in different chats.
- Both sides are uncertain about when money should be released or refunded.

TOKLONG should solve the **transaction trust layer**, not replace the communities where users discover goods.

## Primary value proposition

For the seller:

> TOKLONG ช่วยคนขายที่จริงใจขายให้คนแปลกหน้าได้ง่ายขึ้น

For the complete transaction:

> ตกลงกันในแชต แล้วจบดีลผ่านลิงก์เดียว

The parties use a compact private offer link that records what they already discussed. The buyer creates the complete proposal and invites the seller to accept or decline it without editing. The app must not present this as publishing a marketplace listing or bidding.

The seller must join, provide or confirm all material seller representations, and accept the final terms before the buyer is asked to pay. This follows the lower-refund pattern used by comparable international transaction-protection products and avoids charging PromptPay before a willing, eligible seller exists.

For the seller:

- Help a hesitant customer proceed without asking them to transfer directly.
- Avoid manual slip checking; fulfill only after provider-confirmed payment.
- Retain the agreed item record, timestamps, and verified tracking as evidence.
- Open a buyer-created offer, see the expected net payout and exact conditions, and accept without re-entering information that is already correct.
- For a physical item, reuse one saved shipping origin, enter the parcel weight
  and dimensions, compare an authoritative shipping quote, and lock the selected
  service before accepting.
- Fulfill only after payment is truly confirmed.
- Add one tracking number for physical goods, or record a digital handoff without exposing credentials, and see the exact payout condition.

For the buyer:

- Review a frozen description, condition, any supplied photos, item price,
  shipping charge, Buyer Protection fee, buyer total, and deadline before paying.
- Track physical shipment or review a digital handoff from the same transaction page.
- Confirm receipt/handoff or report a problem before payout.

## Primary personas

### Social seller

An individual or small reseller transferring a physical item or supported digital right through a group or chat. They want fast checkout and protection against fake payment evidence without a complex merchant flow for each agreement.

### Social buyer

A person buying from someone they do not fully know. They want clarity about what they are buying, proof that payment was received by a real provider, shipment tracking, a reasonable inspection period, and a simple problem-reporting path.

### Operations reviewer

An authorized human who reviews exceptions and disputes. They need a complete transaction snapshot, event timeline, payment and tracking references, user statements, and evidence without relying on AI-generated conclusions.

## MVP goals

The native mobile MVP follows the same buyer-first lifecycle as Web, including
seller invitation-link claiming, accept/decline, payout-account setup, multiple
purchase/sale records, native Stripe PaymentSheet, fulfillment, receipt
confirmation, and dispute initiation. Live OTP, payout, notification, carrier,
KYC, and beneficiary verification still require approved providers and
production configuration; code presence must not be presented as provider
approval.

1. A seller can authenticate, complete, and accept a valid buyer-created offer in under two minutes after onboarding.
2. A buyer specifies the complete offer and intended seller phone; for physical
   goods this includes selecting the complete delivery address, which is locked
   before the seller reviews the offer. Only the authenticated matching seller
   can confirm or decline it. Before accepting a physical offer, the seller
   supplies the shipping origin and parcel measurements and selects a quoted
   service. Acceptance reserves that exact provider shipment without charging
   it yet. Before paying, the buyer sees the same item, locked address, item
   price, shipping charge, Buyer Protection fee, buyer total, payout trigger,
   and dispute deadline.
3. A seller never receives a “ship now” signal from an unverified payment state.
   After payment confirmation, the system confirms the reserved shipment and
   gives the seller its carrier tracking number and printable label.
4. A trusted carrier-confirmed physical-delivery event starts a clearly displayed 72-hour inspection and payout-hold window; digital fulfillment never auto-releases from time alone.
5. A dispute reliably blocks payout.
6. Every important state is auditable and explainable to both parties and operations.
7. The interface feels like four stages, even though the backend has more detailed states.

## MVP non-goals

- Becoming an open marketplace.
- Providing a second product-listing workflow after the parties already agreed elsewhere.
- Replacing Facebook, LINE, Messenger, or community discovery.
- Supporting services, freelance projects, milestones, rentals, preorders, crypto, wallets, stored value, or unrestricted digital delivery.
- Providing social profiles, follower graphs, public ratings, or algorithmic recommendations.
- Holding customer funds directly in application-controlled accounts.
- Automating binding dispute judgments with AI.

## Supported transaction characteristics

- One fixed physical item/bundle or one allow-listed transferable digital item/right.
- Item/right already in the seller's possession or control, with a valid right to transfer.
- Physical: domestic shipment through a supported carrier with a shipping
  service selected before seller acceptance and machine-verifiable tracking
  events. For the production SHIPPOP path, booking confirmation, tracking, and
  label creation are managed by TOKLONG rather than typed by the seller.
- Digital: handoff outside secret-bearing transaction fields; payout requires buyer confirmation or authorized manual review and has no time-based auto-release.
- One payment and one payout.
- Amount denominated in THB.
- Fixed 72-hour physical inspection and payout-hold window for the initial launch, configurable only by authorized product configuration.

## Initial category policy

Good initial categories:

- Cameras and accessories.
- Sneakers and apparel with clear condition photos.
- Bags and fashion accessories.
- Collectibles.
- Consumer electronics below an approved risk/value threshold.
- Hobby equipment and household items.
- Transferable game accounts/items or digital licenses only where the seller attests transfer is permitted and the item is not stored value, crypto, a service, or a reusable secret.

Excluded until explicitly reviewed:

- Services and non-transferable digital access.
- Preorders and made-to-order goods.
- Perishables and temperature-sensitive goods.
- Weapons, drugs, regulated products, counterfeit goods, stolen goods, adult products, hazardous materials, wildlife contraband, and other prohibited categories.
- High-risk luxury goods without authenticity operations.
- Vehicles, real estate, or transactions requiring registration/transfer outside normal parcel delivery.
- Crypto, wallets, private keys, gift cards/stored value, financial accounts, identity documents, stolen/compromised accounts, and access obtained in violation of another platform's rules.

## Product principles

1. **Hide complexity, not conditions.** Users should not manage a contract workflow, but must see the material terms that affect payment and payout.
2. **Provider and carrier truth over user assertions.** Money and delivery states come from verified external events.
3. **One primary action per state.** The next safe action should be obvious.
4. **Exact deadlines.** Always show date, time, timezone, and what happens next.
5. **No silent auto-release.** Notify the buyer before payout and make “report a problem” easy to find.
6. **Immutable paid facts.** The item snapshot at checkout must remain available for later reference.
7. **Human accountability for disputes.** AI assists, humans decide binding outcomes.
8. **Acceptance before collection.** A buyer-created proposal is not payable until the authenticated seller has accepted the final material terms and completed the required eligibility checks.
9. **No indefinite waiting.** The seller has 24 hours to respond. After
   acceptance, the buyer has one hour to complete provider-confirmed payment.
   Both screens show the exact deadline, and an unpaid expired offer does not
   authorize fulfillment.

## Suggested headline and copy

Hero headline:

> ลูกค้าไม่กล้าโอน ก็ปิดการขายได้

Hero explanation:

> ให้ลูกค้าส่งข้อเสนอผ่าน TOKLONG คุณยืนยันรายละเอียด
> เห็นยอดที่ระบบยืนยันแล้วค่อยส่งของ
> มี Tracking และหลักฐานสำคัญอยู่ในรายการเดียวจนจบดีล

Primary action:

> สร้างข้อเสนอซื้อ
