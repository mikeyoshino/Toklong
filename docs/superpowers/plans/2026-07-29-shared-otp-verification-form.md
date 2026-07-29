# Shared OTP Verification Form Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the physical-device Account crash and make Login OTP and email-change OTP use the original Login `AuthFormCard` through one reusable, stateless verification-form UI.

**Architecture:** Keep `OtpCodeInput` as the low-level six-digit entry and use `OtpVerificationFormView` as a presentation-only composition of the Login `AuthFormCard`, that input, an optional Development hint, and a confirmation button. Both pages bind their existing view models into the component; headers, destinations, resend, errors, navigation, timers, and session state remain page-owned.

**Tech Stack:** .NET 10, .NET MAUI XAML, C# bindable properties, xUnit XML layout tests, iOS arm64/CoreDevice verification.

## Global Constraints

- `OtpVerificationFormView` must not reference a Login or email-change view model.
- The component must not own session, workflow, retry, timer, resend, navigation, or server state.
- The component must contain exactly one `AuthFormCard`; do not use the email-specific `RefinedFormCard`.
- `Code` is the only two-way property; commands and presentation values are supplied by the consuming page.
- `IsConfirmVisible` may hide only the confirmation button and must never hide the card or OTP input.
- Login and email verification retain their current business behavior and page-specific content.
- Keep one accessible OTP entry and keep decorative digit labels outside the accessibility tree.
- Do not change OTP generation, verification, cooldown, idempotency, authentication, transaction, or payment rules.
- Preserve every unrelated dirty or untracked workspace change and stage only task-owned hunks.
- The physical-device test build may override unsupported Development entitlements and API address from an isolated `/private/tmp` copy only; never commit those overrides.

---

### Task 1: Prevent missing-resource Account crashes

**Files:**
- Modify: `src/Toklong.Mobile/App.xaml`
- Modify: `tests/Toklong.Mobile.Core.Tests/EmailChangeLayoutTests.cs`

**Interfaces:**
- Consumes: application-level MAUI resource dictionary.
- Produces: `BrandBlueSoft` color resource with value `#EEF7FF`.

- [ ] **Step 1: Add the failing resource-consistency test**

Add this test to `EmailChangeLayoutTests`:

```csharp
[Fact]
public void Account_and_email_verification_reference_only_declared_resources()
{
    var app = LoadUi("App.xaml");
    var declaredKeys = app
        .Descendants()
        .Select(element => AttributeValue(element, "Key"))
        .Where(key => !string.IsNullOrWhiteSpace(key))
        .ToHashSet(StringComparer.Ordinal);
    const string prefix = "{StaticResource ";

    foreach (var pageName in new[]
             {
                 "AccountPage.xaml",
                 "VerifyEmailChangePage.xaml"
             })
    {
        var missingKeys = LoadPage(pageName)
            .Descendants()
            .Attributes()
            .Select(attribute => attribute.Value)
            .Where(value =>
                value.StartsWith(prefix, StringComparison.Ordinal) &&
                value.EndsWith('}'))
            .Select(value => value[prefix.Length..^1])
            .Distinct(StringComparer.Ordinal)
            .Where(key => !declaredKeys.Contains(key))
            .ToArray();

        Assert.True(
            missingKeys.Length == 0,
            $"{pageName} references missing resources: " +
            string.Join(", ", missingKeys));
    }
}
```

- [ ] **Step 2: Run the test and verify the crash condition is reproduced**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter FullyQualifiedName~Account_and_email_verification_reference_only_declared_resources
```

Expected: FAIL with
`AccountPage.xaml references missing resources: BrandBlueSoft`.

- [ ] **Step 3: Declare the missing semantic resource**

Add next to the existing blue tokens in `App.xaml`:

```xml
<Color x:Key="BrandBlue">#2B7FFF</Color>
<Color x:Key="BrandBlueSoft">#EEF7FF</Color>
<Color x:Key="BrandBlueDeep">#145FC7</Color>
```

- [ ] **Step 4: Run the focused and full Mobile Core tests**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter FullyQualifiedName~EmailChangeLayoutTests
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --no-restore
```

Expected: PASS; the current baseline after adding the regression is
9 focused tests and 337 full Mobile Core tests.

