# Buyer Receipt Confirmation Card Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the buyer receipt-confirmation card with the approved `Clear & Calm` design while preserving the existing confirmation, dispute, authorization, and payout rules.

**Architecture:** Add a pure mobile-core presenter that converts an eligible buyer `AppTransaction` into fulfillment-specific card copy and an exact trusted physical deadline. The transaction-detail view model exposes that presentation to XAML; the page owns only layout and bindings. No API, domain transition, payment, shipping, dispute, or persistence code changes.

**Tech Stack:** .NET 10, C#, .NET MAUI XAML, xUnit, SVG MauiImage assets

## Global Constraints

- The primary action is available only to the authenticated buyer when the existing state presenter returns `TransactionAction.ConfirmReceipt`.
- A physical confirmation card requires `AppTransaction.ActionDeadline`; the API already maps `DeliveredDisputeWindow.DisputeWindowEndsAt` to that field.
- The physical deadline is rendered as an exact Thai-localized date and time and is never computed in the client.
- Digital fulfillment never shows an automatic deadline or implies time-based payout release.
- Tapping the card action still opens the existing final disclosure before calling `ConfirmReceiptAsync`.
- The problem form remains collapsed initially and opens only from the neutral secondary action.
- Any open dispute continues to block payout; no domain or payout behavior changes.
- One primary action per state; minimum interactive target height is 44 device-independent pixels.
- Do not add marketplace, chat, wallet, escrow, payment, carrier, or dispute-scope functionality.

---

## File map

- Create `src/Toklong.Mobile/Core/BuyerReceiptConfirmationPresenter.cs` — pure, testable mapping from `AppTransaction` to card copy and deadline visibility.
- Create `tests/Toklong.Mobile.Core.Tests/BuyerReceiptConfirmationPresenterTests.cs` — physical, digital, ineligible-state, and missing-trusted-deadline coverage.
- Modify `src/Toklong.Mobile/ViewModels/TransactionDetailViewModel.cs` — expose the presentation and use its fulfillment-specific confirmation-dialog copy.
- Modify `src/Toklong.Mobile/Pages/TransactionDetailPage.xaml` — render the approved card, exact deadline notice, and neutral problem action.
- Create `src/Toklong.Mobile/Resources/Images/ui_receipt_check.svg` — thin package/check card icon.
- Create `src/Toklong.Mobile/Resources/Images/ui_clock.svg` — thin clock icon for the exact-deadline notice.
- Modify `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs` — assert structure, bindings, touch sizes, semantic descriptions, and collapsed problem form.
- Modify `tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj` — copy the two SVG assets into the test output for asset checks.
- Modify `docs/02_UI_UX_AND_CONTENT_SPEC.md` — replace the old delivered-window card wording with the approved fulfillment-specific copy.
- Modify `docs/05_ACCEPTANCE_TESTS.md` — make exact physical deadline and no digital deadline explicit at the mobile UI boundary.

---

### Task 1: Add a safe fulfillment-specific card presenter

**Files:**
- Create: `src/Toklong.Mobile/Core/BuyerReceiptConfirmationPresenter.cs`
- Create: `tests/Toklong.Mobile.Core.Tests/BuyerReceiptConfirmationPresenterTests.cs`

**Interfaces:**
- Consumes: `AppTransaction`, `AppTransactionRole`, `AppFulfillmentType`, and `TransactionAction`.
- Produces: `BuyerReceiptConfirmationPresenter.Present(AppTransaction?) -> BuyerReceiptConfirmationPresentation?`.
- Produces record properties used by Task 2: `Heading`, `SupportingText`, `HasDeadline`, `DeadlineText`, `PrimaryActionText`, `ProblemActionText`, `ConfirmationTitle`, `ConfirmationMessage`, `ConfirmationAcceptText`, `ConfirmationCancelText`, and `SuccessMessage`.

- [ ] **Step 1: Write presenter tests that fail because the presenter does not exist**

Create `tests/Toklong.Mobile.Core.Tests/BuyerReceiptConfirmationPresenterTests.cs`:

```csharp
using System.Globalization;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class BuyerReceiptConfirmationPresenterTests
{
    [Fact]
    public void Physical_delivery_uses_exact_trusted_deadline_and_approved_copy()
    {
        var deadline = new DateTimeOffset(
            2026, 8, 2, 23, 58, 0, TimeSpan.FromHours(7));
        var transaction = Eligible(
            AppFulfillmentType.Physical,
            "DeliveredDisputeWindow",
            deadline);

        var result = BuyerReceiptConfirmationPresenter.Present(transaction);

        Assert.NotNull(result);
        Assert.Equal("ตรวจสินค้าให้เรียบร้อย", result.Heading);
        Assert.Equal(
            "เช็กสินค้าและอุปกรณ์ให้ครบก่อนยืนยัน เมื่อยืนยันแล้ว ระบบจะเริ่มจ่ายเงินให้ผู้ขาย",
            result.SupportingText);
        Assert.True(result.HasDeadline);
        var localized = deadline.ToLocalTime().ToString(
            "d MMM yyyy · HH:mm 'น.'",
            CultureInfo.GetCultureInfo("th-TH"));
        Assert.Equal($"แจ้งปัญหาได้ถึง {localized}", result.DeadlineText);
        Assert.Equal(
            "ยืนยันว่าได้รับของเรียบร้อย",
            result.PrimaryActionText);
        Assert.Equal(
            "พบปัญหากับรายการนี้",
            result.ProblemActionText);
    }

    [Fact]
    public void Digital_delivery_has_specific_copy_and_no_automatic_deadline()
    {
        var transaction = Eligible(
            AppFulfillmentType.Digital,
            "DigitalDeliverySubmitted",
            null);

        var result = BuyerReceiptConfirmationPresenter.Present(transaction);

        Assert.NotNull(result);
        Assert.Equal("ตรวจรายการที่ได้รับ", result.Heading);
        Assert.Equal(
            "ตรวจรายการและการเข้าถึงให้เรียบร้อยก่อนยืนยัน เมื่อยืนยันแล้ว ระบบจะเริ่มจ่ายเงินให้ผู้ขาย",
            result.SupportingText);
        Assert.False(result.HasDeadline);
        Assert.Equal("", result.DeadlineText);
        Assert.Contains("รายการ", result.ConfirmationMessage);
        Assert.DoesNotContain("หมดเวลา", result.SupportingText);
    }

    [Fact]
    public void Physical_delivery_without_trusted_deadline_does_not_offer_confirmation()
    {
        var transaction = Eligible(
            AppFulfillmentType.Physical,
            "DeliveredDisputeWindow",
            null);

        Assert.Null(BuyerReceiptConfirmationPresenter.Present(transaction));
    }

    [Theory]
    [InlineData(AppTransactionRole.Seller, "DeliveredDisputeWindow")]
    [InlineData(AppTransactionRole.Buyer, "InTransit")]
    [InlineData(AppTransactionRole.Buyer, "Disputed")]
    [InlineData(AppTransactionRole.Buyer, "BuyerConfirmedReceipt")]
    public void Ineligible_role_or_state_has_no_confirmation_card(
        AppTransactionRole role,
        string state)
    {
        var transaction = Eligible(
            AppFulfillmentType.Physical,
            state,
            DateTimeOffset.UtcNow.AddHours(72)) with
        {
            Role = role
        };

        Assert.Null(BuyerReceiptConfirmationPresenter.Present(transaction));
    }

    private static AppTransaction Eligible(
        AppFulfillmentType fulfillmentType,
        string state,
        DateTimeOffset? deadline) =>
        new(
            Guid.NewGuid(),
            "สินค้า",
            450000,
            "THB",
            AppTransactionRole.Buyer,
            fulfillmentType,
            state,
            DateTimeOffset.UtcNow,
            deadline,
            "ผู้ขาย ทดสอบ");
}
```

- [ ] **Step 2: Run the focused tests and verify the red state**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --no-restore \
  --filter FullyQualifiedName~BuyerReceiptConfirmationPresenterTests
