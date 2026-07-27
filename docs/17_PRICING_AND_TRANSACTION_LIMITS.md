# 17 — Buyer Protection Pricing and Transaction Limits

## Status and scope

This document records the decided implementation baseline for new TOKLONG
transactions as of 2026-07-27. It separates the commercial Pilot policy from
the absolute technical boundary so that a risk limit is never presented as a
Thai statutory limit.

The policy applies only when the relevant fulfillment category and payment
provider are enabled. Shipping remains an exact buyer-funded pass-through and
is not included when calculating the Buyer Protection fee. The seller-funded
platform fee remains zero, so the seller's expected net is the immutable item
price.

## Business rules

### Two different limits

| Limit | Amount | Meaning |
|---|---:|---|
| Absolute technical maximum | 999,999 THB | Hard invariant accepted by the domain model; values above it are invalid even if configuration is wrong |
| Active Pilot maximum | 30,000 THB | Highest item price a normal application command may create under `buyer-protection-v2` |
| Active Pilot minimum | 1,000 THB | Lowest item price accepted under `buyer-protection-v2` |

The 30,000 THB Pilot maximum is a TOKLONG risk decision, not a limit claimed to
come from Thai law. Supporting 999,999 THB in the data type and domain boundary
does not authorize the product to accept that amount in production.

Increasing the active maximum requires a new approved policy version and all of
the following:

- approved marginal tiers covering the complete new range;
- written payment-provider approval and tested payment/refund/payout limits;
- the approved higher-value KYC, beneficiary verification, velocity, category,
  manual-review, insured-shipping, declared-value, and reserve controls;
- legal, tax, operations, risk, and provider sign-off;
- consumer copy and client validation updated before activation.

### `buyer-protection-v2` Pilot tiers

The fee uses marginal tiers. Moving into another tier changes only the portion
of the item price inside that tier; it never applies the lower rate to the whole
price.

| Portion of item price | Rate |
|---|---:|
| First 5,000 THB | 4.00% |
| Portion above 5,000 through 15,000 THB | 3.50% |
| Portion above 15,000 through 30,000 THB | 3.00% |

The minimum Buyer Protection fee is 59 THB. There is no separate maximum fee
cap in v2. The active item-price maximum naturally limits the Pilot fee to
1,000 THB.

The server computes:

```text
weighted_fee_numerator =
    sum(portion_satang_in_tier * tier_rate_basis_points)

percentage_fee_satang =
    ceil(weighted_fee_numerator / 10,000)

buyer_protection_fee_satang =
    max(5,900, percentage_fee_satang)

buyer_total_satang =
    item_price_satang
    + shipping_fee_satang
    + buyer_protection_fee_satang

seller_expected_net_satang = item_price_satang
```

The weighted result is rounded up once to one satang after all marginal
portions are summed. Money uses integer satang and rates use integer basis
points. Floating-point arithmetic is prohibited.

Examples:

| Item price | Buyer Protection fee |
|---:|---:|
| 1,000 THB | 59 THB |
| 3,000 THB | 120 THB |
| 5,000 THB | 200 THB |
| 10,000 THB | 375 THB |
| 15,000 THB | 550 THB |
| 20,000 THB | 700 THB |
| 30,000 THB | 1,000 THB |

No percentage is approved above 30,000 THB. The application must reject that
range rather than extrapolate the last tier or silently use an invented rate.

## Technical design

### One policy boundary

`IPaymentFeePolicy` in `Toklong.Application/Pricing` is the single,
provider-neutral application boundary. `BuyerProtectionFeeOptions` and
`ConfiguredBuyerProtectionFeePolicy` live in `Toklong.Infrastructure/Pricing`.
Payment-provider adapters, including Stripe, do not own tiers, limits, fee
versions, or pricing configuration; they receive only the already calculated
immutable buyer total from the application checkout flow.

There is no `PricingStrategy` enum and no client-selectable pricing mode. The
configured implementation:

1. validates the active item-price range;
2. validates ordered, non-increasing marginal tiers;
3. calculates the Buyer Protection fee server-side;
4. returns the seller fee, seller expected net, and policy version;
5. fails closed if the active maximum is above the absolute technical maximum
   or is not covered by the configured tiers.

The configuration shape is:

```json
{
  "BuyerProtectionFee": {
    "Enabled": true,
    "MinimumFeeSatang": 5900,
    "MinimumItemPriceSatang": 100000,
    "MaximumItemPriceSatang": 3000000,
    "PolicyVersion": "buyer-protection-v2",
    "Tiers": [
      {
        "UpToItemPriceSatang": 500000,
        "RateBasisPoints": 400
      },
      {
        "UpToItemPriceSatang": 1500000,
        "RateBasisPoints": 350
      },
      {
        "UpToItemPriceSatang": 3000000,
        "RateBasisPoints": 300
      }
    ]
  }
}
```

`MaximumItemPriceSatang` is the active commercial limit. It is not the
technical maximum. The absolute 99,999,900-satang boundary is a domain
invariant and cannot be raised by configuration.

### Enforcement points

- Offer creation validates the active policy before storing an offer or
  notifying the seller.
- The domain aggregate independently rejects prices below 1,000 THB or above
  999,999 THB.
- Seller acceptance recalculates the fee and rejects stale or client-supplied
  differences.
- Buyer checkout recalculates the same policy and requires the stored policy
  version and fee to match.
- Provider payment, refund, ledger, evidence, and reconciliation use the frozen
  buyer total.
- A paid transaction never recalculates under a later policy.

### Versioning and compatibility

`buyer-protection-v1` remains historical evidence for transactions that already
froze it. New local/Pilot transactions use `buyer-protection-v2`. No migration
rewrites an existing fee, total, acceptance hash, or paid snapshot.

Changing a tier, minimum fee, active range, or rounding rule requires a new
policy version. An already accepted but unpaid offer whose recomputed policy no
longer matches must end and be recreated. A paid transaction always follows
its immutable stored values.

### Required tests

- exact examples in the business table;
- one-satang values immediately before and after each tier boundary;
- minimum-fee behavior;
- active-range rejection at 999.99 THB and 30,000.01 THB;
- domain technical-boundary acceptance at 999,999 THB and rejection above it;
- invalid, overlapping/unordered, increasing-rate, and incomplete tier
  configuration;
- stale disclosed fee or policy version rejection at seller acceptance and
  checkout;
- full refund continues to use the complete immutable buyer total.
- local HTTP lifecycle coverage for offer creation, seller disclosure,
  shipping quote, seller acceptance, buyer checkout, stored snapshot, and
  transaction read at 1,000 / 5,000 / 10,000 / 15,000 / 30,000 THB;
- local HTTP rejection at 30,000.01 THB and read compatibility for a frozen
  historical `buyer-protection-v1` snapshot.

## Production gates and open decisions

The Pilot implementation does not decide:

- rates above 30,000 THB;
- when the active maximum may be raised;
- VAT, invoicing, or withholding-tax treatment;
- loss reserve and carrier-insurance funding;
- higher-value KYC and category thresholds;
- payment-provider approval of the final third-party seller and delayed-payout
  fund flow.

These remain launch decisions. The system supporting a larger integer amount
must never be described as provider, legal, insurance, or operational approval.