- [ ] **Step 5: Commit the crash fix**

```bash
git add src/Toklong.Mobile/App.xaml \
  tests/Toklong.Mobile.Core.Tests/EmailChangeLayoutTests.cs
git commit -m "fix: prevent account resource crash"
```

---

### Task 2: Add the stateless shared OTP verification form

**Files:**
- Create: `src/Toklong.Mobile/Controls/OtpVerificationFormView.xaml`
- Create: `src/Toklong.Mobile/Controls/OtpVerificationFormView.xaml.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/EmailChangeLayoutTests.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj`

**Interfaces:**
- Consumes: `OtpCodeInput`, `ICommand`, MAUI bindable properties, existing shared button styles.
- Produces: `OtpVerificationFormView` with bindable properties:
  `string Code`, `ICommand? ConfirmCommand`, `bool CanConfirm`,
  `bool IsBusy`, `string ConfirmText`, `string BusyText`,
  `string ConfirmSemanticDescription`, `string DevelopmentHint`,
  and `bool HasDevelopmentHint`.
- Produces: read-only presentation property `DisplayedConfirmText`.
- Produces: `void FocusInput()` forwarding focus to the nested
  `OtpCodeInput`.

- [ ] **Step 1: Link the planned component XAML into the layout-test output**

Add only these two entries to the existing test project item group:

```xml
<None Include="../../src/Toklong.Mobile/Controls/OtpVerificationFormView.xaml"
      Link="Ui/Controls/OtpVerificationFormView.xaml"
      CopyToOutputDirectory="PreserveNewest" />
<None Include="../../src/Toklong.Mobile/Controls/OtpVerificationFormView.xaml.cs"
      Link="Ui/Controls/OtpVerificationFormView.xaml.cs"
      CopyToOutputDirectory="PreserveNewest" />
```

When staging, use an interactive hunk so the unrelated existing
`splash_ios.svg` test-project change remains unstaged.

- [ ] **Step 2: Write the failing component-contract test**

Add:

```csharp
[Fact]
public void Shared_otp_form_has_one_input_one_action_and_no_workflow_state()
{
    var form = LoadUi("Controls", "OtpVerificationFormView.xaml");
    var codeInput = Assert.Single(
        form.Descendants(),
        element => element.Name.LocalName == "OtpCodeInput");
    var confirm = Assert.Single(form.Descendants(Maui + "Button"));
    var source = File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory,
        "Ui",
        "Controls",
        "OtpVerificationFormView.xaml.cs"));

    Assert.Equal(
        "{Binding Code, Source={x:Reference Root}, Mode=TwoWay}",
        AttributeValue(codeInput, "Code"));
    Assert.Equal(
        "{Binding ConfirmCommand, Source={x:Reference Root}}",
        AttributeValue(confirm, "Command"));
    Assert.Equal(
        "{Binding CanConfirm, Source={x:Reference Root}}",
        AttributeValue(confirm, "IsEnabled"));
    Assert.Equal(
        "{Binding DisplayedConfirmText, Source={x:Reference Root}}",
        AttributeValue(confirm, "Text"));
    Assert.DoesNotContain("ViewModel", source);
    Assert.DoesNotContain("INavigation", source);
    Assert.DoesNotContain("Resend", source);
    Assert.DoesNotContain("TimeProvider", source);
}
```

- [ ] **Step 3: Run the contract test and verify it fails**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter FullyQualifiedName~Shared_otp_form_has_one_input_one_action_and_no_workflow_state
```

Expected: FAIL because `OtpVerificationFormView.xaml` does not exist.

- [ ] **Step 4: Create the presentation-only bindable-property class**

Create `OtpVerificationFormView.xaml.cs`:

```csharp
using System.Windows.Input;

namespace Toklong.Mobile.Controls;

public partial class OtpVerificationFormView : ContentView
{
    public static readonly BindableProperty CodeProperty =
        BindableProperty.Create(
            nameof(Code),
            typeof(string),
            typeof(OtpVerificationFormView),
            string.Empty,
            BindingMode.TwoWay);

    public static readonly BindableProperty ConfirmCommandProperty =
        BindableProperty.Create(
            nameof(ConfirmCommand),
            typeof(ICommand),
            typeof(OtpVerificationFormView));

