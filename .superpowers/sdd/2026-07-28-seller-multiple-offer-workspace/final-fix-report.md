# Final Fix Report — Seller Multiple-Offer Workspace

Date: 2026-07-28 (Asia/Bangkok)

## Status

All four final-review findings were fixed in one direct-`main` wave from
baseline `1a53c50304a844c53d330b3d625c62f5dc2f6351`.

The five automated suites pass with 591 tests and zero failures. The exact iOS
simulator build succeeds with zero errors.

## Root causes

### 1. Cross-account singleton state

`AuthenticatedHomeViewModel`, `TransactionsViewModel`, and their pages are
singletons. `AccountViewModel` previously cleared credentials and navigated to
Welcome without notifying either workspace. Each workspace therefore retained
its last successful `SellerWorkspaceState`; `TransactionsViewModel` also
retained `allTransactions`, its observable collection, spotlight, error, and
selected category.

Neither load method captured an authenticated-session generation. A request
started by account A could therefore complete after logout and repopulate the
singleton while account B was signing in.

### 2. Managed ShipmentOverdue role leak

`AppTransaction.Presentation` had seller-role protection for the
provider-managed `PaidAwaitingShipment` override, but its
`ShipmentOverdue` override checked only `ShippingManagedByProvider`. That
presentation-only override also changed a buyer record from ActionRequired to
InProgress.

### 3. False successful-empty list

`TransactionsPage` supplied an unconditional `CollectionView.EmptyView`.
Before the first successful response, the observable collection is empty, so
MAUI rendered successful-empty copy during initial loading and initial failure.
The view model exposed no first-success signal for this surface.

### 4. Deadline truncation

The spotlight placed its amount and deadline in competing `*,Auto` columns.
Compact buyer and seller cards placed the deadline in the same constrained row
as the action; both explicitly used `TailTruncation` and `MaxLines="1"`.
Long Thai exact dates could therefore be elided at narrow or accessibility text
sizes.

## What changed

- Added one singleton `AuthenticatedSessionBoundary` with an incrementing
  generation and synchronous reset notification.
- `AccountViewModel` now clears its profile, resets authenticated workspaces,
  then signs out and navigates. Workspace data is gone before logout
  navigation.
- Both singleton workspace view models reset snapshots, collections,
  spotlight, errors, selected filters, success flags, and busy/refresh state.
- Both load paths capture the current generation and ignore success, failure,
  analytics, and final busy-state writes from a prior session.
- `SellerWorkspaceState.Reset()` restores a true never-loaded state.
- `TransactionsViewModel.ShowTransactionCollectionEmptyState` becomes true
  only after a successful load whose remaining collection is empty. Initial
  failure leaves it false; a same-session failed refresh retains the last
  successful empty/non-empty meaning.
- Added the seller-role guard to the provider-managed `ShipmentOverdue`
  override. Managed sellers remain status-only; managed buyers keep their
  existing ActionRequired/ViewStatus presentation; unmanaged sellers keep
  AddTracking.
- Moved spotlight deadlines to a full-width wrapping stack.
- Moved compact buyer and seller deadlines to a second full-width row and
  removed one-line tail truncation.
- Kept seller amount bindings on `ItemPriceText`; no buyer protection fee or
  buyer total was introduced.
- Added test-only MAUI command/navigation/preference boundaries so the plain
  Mobile Core suite exercises the real account and transaction-list view
  models.

## State transitions and domain scope

No backend state transition, authorization rule, webhook, money calculation,
payment truth, shipment truth, dispute decision, refund, or payout behavior
changed.

The `ShipmentOverdue` fix restores existing buyer presentation and preserves:

- managed seller: InProgress / ViewStatus;
- managed buyer: ActionRequired / ViewStatus;
- unmanaged seller: ActionRequired / AddTracking.

SHIPPOP-managed sellers still never receive manual AddTracking. Legacy
unmanaged records remain actionable.

## RED evidence

Tests were written before production changes.