```

Expected: build failure because `BuyerReceiptConfirmationPresenter` is not defined.

- [ ] **Step 3: Implement the minimal pure presenter**

Create `src/Toklong.Mobile/Core/BuyerReceiptConfirmationPresenter.cs`:

```csharp
using System.Globalization;

namespace Toklong.Mobile.Core;

public sealed record BuyerReceiptConfirmationPresentation(
    string Heading,
    string SupportingText,
    bool HasDeadline,
    string DeadlineText,
    string PrimaryActionText,
    string ProblemActionText,
    string ConfirmationTitle,
    string ConfirmationMessage,
    string ConfirmationAcceptText,
    string ConfirmationCancelText,
    string SuccessMessage);

public static class BuyerReceiptConfirmationPresenter
{
    private static readonly CultureInfo ThaiCulture =
        CultureInfo.GetCultureInfo("th-TH");

    public static BuyerReceiptConfirmationPresentation? Present(
        AppTransaction? transaction)
    {
        if (transaction is null ||
            transaction.Role != AppTransactionRole.Buyer ||
            transaction.Presentation.PrimaryAction !=
                TransactionAction.ConfirmReceipt)
            return null;

        if (transaction.FulfillmentType ==
                AppFulfillmentType.Physical &&
            !transaction.ActionDeadline.HasValue)
            return null;

        return transaction.FulfillmentType ==
            AppFulfillmentType.Physical
                ? Physical(transaction.ActionDeadline!.Value)
                : Digital();
    }

    private static BuyerReceiptConfirmationPresentation Physical(
        DateTimeOffset deadline) =>
        new(
            "ตรวจสินค้าให้เรียบร้อย",
            "เช็กสินค้าและอุปกรณ์ให้ครบก่อนยืนยัน เมื่อยืนยันแล้ว ระบบจะเริ่มจ่ายเงินให้ผู้ขาย",
            true,
            "แจ้งปัญหาได้ถึง " +
            deadline.ToLocalTime().ToString(
                "d MMM yyyy · HH:mm 'น.'",
                ThaiCulture),
            "ยืนยันว่าได้รับของเรียบร้อย",
            "พบปัญหากับรายการนี้",
            "ยืนยันหลังตรวจสินค้า",
            "คุณตรวจสินค้าแล้วและไม่พบปัญหา เมื่อยืนยัน ระบบจะเริ่มขั้นตอนจ่ายเงินให้ผู้ขาย",
            "ยืนยันและเริ่มจ่ายให้ผู้ขาย",
            "กลับไปตรวจสินค้า",
            "ยืนยันว่าตรวจแล้ว ระบบจะเริ่มขั้นตอนจ่ายเงินให้ผู้ขาย");

    private static BuyerReceiptConfirmationPresentation Digital() =>
        new(
            "ตรวจรายการที่ได้รับ",
            "ตรวจรายการและการเข้าถึงให้เรียบร้อยก่อนยืนยัน เมื่อยืนยันแล้ว ระบบจะเริ่มจ่ายเงินให้ผู้ขาย",
            false,
            "",
            "ยืนยันว่าได้รับเรียบร้อย",
            "พบปัญหากับรายการนี้",
            "ยืนยันหลังตรวจรายการ",
            "คุณตรวจรายการที่ได้รับแล้วและไม่พบปัญหา เมื่อยืนยัน ระบบจะเริ่มขั้นตอนจ่ายเงินให้ผู้ขาย",
            "ยืนยันและเริ่มจ่ายให้ผู้ขาย",
            "กลับไปตรวจรายการ",
            "ยืนยันว่าตรวจแล้ว ระบบจะเริ่มขั้นตอนจ่ายเงินให้ผู้ขาย");
}
```

- [ ] **Step 4: Run the focused tests and verify green**

Run the Task 1 test command again.

Expected: all `BuyerReceiptConfirmationPresenterTests` pass.

- [ ] **Step 5: Commit the presenter slice**

```bash
git add \
  src/Toklong.Mobile/Core/BuyerReceiptConfirmationPresenter.cs \
  tests/Toklong.Mobile.Core.Tests/BuyerReceiptConfirmationPresenterTests.cs
