# Optional Parcel Protection at Checkout Design

**Date:** 2026-07-30  
**Status:** Approved for written-spec review  
**Scope:** Optional buyer-funded parcel protection for physical transactions

## 1. Goal

Offer additional parcel protection only when the item value exceeds the
coverage already included with the selected delivery service. The Buyer makes
one informed choice in the payment screen, after the Seller has supplied the
real parcel information and selected a delivery service.

The ordinary consumer experience presents one product:
`ความคุ้มครองพัสดุ`. It does not expose a provider name, package name,
provider cost, TOKLONG margin, or uncovered-value calculation.

## 2. Product decision

This design replaces the current rule that every enabled physical service must
carry paid full-value parcel insurance.

The new rule is:

- included carrier coverage remains available according to the selected
  service's certified terms;
- no protection question or additional charge appears when included coverage
  is at least the item value;
- when the item value exceeds included coverage and a valid additional option
  exists, the Buyer may add or decline it at checkout;
- declining additional protection does not block the physical transaction;
- the exact election and applicable coverage terms are retained in the
  agreement record; and
- a missing or unavailable additional option never becomes an invented
  coverage promise.

The change does not weaken payment, fulfillment, dispute, or payout truth
rules. Provider-confirmed payment is still required before fulfillment.
Trusted delivery still controls the physical inspection window. A dispute or
shipping exception still blocks payout.

## 3. Non-goals

- TOKLONG does not underwrite parcel loss or damage.
- TOKLONG does not create a branded insurance package.
- The Seller does not choose, fund, approve, or see the Buyer's optional
  protection.
- The Buyer cannot choose an arbitrary insured amount.
- The client cannot calculate or override the premium, coverage limit, service
  fee, or buyer total.
- The feature does not support split shipments, multiple protection policies,
  partial item coverage selected by the Buyer, or post-payment upgrades.
- This design does not remove parcel weight or dimensions. They remain required
  until the configured account and every enabled service have been certified
  to omit them without making the pre-payment price unreliable.

## 4. Consumer terminology

Use:

- `ความคุ้มครองพัสดุ`
- `เพิ่มความคุ้มครองพัสดุไหม?`
- `วงเงินคุ้มครองสูงสุด`
- `ค่าความคุ้มครอง`
- `เพิ่มความคุ้มครอง ฿[ราคา]`
- `ไม่เพิ่มความคุ้มครอง`
- `ดูเงื่อนไขและสินค้าที่ไม่คุ้มครอง`

Do not use in ordinary consumer UI:

- a provider or carrier-platform brand as the protection-product name;
- `แพ็กเสริม`, `ประกันของ TOKLONG`, or language implying TOKLONG is the
  insurer;
- provider cost, commission, margin, or cost-plus calculations;
- `ส่วนที่ไม่คุ้มครอง`;
- a green recommendation callout;
- claims that protection is complete, guaranteed, or covers every cause.

Legally required identity, claim, and responsible-party information remains
available in the detailed terms and authenticated support flow. It is not used
as the ordinary product label.

## 5. When the choice appears

The protection choice appears only when all conditions are true:

1. the transaction is physical;
2. the authenticated Buyer is in the final payment flow;
3. the Seller has accepted the item, parcel facts, and selected delivery
   service;
4. the item price exceeds the selected service's certified included coverage;
5. the server has a fresh, matching additional-protection quote;
6. the additional coverage limit is greater than the included coverage;
7. the quote remains valid long enough to create the matching booking and
   payment object; and
8. no checkout, payment, expiry, cancellation, or dispute state blocks the
   action.

Do not ask during offer creation or Seller review. At those stages the exact
parcel and service facts are not yet final.

If included coverage is sufficient, the additional-protection UI is absent and
the additional price is zero.

If additional protection is unavailable, the UI does not invent a price or
coverage limit. The Buyer may continue with the included coverage. Detailed
terms may state that the transaction uses the coverage included with the
delivery service, but the ordinary payment breakdown does not display an
uncovered-value calculation.

## 6. Buyer experience

### 6.1 First checkout visit

Before creating a PaymentIntent, show a blocking decision card:

```text
เพิ่มความคุ้มครองพัสดุไหม?

มูลค่าสินค้าสูงกว่าวงเงินที่รวมมากับการจัดส่ง
แนะนำเพิ่มความคุ้มครองก่อนชำระเงิน

วงเงินคุ้มครองสูงสุด     ฿12,000
ค่าความคุ้มครอง             ฿100

ดูเงื่อนไขและสินค้าที่ไม่คุ้มครอง

[ เพิ่มความคุ้มครอง ฿100 ]
  ไม่เพิ่มความคุ้มครอง
```