1. Session/empty-state RED:

```text
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~ViewModelSessionBoundaryTests|..." --no-restore
```

The build failed on the intentionally missing
`AuthenticatedSessionBoundary`, new constructor dependencies,
`ShowTransactionCollectionEmptyState`, and testable sign-out orchestration.

2. Managed ShipmentOverdue RED:

```text
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter FullyQualifiedName~AppTransactionManagedShipmentPresentationTests \
  --no-restore
```

Result after correcting the test fixture itself: 1 failed, 2 passed. The
managed buyer expected `ActionRequired` but received `InProgress`.

3. Deadline-layout RED:

```text
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter FullyQualifiedName~TransactionDeadlines_UseFullWidthWrappingRows \
  --no-restore
```

Result: 1 failed. The spotlight deadline had no `WordWrap` contract.

## GREEN evidence

Focused final regressions:

```text
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~ViewModelSessionBoundaryTests|FullyQualifiedName~AppTransactionManagedShipmentPresentationTests|FullyQualifiedName~TransactionDeadlines_UseFullWidthWrappingRows" \
  --no-restore
```

Result: 6 passed, 0 failed.

Adjacent mobile coverage:

```text
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~AuthenticatedHomeViewModelTests|FullyQualifiedName~ViewModelSessionBoundaryTests|FullyQualifiedName~SellerWorkspaceStateTests|FullyQualifiedName~SellerWorkSummaryTests|FullyQualifiedName~AppTransactionManagedShipmentPresentationTests|FullyQualifiedName~UiLayoutConsistencyTests|FullyQualifiedName~TransactionFilterTests" \
  --no-restore
```

Result: 93 passed, 0 failed.

The account-switch regression proves this sequence:

1. account A successfully loads home counts, products, counterparties, list,
   and spotlight;
2. account A starts two pending refreshes;
3. sign-out synchronously clears both singleton view models before the
   authentication call and Welcome navigation;
4. account B's first home and list loads fail;
5. both late account A results complete; and
6. no account A count, product, counterparty, collection item, or spotlight is
   observable.

The empty-state regression proves initial false, first failure false with an
error, successful empty true, failed refresh after successful empty true with
stale error semantics, and session reset false.

## Full verification

All commands were run fresh after the implementation:

| Suite | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Domain | 85 | 0 | 0 |
| Application | 158 | 0 | 0 |
| API | 42 | 0 | 0 |
| CRM | 45 | 0 | 0 |
| Mobile Core | 261 | 0 | 0 |
| **Total** | **591** | **0** | **0** |

Exact iOS simulator build:

```text
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj \
  -p:TargetFrameworks=net10.0-ios \
  -f net10.0-ios \
  -r iossimulator-arm64 \
  --no-restore \
  -p:NuGetAudit=false
```

The sandboxed attempt compiled `Toklong.Mobile.dll` but did not terminate,
matching Task 6's documented post-build sandbox hang. Only that owned build
process was stopped. The exact command was rerun in the permitted context and
completed in 3.77 seconds:

```text
Build succeeded.
1 Warning(s)
0 Error(s)
```

The warning is the existing `NU1900` inability to retrieve NuGet vulnerability
metadata. The earlier focused compile also reported the existing obsolete
single-photo picker warning; the final incremental build reported only
`NU1900`.

`git diff --check` passed.

## Layout and accessibility evidence / manual gaps

Automated XAML contracts prove:

- spotlight deadline uses `WordWrap`, has no line cap, and has no competing
  deadline column;
- compact buyer and seller deadlines use a second row spanning both columns,
  `WordWrap`, and no `MaxLines`;
- the CollectionView empty surface is gated by the successful-load property;
  and
- seller cards and spotlight continue using item price rather than buyer total
  or buyer protection fee.

The iOS XAML source generator and simulator build compile those contracts.
The presentation fixture includes long Thai product/counterparty text, a
30,000 THB item amount, an exact Thai date/time, and the three managed/unmanaged
role variants.