git commit -m "feat: present buyer receipt confirmation copy"
```

---

### Task 2: Render the approved card and preserve the confirmation/dispute interactions

**Files:**
- Create: `src/Toklong.Mobile/Resources/Images/ui_receipt_check.svg`
- Create: `src/Toklong.Mobile/Resources/Images/ui_clock.svg`
- Modify: `src/Toklong.Mobile/ViewModels/TransactionDetailViewModel.cs:30-65,141-145,178-190,591-621`
- Modify: `src/Toklong.Mobile/Pages/TransactionDetailPage.xaml:740-805`
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs:1396-1440`
- Modify: `tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj`

**Interfaces:**
- Consumes: `BuyerReceiptConfirmationPresenter.Present(Transaction)`.
- Produces view-model property: `BuyerReceiptConfirmationPresentation? BuyerConfirmation`.
- Produces view-model property: `string ProblemFormToggleText`.
- Preserves commands: `ConfirmReceiptCommand`, `ToggleProblemFormCommand`, and `ReportProblemCommand`.

- [ ] **Step 1: Replace the existing layout test with assertions for the approved structure**

Update `BuyerProblemFormIsHiddenBehindNeutralTextAction` and add
`BuyerReceiptConfirmationUsesClearCalmLayout` in
`tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`:

```csharp
[Fact]
public void BuyerReceiptConfirmationUsesClearCalmLayout()
{
    var detail = Load("Ui", "Pages", "TransactionDetailPage.xaml");
    var card = detail
        .Descendants(Maui + "Border")
        .Single(border =>
            AttributeValue(border, "AutomationId") ==
                "BuyerReceiptConfirmationCard");

    Assert.Equal(
        "{Binding IsBuyerConfirmationAction}",
        AttributeValue(card, "IsVisible"));
    Assert.Contains(
        card.Descendants(Maui + "Image"),
        image => AttributeValue(image, "Source") ==
            "ui_receipt_check.png");

    var labels = card.Descendants(Maui + "Label").ToArray();
    Assert.Contains(
        labels,
        label => AttributeValue(label, "Text") ==
            "{Binding BuyerConfirmation.Heading}");
    Assert.Contains(
        labels,
        label => AttributeValue(label, "Text") ==
            "{Binding BuyerConfirmation.SupportingText}");

    var deadline = card
        .Descendants(Maui + "Border")
        .Single(border =>
            AttributeValue(border, "AutomationId") ==
                "BuyerReceiptDeadline");
    Assert.Equal(
        "{Binding BuyerConfirmation.HasDeadline}",
        AttributeValue(deadline, "IsVisible"));
    Assert.Contains(
        deadline.Descendants(Maui + "Label"),
        label => AttributeValue(label, "Text") ==
            "{Binding BuyerConfirmation.DeadlineText}");

    var primary = card
        .Descendants(Maui + "Button")
        .Single(button =>
            AttributeValue(button, "Command") ==
                "{Binding ConfirmReceiptCommand}");
    Assert.Equal(
        "{Binding BuyerConfirmation.PrimaryActionText}",
        AttributeValue(primary, "Text"));
    Assert.Equal(
        "48",
        AttributeValue(primary, "MinimumHeightRequest"));
    Assert.Equal(
        "{Binding BuyerConfirmation.PrimaryActionText}",
        AttributeValue(
            primary,
            "SemanticProperties.Description"));
}
```

Keep the current checks that:

```csharp
Assert.Equal(
    "{Binding ToggleProblemFormCommand}",
    AttributeValue(toggle, "Command"));
Assert.Equal(
    "{Binding ProblemFormToggleText}",
    AttributeValue(toggle, "Text"));
Assert.Equal(
    "{Binding IsProblemFormExpanded}",
    AttributeValue(form, "IsVisible"));
Assert.Single(form.Descendants(Maui + "Picker"));
Assert.Single(form.Descendants(Maui + "Editor"));
```

Also assert the neutral secondary button has
`MinimumHeightRequest="44"`, `HorizontalOptions="Fill"`, and no red
`TextColor`.

- [ ] **Step 2: Include the new asset paths in the test project and verify red**