The primary action is filled Buyer Blue. The decline action is a neutral
secondary text action with a minimum 44-by-44-point target and readable
contrast. It may be visually quieter but must not be hidden, disabled,
preselected, or made intentionally difficult to understand.

No payment starts until the Buyer chooses.

### 6.2 After a choice

Persist the election server-side. Reopening the transaction does not
automatically show the decision card again.

Before a PaymentIntent exists, the Buyer may use a visible `เปลี่ยน` action to
reopen the decision. A changed election invalidates the prior booking intent
and total and requires fresh server validation.

If the Buyer closes the app without choosing, no election exists and the card
appears again on the next checkout visit. This is an incomplete choice, not a
repeated sales prompt.

### 6.3 Payment summary

When accepted, the normal Buyer payment breakdown adds exactly one row:

```text
ค่าความคุ้มครองพัสดุ     ฿100
```

The payment summary does not show the coverage limit. A separate
`ดูรายละเอียดความคุ้มครองพัสดุ` action exposes the selected limit, exclusions,
terms, and claim route before payment.

When declined or not applicable, the additional-protection row is omitted
rather than displayed as zero.

The exact total-payment button reflects the selected final amount.

## 7. Seller experience and authorization

The Seller:

- continues to supply the origin, weight, width, length, and height;
- selects the delivery service and reviews the shipping charge;
- accepts the item, parcel, delivery service, payout trigger, and Seller terms;
- never sees whether the Buyer was offered additional protection;
- never sees the protection price, limit, provider cost, TOKLONG service fee,
  or Buyer's resulting total; and
- receives the same item-price Seller net regardless of the Buyer's election.

Seller-facing API projections omit all optional-protection consumer and
internal financial fields. Hiding them only in XAML is insufficient.

The optional election is a Buyer-only checkout annex. It does not modify the
Seller-accepted item, parcel, carrier/service, shipping charge, Seller net, or
Seller obligations, so it does not require a second Seller acceptance.

## 8. Pricing

All values are integer satang.

For an accepted additional-protection option:

```text
toklong_service_fee_satang = 1_500

customer_protection_price_satang =
  provider_protection_cost_satang
  + toklong_service_fee_satang
```

For declined, unavailable, or unnecessary additional protection:

```text
customer_protection_price_satang = 0
toklong_service_fee_satang = 0
```

The customer sees only `customer_protection_price_satang`. The provider cost
and TOKLONG service fee remain separate internal accounting values.

The displayed customer price is final and includes any applicable treatment of
the fixed service fee. No tax, service, or handling amount is added after the
Buyer chooses. Final tax and invoice accounting must be reviewed separately,
but that review cannot introduce a hidden checkout charge.

The payment amount is:

```text
buyer_total_satang =
  item_price_satang
  + shipping_fee_satang
  + customer_protection_price_satang
  + buyer_protection_fee_satang
```

The optional protection price is not Seller proceeds and never reduces Seller
net.

## 9. Quote and coverage validation

The server, not the mobile app:

- reads the selected delivery service and immutable parcel facts;
- obtains or validates the current included coverage;
- obtains the current additional-protection provider cost and maximum limit;
- confirms that the option belongs to the same service, parcel, origin,
  destination, item value, and currency;
- rejects negative, ambiguous, floating-point, stale, mismatched, or
  insufficient response values;
- adds the fixed 1,500-satang TOKLONG service fee; and
- signs or otherwise binds the quote to the complete request fingerprint and
  expiry.

The mobile request sends only the election and the opaque server-issued option
reference. It does not send a trusted price or coverage limit.

The customer-facing maximum is the certified coverage limit returned for that
exact option. It is shown only in the decision/details experience and retained
in the paid record.

## 10. Agreement and paid snapshot

Seller acceptance continues to bind the common agreement core:

- item and condition;
- item price;
- origin and destination disclosure;
- parcel weight and dimensions;
- selected delivery service and shipping charge;
- Seller net;
- fulfillment deadline;
- payout and dispute rules; and
- common terms version.

Buyer checkout acceptance references the same common agreement-core hash and
adds an immutable Buyer-only parcel-protection annex:

```text
election = accepted | declined | not_applicable | unavailable
customer_price_satang
provider_cost_satang
toklong_service_fee_satang
included_coverage_limit_satang
selected_coverage_limit_satang
protection_terms_version
provider_option_reference
quoted_at
expires_at
buyer_elected_at
```