No native UI runner was available in this final-fix turn to render that fixture
at a specific narrow viewport and Accessibility Large. No screenshot,
physical-device, spoken VoiceOver, or native manual-layout claim is made.
Those remain manual/native verification gaps rather than inferred success.

## Tests added or updated

- Added `ViewModelSessionBoundaryTests` for cross-account clearing, old-result
  rejection, initial/refresh empty-state semantics, and reset.
- Added `AppTransactionManagedShipmentPresentationTests` for the three required
  managed/unmanaged role cases.
- Added the full-width/wrapping and EmptyView XAML contract.
- Updated authenticated-home tests for the shared session boundary.
- Linked the real `AccountViewModel` and `TransactionsViewModel` into Mobile
  Core tests with narrow test-only MAUI doubles.

## Changed files

Production:

- `src/Toklong.Mobile/Core/AppTransaction.cs`
- `src/Toklong.Mobile/Core/AuthenticatedSessionBoundary.cs`
- `src/Toklong.Mobile/Core/SellerWorkspaceState.cs`
- `src/Toklong.Mobile/MauiProgram.cs` — only the session-boundary registration
- `src/Toklong.Mobile/Pages/TransactionsPage.xaml`
- `src/Toklong.Mobile/ViewModels/AccountViewModel.cs`
- `src/Toklong.Mobile/ViewModels/AuthenticatedHomeViewModel.cs`
- `src/Toklong.Mobile/ViewModels/TransactionsViewModel.cs`

Tests:

- `tests/Toklong.Mobile.Core.Tests/AppTransactionManagedShipmentPresentationTests.cs`
- `tests/Toklong.Mobile.Core.Tests/AuthenticatedHomeViewModelTests.cs`
- `tests/Toklong.Mobile.Core.Tests/MauiViewModelTestDoubles.cs`
- `tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj`
- `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`
- `tests/Toklong.Mobile.Core.Tests/ViewModelSessionBoundaryTests.cs`

Process evidence:

- `.superpowers/sdd/2026-07-28-seller-multiple-offer-workspace/progress.md`
- `.superpowers/sdd/2026-07-28-seller-multiple-offer-workspace/final-fix-report.md`

## Repository preservation

These unrelated pre-existing changes were preserved and excluded from the fix
staging set:

- `src/Toklong.Mobile/Core/TransactionStatePresenter.cs`
- the development-simulator session-store hunk in
  `src/Toklong.Mobile/MauiProgram.cs`
- `tests/Toklong.Mobile.Core.Tests/TransactionPresentationTests.cs`
- `src/Toklong.Mobile/Core/DevelopmentSimulatorMobileSessionStore.cs`
- `tests/Toklong.Mobile.Core.Tests/DevelopmentSimulatorMobileSessionStoreTests.cs`

No unrelated cleanup was performed.

## Assumptions

- The authenticated session boundary advances at sign-out. A later successful
  sign-in naturally uses the new generation; no separate account identity is
  needed in client presentation state.
- Keeping the remembered buy/sell role across sessions remains the approved
  transaction-root behavior. Session reset clears status/category filters to
  All.
- Event delivery is synchronous because sign-out originates on the UI thread
  and all three consumers are application singletons.

## Open decisions and provider capabilities

No provider capability blocks this client-only fix. Existing production gates
remain unchanged: SHIPPOP commercial credentials/certification, approved
third-party seller payment and bank payout flow, and supported digital-transfer
policy review.

## Commit

This report is included in the single final fix commit. A Git commit cannot
contain its own SHA-1/SHA-256 identifier without changing that identifier, so
the exact resulting commit hash is reported in the completion response and by
`git rev-parse HEAD`.

## Next smallest vertical slice

Add a native iOS UI test fixture that renders the long Thai
deadline/action/30,000 THB combination on the narrow supported phone size at
Accessibility Large, then captures the accessibility tree and screenshot. This
would close the only layout-verification gap without changing product behavior.