Add to the asset `ItemGroup` in
`tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj`:

```xml
<None Include="../../src/Toklong.Mobile/Resources/Images/ui_receipt_check.svg"
      Link="Brand/ui_receipt_check.svg"
      CopyToOutputDirectory="PreserveNewest" />
<None Include="../../src/Toklong.Mobile/Resources/Images/ui_clock.svg"
      Link="Brand/ui_clock.svg"
      CopyToOutputDirectory="PreserveNewest" />
```

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --no-restore \
  --filter "FullyQualifiedName~BuyerReceiptConfirmationUsesClearCalmLayout|FullyQualifiedName~BuyerProblemFormIsHiddenBehindNeutralTextAction"
```

Expected: layout test failure because the approved bindings and automation IDs
do not exist yet.

- [ ] **Step 3: Add the two thin line SVG assets**

Create `src/Toklong.Mobile/Resources/Images/ui_receipt_check.svg`:

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 48 48">
  <path d="M8 15 24 7l16 8-16 8-16-8Z" fill="none" stroke="#145FC7" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"/>
  <path d="M8 15v18l16 8 16-8V15M24 23v18" fill="none" stroke="#145FC7" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"/>
  <path d="m15 28 4 4 8-9" fill="none" stroke="#087C68" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
</svg>
```

Create `src/Toklong.Mobile/Resources/Images/ui_clock.svg`:

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
  <circle cx="12" cy="12" r="9" fill="none" stroke="#365778" stroke-width="2"/>
  <path d="M12 7v5l3 2" fill="none" stroke="#365778" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
</svg>
```

The existing wildcard `MauiImage Include="Resources/Images/*"` packages both
assets without a mobile project-file change.

- [ ] **Step 4: Expose the presentation and use its dialog copy**

In `TransactionDetailViewModel`, notify the new property whenever
`Transaction` changes:

```csharp
OnPropertyChanged(nameof(BuyerConfirmation));
OnPropertyChanged(nameof(ProblemFormToggleText));
```

Add:

```csharp
public BuyerReceiptConfirmationPresentation? BuyerConfirmation =>
    BuyerReceiptConfirmationPresenter.Present(Transaction);

public bool IsBuyerConfirmationAction =>
    BuyerConfirmation is not null;
```

Replace `ProblemFormToggleText` with:

```csharp
public string ProblemFormToggleText =>
    IsProblemFormExpanded
        ? "ปิดแบบฟอร์ม"
        : BuyerConfirmation?.ProblemActionText ?? "";
```

In `ConfirmReceiptAsync`, capture the presentation before showing the dialog
and return if it is absent:

```csharp
var presentation = BuyerConfirmation;
if (Transaction is null || presentation is null)
    return;