Internal provider identity and reconciliation references remain server-side.
Normal Seller responses and consumer summaries do not expose them.

The annex is included in the Buyer's acceptance hash and checkout snapshot.
Provider-confirmed payment seals the complete paid snapshot. After sealing,
the election, price, coverage values, terms, service, and parcel facts are
immutable.

Declining protection is recorded explicitly. The normal UI does not show the
uncovered difference, but evidence retains the applicable included-coverage
limit and terms so the record does not imply full-value protection.

## 11. Booking and payment ordering

The sequence is:

```text
Seller accepts item + parcel + delivery service
  → Buyer opens payment
  → server validates current protection option
  → Buyer accepts or declines
  → election and durable booking intent commit
  → Worker creates the exact unconfirmed provider booking
  → matching booking result is retained
  → server creates PaymentIntent for the exact frozen buyer total
  → verified payment event authorizes booking confirmation
```

No PaymentIntent is created before the selected booking variant succeeds.

The booking request contains the provider-facing protection fields only when
the Buyer accepted. A declined or not-applicable election books the same locked
delivery service without the optional addition.

The Seller's selected carrier/service cannot change during this sequence. If
the chosen service cannot support the election, the operation fails before
payment.

The existing buyer-payment deadline remains authoritative. Protection quote or
booking work does not extend it. If the deadline passes, checkout is rejected
and normal expiry/cleanup applies.

## 12. Price changes and retries

If provider cost, customer price, coverage limit, terms, or availability
changes before booking:

- invalidate the old election price and total;
- show the new price/limit and require a fresh Buyer confirmation;
- do not describe this as the same already-confirmed choice; and
- never add the difference silently.

If a read-only quote request fails, show a retry action without creating a
payment or booking.

If a booking mutation definitely fails before being sent, the durable operation
may retry under the existing bounded policy.

If a mutation may have reached the provider, record `outcome_unknown` and do
not issue another booking until safe provider lookup proves the result. A
second Buyer tap cannot create another booking.

If the selected additional option becomes unavailable, the Buyer must
explicitly choose whether to continue without it. The system cannot silently
convert an accepted election to declined.

## 13. Visibility matrix

| Data | Buyer decision | Buyer summary | Seller | Internal |
|---|---:|---:|---:|---:|
| Customer protection price | Yes | Yes | No | Yes |
| Maximum coverage | Yes | Details only | No | Yes |
| Included coverage | Explanatory copy only | Details only | No | Yes |
| Uncovered difference | No | No | No | Derived only |
| Provider protection cost | No | No | No | Yes |
| TOKLONG service fee | Included in one price | Included in one price | No | Yes |
| Provider identity/reference | Detailed terms only when legally required | No | No | Yes |
| Buyer election | Yes | Status/change action | No | Yes |

## 14. Audit and analytics

Write immutable audit events for:

- additional protection offered;
- Buyer accepted;
- Buyer declined;
- Buyer changed election before payment;
- quote or terms changed and reconfirmation was required;
- option unavailable;
- booking intent created;
- provider booking outcome; and
- paid snapshot sealed.

Analytics may measure offer, accept, decline, unavailable, price-change,
checkout conversion, and average customer price. Analytics must not contain
addresses, phone numbers, provider credentials, raw quote responses, or claim
evidence.

## 15. Error handling

- Invalid or missing parcel facts block quote and checkout.
- Unsupported service or response contract fails closed.
- An unavailable option never displays a zero-price full-coverage claim.
- A stale option requires refresh.
- A customer price mismatch blocks booking and payment.
- A booking mismatch in service, shipping charge, protection cost, or coverage
  limit enters shipping review and creates no PaymentIntent.
- Reopening the app resumes the persisted election and exact current state.
- An expired accepted offer cannot be revived by a protection quote, election,
  or late booking response.
- A provider adjustment after payment is an internal operational cost and never
  changes the paid Buyer total or Seller net.

## 16. Accessibility

- Both decision actions have at least 44-by-44-point targets.
- The decline action has readable contrast and an explicit accessible name.
- The choice is not preselected.
- VoiceOver announces the maximum coverage, customer price, terms action, and
  both choices in a logical order.
- Dynamic Type may wrap all Thai copy without hiding either choice or the exact
  price.
- The choice is not communicated by color alone.
- Returning to the summary exposes the selected state and a reachable change
  action before payment.

## 17. Testing strategy

### Domain and application tests