    public static readonly BindableProperty CanConfirmProperty =
        BindableProperty.Create(
            nameof(CanConfirm),
            typeof(bool),
            typeof(OtpVerificationFormView),
            true);

    public static readonly BindableProperty IsBusyProperty =
        BindableProperty.Create(
            nameof(IsBusy),
            typeof(bool),
            typeof(OtpVerificationFormView),
            false,
            propertyChanged: OnDisplayedTextInputChanged);

    public static readonly BindableProperty ConfirmTextProperty =
        BindableProperty.Create(
            nameof(ConfirmText),
            typeof(string),
            typeof(OtpVerificationFormView),
            "ยืนยัน",
            propertyChanged: OnDisplayedTextInputChanged);

    public static readonly BindableProperty BusyTextProperty =
        BindableProperty.Create(
            nameof(BusyText),
            typeof(string),
            typeof(OtpVerificationFormView),
            "กำลังยืนยัน...",
            propertyChanged: OnDisplayedTextInputChanged);

    public static readonly BindableProperty ConfirmSemanticDescriptionProperty =
        BindableProperty.Create(
            nameof(ConfirmSemanticDescription),
            typeof(string),
            typeof(OtpVerificationFormView),
            "ยืนยันรหัส 6 หลัก");

    public static readonly BindableProperty DevelopmentHintProperty =
        BindableProperty.Create(
            nameof(DevelopmentHint),
            typeof(string),
            typeof(OtpVerificationFormView),
            string.Empty);

    public static readonly BindableProperty HasDevelopmentHintProperty =
        BindableProperty.Create(
            nameof(HasDevelopmentHint),
            typeof(bool),
            typeof(OtpVerificationFormView),
            false);

    public OtpVerificationFormView() => InitializeComponent();

    public void FocusInput() => OtpInput.FocusInput();

