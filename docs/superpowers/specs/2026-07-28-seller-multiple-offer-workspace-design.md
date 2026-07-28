# Seller Multiple-Offer Workspace Design

Date: 2026-07-28
Status: Approved

## Goal

Make the seller experience understandable when one verified Thai phone has
more than one targeted offer or active sale. The seller must be able to see:

- how many new offers still need a response;
- how many paid sales need fulfillment;
- how many sales are progressing without a seller action;
- whether any sale has a reported problem;
- which single item is most urgent now; and
- how many seller-side records exist in total.

This remains a private transaction workspace. It does not add marketplace
discovery, bidding, storefronts, chat, bulk acceptance, bulk fulfillment, or a
seller-created offer flow.

## Selected direction

Use the approved **Summary + Priority Spotlight** layout.

The existing `รายการของคุณ` page keeps its buyer/seller mode switch. Seller
mode adds:

1. an exact total count;
2. three tappable summary counts;
3. a conditional problem banner;
4. one large `ต้องทำตอนนี้` spotlight card; and
5. a compact list of the remaining records.

The authenticated home page keeps the existing blue `ซื้อ` and purple `ขาย`
cards. When seller work exists, the purple card additionally shows:

- `N ข้อเสนอใหม่`; and
- `มี N รายการที่ต้องจัดการ`.

Both lines disappear when their respective count is zero.

## Language and information model

Use `ข้อเสนอใหม่` only for a buyer-created offer that still awaits the
intended seller's response. Once the seller accepts, call it a `รายการขาย`.

The seller summary uses these classifications:

### New offers

`ข้อเสนอใหม่` contains seller-role records whose presentation action is
`ReviewSellerOffer`.

These records show `ตรวจข้อเสนอ` and the exact response deadline. The count on
the authenticated home card and the seller transaction page must match.

### Fulfillment required

`ต้องส่ง` contains seller-role records requiring physical or digital
fulfillment, including:

- `AddTracking`;
- `ConfirmDigitalHandoff`;
- correcting an unverified tracking number; and
- an overdue physical shipment that still exposes the fulfillment action.

The item copy shows an exact ship-by date and time where applicable.

### In progress

`กำลังไปต่อ` contains active seller-role records that do not currently require
a seller response or fulfillment action, including:

- waiting for the buyer to pay;
- waiting for verified payment;
- carrier verification or in-transit delivery;
- buyer inspection or digital-handoff review; and
- payout preparation or payout pending.

Disputed records are not included in this count.

### Problems

`Disputed` and `ResolutionPending` records appear in a separate conditional
problem banner. The banner states the number of affected records and explains
in plain Thai that seller payout is stopped during review.

The banner is not a permanent fourth summary tile. It appears only when at
least one affected record exists and opens the problem-filtered list.

### Total

`รายการขายทั้งหมด` includes every seller-role record, including active,
completed, expired, cancelled, refunded, and paid-out records.

`มี N รายการที่ต้องจัดการ` includes only new offers and fulfillment-required
records. It excludes in-progress, problem-review, and completed records because
the seller has no immediate action in those categories.

Every seller record belongs to exactly one summary classification or the
completed/history group. Buyer-role records never contribute to seller counts.

## Priority spotlight

Seller mode shows at most one large `ต้องทำตอนนี้` card. Select it using this
fixed order:

1. overdue fulfillment or tracking that requires correction;
2. paid fulfillment, nearest exact ship-by deadline first;
3. new offer, nearest exact response deadline first;
4. other new offers; and
5. no spotlight when the seller has no actionable record.

Within the same priority and deadline, sort by newest creation time and then by
transaction ID for deterministic output.

Waiting-for-payment, in-transit, inspection, dispute-review, payout-progress,
and completed records never displace an actionable spotlight.

## Summary-filter interaction

The three summary counts are buttons, not decorative statistics:

- tapping `รอตอบ N` shows only new offers;
- tapping `ต้องส่ง N` shows only fulfillment-required records;
- tapping `กำลังไปต่อ N` shows only in-progress records;
- tapping the problem banner shows only disputed or resolution-pending
  records; and
- tapping `ดูทั้งหมด` restores all seller records.

The selected summary uses a filled treatment and an accessibility selected
state. Other summaries remain visible so the seller can switch in one tap.

