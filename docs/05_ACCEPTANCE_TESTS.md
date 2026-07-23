# 05 — Acceptance Tests

The following scenarios are product and domain acceptance criteria. Adapt them into automated unit, integration, and end-to-end tests.

## A. Create and share link

### A0 — Buyer creates a private offer link

**Given** a verified buyer has already found a seller outside TOKLONG  
**When** the buyer records one proposed item, fulfillment type, amounts, expected deadline, contact email, and creates an offer  
**Then** the system creates an unguessable private link in `AWAITING_SELLER_ACCEPTANCE`  
**And** no checkout, payment, refund, or payout instruction is created  
**And** the UI states that the seller has not yet accepted.

### A0.1 — Seller acceptance enables buyer checkout

**Given** a buyer-created offer in `AWAITING_SELLER_ACCEPTANCE`  
**When** the authenticated eligible seller completes the material seller facts, required photos and attestations, and accepts  
**Then** the transaction moves to `SELLER_ACCEPTED_AWAITING_PAYMENT`  
**And** the buyer is notified to review the final seller-confirmed terms and pay  
**And** the seller is not told that payment has completed.

### A0.2 — Buyer cannot pay before seller acceptance

**Given** a buyer-created offer still awaiting seller acceptance  
**When** the buyer attempts to start checkout or create a PaymentIntent  
**Then** the request is rejected  
**And** no provider payment object is created  
**And** the rejection is audited.

### A0.3 — Seller revisions require buyer review

**Given** a buyer-created proposal  
**When** the seller completes or corrects a material product fact before accepting  
**Then** the revised final agreement is shown prominently to the buyer before payment  
**And** creating the original proposal is not treated as buyer acceptance of the revision.

### A1 — Seller activates a valid link

**Given** an onboarded seller records the agreed item, combined agreement details, at least one photo, amounts, deadline, and passes prohibited-goods checks  
**When** the seller activates the agreement link  
**Then** the system creates an unguessable public link in `LINK_ACTIVE`  
**And** the seller can copy/share it  
**And** no payment or payout record is marked successful.

### A1.1 — Compact agreement form is not a marketplace listing form

**Given** an onboarded seller opens the create screen  
**Then** the primary fields are the agreed item, combined agreement details, direct photo capture/upload, amounts, deadline, and saved payout account  
**And** the normal flow does not show category, condition, separate defect, or raw photo-URL inputs  
**And** a photo can be saved without invoking AI.

### A1.2 — Seller chooses a fulfillment type

**Given** the seller creates an agreement  
**When** physical shipment is selected  
**Then** shipping fee, ship-by copy, address, tracking, and carrier rules apply  
**When** digital handoff is selected  
**Then** shipping is zero, address/tracking are omitted, and the no-auto-release rule is visible.

### A2 — Unsupported item is blocked

**Given** the item category or content matches a prohibited or unsupported rule  
**When** the seller attempts to activate  
**Then** activation is blocked  
**And** the reason is shown in plain language  
**And** an audit/risk event is written.

## B. Buyer review and payment

### B1 — Buyer sees material terms before payment

**Given** an active link  
**When** the buyer opens checkout  
**Then** the buyer sees agreement photos, the frozen agreement description including represented condition and defects, price, shipping, total, ship-by deadline, payout trigger, dispute window, and terms version before confirming payment.

### B1.1 — PromptPay checkout collects refund contact

**Given** the buyer selects PromptPay  
**When** checkout is prepared  
**Then** a deliverable buyer email is required for receipts and Stripe refund instructions  
**And** TOKLONG does not request or persist a refund bank-account number.

### B2 — Redirect does not mark paid

**Given** the buyer returns to the app from provider checkout  
**And** no verified success webhook has been processed  
**Then** the transaction remains `PAYMENT_PENDING`  
**And** the seller does not see any physical or digital fulfillment action.

### B3 — Verified payment enables shipment

**Given** a valid provider success event  
**When** the webhook signature and idempotency checks pass  
**Then** the transaction moves to `PAID_AWAITING_SHIPMENT` once  
**And** an immutable paid snapshot exists  
**And** the seller receives the ship-by notification.

### B4 — Duplicate payment webhook is safe

**Given** the same provider event is delivered twice  
**When** both requests are processed  
**Then** only one state transition and one audit event occur  
**And** no duplicate financial instruction is created.

### B5 — Confirmed payment posts one settlement liability

**Given** a verified provider payment success event  
**When** it is processed or replayed  
**Then** the append-only settlement ledger records the buyer funds and seller-payable liability exactly once  
**And** all amounts are integer satang  
**And** no user wallet or spendable balance is created.

## C. Shipment and tracking

### C1 — Seller submits valid supported tracking

**Given** a paid transaction before ship-by deadline  
**When** the seller submits a supported carrier and valid tracking number  
**Then** the system records the shipment  
**And** begins carrier verification  
**And** notifies the buyer.

### C2 — Unverified tracking does not start the clock

**Given** tracking cannot be verified  
**Then** the transaction enters `TRACKING_UNVERIFIED` or review  
**And** no `delivered_at` or dispute deadline is created  
**And** automatic payout is blocked.

### C3 — Seller-entered delivery is ignored

**Given** a seller claims the item was delivered  
**But** no trusted carrier event or buyer confirmation exists  
**Then** the system does not start the seven-day window.

### C4 — Trusted delivery starts exact deadline