if (Shell.Current is not null)
{
    var confirmed = await Shell.Current.DisplayAlertAsync(
        presentation.ConfirmationTitle,
        presentation.ConfirmationMessage,
        presentation.ConfirmationAcceptText,
        presentation.ConfirmationCancelText);
    if (!confirmed)
        return;
}
```

After the existing service call, set:

```csharp
Message = presentation.SuccessMessage;
```

Do not modify `ITransactionService`, `ApiTransactionService`, or the
server-side receipt-confirmation endpoint.

- [ ] **Step 5: Replace only the confirmation-card header and actions in XAML**

Keep the existing `ProblemReportForm` children and `ReportProblemCommand`.
Replace the card opening through the toggle button with:

```xml
<Border
    AutomationId="BuyerReceiptConfirmationCard"
    IsVisible="{Binding IsBuyerConfirmationAction}"
    Style="{StaticResource SurfaceCard}"
    Padding="20">
    <VerticalStackLayout Spacing="14">
        <Border
            WidthRequest="44"
            HeightRequest="44"
            HorizontalOptions="Start"
            BackgroundColor="{StaticResource SurfaceBlue}"
            StrokeThickness="0"
            StrokeShape="RoundRectangle 14">
            <Image
                WidthRequest="28"
                HeightRequest="28"
                Source="ui_receipt_check.png"
                SemanticProperties.Description="ตรวจรายการที่ได้รับ" />
        </Border>
        <Label
            Style="{StaticResource SectionTitle}"
            FontSize="20"
            Text="{Binding BuyerConfirmation.Heading}" />
        <Label
            Style="{StaticResource MutedText}"
            FontSize="14"
            LineHeight="1.35"
            Text="{Binding BuyerConfirmation.SupportingText}" />
        <Border
            AutomationId="BuyerReceiptDeadline"
            IsVisible="{Binding BuyerConfirmation.HasDeadline}"
            Padding="12,10"
            BackgroundColor="{StaticResource SurfaceBlue}"
            StrokeThickness="0"
            StrokeShape="RoundRectangle 13"
            SemanticProperties.Description="{Binding BuyerConfirmation.DeadlineText}">
            <Grid ColumnDefinitions="20,*" ColumnSpacing="9">
                <Image
                    WidthRequest="18"
                    HeightRequest="18"
                    VerticalOptions="Start"
                    Source="ui_clock.png"
                    AutomationProperties.IsInAccessibleTree="False" />
                <Label
                    Grid.Column="1"
                    FontSize="13"
                    LineHeight="1.3"
                    Text="{Binding BuyerConfirmation.DeadlineText}"
                    TextColor="#365778" />
            </Grid>
        </Border>
        <Button
            MinimumHeightRequest="48"
            Style="{StaticResource RefinedPrimaryButton}"
            BackgroundColor="{Binding Transaction.RoleColor}"
            Command="{Binding ConfirmReceiptCommand}"
            SemanticProperties.Description="{Binding BuyerConfirmation.PrimaryActionText}"
            Text="{Binding BuyerConfirmation.PrimaryActionText}" />
        <Button
            AutomationId="ToggleProblemFormButton"
            MinimumHeightRequest="44"
            HorizontalOptions="Fill"
            Style="{StaticResource RefinedInlineButton}"
            Command="{Binding ToggleProblemFormCommand}"
            SemanticProperties.Description="{Binding ProblemFormToggleText}"
            Text="{Binding ProblemFormToggleText}"
            TextColor="{StaticResource Muted}" />
```

Close the existing card after the unchanged `ProblemReportForm`. The form must
remain inside this card and remain bound to `IsProblemFormExpanded`.

- [ ] **Step 6: Run the focused presenter and layout tests**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --no-restore \
  --filter "FullyQualifiedName~BuyerReceiptConfirmationPresenterTests|FullyQualifiedName~BuyerReceiptConfirmationUsesClearCalmLayout|FullyQualifiedName~BuyerProblemFormIsHiddenBehindNeutralTextAction"
```

Expected: all selected tests pass.

- [ ] **Step 7: Compile the iOS simulator target**

```bash
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj \
  -f net10.0-ios \
  -p:RuntimeIdentifier=iossimulator-arm64 \
  --no-restore
```

Expected: build succeeds with zero errors.

- [ ] **Step 8: Commit the mobile UI slice**

```bash
git add \
  src/Toklong.Mobile/Core/BuyerReceiptConfirmationPresenter.cs \
  src/Toklong.Mobile/ViewModels/TransactionDetailViewModel.cs \
  src/Toklong.Mobile/Pages/TransactionDetailPage.xaml \
  src/Toklong.Mobile/Resources/Images/ui_receipt_check.svg \
  src/Toklong.Mobile/Resources/Images/ui_clock.svg \
  tests/Toklong.Mobile.Core.Tests/BuyerReceiptConfirmationPresenterTests.cs \
  tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs \
  tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj
git commit -m "feat: redesign buyer receipt confirmation card"
```

---

### Task 3: Update the product contract and run the complete mobile gate

**Files:**
- Modify: `docs/02_UI_UX_AND_CONTENT_SPEC.md:533-550`
- Modify: `docs/05_ACCEPTANCE_TESTS.md:912-920`

**Interfaces:**
- Documents the same `BuyerReceiptConfirmationPresentation` copy implemented in Tasks 1–2.
- Does not introduce a new transition, API field, audit event, or analytics event.