After filtering:

- the spotlight is recalculated within the selected category;
- the remaining records stay below it;
- new offers are ordered by response deadline;
- fulfillment records are ordered by ship-by deadline and risk;
- in-progress records are ordered by most recent verified change; and
- problem records are ordered by most recent problem activity.

Changing a server state updates the counts, selected list, banner, and
spotlight together. The filter remains selected when its category still
contains records. If its count becomes zero, seller mode returns to
`ดูทั้งหมด`.

## Visual design

Retain the current mobile-first TOKLONG shell:

- white surfaces over the pale-blue radial page background;
- Buyer Blue for buyer mode;
- Seller Purple for seller mode and the spotlight;
- amber for new offers and response deadlines;
- purple for fulfillment-required work;
- blue for in-progress records;
- red only for overdue work or reported problems; and
- mint as a supporting confirmation accent.

Seller mode shows three equal summary tiles in one row:

- large count;
- short label;
- category-specific border and soft background.

The spotlight retains the existing large rounded-card pattern and one primary
button. It shows:

- reason for priority;
- product name;
- item price only;
- exact relevant deadline;
- current seller action; and
- no buyer protection fee or buyer total.

Remaining records use compact rounded cards. Each shows product name, seller
status, exact relevant deadline when one exists, item price, and one navigation
action. Do not duplicate full transaction detail in the list.

## Authenticated home behavior

The purple `ขาย` card adds a white pill at its upper-right corner:

`N ข้อเสนอใหม่`

Below its existing description, it adds a mint-dot line:

`มี N รายการที่ต้องจัดการ`

Rules:

- hide the offer pill when the new-offer count is zero;
- hide the work line when the actionable count is zero;
- keep the original card height when both are hidden;
- expose the combined seller-card accessibility description with both counts;
  and
- tapping anywhere on the card continues opening seller mode.

The home page refreshes seller counts when it appears. It does not poll every
five seconds while it is covered or backgrounded.

## Components and data flow

Add one pure mobile-core summary component, named `SellerWorkSummary`, that
accepts the current `AppTransaction` collection and returns:

- total seller count;
- new-offer count;
- fulfillment-required count;
- in-progress count;
- problem count;
- actionable count;
- selected-category records; and
- priority spotlight.

The component derives classifications from the existing role, state,
presentation action, deadlines, creation time, and update time. XAML and page
code must not independently interpret raw transaction state.

Classification uses this precedence so every seller record has one result:

1. `Disputed` or `ResolutionPending` becomes problem;
2. `ReviewSellerOffer` becomes new offer;
3. `AddTracking` or `ConfirmDigitalHandoff` becomes fulfillment required;
4. a completed presentation bucket becomes history; and
5. every other non-terminal seller record becomes in progress.

The actionable count is new offer plus fulfillment required. History contributes
to total only. This precedence also covers readable legacy states without
creating a new seller-first flow.

Add an authenticated-home view model that loads the existing
`ITransactionService.GetTransactionsAsync()` result and exposes only the
seller-card counts and loading/error state.

`TransactionsViewModel` continues loading the same authenticated transaction
endpoint and delegates seller classification, filtering, and spotlight
selection to `SellerWorkSummary`.

No new API endpoint, database field, transaction state, domain transition,
webhook behavior, audit event, or money calculation is required.

The transaction page retains its five-second refresh while visible and its
pull-to-refresh command. The home page refreshes on appearance. These screens
use the same summary implementation, so counts follow one rule even when the
underlying API snapshots were fetched at different times.

## Loading and error handling

Do not display zero counts before the first successful load.

On an initial load failure:

- hide numerical badges and summary counts;
- show `โหลดรายการไม่สำเร็จ · ลองอีกครั้ง`; and
- provide one retry action.

On a later refresh failure:

- retain the last successful records and counts;
- show `อัปเดตล่าสุดไม่สำเร็จ`;
- keep filters and navigation usable; and
- never replace known records with an empty collection.

An empty successful response shows the existing seller empty state and no
home-card badges.

If the selected category becomes empty after a successful refresh, return to
the all-seller view. If a record is no longer authorized or returned by the
API, remove it on the successful refresh rather than retaining stale access.

## Authorization and domain safety

The existing server remains authoritative:

- an unaccepted offer is returned only when its normalized intended-seller
  phone matches the authenticated phone;
- possession of an invitation link alone grants no access;
- seller fulfillment remains hidden until verified payment;
- a reported problem continues blocking payout;
- client counts never create, accept, fulfill, pay, refund, release, or pay out
  a transaction; and
- paid transaction snapshots remain immutable.

The workspace is presentation and navigation only. It creates no financial or
state-transition audit event.

## Accessibility

- Every summary button announces its label and exact count, for example
  `ข้อเสนอใหม่ 3 รายการ`.
- The selected summary announces that it is selected.
- The home seller card announces the new-offer and actionable counts.
- The problem banner announces that payout is stopped during review.
- Spotlight priority is communicated by text, not color alone.
- Deadlines use exact date and time in visible and semantic text.
- Touch targets meet the existing compact-control minimum.
- Dynamic text must not clip counts, labels, product names, or primary actions
  at supported accessibility sizes.

## Analytics

Record presentation analytics without personal data or product text:

- `seller_summary_filter_selected` with category and visible count;
- `seller_spotlight_opened` with action kind and state;
- `seller_problem_banner_opened` with visible count; and
- `seller_home_opened` with new-offer and actionable counts.

Do not include phone numbers, names, product names, transaction access tokens,
payment references, addresses, or provider credentials.

## Testing

Add mobile-core tests that verify:

- every supported seller state is classified once;
- buyer-role records are excluded;
- new-offer, fulfillment, progress, problem, actionable, and total counts;
- completed records affect only total/history;
- dispute states do not affect in-progress or actionable counts;
- priority ordering follows overdue, fulfillment deadline, offer deadline,
  creation time, and deterministic ID ordering;
- each summary filter returns the correct records;
- spotlight recalculates within a selected category;
- an emptied selected category returns to all records;
- home and transaction-page summaries produce identical counts from the same
  fixture;
- first-load failure exposes no false zero;
- refresh failure preserves the last successful collection and counts; and
- a successful empty refresh clears prior records.

Update UI contract and accessibility tests to verify:

- three seller summary buttons and their semantic descriptions;
- the conditional problem banner;
- the selected visual state;
- one spotlight card and compact remaining-record cards;
- the home seller-card offer pill and actionable line;
- hidden zero-count elements;
- seller-purple, amber, blue, red, and neutral treatments are not the only
  state indicators; and
- no seller list or spotlight exposes buyer protection fee or buyer total.

Retain API coverage that verifies:

- the intended authenticated seller phone can list and open every matching
  pending offer;
- another authenticated phone cannot list, read, accept, or decline it; and
- accepting one offer does not mutate or remove another offer.

Run the full mobile-core suite and iOS simulator build. Visually verify:

- no seller records;
- one new offer;
- three new offers plus one fulfillment-required record;
- a conditional problem banner;
- a selected summary filter;
- completed history mixed with active work;
- initial load failure and stale refresh failure; and
- default and accessibility text sizes.

## Assumptions

- The MVP transaction volume remains suitable for the existing authenticated
  transaction-list endpoint.
- The app may fetch a newer snapshot between the home page and transaction
  page; consistency means shared classification rules, not a permanently
  frozen count.
- Summary counts are seller-work navigation, not notification unread counts.
- A dispute may require attention but is not labeled actionable unless the
  domain exposes a specific authorized seller action.
- The existing transaction detail pages remain the only place for full terms,
  evidence, fulfillment detail, and payout detail.

## Out of scope

- bulk accept, decline, ship, refund, or payout actions;
- seller-created links or offers;
- search, marketplace discovery, storefronts, and product catalog management;
- custom seller labels, folders, or manual priority;
- cross-device read/unread synchronization;
- pagination or a new seller-summary API endpoint;
- changes to transaction states, deadlines, fees, payment truth, shipment
  verification, dispute decisions, or payout truth; and
- push-provider configuration.

## Success criteria

The design succeeds when a seller with multiple records can answer, without
opening each item:

1. How many new offers need a response?
2. How many paid records need fulfillment?
3. Is any record under problem review?
4. What is the single most urgent authorized action?
5. How many seller-side records exist in total?

The seller can then reach the relevant subset in one tap and the highest-risk
record in one additional tap, without any change to payment, fulfillment,
dispute, or payout authorization.
