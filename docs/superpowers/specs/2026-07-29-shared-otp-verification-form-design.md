# Shared OTP Verification Form Design

**Date:** 2026-07-29  
**Status:** Approved

## Goal

Make the six-digit OTP form look and behave consistently on the phone-login
verification page and the email-change verification page, without coupling
their view models or workflow state.

The physical-device Account crash found during verification is included in
this slice because it blocks access to the email-change flow.

## Current problems

1. `AccountPage.xaml` and `VerifyEmailChangePage.xaml` reference
   `BrandBlueSoft`, but that application resource is missing. On iOS this
   throws a `XamlParseException` when Account is first created and terminates
   the app.
2. Both OTP pages use `OtpCodeInput`, but each page builds the surrounding
   form independently.
3. The email-change version wraps the input in `RefinedFormCard` and adds a
   duplicate field label. On a phone with the numeric keyboard open this
   produces a tall empty card that does not match the login OTP pattern.

## Chosen design

Create a reusable `OtpVerificationFormView` presentation component.

The component owns only the shared form layout:

- the existing `OtpCodeInput`;
- the primary confirmation button;
- the busy label/disabled behavior for that button; and
- an optional Development hint.

It does not own, resolve, or reference a view model. It does not keep workflow,
session, retry, timer, resend, navigation, or server state.

Pages provide values using bindable properties and commands:

- `Code`;
- `ConfirmCommand`;
- `CanConfirm`;
- `IsBusy`;
- `ConfirmText`;
- `BusyText`;
- `ConfirmSemanticDescription`;
- `DevelopmentHint`; and
- `HasDevelopmentHint`.

`Code` uses two-way binding. All other inputs flow from the page binding
context. The component contains no network call and no navigation.

## Page responsibilities

`VerifyCodePage` continues to own:

- the Login header and phone destination;
- edit-phone action;
- resend action and countdown;
- validation/error message;
- activity outside the shared form; and
- Login-specific navigation.

`VerifyEmailChangePage` continues to own:

- the email-change header and masked destination;
- resend/expiry/locked/recovery actions;
- validation/error announcement and focus bridge;
- email-change navigation; and
- all session-generation safeguards.

Neither page shares a view model or state object with the other.

## Visual behavior

Both pages use the Login OTP form pattern:

- no tall white `RefinedFormCard` around the six digits;
- no duplicate “รหัสยืนยัน 6 หลัก” form label above the digits;
- six digit positions rendered by `OtpCodeInput`;
- confirmation button directly below the input; and
- optional Development hint between the input and button.

The email destination card, resend controls, terminal-state actions, and error
summary remain outside the shared component.

## Crash correction

Declare `BrandBlueSoft` in the application resource dictionary using the
existing soft blue surface value. Both Account and email verification then
resolve the same semantic color token at runtime.

Add a resource-consistency regression covering Account and email verification
so a missing `StaticResource` fails in tests instead of crashing on first
navigation.

## Accessibility

- `OtpCodeInput` remains the single accessible OTP entry.
- Decorative digit labels remain outside the accessibility tree.
- The shared confirmation button accepts page-specific semantic copy.
- Error announcements and focus movement remain page responsibilities.
- Dynamic Type behavior of `OtpCodeInput` remains unchanged.

## Testing

Add or update tests that prove:

1. Account and email verification reference only declared application
   resources.
2. Login and email verification both use `OtpVerificationFormView`.
3. The pages no longer duplicate the OTP input and primary confirmation form.
4. Required bindings are wired correctly for each workflow.
5. Existing email lifecycle, accessibility, and mobile tests remain green.
6. The iOS build succeeds and the Account tab plus email-verification form can
   be opened on the connected physical device without a crash.

## Non-goals

- No shared Login/email view model.
- No changes to OTP generation, verification, cooldown, idempotency, or
  authentication rules.
- No changes to transaction or payment state.
- No production email-provider work.
- No redesign of page headers, destination cards, resend controls, or errors.