**Given** a verified carrier delivery event at `2026-07-20T14:18:00+07:00`  
**When** it is processed  
**Then** the transaction enters `DELIVERED_DISPUTE_WINDOW`  
**And** `window_ends_at` equals `2026-07-27T14:18:00+07:00`  
**And** both parties see that exact deadline.

### C5 — Digital seller handoff never releases payout

**Given** provider-confirmed payment for a digital agreement  
**When** the seller records a non-secret handoff statement  
**Then** the state becomes `DIGITAL_DELIVERY_SUBMITTED`  
**And** no dispute deadline or payout instruction is created.

### C6 — Digital buyer confirmation creates eligibility

**Given** a digital agreement in `DIGITAL_DELIVERY_SUBMITTED` with no dispute/refund/hold  
**When** the authenticated buyer confirms receipt  
**Then** the transaction becomes payout eligible and creates at most one payout instruction.

### C7 — Digital elapsed time never auto-releases

**Given** a digital agreement in `DIGITAL_DELIVERY_SUBMITTED`  
**When** any deadline/release job runs at any later time  
**Then** the state remains unchanged and no payout instruction is created.

### C8 — Digital dispute blocks payout

**Given** a digital agreement in `DIGITAL_DELIVERY_SUBMITTED`  
**When** the buyer reports non-receipt, inaccessible credentials, or material mismatch  
**Then** the transaction enters the dispute path atomically and payout remains blocked.

## D. Receipt confirmation and release

### D1 — Buyer confirms early

**Given** the transaction is in the delivered dispute window  
**And** no dispute/refund/hold exists  
**When** the buyer confirms receipt  
**Then** the system records the confirmation once  
**And** evaluates the transaction as payout eligible  
**And** creates at most one payout instruction.

### D2 — Deadline creates payout eligibility

**Given** the verified dispute deadline has passed  
**And** no dispute/refund/hold exists  
**When** the release job runs  
**Then** the transaction moves to `PAYOUT_ELIGIBLE` once  
**And** payout creation is queued idempotently.

### D3 — Open dispute blocks deadline release

**Given** a dispute opened before the deadline  
**When** the release job runs after the deadline  
**Then** no payout instruction is created  
**And** the transaction remains in the dispute/resolution flow.

### D4 — Provider payout processing is not completion

**Given** a payout instruction has been accepted for processing  
**Then** the transaction is `PAYOUT_PENDING`  
**And** the UI does not claim the seller has received funds.

### D5 — Provider confirmation closes payout

**Given** a verified payout-completed provider event or authorized bank reconciliation result  
**When** it is processed  
**Then** the transaction moves to `PAID_OUT` once  
**And** the seller is notified  
**And** the transaction summary includes the provider reference.

## E. Disputes

### E1 — Buyer opens dispute before deadline

**Given** the deadline has not passed  
**When** the buyer submits a supported reason and required statement  
**Then** dispute creation and payout blocking occur atomically  
**And** both parties are notified.

### E2 — Buyer cannot open normal dispute after deadline/payout eligibility

**Given** the deadline has passed and the transaction was already made payout eligible  
**When** the buyer attempts the normal dispute action  
**Then** the action is rejected or routed to an exceptional support process  
**And** no state is silently rolled back.

### E3 — AI cannot resolve a dispute

**Given** AI generates a case summary  
**When** no authorized human or mutually confirmed resolution exists  
**Then** the system cannot create refund or payout instructions from the AI output.

## F. Missed shipment and refund

### F1 — Shipment deadline expires without valid tracking

**Given** a paid transaction has passed `ship_by_at`  
**And** no valid shipment exists  
**When** the deadline job runs  
**Then** the transaction enters the approved overdue/cancellation path  
**And** both parties receive a clear next-step notification  
**And** payout remains blocked.

### F2 — Refund success requires provider confirmation

**Given** a refund was requested  
**Then** the transaction is `REFUND_PENDING`  
**And** only a verified provider refund-completed event moves it to `REFUNDED`.

### F3 — PromptPay refund may require buyer action

**Given** a Stripe PromptPay refund is created  
**When** Stripe reports `requires_action`  
**Then** the transaction remains `REFUND_PENDING`  
**And** the buyer is told to check email and provide the account used for payment to Stripe  
**When** Stripe later reports `succeeded` through a verified event  
**Then** the transaction moves to `REFUNDED` once.

## G. Immutability and authorization

### G1 — Paid product details cannot be edited

**Given** payment is confirmed  
**When** the seller attempts to edit price, condition, photos, defects, shipping, or terms  
**Then** the paid snapshot remains unchanged  
**And** the user is instructed to cancel/resolution and create a new link where allowed.

### G2 — Buyer cannot alter tracking

**Given** a buyer-authenticated session  
**When** it attempts the seller tracking endpoint  
**Then** access is denied and audited.

### G3 — User cannot directly force financial states

**Given** any ordinary client  
**When** it calls an endpoint attempting to set `PAID`, `REFUNDED`, or `PAID_OUT`  
**Then** the request is rejected  
**And** no domain state changes.

### G4 — Digital secrets are not accepted as fulfillment data

**Given** a seller records digital handoff  
**When** the handoff statement appears to contain a password, recovery code, private key, or seed phrase  
**Then** the submission is rejected before persistence  
**And** the UI instructs the seller to deliver through the agreed external channel.

## H. Accessibility and reduced motion

### H1 — Landing walkthrough works without autoplay

**Given** reduced motion is enabled  
**Then** autoplay is disabled or effectively paused  
**And** the user can navigate all four scenes with controls  
**And** the static four-step section communicates the full flow.
