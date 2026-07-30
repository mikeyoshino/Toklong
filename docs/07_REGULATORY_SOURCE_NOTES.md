# 07 — Regulatory Source Notes

> These dated notes are for product and legal scoping only. They are not a legal opinion. Use the original Thai legal text and qualified Thai counsel for production decisions.

## Electronic transaction records

The Electronic Transactions Act published by ETDA states that information is not denied legal effect merely because it is in data-message form, and that an offer or acceptance may be expressed by a data message. It also describes retention characteristics such as accessibility for later reference, accurate representation, and retention of source/destination/date/time information.

Product implication:

- A separate visible “sign contract” step is not inherently required merely because the transaction is electronic.
- The system should preserve the transaction snapshot, terms version, identities/attribution method, and timestamps in a reliable form.
- For optional parcel protection, retain the buyer's election, the combined
  disclosed price, applicable limit, terms version, quote/expiry, and a
  canonical buyer checkout-annex acceptance record. The seller's prior delivery
  acceptance is not evidence of that later buyer election.
- The final legal design and evidentiary method require Thai legal review.

Official source:

- ETDA, Electronic Transactions Act B.E. 2544 (2001), English translation: https://www.etda.or.th/getattachment/8faa736b-3235-49c8-8b01-d37ff53a9a45/ENG-Version.aspx

## Conditional payments and escrow terminology

A December 2025 Bank of Thailand programmable-payment testing framework distinguishes payment/settlement with pre-defined automatic conditions from escrow service with pre-defined asset-delivery conditions.

Product implication:

- Conditional release and escrow-related structures are regulated/structured concepts, not merely marketing labels.
- Do not assume the application can receive, hold, or release customer money directly.
- Use a payment partner and obtain written confirmation of the supported fund flow and permitted copy before launch.
- Describe an optional parcel-protection price as a buyer choice with versioned
  terms and disclosed maximum, not as TOKLONG insurance, a TOKLONG guarantee,
  or escrow. Do not claim a carrier or provider benefit until the account-
  specific capability and terms are certified.

Official source:

- Bank of Thailand, Programmable Payment testing framework: https://www.bot.or.th/content/dam/bot/financial-innovation/digital-finance/fintech/sandbox/Published_%E0%B8%81%E0%B8%A3%E0%B8%AD%E0%B8%9A%E0%B8%81%E0%B8%B2%E0%B8%A3%E0%B8%97%E0%B8%94%E0%B8%AA%E0%B8%AD%E0%B8%9A%20programmable%20payment.pdf

## Digital platform service duties

ETDA's English translation of the Royal Decree on Digital Platform Service Businesses describes notification and operating duties for digital platform services. For intermediary services offering goods or services to consumers, it includes transparency around terms, fees, support, complaint handling, dispute resolution, data use, and illegal goods/services/content.

Product implication:

- Even though TOKLONG is not a discovery marketplace, its role as an intermediary in a goods transaction may still require digital-platform-service analysis.
- Supporting transferable digital accounts, items, or rights may introduce additional platform-contract, consumer-protection, cybersecurity, and fraud obligations; each launch category requires legal and provider review.
- Terms, fees, complaint channels, dispute timing, and prohibited-goods actions should be explicit before launch.
- Before payment, the buyer must see any optional-protection combined price,
  maximum, and a route to its exclusions/terms at the point of choice. When a
  paid add-on is accepted, restate the combined price in the buyer payment
  summary; keep the maximum at choice/details. Retain the accepted terms
  version and buyer annex evidence for the transaction; seller-facing views
  must not disclose that buyer-only price or internal accounting split.
- Determine notification/reporting obligations based on the actual company, revenue, users, contractual relationships, and service design.

Official source:

- ETDA, Royal Decree on the Operation of Digital Platform Service Businesses, English translation: https://www.etda.or.th/getattachment/Regulator/DigitalPlatform/law/Clean-Royal-Decree-on-DP-Corrected-1.pdf.aspx?lang=th-TH

## Payment-system supervision

The Bank of Thailand describes its role in supervising payment systems, reducing risk, promoting secure/reliable electronic payment services, and protecting/educating users.

Product implication:

- Provider selection, security, fraud controls, customer disclosures, and reconciliation are first-class product requirements, not implementation details.
- The supervised designated-payment-service categories include electronic
  acceptance of payment on behalf of sellers/service providers/creditors and
  electronic money transfer. The final TOKLONG custody, settlement, delayed
  payout, and third-party-seller structure therefore requires written provider
  confirmation and qualified Thai legal review; describing a value as a
  product limit does not resolve the fund-flow question.

Official source:

- Bank of Thailand, About payment systems: https://www.bot.or.th/th/our-roles/payment-systems/about-payment-systems.html
- Bank of Thailand, Payment Systems Act oversight:
  https://www.bot.or.th/th/our-roles/payment-systems/payment-act-oversight.html
- Bank of Thailand, Payment Systems Act:
  https://www.bot.or.th/th/laws-and-rules/bot-takes-responsibilities-and-other-relevant-laws-and-regulations/law04.html

## Transaction amount boundaries

The official sources reviewed for this product-scoping note do not establish
30,000 THB as a statutory maximum for TOKLONG and do not establish 999,999 THB
as an automatically permitted or exempt amount. PromptPay transfer limits are
also subject to the participating bank, service, and customer-configured
limits.

Product implication:

- 30,000 THB is documented as the initial TOKLONG Pilot risk limit.
- 999,999 THB is only the absolute technical boundary in the current domain
  model.
- Neither value may be marketed as a legal safe harbor.
- Raising the active limit still requires provider limits, KYC/AML, fraud,
  insured-shipping, reserve, tax, legal, risk, and operations review.
- Do not select a value immediately below a round threshold as a method of
  avoiding monitoring, reporting, or provider controls.

Official source:

- Bank of Thailand, PromptPay:
  https://www.bot.or.th/th/financial-innovation/digital-finance/digital-payment/promptpay.html

## Required pre-launch reviews

1. Thai legal counsel: contract/transaction record, platform terms, consumer protection, dispute process, prohibited goods, transferable-digital-item scope, privacy, and digital platform obligations.
2. Selected payment partner: exact custody/settlement/payout/refund model, onboarding, chargebacks, wording, and webhook truth states.
3. Tax adviser/accountant: fees, VAT, invoices/receipts, and withholding tax where relevant.
4. Logistics/carrier partner: trusted event definitions, tracking reliability, delivery corrections, and return workflows.
5. Privacy/security: identity, addresses, evidence files, retention, access logging, and incident response.
