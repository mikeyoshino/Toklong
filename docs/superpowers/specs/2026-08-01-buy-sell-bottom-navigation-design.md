# Buy/Sell Bottom Navigation Design

**Date:** 2026-08-01  
**Status:** Approved in brainstorming; awaiting written-spec review

## Objective

Make buyer and seller workspaces immediately reachable from native bottom
navigation while moving the existing Activity feed to a consistent top-right
entry point.

The resulting authenticated navigation is:

```text
Bottom navigation
  → ซื้อ
  → ขาย
  → บัญชี

Top-right action on all three roots
  → กิจกรรม
```

This is a navigation and presentation change. It does not change transaction
commands, domain states, payment verification, fulfillment, disputes, refunds,
or payout rules.

## Product boundaries

- `ซื้อ` and `ขาย` are workspaces, not permanent account roles. One account may
  participate in both roles.
- The buyer remains the only party who creates an MVP private offer.
- The seller workspace does not gain listings, discovery, storefronts, bidding,
  or a seller-created link action.
- Activity remains a feed of existing transaction notifications. It does not
  become chat, a task engine, or a source of transaction truth.
- Navigation must never create or mutate a transaction.
- Existing role colors and Noto Sans Thai fonts remain unchanged.

## Approved direction

Use the approved **Action First** workspace design:

- fixed native bottom tabs for `ซื้อ`, `ขาย`, and `บัญชี`;
- separate buyer and seller root workspaces;
- a global Activity bell at the top right of every root;
- one visually dominant role-appropriate action or next step;
- the most urgent applicable transaction before the remaining list; and
- no redundant in-page `ซื้อ | ขาย` switch.

The design borrows the navigation clarity of established finance apps without
copying their trading density. Coinbase and Wise place notifications in a home
screen notification or inbox hub, while Binance uses prominent role/action
entry points and top-right controls. TOKLONG keeps the pattern calm and
transaction-focused because it has only two transaction roles and no market
data or marketplace discovery.

Reference material:

- Coinbase notification hub:
  <https://help.coinbase.com/en-in/coinbase/managing-my-account/other/manage-notifications>
- Wise homepage inbox bell:
  <https://wise.com/help/articles/2952327/how-do-i-manage-my-notifications>
- Binance P2P navigation and top-right controls:
  <https://academy.binance.com/ur-PK/articles/what-is-binance-p2p-and-how-to-use-it>

## Root navigation

### Bottom destinations

The authenticated `TabBar` contains exactly three visible destinations in this
order:

1. `ซื้อ`
2. `ขาย`
3. `บัญชี`

Each destination uses an icon plus a visible Thai label. Selected state is
communicated by native tab state and text rather than color alone. The native
tab bar may retain the existing Brand Blue selected color on both platforms;
the workspace content carries the stronger blue or purple role identity.

`กิจกรรม` is removed from the bottom bar. The existing Account destination
remains the third tab.

### Role restoration

- On the first authenticated use, open `ซื้อ`.
- Selecting `ซื้อ` or `ขาย` stores that role locally as the preferred role.
- On later normal launches, restore the last stored Buy/Sell role.
- Visiting `บัญชี` does not replace the stored role preference.
- A missing, invalid, or unreadable preference falls back to `ซื้อ`.
- Explicit logout clears the stored role so another account on the device
  starts on `ซื้อ`. Ordinary app closure or session refresh preserves it.
- The saved role is presentation-only and conveys no authorization.

### Deep-link precedence

Notification, invitation, and transaction deep links keep their existing
destinations and take precedence over the stored role.

Deep-link navigation does not overwrite the preferred role. Only an explicit
Buy or Sell root-tab selection changes the preference.

After leaving a deep-linked transaction, navigation returns to the applicable
authenticated root without changing transaction state. A deep link must never
be redirected merely because another role was remembered.

### Authenticated home

The current two-card authenticated role chooser is removed from the normal
signed-in path. Successful authentication and valid-session startup resolve
directly to the remembered Buy/Sell root.

The old route may temporarily redirect to the resolved role during migration,
but it must not remain a second role-selection interface. Once route callers
and tests are migrated, the unused chooser page may be removed.

## Shared root header

Buy, Sell, and Account use one shared root-header pattern:

- page identity on the left;
- Activity bell on the right;
- safe-area-aware top spacing on Android and iOS;
- a minimum 44 by 44 point/dp bell target; and
- semantic label `กิจกรรม` with a hint that it opens transaction updates.

The header preserves the current theme. Buy uses Buyer Blue, Sell uses Seller
Purple, and Account remains neutral.

No unread badge is required in this slice because the current activity model
does not expose authoritative unread state. A badge or dot may be added only
when backed by real unread data and tested read-state behavior. The design must
not render a permanent or inferred badge.

## Buy workspace

The Buy root is fixed to buyer-role records and removes the existing role
switch.

Content order:

1. shared root header with `ซื้อ` and Activity bell;
2. full-width `+ สร้างดีลซื้อ` primary action;
3. one buyer action spotlight when an actionable record exists;
4. buyer filters where they remain useful;
5. `รายการซื้อ` list; and
6. loading, error, or empty state in the list region.

The spotlight uses the existing buyer prioritization and transaction
presentation rules. It shows the applicable next action and exact deadline; it
does not independently interpret raw transaction state.

The buyer empty state states `ยังไม่มีรายการซื้อ` and repeats one
`สร้างดีลซื้อ` action. It must not introduce product discovery or a marketplace.

## Sell workspace

The Sell root is fixed to seller-role records and removes the existing role
switch.

Content order:

1. shared root header with `ขาย` and Activity bell;
2. existing seller work summaries for response, fulfillment, and progress;
3. one seller priority spotlight when an actionable record exists;
4. seller category filters;
5. `รายการขาย` list; and
6. loading, error, or empty state in the list region.

The workspace keeps the existing seller summary and priority rules. It does not
add a create-listing, create-sales-link, or generic seller action.

The seller empty state states `ยังไม่มีรายการขาย` and explains that buyer-created
offers will appear here. It has no primary creation action.

## Account root

Account keeps its existing content and behavior. Its only navigation change is
the shared top-right Activity bell.

Opening Account does not change the stored Buy/Sell preference. Returning from
Account by selecting Buy or Sell updates the preference to the newly selected
role.

## Activity hub

Tapping the Activity bell pushes the existing Activity page as a non-root page.

- Title: `กิจกรรม`
- Visible Back action.
- Bottom navigation is hidden while Activity is open.
- Back returns to the exact root that opened Activity.
- Selecting an activity item uses its existing destination and opens the
  applicable transaction or seller offer.
- Returning from an activity destination returns through Activity and then to
  the originating root according to the native navigation stack.

The Activity page retains its existing feed, loading, refresh, empty, and error
behavior. This slice does not add notification settings, read receipts, bulk
mark-as-read, chat, or new backend notification fields.

If Activity loading fails, keep the Back action available, retain any previously
loaded feed where current behavior permits, and expose the existing retry path.
The UI must not imply that a lifecycle event did or did not occur based only on
feed loading.

## Component architecture

### Shell roots

`AppShell` owns three native authenticated roots:

```text
Buy root      → fixed buyer workspace
Sell root     → fixed seller workspace
Account root  → existing account page
```

Activity is registered as a pushed route, not a `ShellContent` inside the
`TabBar`.

### Shared workspace

Use one shared transaction-workspace view for common presentation, with the
role fixed by its owning root page.

The Buy and Sell pages must have separate page and ViewModel instances. The
current singleton role-switching ViewModel must not be shared simultaneously by
two native tabs, because changing filters or refreshing one workspace could
otherwise leak state into the other.

The shared unit owns only common UI composition. Existing pure presentation
components remain authoritative for:

- buyer/seller filtering;
- seller summary classification;
- priority spotlight selection;
- status and next-action copy; and
- role-specific amount presentation.

XAML must not add a second raw-state classifier.

### Preferred-role service

Add one small presentation service that reads and writes the preferred
Buy/Sell role using platform preferences.

Its contract is limited to:

- return the stored valid role or Buy fallback;
- save Buy or Sell after explicit root selection; and
- ignore Account and pushed-route navigation.

This preference is not transaction data, authorization, an audit record, or an
account profile field.

## Data and navigation flow

