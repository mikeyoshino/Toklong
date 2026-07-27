# TOKLONG Logo and Startup Motion Design

**Date:** 2026-07-27  
**Status:** Approved visual direction; written specification awaiting review

## Purpose

Replace the inconsistent TOKLONG marks with one approachable fintech identity
and add a short mobile logo-build animation after the operating-system launch
screen.

The identity should feel friendly to ordinary social-commerce buyers and
sellers while retaining the precision and confidence expected from a payment
product. It must not use a baht sign, coin, banknote, wallet, safe, lock, shield,
or imagery that implies TOKLONG itself holds customer money.

## Approved direction

The selected concept is **Transaction Rail**.

The mark consists of:

- one upper rounded rail entering from the left;
- one lower rounded rail entering from the right;
- the two rails meeting at the center to form one continuous, compact symbol;
- a Mint confirmation node at the connection point; and
- the uppercase `TOKLONG` wordmark when horizontal space permits.

The two rails represent the buyer and seller entering one shared transaction.
Their structured movement and balanced geometry provide the financial signal
without showing currency. The Mint node means that the applicable step has been
confirmed; it does not represent custody, payout completion, or guaranteed
safety.

## Visual system

### Shape

- Use thick, round-ended rails with no arrowheads.
- Keep the silhouette legible at 24 px and distinct at app-icon size.
- Preserve an open center around the Mint node so the mark does not become a
  generic chain link or letter `S`.
- The static mark is the completed final animation frame.
- Do not add a checkmark inside the node; the change of color and pulse supply
  the confirmation signal.

### Color

- Primary rail: Brand Blue `#2B7FFF`.
- Secondary rail: Sky Blue `#73C8FF`.
- Confirmation node: Mint `#65D6BF`.
- Primary wordmark: Ink `#122A47` on light surfaces.
- Reversed rail/wordmark: white on Brand Blue, retaining the Mint node.
- Startup background: the existing soft white/blue launch surface
  `#F6FAFF`.

Color is not the only means of identification: the joined geometry remains
recognizable in monochrome, and accessible text names the brand where a
semantic description is exposed.

### Wordmark

Retain the uppercase `TOKLONG` name and the current bold, rounded system/Noto
Sans presentation for this slice. A custom letterform project is out of scope.
Use optical spacing rather than inserting visible separators between the mark
and name.

### Required brand surfaces

Use the same completed mark on:

- the mobile app icon;
- `BrandLockupView`;
- mobile in-product brand imagery;
- the landing-page header and footer; and
- the branded AI-assist scan icon, simplified as needed at 24 px.

The app icon and compact in-product mark omit the wordmark. The landing page and
authentication lockup use the horizontal mark plus wordmark.

The native static splash is the one permitted motion-specific adaptation: it
shows the two separated rail endpoints on the same background and alignment as
the first in-app animation frame. It is not a competing logo.

## Startup motion

The motion is an **animated logo reveal**, specifically a **logo build
animation**. The complete mobile startup treatment may be described as an
**animated splash screen**, but the operating-system launch screen itself
remains static.

### Sequence

The in-app sequence lasts 1.2 seconds:

1. **Arrive — 0–250 ms:** the upper rail enters from the left and the lower
   rail enters from the right.
2. **Connect — 250–650 ms:** both rails settle into the completed Transaction
   Rail geometry using a smooth ease-in/ease-out curve.
3. **Confirm — 650–850 ms:** the Mint node scales into place and emits one
   restrained outward pulse.
4. **Enter — 850–1,200 ms:** the `TOKLONG` wordmark fades and moves a short
   distance into place, then the application replaces the intro with the
   correct authenticated or unauthenticated destination.

There is no spin, bounce, sound, particle effect, vibration, repeated pulse, or
loop in the application. The browser prototype loops only so reviewers can
inspect the motion.

### Native launch handoff

The operating-system launch screen remains a static MAUI splash. It shows the
two separated rail endpoints from the animation's initial frame. Its
background, scale, and central alignment exactly match the in-app animation so
the endpoints continue moving inward without a flash, reverse movement, or
completed-logo jump.

The app determines the initial route while the animation is playing:

- a valid session continues to `//transactions`;
- no valid session continues to `//welcome`;
- pending deep-link and push initialization occurs only after the Shell is
  active, preserving the existing authorization checks.

The animation must never claim that payment, delivery, or payout succeeded. It
is a brand confirmation gesture only.

### Replay policy

- Play once per cold application launch.
- Do not replay when the app resumes from the background.
- Do not add a settings switch for replay in this MVP.
- Authentication and startup I/O run concurrently with the animation; the
  animation must not add more than its 1.2-second presentation floor.
- If startup work exceeds the animation, retain the final assembled mark
  without looping while routing completes.
- If startup work fails, route to the existing safe unauthenticated/error
  experience rather than leaving the user on the logo.

## Accessibility

- When the platform requests reduced motion, show the completed static mark
  immediately and continue routing without the 1.2-second animation floor.
- Expose one semantic description, `โลโก้ TOKLONG`, and exclude individual
  decorative rail pieces from the accessibility tree.
- Do not announce intermediate animation states.
- Keep the final mark readable in high contrast and monochrome.
- The intro does not accept input and must not trap focus.

## Mobile architecture

Use a dedicated startup presentation rather than adding the intro to the
welcome page. This keeps authenticated and unauthenticated launches consistent
and prevents the animation from entering Shell navigation history.

The implementation has three focused units:

1. `StartupCoordinator`: determines the initial route and coordinates the
   animation minimum duration without changing authentication truth.
2. `IStartupMotionPreference`: reports the platform reduced-motion preference
   through a testable abstraction.
3. `StartupLogoPage` and a reusable `TransactionRailMarkView`: render and
   animate the two rails, node, and wordmark.

`App.CreateWindow` initially presents `StartupLogoPage`. After coordination
finishes, it installs the existing `AppShell`, navigates to the resolved route,
then initializes push registration and resumes authorized pending deep links.
The startup page is discarded and is not reachable with Back navigation.

Use MAUI-native vector paths and view animations. Do not add a web view, GIF,
video, Lottie package, or another runtime dependency for this short animation.

## Failure and cancellation behavior

- Cancel view animations when the startup page is removed or the window is
  destroyed.
- A route-resolution exception falls back to the welcome route and records a
  diagnostic event without exposing credentials or session contents.
- Push registration failure remains non-blocking, matching the existing
  behavior.
- A rapid lifecycle transition must not create two Shell instances or navigate
  twice.

## Verification

Add or update tests for:

- initial route selection with and without a valid session;
- one-time startup completion and duplicate-callback safety;
- reduced motion skipping the animation delay;
- normal motion honoring the designed sequence duration;
- startup failure falling back safely;
- the startup page not being part of Shell history;
- presence of one accessible logo description and decorative child elements
  excluded from semantics;
- shared use of the new mark across the app icon, splash, brand lockup, landing
  header/footer, and branded AI-assist icon; and
- changed mobile pages passing existing accessibility/layout checks.

Run mobile core tests, project type checking/build for the supported target
available in the development environment, and visual checks at small app-icon
size, normal launch, reduced motion, signed-out launch, and signed-in launch.

## Non-goals

- No custom wordmark typeface.
- No payment-state animation or claim of payment success.
- No animation on every foreground resume.
- No audio or haptic branding.
- No marketplace, wallet, stored-value, escrow, or guaranteed-safety imagery.
- No change to transaction states, payment rules, authorization, or audit
  behavior.
