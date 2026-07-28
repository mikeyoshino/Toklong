# SDD ledger — plan: docs/superpowers/plans/2026-07-28-seller-multiple-offer-workspace.md

Workspace: main checkout (user explicitly approved direct work on main)
Starting feature commit: a539118
Pre-existing working-tree changes: TransactionStatePresenter.cs, MauiProgram.cs,
TransactionPresentationTests.cs, DevelopmentSimulatorMobileSessionStore.cs,
DevelopmentSimulatorMobileSessionStoreTests.cs

Pre-flight ruling: user confirmed the latest discussed/approved design governs.
Therefore use risk ordering, `รอตอบ / ต้องส่ง / กำลังไปต่อ`, and hide buyer
protection fee/buyer total from seller surfaces despite older conflicting text.

Baseline: Toklong.Mobile.Core.Tests 204/204 passed before feature implementation.

Task 1: dispatched implementer at base a539118.
Task 1: reviewer approved spec and quality with no findings.
Task 1: controller resolved test-evidence warning by freshly running focused
25/25 and full mobile-core 229/229 successfully.
Task 1: complete (commits a539118..050b850, review clean).
Task 2: dispatched implementer at base 050b850.
Task 2: review found Important — provider-managed ShipmentOverdue is elevated
as manual AddTracking work. This conflicts with the plan's unconditional
ShipmentOverdue priority but violates the binding managed-shipping rule.
Awaiting user ruling before fix round 1.
Task 2 ruling: user confirmed the product changed from seller-managed tracking
to SHIPPOP-managed tracking. Provider-managed overdue/unverified records must
never expose manual AddTracking; legacy non-managed records remain readable.
Task 2: fix round 1/5 (1 addressed, 0 open — managed ShipmentOverdue now
status-only; commit 448c4bd).
Task 2: minor (deferred): provider-managed ShipmentOverdue retains numeric
priority 0 in status-only list ordering, but cannot become fulfillment or
spotlight action.
Task 2: controller verification focused 42/42 and full mobile-core 240/240.
Task 2: complete (commits 050b850..448c4bd, review clean).
Task 3: dispatched implementer at base 448c4bd.
Task 3: reviewer approved spec and quality.
Task 3: minor (deferred): assert complete dictionaries for all four analytics
events to guard against future extra properties.
Task 3: controller verification analytics 1/1, full mobile-core 241/241, iOS
compile exit 0 with existing NU1900 and CS0618 warnings only.
Task 3: complete (commits 448c4bd..bd153ef, review clean).
Task 4: dispatched implementer at base bd153ef.
Task 4: review found Important — seller in-progress/problem lists show the
large buyer `ยังไม่มีรายการ` spotlight placeholder despite having compact
records. Fix round 1 dispatched.
Task 4: fix round 1/5 (1 addressed, 0 open — buyer-only empty spotlight
presentation; commit 586aa5c).
Task 4: controller verification focused 7/7, full mobile-core 248/248, iOS
compile exit 0 with existing NU1900 and CS0618 warnings only.
Task 4: complete (commits bd153ef..586aa5c, review clean).
Task 5: dispatched implementer at base 586aa5c.
Task 5: review found two Important issues — actionable count row can overflow
at accessibility text sizes, and AuthenticatedHomeViewModel load/failure/retry
orchestration has no executing tests. Fix round 1 dispatched.
Task 5: minor (deferred): XAML test duplicates seller semantic assertion and
does not directly assert mint dot/actionable text structure.
Task 5: fix round 1/5 (2 addressed, 0 open — wrapping Auto,* actionable row
and direct home view-model orchestration tests; commit 6e1c047).
Task 5: controller verification focused 19/19, full mobile-core 254/254, iOS
compile exit 0 with existing NU1900 warning.
Task 5: complete (commits 586aa5c..6e1c047, review clean).
Task 6: dispatched verifier at base 6e1c047.
Task 6: review found three Important evidence gaps — invalid accessibility
large capture, missing VoiceOver/stale-refresh ceremony, and missing durable
logs/remaining runtime states — plus owned Simulator.app cleanup. Verification
fix round 1 dispatched; no source defect established.
Task 6: fix round 1/5 (accessibility, durable logs, runtime states, cleanup
addressed; crash discovered/fixed in 1a53c50; VoiceOver and exact pull gesture
remain open).
Task 6: verification fix round 2 dispatched for exact pull-to-refresh; spoken
VoiceOver remains a physical-device-only blocker.
Task 6: fix round 2/5 (0 addressed, 2 open — actual pull gesture blocked by
Simulator window_count=0; spoken VoiceOver blocked by no physical device).
Task 6: awaiting user ruling whether to accept these manual-only verification
gaps and proceed to final review, or wait for a native UI runner/device.
Task 6: user accepted both external manual-verification gaps and authorized
proceeding to final review.
Task 6: parked — exact pull-to-refresh gesture unverified — ruling: no
Simulator device window/native UI runner; stale behavior and recovery verified
through OnAppearing plus direct view-model tests.
Task 6: parked — spoken VoiceOver unverified — ruling: Apple does not support
VoiceOver in Simulator and no physical iOS device is connected; semantic
bindings/output and Accessibility Large were verified.
Task 6: complete (commits 6e1c047..1a53c50, 2 manual-only gaps parked by user).
Final review: four findings accepted for one direct-main fix wave at baseline
1a53c50304a844c53d330b3d625c62f5dc2f6351.
Final fix wave: required docs, spec, plan, reports, review diff, and debugging /
TDD / verification skills read in full before source changes.
Final fix wave RED: session-boundary test compilation failed on the missing
boundary/reset/generation API; managed-buyer ShipmentOverdue failed
ActionRequired vs InProgress; deadline layout failed missing WordWrap/full-width
contract.
Final fix wave GREEN: focused 6/6; adjacent mobile-core 93/93; full Domain
85/85, Application 158/158, API 42/42, CRM 45/45, Mobile Core 261/261
(591 total); exact iOS simulator build exit 0 with 0 errors and the existing
NU1900 warning.
Final fix wave: session reset now clears singleton home/list state before logout
navigation and generation-checks reject prior-session results; managed overdue
override is seller-only; list EmptyView requires a successful load; spotlight
and compact deadlines use full-width wrapping rows.
Final fix wave manual gap: XAML contracts and iOS source/build compilation cover
the layout mechanism, but no native runner was available to render the long
Thai fixture at narrow width and Accessibility Large; no manual-native claim
was made.
Final fix wave repository hygiene: unrelated pre-existing changes in
TransactionStatePresenter.cs, the simulator-session MauiProgram hunk,
TransactionPresentationTests.cs, DevelopmentSimulatorMobileSessionStore.cs,
and DevelopmentSimulatorMobileSessionStoreTests.cs remained unstaged.