```text
Valid authenticated startup
  → read preferred Buy/Sell role
  → route to Buy fallback or stored role
  → load existing authenticated transaction collection
  → fixed-role workspace derives its own records and spotlight

Bottom-tab selection
  → switch native root
  → save role only for Buy or Sell
  → load/refresh through existing transaction service

Activity bell
  → push Activity route
  → existing ActivityViewModel loads feed
  → item opens existing destination
  → Back restores originating stack
```

No new API endpoint, database field, payment-provider operation, carrier
operation, transaction transition, money calculation, webhook behavior, or
domain audit event is required.

Navigation analytics may record a non-sensitive `workspace_opened` event with:

- role: `buying` or `selling`;
- source: `startup`, `tab`, or `deep_link`; and
- no phone number, transaction details, counterparty, or monetary value.

## Loading and error behavior

- Role restoration must not display the obsolete authenticated chooser while
  resolving the destination.
- Initial workspace loading uses the existing neutral loading treatment.
- A workspace load failure keeps the selected tab, shows the existing retry
  action, and never falls through to the opposite role.
- A later refresh failure preserves already loaded records where supported.
- Switching tabs during a load must not let a stale response replace the other
  role's visible data.
- Account loading errors remain isolated from Buy and Sell.
- Activity errors remain isolated from all three root stacks.

## Accessibility and responsive behavior

- Use native tab semantics and selected-state announcements.
- Every tab has a visible label; role is not conveyed only through blue or
  purple.
- The Activity bell is one focusable control with a 44 by 44 minimum target.
- Decorative header icons are excluded from the accessibility tree.
- Thai titles, status labels, counters, and deadlines support large text and
  word wrapping without horizontal scrolling.
- Bottom content respects Android gesture/navigation insets and the iOS home
  indicator.
- The root header respects status-bar and Dynamic Island/notch safe areas.
- Focus returns sensibly to the bell or originating page heading after Back.
- Empty, loading, and error states occupy a stable content region to reduce
  layout shifts.

## Testing

### Core and navigation tests

- Authenticated first use opens Buy.
- Valid stored Buy and Sell roles restore correctly.
- Missing or invalid preference falls back to Buy.
- Visiting Account does not replace the stored role.
- Explicit Buy/Sell tab selection updates the preference.
- Deep links override the preference and retain their existing authorization.
- Activity opens from Buy, Sell, and Account and returns to the originating
  root.
- Buy and Sell page/ViewModel instances do not share filters, busy state, or
  refresh results.
- Stale loads cannot populate the opposite workspace.

### Static UI tests

- Bottom navigation exposes exactly `ซื้อ`, `ขาย`, and `บัญชี` in order.
- Activity is absent from the `TabBar` and registered as a pushed route.
- The old in-page `ซื้อ | ขาย` switch is absent.
- All three roots expose the shared Activity action.
- Buy retains `+ สร้างดีลซื้อ`.
- Sell exposes no seller-created listing or link action.
- Existing theme resources and font registration remain unchanged.

### Platform smoke tests

On one narrow iPhone and one narrow Android device or simulator:

- verify selected tabs, safe areas, and bottom gesture insets;
- verify first launch and remembered-role relaunch;
- open and return from Activity on every root;
- test large text and long Thai deadlines;
- test VoiceOver and TalkBack traversal order; and
- verify deep-link entry and Back behavior.

All existing state-transition, authorization, payment webhook, carrier,
dispute, digital-release, refund, and payout tests remain required and must not
change merely to accommodate navigation.

## Documentation changes required during implementation

This approved design intentionally supersedes the navigation portions of:

- `docs/02_UI_UX_AND_CONTENT_SPEC.md` that require the authenticated two-card
  home and one in-page `ซื้อ | ขาย` switch;
- `docs/05_ACCEPTANCE_TESTS.md` scenarios that enter role modes through the
  authenticated home; and
- `docs/superpowers/specs/2026-07-28-authenticated-home-and-create-deal-design.md`
  only where it defines the authenticated role-home navigation.

The buyer-first create flow and all transaction lifecycle requirements in those
documents remain in force.

## Out of scope

- Marketplace discovery, listings, storefronts, bidding, or seller-created
  deal links.
- A new home/dashboard tab.
- Chat or messaging.
- Unread notification persistence or badge counts.
- Notification settings.
- Backend transaction, payment, fulfillment, dispute, refund, or payout
  changes.
- Theme, color-palette, or font changes.