    public string Code
    {
        get => (string)GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    public ICommand? ConfirmCommand
    {
        get => (ICommand?)GetValue(ConfirmCommandProperty);
        set => SetValue(ConfirmCommandProperty, value);
    }

    public bool CanConfirm
    {
        get => (bool)GetValue(CanConfirmProperty);
        set => SetValue(CanConfirmProperty, value);
    }

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public string ConfirmText
    {
        get => (string)GetValue(ConfirmTextProperty);
        set => SetValue(ConfirmTextProperty, value);
    }

    public string BusyText
    {
        get => (string)GetValue(BusyTextProperty);
        set => SetValue(BusyTextProperty, value);
    }

    public string ConfirmSemanticDescription
    {
        get => (string)GetValue(ConfirmSemanticDescriptionProperty);
        set => SetValue(ConfirmSemanticDescriptionProperty, value);
    }

    public string DevelopmentHint
    {
        get => (string)GetValue(DevelopmentHintProperty);
        set => SetValue(DevelopmentHintProperty, value);
    }

    public bool HasDevelopmentHint
    {
        get => (bool)GetValue(HasDevelopmentHintProperty);
        set => SetValue(HasDevelopmentHintProperty, value);
    }

    public string DisplayedConfirmText =>
        IsBusy ? BusyText : ConfirmText;

    private static void OnDisplayedTextInputChanged(
        BindableObject bindable,
        object oldValue,
        object newValue) =>
        ((OtpVerificationFormView)bindable)
            .OnPropertyChanged(nameof(DisplayedConfirmText));
}
```

- [ ] **Step 5: Create the shared form XAML**

Create `OtpVerificationFormView.xaml`:

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<ContentView
    x:Class="Toklong.Mobile.Controls.OtpVerificationFormView"
    x:Name="Root"
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:controls="clr-namespace:Toklong.Mobile.Controls">
    <VerticalStackLayout Spacing="{StaticResource SpacingLg}">
        <controls:OtpCodeInput
            x:Name="OtpInput"
            Code="{Binding Code, Source={x:Reference Root}, Mode=TwoWay}" />

        <Border
            IsVisible="{Binding HasDevelopmentHint, Source={x:Reference Root}}"
            Padding="12"
            BackgroundColor="{StaticResource BrandBlueSoft}"
            StrokeThickness="0"
            StrokeShape="RoundRectangle 12">
            <Label
                HorizontalTextAlignment="Center"
                Style="{StaticResource RefinedHelperText}"
                Text="{Binding DevelopmentHint, Source={x:Reference Root}}"
                TextColor="{StaticResource BrandBlueDeep}" />
        </Border>

        <Button
            Style="{StaticResource RefinedPrimaryButton}"
            Command="{Binding ConfirmCommand, Source={x:Reference Root}}"
            IsEnabled="{Binding CanConfirm, Source={x:Reference Root}}"
            SemanticProperties.Description="{Binding ConfirmSemanticDescription, Source={x:Reference Root}}"
            Text="{Binding DisplayedConfirmText, Source={x:Reference Root}}">
            <Button.Triggers>
                <DataTrigger
                    TargetType="Button"
                    Binding="{Binding IsBusy, Source={x:Reference Root}}"
                    Value="True">
                    <Setter Property="IsEnabled" Value="False" />
                </DataTrigger>
            </Button.Triggers>
        </Button>
    </VerticalStackLayout>
</ContentView>
```

- [ ] **Step 6: Run the component test**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter FullyQualifiedName~Shared_otp_form_has_one_input_one_action_and_no_workflow_state
```

Expected: PASS.

- [ ] **Step 7: Commit the reusable component**

```bash
git add src/Toklong.Mobile/Controls/OtpVerificationFormView.xaml \
  src/Toklong.Mobile/Controls/OtpVerificationFormView.xaml.cs \
  tests/Toklong.Mobile.Core.Tests/EmailChangeLayoutTests.cs
git add -p tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj
git commit -m "feat: add shared otp verification form"
```

---

### Task 3: Migrate Login and email verification to the shared form

**Files:**
- Modify: `src/Toklong.Mobile/Pages/VerifyCodePage.xaml`
- Modify: `src/Toklong.Mobile/Pages/VerifyEmailChangePage.xaml`
- Modify: `tests/Toklong.Mobile.Core.Tests/EmailChangeLayoutTests.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs` only if its existing Login assertions require the shared component contract.

**Interfaces:**
- Consumes: `OtpVerificationFormView` from Task 2.
- Produces: two page bindings into the shared presentation component with
  page-owned workflow state.

- [ ] **Step 1: Write failing page-adoption tests**

Add:

```csharp
[Fact]
public void Login_and_email_verification_use_the_shared_otp_form()
{
    var login = LoadPage("VerifyCodePage.xaml");
    var email = LoadPage("VerifyEmailChangePage.xaml");

    foreach (var page in new[] { login, email })
    {
        Assert.Single(
            page.Descendants(),
            element =>
                element.Name.LocalName ==
                "OtpVerificationFormView");
        Assert.Empty(
            page.Descendants(),
            element => element.Name.LocalName == "OtpCodeInput");
    }

    var loginForm = login.Descendants().Single(element =>
        element.Name.LocalName == "OtpVerificationFormView");
    Assert.Equal(
        "{Binding ConfirmCommand}",
        AttributeValue(loginForm, "ConfirmCommand"));
    Assert.Equal(
        "{Binding ConfirmButtonText}",
        AttributeValue(loginForm, "ConfirmText"));
    Assert.Equal(
        "{Binding HasDevelopmentHint}",
        AttributeValue(loginForm, "HasDevelopmentHint"));

    var emailForm = email.Descendants().Single(element =>
        element.Name.LocalName == "OtpVerificationFormView");
    Assert.Equal(
        "{Binding ConfirmCommand}",
        AttributeValue(emailForm, "ConfirmCommand"));
    Assert.Equal(
        "{Binding CanConfirm}",
        AttributeValue(emailForm, "CanConfirm"));
    Assert.Equal(
        "{Binding CanUseChallenge}",
        AttributeValue(emailForm, "IsVisible"));
}
```

- [ ] **Step 2: Run the test and verify it fails**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter FullyQualifiedName~Login_and_email_verification_use_the_shared_otp_form
```

Expected: FAIL because both pages still contain `OtpCodeInput` directly.

- [ ] **Step 3: Replace the Login form markup**

In `VerifyCodePage.xaml`, replace the `AuthFormCard` block containing the
direct OTP input, Development hint, and button with:

```xml
<controls:OtpVerificationFormView
    x:Name="OtpForm"
    Code="{Binding Code, Mode=TwoWay}"
    ConfirmCommand="{Binding ConfirmCommand}"
    ConfirmText="{Binding ConfirmButtonText}"
    BusyText="กำลังยืนยัน..."
    ConfirmSemanticDescription="ยืนยันรหัสเข้าสู่ระบบ 6 หลัก"
    DevelopmentHint="{Binding DevelopmentHint}"
    HasDevelopmentHint="{Binding HasDevelopmentHint}"
    IsBusy="{Binding IsBusy}" />
```

Keep the Login resend, activity indicator, error, phone editing, and page
header unchanged.

- [ ] **Step 4: Replace the email form markup**

In `VerifyEmailChangePage.xaml`, remove the `RefinedFormCard`,
`FormLabelView`, direct OTP input, and direct confirm button. Replace them
with this page-owned layout:

```xml
<VerticalStackLayout Spacing="{StaticResource SpacingLg}">
    <controls:OtpVerificationFormView
        x:Name="OtpForm"
        Code="{Binding Code, Mode=TwoWay}"
        ConfirmCommand="{Binding ConfirmCommand}"
        CanConfirm="{Binding CanConfirm}"
        ConfirmText="ยืนยันอีเมลใหม่"
        BusyText="กำลังยืนยัน..."
        ConfirmSemanticDescription="ยืนยันอีเมลใหม่ด้วยรหัส 6 หลัก"
        IsBusy="{Binding IsBusy}"
        IsVisible="{Binding CanUseChallenge}" />

    <Button
        x:Name="NewRequestButton"
        Style="{StaticResource RefinedPrimaryButton}"
        Command="{Binding StartNewRequestCommand}"
        IsVisible="{Binding RequiresNewRequest}"
        SemanticProperties.Description="ขอรหัสยืนยันอีเมลใหม่"
        Text="ขอรหัสใหม่" />

    <Button
        x:Name="ReturnToAccountButton"
        Style="{StaticResource RefinedPrimaryButton}"
        Command="{Binding ReturnToAccountCommand}"
        IsVisible="{Binding CanReturnToAccount}"
        SemanticProperties.Description="{Binding AccountReturnSemanticDescription}"
        Text="{Binding AccountReturnButtonText}" />
</VerticalStackLayout>
```

Keep destination, resend, expiry, errors, focus bridge, and navigation
unchanged. In `VerifyEmailChangePage.xaml.cs`, replace each
`OtpInput.FocusInput()` call with `OtpForm.FocusInput()`.

- [ ] **Step 5: Run focused layout and view-model tests**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~EmailChangeLayoutTests|FullyQualifiedName~VerifyCode|FullyQualifiedName~VerifyEmailChange"
```

Expected: PASS with one shared form per page and unchanged workflow tests.

- [ ] **Step 6: Run the full Mobile Core suite and iOS builds**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj
dotnet restore src/Toklong.Mobile/Toklong.Mobile.csproj \
  -p:TargetFrameworks=net10.0-ios \
  -p:RuntimeIdentifier=ios-arm64
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj \
  -f net10.0-ios \
  -p:RuntimeIdentifier=ios-arm64 \
  --no-restore
```

Expected: all Mobile Core tests pass and iOS compiles with zero errors. The
normal signed build may retain the documented profile warnings for push and
associated domains.

- [ ] **Step 7: Commit the page migration**

```bash
git add src/Toklong.Mobile/Pages/VerifyCodePage.xaml \
  src/Toklong.Mobile/Pages/VerifyEmailChangePage.xaml \
  tests/Toklong.Mobile.Core.Tests/EmailChangeLayoutTests.cs
git add -p tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs
git commit -m "refactor: share otp verification form"
```

---

### Task 4: Verify the shared form on the connected iPhone

**Files:**
- Modify only files required to correct a reproduced failure.

**Interfaces:**
- Consumes: Tasks 1–3 and the current-tree Development API.
- Produces: physical-device evidence for Account navigation and both OTP
  workflows.

- [ ] **Step 1: Prepare an isolated device-test build**

Create a unique `/private/tmp/toklong-device-test.XXXXXX` copy. In that copy
only:

- set the Debug iOS API URL to `http://<current-mac-lan-ip>:5191/`;
- add `NSLocalNetworkUsageDescription`; and
- build with `-p:CodesignEntitlements=` because the personal-team profile
  does not grant the app's push or associated-domain entitlements.

Do not modify or commit these three test-only overrides in the main tree.

- [ ] **Step 2: Start the current-tree API**

Run:

```bash
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS=http://0.0.0.0:5191 \
dotnet run --project src/Toklong.Api/Toklong.Api.csproj \
  --no-launch-profile
```

Expected: API listens on `0.0.0.0:5191` and logs parameterized values.

- [ ] **Step 3: Install and launch through CoreDevice**

Run with the paired CoreDevice identifier:

```bash
xcrun devicectl device install app \
  --device A2D699A7-D818-5883-AA02-1E9FDEF7D0A2 \
  <isolated-app-path>
xcrun devicectl device process launch \
  --device A2D699A7-D818-5883-AA02-1E9FDEF7D0A2 \
  --terminate-existing \
  --console \
  th.co.toklong.mobile
```

Expected: install and launch succeed.

- [ ] **Step 4: Perform the physical-device ceremony**

Verify:

1. Account opens without `XamlParseException`.
2. Login OTP and email-change OTP show the same six-digit form pattern.
3. Email OTP has no tall white form card and no duplicate field label.
4. Numeric keyboard, cursor, OTP entry, and confirm action work.
5. Entering Development code `123456` verifies the pending email challenge.
6. Returning to Account shows `เปลี่ยนอีเมลเรียบร้อยแล้ว` once.
7. Closing and reopening the app retains the authenticated session and the
   confirmed email.
8. API and device console contain no raw OTP or full pending email.

- [ ] **Step 5: Run final verification**

Run:

```bash
git diff --check
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj --no-restore
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj --no-restore
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj --no-restore
dotnet test tests/Toklong.Crm.Tests/Toklong.Crm.Tests.csproj --no-restore
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore
```

Expected: zero failures and no whitespace errors.

- [ ] **Step 6: Clean only task-owned temporary resources**

Terminate the Task 4 console app and API on `5191`, then remove only the
unique isolated `/private/tmp/toklong-device-test.*` directory created by
this task. Leave the pre-existing API on `5181`, simulators, database rows,
and unrelated workspace files untouched.

If no correction was needed during Task 4, do not create an empty commit.

---

### Task 5: Restore the Login OTP card as the shared visual source

**Files:**
- Modify: `src/Toklong.Mobile/Controls/OtpVerificationFormView.xaml`
- Modify: `tests/Toklong.Mobile.Core.Tests/EmailChangeLayoutTests.cs`

**Interfaces:**
- Consumes: existing `OtpVerificationFormView` bindable properties and
  `AuthFormCard`.
- Produces: one shared Login-style card containing the OTP input, optional
  Development hint, and confirmation button.

- [ ] **Step 1: Add a failing Login-card ownership assertion**

Extend
`Shared_otp_form_has_one_input_one_action_and_no_workflow_state`:

```csharp
var card = Assert.Single(
    form.Descendants(Maui + "Border"),
    border =>
        AttributeValue(border, "Style") ==
        "{StaticResource AuthFormCard}");

Assert.True(codeInput.Ancestors().Contains(card));
Assert.True(confirm.Ancestors().Contains(card));
Assert.DoesNotContain(
    form.Descendants(Maui + "Border"),
    border =>
        AttributeValue(border, "Style") ==
        "{StaticResource RefinedFormCard}");
```

- [ ] **Step 2: Run the contract test and verify it fails**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter FullyQualifiedName~Shared_otp_form_has_one_input_one_action_and_no_workflow_state \
  --no-restore
```

Expected: FAIL because the shared component has no `AuthFormCard`.

- [ ] **Step 3: Wrap the existing shared layout in the Login card**

Change the component content to:

```xml
<Border Style="{StaticResource AuthFormCard}">
    <VerticalStackLayout Spacing="{StaticResource SpacingLg}">
        <controls:OtpCodeInput
            x:Name="OtpInput"
            Code="{Binding Code, Source={x:Reference Root}, Mode=TwoWay}" />