- No offer when item value is within included coverage.
- Offer only for a fresh matching additional option.
- Accepted price equals provider cost plus exactly 1,500 satang.
- Declined, unavailable, and not-applicable states add zero.
- Buyer total includes the accepted customer price exactly once.
- Seller net is unchanged.
- Seller projections contain no optional-protection fields.
- Buyer selection annex does not change the Seller-accepted common core.
- Paid snapshot seals the exact annex and rejects later mutation.
- Declining allows the transaction to continue with included coverage.

### API and authorization tests

- Only the authenticated transaction Buyer may elect or change the option.
- Seller, another Buyer, and anonymous requests are forbidden.
- Client-supplied price, limit, provider cost, or service fee is ignored or
  rejected.
- Duplicate election and booking requests are idempotent.
- Payment preparation is unavailable until the matching booking succeeds.
- Deadline and state authorization remain enforced.

### Provider and mutation tests

- Included and additional coverage parse with integer-satang values.
- Missing, ambiguous, insufficient, mismatched, or stale options fail closed.
- Exact service/parcel/address request fingerprint is enforced.
- Definite failure, rate limit, timeout, outcome-unknown, and lookup behavior
  issue no duplicate booking.
- Price or terms drift requires Buyer reconfirmation.

### Mobile tests

- The choice never appears during offer creation or Seller review.
- It appears once at checkout when eligible.
- Closing without choosing asks again; choosing and reopening resumes without
  an automatic repeat.
- Buyer decision shows one maximum and one customer price.
- Buyer summary shows price but no maximum.
- Seller offer and transaction detail show neither price nor maximum.
- The primary and decline actions remain accessible at supported text sizes.

### Regression tests

- Type checking and unit/integration suites pass.
- Payment webhook signature, idempotency, and replay tests pass.
- Shipping-operation state, authorization, and idempotency tests pass.
- Trusted delivery remains the only default source of the physical 72-hour
  window.
- Dispute and shipping exceptions block payout.
- No digital auto-release behavior changes.
- No provider key, raw response, or personal data enters source control or
  logs.

## 18. Documentation updates

Implementation must align:

- `docs/00_PRODUCT_BRIEF.md`
- `docs/01_USER_FLOWS_AND_STATE_MACHINE.md`
- `docs/02_UI_UX_AND_CONTENT_SPEC.md`
- `docs/03_BACKEND_TRANSACTION_RECORD.md`
- `docs/04_PAYMENT_SHIPPING_AND_DISPUTE_RULES.md`
- `docs/05_ACCEPTANCE_TESTS.md`
- `docs/06_OPEN_DECISIONS.md`
- `docs/08_SHIPPOP_PRODUCTION_FLOW.md`
- `docs/08_IMPLEMENTATION.md`
- the existing SHIPPOP production design and certification runbook

The updates must explicitly remove the old mandatory full-value-insurance rule
and replace it with this optional checkout annex. Provider certification still
controls which service can expose an option and what maximum may be shown.

## 19. Assumptions and open provider capabilities

Assumptions approved for this design:

- the fixed TOKLONG service fee is 15 THB per accepted option;
- the customer sees one combined protection price;
- the normal UI does not use the provider brand;
- the Seller sees no optional-protection values;
- declining permits checkout with included coverage;
- parcel weight and dimensions remain required for now.

Provider capabilities still requiring certification:

- included coverage by service and category;
- additional-protection field names, unit, rounding, limit, exclusions, and
  claim contract;
- whether an unconfirmed booking can be created after the Buyer election while
  retaining the Seller-selected service and quote;
- safe booking lookup and duplicate behavior;
- cancellation and refund treatment of the optional premium;
- post-payment adjustment reporting; and
- whether this account may omit weight/dimensions without making the final
  pre-payment amount uncertain.

If the provider cannot support booking after the Buyer election without
duplicate or price risk, the optional feature remains disabled. The application
must not move the choice earlier merely to bypass that safety gate.

## 20. Success criteria

The feature is complete when:

1. eligible Buyers make one server-priced choice at checkout;
2. included coverage requires no question or additional charge;
3. the customer sees one price and one maximum only in the decision/details
   experience;
4. the Buyer summary hides the maximum;
5. every Seller surface and response hides optional-protection values;
6. accepted pricing adds exactly 15 THB to certified provider cost;
7. booking succeeds before a PaymentIntent is created;
8. provider or price uncertainty cannot create duplicate bookings or hidden
   charges;
9. the paid snapshot immutably records the complete election and exact total;
   and
10. all required repository checks pass.

