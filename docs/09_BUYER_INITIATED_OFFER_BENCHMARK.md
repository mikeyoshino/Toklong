# 09 — Buyer-Initiated Offer Benchmark and Recommended Flow

> Research snapshot: 2026-07-23. This is a product benchmark, not a legal conclusion or permission to copy another service's regulated fund flow.

## Decision summary

TOKLONG MVP uses one private buyer-first initiation path. Use the Trustap-style ordering:

```text
buyer creates proposed transaction
→ seller joins, completes seller facts, and accepts
→ buyer reviews the unchanged seller-accepted terms and pays
→ verified payment enables fulfillment
→ tracked delivery starts the inspection window
→ buyer confirms early or reports a problem
→ no problem at the deadline makes payout eligible
```

Do not collect PromptPay before seller acceptance in the normal MVP. Stripe PromptPay has no manual capture in the current flow, and refunding an offer that the seller never accepts requires the buyer to complete refund-account instructions.

## International comparison

| Product | Transaction/offer commitment | Fulfillment evidence | Inspection period | Release behavior |
|---|---|---|---|---|
| Trustap | Buyer or seller can create a standalone transaction and share a link. In the buyer-created courier flow, the seller joins before the buyer pays. | Registered post/courier plus tracking; seller has 96 hours to ship in the documented flow. | 24 hours after delivery confirmation. | Automatic release if no complaint. |
| Vinted | Seller listing exists before buyer payment. | Platform shipping/delivery status. | 2 days after marked delivered. | Buyer presses `Everything is OK` or payment releases automatically. |
| Wallapop | In the documented offer flow, seller accepts the offer before the buyer has 24 hours to purchase. | Carrier-confirmed delivery. | 48 hours. | Buyer confirms or the seller receives funds after the deadline; disputes pause release. |
| Mercari | Seller has 24 hours to accept/decline/counter; buyer is charged when an offer is accepted. | Valid tracking and carrier-confirmed delivery. | 72 hours. | Buyer confirms/rates or the transaction auto-completes; an issue pauses release. |

## Common product pattern

The relevant products consistently separate these events:

1. Agreement or seller acceptance.
2. Confirmed buyer payment.
3. Seller fulfillment.
4. Trusted delivery.
5. Buyer inspection.
6. Payout release.

They also make early buyer confirmation the seller's fastest path to payment, while a short automatic deadline prevents an inactive buyer from blocking the seller forever.

## Recommended TOKLONG UX

### Buyer creates

Require buyer phone sign-in plus first/last name and a payment-contact email at
registration, then collect fulfillment type, an optional managed photo,
agreement details from the external conversation, one agreed total, and
expected fulfillment time. Checkout reuses the account email without another
field.

Success copy:

> ส่งข้อเสนอให้ผู้ขายแล้ว ระบบจะแจ้งบัญชีที่ยืนยันด้วยเบอร์นี้ เมื่อผู้ขายยอมรับ เราจะแจ้งให้คุณตรวจรายละเอียดและชำระ

### Seller accepts

Show before acceptance:

- Proposed item and amount.
- Proposed total, plus exact expected net before real payment once the fee policy is approved.
- Offer-expiry date/time only after the open deadline policy is approved and implemented.
- Fulfillment deadline.
- Release and dispute conditions.
- Optional photos plus required condition/defect facts,
  possession/right-to-transfer, prohibited-goods, identity, and bank-account
  steps.

Copy:

> เมื่อคุณตกลง ผู้ซื้อจะจ่ายเงินได้

Do not say `ผู้ซื้อชำระแล้ว` until Stripe confirms payment through a verified event.

### Buyer pays

After seller acceptance, show every unchanged buyer-specified material term.
Require buyer acceptance of that record and use the authenticated buyer profile
email for Stripe PromptPay receipts and refund instructions.

### Seller fulfills and gets paid

Only provider-confirmed payment exposes the physical tracking or digital handoff action. Trusted carrier delivery starts the physical inspection window. Buyer confirmation may make payout eligible early; a timely dispute blocks payout atomically.

TOKLONG does not create a user wallet. After release eligibility, it initiates an external bank payout and marks the transaction `PAID_OUT` only after authenticated bank completion or authorized reconciliation.

## Inspection-window decision

The comparator range is 24–72 hours:

- Trustap: 24 hours.
- Vinted: 48 hours.
- Wallapop: 48 hours.
- Mercari: 72 hours.

TOKLONG selected a fixed 72 elapsed hours for the physical MVP on 2026-07-25.
The clock begins only at trusted carrier-confirmed delivery, and a shipped or
in-transit status does not start it. This choice bounds the seller's wait while
giving the buyer three days to inspect. It does not apply to digital
fulfillment, which never auto-releases from elapsed time.

## Sources

- Trustap, Buyer Guide: Transaction with Post/Courier Delivery: https://trustap.zendesk.com/hc/en-us/articles/15819980960017-Buyer-Guide-Transaction-with-Post-Courier-Delivery
- Trustap, Overview of the Online Transaction Process: https://trustap.zendesk.com/hc/en-us/articles/4436796304145-Overview-of-the-Online-Transaction-process
- Vinted, Buyer Protection: https://www.vinted.com/help/548/550-buyer-protection
- Vinted, Making an Offer: https://www.vinted.com/help/258
- Wallapop, Making an Offer: https://ayuda.wallapop.com/hc/es-es/articles/30927612898449-Hacer-una-oferta
- Wallapop, Legal Terms and Conditions: https://about.wallapop.com/en/legal-terms-and-conditions/
- Mercari, How Offers Work for Buyers: https://www.mercari.com/us/help_center/article/327/
- Mercari, Buyer Protection: https://www.mercari.com/us/help_center/article/235/
- Stripe, PromptPay Payments: https://docs.stripe.com/payments/promptpay
- Stripe, Refunds: https://docs.stripe.com/refunds