        <Border
            IsVisible="{Binding HasDevelopmentHint, Source={x:Reference Root}}"
            Padding="12"
            BackgroundColor="{StaticResource BrandBlueSoft}"
            StrokeThickness="0"
            StrokeShape="RoundRectangle 12">
            <Label
                HorizontalTextAlignment="Center"
                Style="{StaticResource RefinedHelperText}"
                Text="{Binding DevelopmentHint, Source={x:Reference Root}}"
                TextColor="{StaticResource BrandBlueDeep}" />
        </Border>

        <Button
            Style="{StaticResource RefinedPrimaryButton}"
            Command="{Binding ConfirmCommand, Source={x:Reference Root}}"
            IsEnabled="{Binding CanConfirm, Source={x:Reference Root}}"
            IsVisible="{Binding IsConfirmVisible, Source={x:Reference Root}}"
            SemanticProperties.Description="{Binding ConfirmSemanticDescription, Source={x:Reference Root}}"
            Text="{Binding DisplayedConfirmText, Source={x:Reference Root}}">
            <Button.Triggers>
                <DataTrigger
                    TargetType="Button"
                    Binding="{Binding IsBusy, Source={x:Reference Root}}"
                    Value="True">
                    <Setter Property="IsEnabled" Value="False" />
                </DataTrigger>
            </Button.Triggers>
        </Button>
    </VerticalStackLayout>
</Border>
```

- [ ] **Step 4: Run focused and full Mobile Core tests**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~EmailChangeLayoutTests|FullyQualifiedName~VerifyCode|FullyQualifiedName~VerifyEmailChange" \
  --no-restore
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --no-restore
```

Expected: PASS; the Login and email pages both consume one shared
Login-style card without view-model coupling.

- [ ] **Step 5: Build iOS and commit**

Run:

```bash
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj \
  -f net10.0-ios \
  -p:RuntimeIdentifier=ios-arm64 \
  --no-restore
git add src/Toklong.Mobile/Controls/OtpVerificationFormView.xaml \
  tests/Toklong.Mobile.Core.Tests/EmailChangeLayoutTests.cs
git commit -m "fix: share login otp card design"
```

Expected: iOS build succeeds with zero errors; existing Personal Team
entitlement warnings may remain.

---

### Task 6: Reverify the Login card on the connected iPhone

**Files:**
- Modify only files required to correct a newly reproduced failure.

**Interfaces:**
- Consumes: Task 5 and the isolated Task 4 device-test copy.
- Produces: physical-device evidence that the email OTP matches Login.

- [ ] **Step 1: Sync Task 5 into the isolated copy and rebuild**

Copy only `OtpVerificationFormView.xaml` to its matching `Controls/` path in
the isolated copy, then run:

```bash
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj \
  -f net10.0-ios \
  -p:RuntimeIdentifier=ios-arm64 \
  -p:CodesignEntitlements= \
  --no-restore
```

Expected: build succeeds with zero errors.

- [ ] **Step 2: Install, launch, and inspect the email OTP page**

Install through CoreDevice, launch with console capture, then verify:

1. Account opens without a crash.
2. Email OTP shows the same compact white card as Login OTP.
3. The card contains six digit positions and no duplicate field label.
4. The confirmation button is inside the card when the challenge permits it.
5. The OTP card remains visible when only the confirmation action is hidden.

- [ ] **Step 3: Run final verification**

Run:

```bash
git diff --check
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj --no-restore
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj --no-restore
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj --no-restore
dotnet test tests/Toklong.Crm.Tests/Toklong.Crm.Tests.csproj --no-restore
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore
```

Expected: zero failures and no whitespace errors.

- [ ] **Step 4: Clean only task-owned temporary resources**

Terminate the device console and Task 4 API on `5191`, then remove
`/private/tmp/toklong-device-test.s7V5Ba` and the older task-owned
`/private/tmp/toklong-device-test.Vv4Qi2`. Do not stop the pre-existing API on
`5181` or remove unrelated files.