- [ ] **Step 1: Update the buyer order-detail UX contract**

Replace the delivered-window bullets and confirmation-card wording in
`docs/02_UI_UX_AND_CONTENT_SPEC.md` with:

```markdown
- Delivered physical window: show `ตรวจสินค้าให้เรียบร้อย`, the exact trusted
  inspection deadline, primary action `ยืนยันว่าได้รับของเรียบร้อย`, and
  neutral secondary action `พบปัญหากับรายการนี้`.
- Digital handoff submitted: show `ตรวจรายการที่ได้รับ`, no automatic deadline,
  primary action `ยืนยันว่าได้รับเรียบร้อย`, and neutral secondary action
  `พบปัญหากับรายการนี้`.

The problem form is collapsed initially and expands only after the secondary
action. Before accepting the primary action, show the fulfillment-specific
confirmation disclosure that confirmation can begin seller payout. The final
physical confirmation actions remain `ยืนยันและเริ่มจ่ายให้ผู้ขาย` and
`กลับไปตรวจสินค้า`. Do not use a bare `ได้รับสินค้าแล้ว` as the release action.
```

- [ ] **Step 2: Extend the mobile-facing acceptance criteria**

In D1 of `docs/05_ACCEPTANCE_TESTS.md`, replace the old card action with
`ยืนยันว่าได้รับของเรียบร้อย`, retain the required final disclosure, and
append:

```markdown
**And** a physical delivered-window card shows the trusted
`dispute_window_ends_at` as an exact localized date and time
**And** a digital handoff card shows no automatic deadline
**And** the problem form remains collapsed until the buyer selects the neutral
problem action.
```

- [ ] **Step 3: Run formatting and contract checks**

```bash
git diff --check
rg -n \
  "ตรวจสินค้าให้เรียบร้อย|ยืนยันว่าได้รับของเรียบร้อย|พบปัญหากับรายการนี้|Digital handoff card shows no automatic deadline" \
  docs/02_UI_UX_AND_CONTENT_SPEC.md \
  docs/05_ACCEPTANCE_TESTS.md
```

Expected: `git diff --check` exits 0 and every approved phrase is present.

- [ ] **Step 4: Run the complete mobile unit/layout suite**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --no-restore
```

Expected: all tests pass with zero failures.

- [ ] **Step 5: Run the iOS simulator build again after all changes**

```bash
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj \
  -f net10.0-ios \
  -p:RuntimeIdentifier=iossimulator-arm64 \
  --no-restore
```

Expected: build succeeds with zero errors.

- [ ] **Step 6: Review the diff against the non-negotiable rules**

Run:

```bash
git diff -- \
  src/Toklong.Mobile \
  tests/Toklong.Mobile.Core.Tests \
  docs/02_UI_UX_AND_CONTENT_SPEC.md \
  docs/05_ACCEPTANCE_TESTS.md
git status --short
```

Confirm from the diff:

- no API/domain/payment/shipping/dispute transition file changed;
- physical confirmation requires an existing server-supplied deadline;
- digital copy has no elapsed-time release claim;
- the final disclosure remains before `ConfirmReceiptAsync`;
- the problem form is still initially collapsed;
- no secret, personal data, provider key, or raw credential was added.

- [ ] **Step 7: Commit the documentation and verification slice**

```bash
git add \
  docs/02_UI_UX_AND_CONTENT_SPEC.md \
  docs/05_ACCEPTANCE_TESTS.md
git commit -m "docs: align receipt confirmation UX contract"
```

## Completion report

Report:

1. The new physical/digital card copy and exact physical deadline presentation.
2. The preserved `DELIVERED_DISPUTE_WINDOW` / `DIGITAL_DELIVERY_SUBMITTED` to buyer-confirmation behavior.
3. Presenter and XAML layout tests added or updated.
4. The assumption that `MobileApi` continues mapping trusted `DisputeWindowEndsAt` to `ActionDeadline`.
5. Any build or simulator limitation encountered.
6. The next smallest vertical slice: install the verified iOS build on the connected device and test one physical delivered transaction plus one digital handoff transaction.
