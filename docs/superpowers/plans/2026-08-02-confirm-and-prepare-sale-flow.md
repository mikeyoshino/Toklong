# Confirm-and-Prepare Sale Flow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the separate consumer-facing offer-acceptance experience with one seller `ยืนยันพร้อมขาย` action and one buyer `ยืนยันและชำระ` action while preserving consent evidence, state transitions, payment gates, and fulfillment safety.

**Architecture:** Keep `AWAITING_SELLER_ACCEPTANCE → SELLER_ACCEPTED_AWAITING_PAYMENT → CHECKOUT_STARTED/PAYMENT_PENDING → PAID_*` and the existing seller `/accept` API. Change mobile orchestration and consumer copy, remove the buyer's standalone checkbox, keep `AcceptedTerms=true` in the authenticated payment-sheet request, and attach the existing buyer payment deadline to the seller-ready notification.

**Tech Stack:** .NET 10, C#, .NET MAUI XAML, MediatR, EF Core, xUnit, Stripe PaymentSheet, TOKLONG transition and notification-outbox infrastructure.

## Global Constraints

- Preserve existing internal states, agreement hashes, acceptance evidence, role authorization, allow-listed transitions, and immutable audit events.
- Do not add a database migration or rename `buyer_offer.seller_accepted`.
- Physical shipment and digital handoff stay hidden until verified provider payment.
- Physical readiness requires a fresh authoritative shipping quote; digital readiness has no address, parcel, carrier, or tracking fields.
- Never store or log passwords, OTPs, recovery codes, private keys, QR login data, or reusable credentials.
- Keep the exact one-hour payment deadline and existing late-payment/refund behavior.
- Show all material terms, total, deadline, payout trigger, dispute rule, and terms version before payment.
- Preserve integer-satang money and ISO currency handling.
- Keep one primary action per state and accessible 44-point targets, focus order, semantic descriptions, and contrast.
- Preserve existing uncommitted workspace changes; stage only files named by each task.

## File Structure

- `SellerOfferPage.xaml` and `SellerOfferViewModel.cs`: one seller preparation surface and command.
- `SellerReadinessAnalytics.cs`: safe readiness analytics dimensions.
- `TransactionDetailPage.xaml` and `TransactionDetailViewModel.cs`: one buyer consent/payment action.
- `TransactionStatePresenter.cs`, `AppTransaction.cs`, and `TransactionView.cs`: readiness-oriented presentation over unchanged states.
- `SaleTransaction.cs`: attach the buyer payment deadline to the existing outbox message.
- `ListNotifications.cs`: render seller-ready notification copy and exact Bangkok deadline.
- Existing xUnit projects: XAML, accessibility, orchestration, domain, notification, authorization, payment, and replay tests.

---

### Task 1: Seller Prepare-Sale Surface and Analytics

**Files:**
- Create: `src/Toklong.Mobile/Core/SellerReadinessAnalytics.cs`
- Create: `tests/Toklong.Mobile.Core.Tests/SellerReadinessAnalyticsTests.cs`
- Modify: `src/Toklong.Mobile/Pages/SellerOfferPage.xaml:7-481`
- Modify: `src/Toklong.Mobile/ViewModels/SellerOfferViewModel.cs:1-363`
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs:1455-1530`
- Modify: `tests/Toklong.Mobile.Core.Tests/SellerOfferNavigationTests.cs:6-141`

**Interfaces:**
- Consumes: existing `ISellerOfferService.AcceptAsync(string, Guid, bool, bool, SellerShippingSelection?, CancellationToken)`.
- Produces: `ConfirmReadyCommand`, `ConfirmReadyAsync()`, and safe `SellerReadinessAnalytics` events.

- [ ] **Step 1: Write failing analytics tests**

```csharp
[Theory]
[InlineData(AppFulfillmentType.Physical, "physical")]
[InlineData(AppFulfillmentType.Digital, "game_account")]
public void Confirmed_records_only_safe_type(
    AppFulfillmentType type,
    string expected)
{
    var value = SellerReadinessAnalytics.Confirmed(type);

    Assert.Equal("seller_readiness_confirmed", value.Name);
    Assert.Equal(expected, value.Properties["type"]);
    Assert.Single(value.Properties);
}

[Fact]
public void Validation_failed_records_safe_enums_only()
{
    var value = SellerReadinessAnalytics.ValidationFailed(
        AppFulfillmentType.Physical,
        SellerReadinessFailureReason.ShippingSelection);

    Assert.Equal("seller_readiness_validation_failed", value.Name);
    Assert.Equal("physical", value.Properties["type"]);
    Assert.Equal("shipping_selection", value.Properties["reason"]);
    Assert.Equal(2, value.Properties.Count);
}
```

- [ ] **Step 2: Write failing XAML and navigation expectations**

Require `เตรียมขาย`, `เตรียมการจัดส่ง`, `เตรียมส่งมอบไอดีเกม`, and:

```csharp
var confirm = page.Descendants(Maui + "Button")
    .Single(button =>
        AttributeValue(button, "AutomationId") ==
            "ConfirmSellerReadyButton");

Assert.Equal(
    "{Binding ConfirmReadyCommand}",
    AttributeValue(confirm, "Command"));
Assert.Equal("ยืนยันพร้อมขาย", AttributeValue(confirm, "Text"));
Assert.Equal(
    "ยืนยันว่ารายละเอียดถูกต้องและเปิดให้ผู้ซื้อชำระเงิน",
    AttributeValue(confirm, "SemanticProperties.Description"));
Assert.DoesNotContain("ตรวจข้อเสนอจากผู้ซื้อ", labels);
```

Update `SellerOfferNavigationTests` to inject a recording `IMobileAnalytics`,
execute `ConfirmReadyCommand`, assert the existing `AcceptAsync` call occurs
once, navigation still opens `TransactionDetailPage`, and one
`seller_readiness_confirmed` event contains only `type=game_account`.

- [ ] **Step 3: Run focused tests and verify RED**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~SellerReadinessAnalyticsTests|FullyQualifiedName~SellerOfferNavigationTests|FullyQualifiedName~UiLayoutConsistencyTests.SellerOffer_"
```

Expected: FAIL because readiness analytics, command, identifiers, and copy do not exist.

- [ ] **Step 4: Implement safe analytics**

Create:

```csharp
public enum SellerReadinessFailureReason
{
    PayoutAccount,
    Confirmations,
    ShippingSelection
}

public static class SellerReadinessAnalytics
{
    public static MobileAnalyticsEvent Confirmed(AppFulfillmentType type) =>
        Event("seller_readiness_confirmed", type);

    public static MobileAnalyticsEvent Declined(AppFulfillmentType type) =>
        Event("seller_readiness_declined", type);

    public static MobileAnalyticsEvent ValidationFailed(
        AppFulfillmentType type,
        SellerReadinessFailureReason reason) =>
        Event(
            "seller_readiness_validation_failed",
            type,
            ("reason", reason switch
            {
                SellerReadinessFailureReason.PayoutAccount => "payout_account",
                SellerReadinessFailureReason.Confirmations => "confirmations",
                SellerReadinessFailureReason.ShippingSelection => "shipping_selection",
                _ => throw new ArgumentOutOfRangeException(nameof(reason))
            }));

    private static MobileAnalyticsEvent Event(
        string name,
        AppFulfillmentType type,
        params (string Key, string Value)[] properties) =>
        new(
            name,
            new[]
            {
                ("type", type switch
                {
                    AppFulfillmentType.Physical => "physical",
                    AppFulfillmentType.Digital => "game_account",
                    _ => throw new ArgumentOutOfRangeException(nameof(type))
                })
            }
            .Concat(properties)
            .ToDictionary(x => x.Item1, x => x.Item2, StringComparer.Ordinal));
}
```

- [ ] **Step 5: Implement seller readiness orchestration**

Inject `IMobileAnalytics`. Rename the UI command/method only; keep
`ISellerOfferService.AcceptAsync` and the API/domain transition unchanged.

```csharp
public ICommand ConfirmReadyCommand =>
    new AsyncCommand(ConfirmReadyAsync);
```

In `ConfirmReadyAsync`, track a safe validation event before each existing
payout/confirmation/shipping return. After `AcceptAsync` succeeds, track
`Confirmed(fulfillmentType)` and preserve the current two-route navigation.
After `DeclineAsync` succeeds, track `Declined(fulfillmentType)`. Never track
IDs, addresses, bank details, product names, quote references, or credentials.

- [ ] **Step 6: Implement seller XAML**

Use exact copy:

```xml
<ContentPage Title="เตรียมขาย" ...>
    <Label Style="{StaticResource PageTitle}" Text="เตรียมขาย" />
    <Label
        Style="{StaticResource RefinedBodyText}"
        Text="ตรวจรายละเอียดและเตรียมข้อมูลให้ครบก่อนเปิดให้ผู้ซื้อชำระ" />
    ...
    <Button
        AutomationId="ConfirmSellerReadyButton"
        Style="{StaticResource RefinedPrimaryButton}"
        Command="{Binding ConfirmReadyCommand}"
        SemanticProperties.Description="ยืนยันว่ารายละเอียดถูกต้องและเปิดให้ผู้ซื้อชำระเงิน"
        Text="ยืนยันพร้อมขาย" />
    <Button
        AutomationId="DeclineSellerOfferButton"
        ...
        Text="ปฏิเสธรายการ" />
</ContentPage>
```

Rename physical `เตรียมค่าจัดส่ง` to `เตรียมการจัดส่ง`. Add a digital-only
`เตรียมส่งมอบไอดีเกม` heading and warning:

```xml
<Label
    Style="{StaticResource RefinedHelperText}"
    Text="ห้ามกรอกหรือแนบรหัสผ่าน OTP รหัสกู้คืน QR เข้าสู่ระบบ หรือข้อมูลลับใน TOKLONG" />
```

Preserve the seller rights and electronic-terms checkboxes because consent
evidence remains required.

- [ ] **Step 7: Verify GREEN and compile XAML**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~SellerReadinessAnalyticsTests|FullyQualifiedName~SellerOfferNavigationTests|FullyQualifiedName~UiLayoutConsistencyTests.SellerOffer_"
xmllint --noout src/Toklong.Mobile/Pages/SellerOfferPage.xaml
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj -f net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64 --no-restore -t:Compile
```

Expected: PASS and 0 compile errors. The unrelated existing
`IMediaPicker.PickPhotoAsync` obsolete warning may remain.

- [ ] **Step 8: Commit Task 1**

```bash
git add -- src/Toklong.Mobile/Core/SellerReadinessAnalytics.cs src/Toklong.Mobile/Pages/SellerOfferPage.xaml src/Toklong.Mobile/ViewModels/SellerOfferViewModel.cs tests/Toklong.Mobile.Core.Tests/SellerReadinessAnalyticsTests.cs tests/Toklong.Mobile.Core.Tests/SellerOfferNavigationTests.cs tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs
git commit -m "feat: combine seller readiness and offer confirmation"
```

### Task 2: Buyer One-Action Consent and Payment

**Files:**
- Modify: `src/Toklong.Mobile/Pages/TransactionDetailPage.xaml:378-532`
- Modify: `src/Toklong.Mobile/ViewModels/TransactionDetailViewModel.cs:1-500,726-756`
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs:2000-2075`
- Modify: `tests/Toklong.Mobile.Core.Tests/TransactionDetailParcelProtectionViewModelTests.cs`

**Interfaces:**
- Consumes: `IStripePaymentSheetService.PresentAsync(Guid, string, CancellationToken)`, whose implementation already sends `{ AcceptedTerms = true }`.
- Produces: `PaymentActionText == "ยืนยันและชำระ {CheckoutAmountText}"`; no local `AcceptedTerms` property.

- [ ] **Step 1: Write the failing XAML test**

Replace the checkbox assertion with:

```csharp
Assert.Empty(payment.Descendants(Maui + "CheckBox"));
Assert.Contains(
    payment.Descendants(Maui + "Label"),
    label => AttributeValue(label, "Text") ==
        "เมื่อกดชำระ คุณยืนยันว่าได้ตรวจรายละเอียดและยอมรับข้อตกลงแล้ว");
```

Keep assertions for the bound primary action, semantic description, busy
feedback, and inline errors.

- [ ] **Step 2: Write failing view-model behavior**

Remove all `viewModel.AcceptedTerms = true;` setup lines. Change payment text
expectations to `ยืนยันและชำระ ฿456`. Add:

```csharp
[Fact]
public async Task Ready_checkout_starts_without_a_separate_acceptance_toggle()
{
    var service = new ParcelProtectionService
    {
        Protection = ReadyProtection(),
        Transaction = Transaction(amountSatang: 456_00)
    };
    var sheet = new RecordingSheet(PaymentSheetOutcome.Completed);
    var viewModel = ViewModel(service, sheet);

    await viewModel.LoadAsync(service.Transaction.Id);

    Assert.True(viewModel.CanStartPayment);
    Assert.Equal("ยืนยันและชำระ ฿456", viewModel.PaymentActionText);

    await ExecuteAsync(viewModel.PrimaryActionCommand);

    Assert.Equal(1, sheet.Calls);
}
```

- [ ] **Step 3: Run focused tests and verify RED**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~TransactionDetailParcelProtectionViewModelTests|FullyQualifiedName~UiLayoutConsistencyTests.TransactionDetail_"
```

Expected: FAIL on the old checkbox, gate, property, and `ชำระ` label.

- [ ] **Step 4: Remove the standalone acceptance toggle**

Delete `acceptedTerms`, the `AcceptedTerms` property, and the early
`if (!AcceptedTerms)` return. Use:

```csharp
public string PaymentActionText =>
    $"ยืนยันและชำระ {CheckoutAmountText}";

public string PaymentSemanticDescription =>
    $"ยืนยันข้อตกลงและเปิดหน้าจ่ายเงินยอด {CheckoutAmountText}";

public bool CanStartPayment =>
    !IsBusy &&
    !IsParcelProtectionChoiceVisible;
```

Do not modify `StripePaymentSheetService`; the authenticated payment action
continues to record buyer consent server-side with `AcceptedTerms = true`.

- [ ] **Step 5: Replace the checkbox with passive consent copy**

```xml
<Label
    FontSize="13"
    LineBreakMode="WordWrap"
    Text="เมื่อกดชำระ คุณยืนยันว่าได้ตรวจรายละเอียดและยอมรับข้อตกลงแล้ว"
    TextColor="{StaticResource Muted}" />
```

Keep the complete cost disclosure, parcel-protection outcome, terms content,
payment feedback, and primary button immediately below it.

- [ ] **Step 6: Verify GREEN and compile XAML**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~TransactionDetailParcelProtectionViewModelTests|FullyQualifiedName~UiLayoutConsistencyTests.TransactionDetail_"
xmllint --noout src/Toklong.Mobile/Pages/TransactionDetailPage.xaml
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj -f net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64 --no-restore -t:Compile
```

Expected: PASS and 0 compile errors.

- [ ] **Step 7: Commit Task 2**

```bash
git add -- src/Toklong.Mobile/Pages/TransactionDetailPage.xaml src/Toklong.Mobile/ViewModels/TransactionDetailViewModel.cs tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs tests/Toklong.Mobile.Core.Tests/TransactionDetailParcelProtectionViewModelTests.cs
git commit -m "feat: combine buyer consent and payment action"
```

### Task 3: Readiness Copy and Exact-Deadline Notification

**Files:**
- Modify: `src/Toklong.Mobile/Core/TransactionStatePresenter.cs:24-52`
- Modify: `src/Toklong.Mobile/Core/AppTransaction.cs:471-491`
- Modify: `src/Toklong.Application/Transactions/TransactionView.cs:280-290`
- Modify: `src/Toklong.Domain/Transactions/SaleTransaction.cs:3679-3730`
- Modify: `src/Toklong.Application/Features/Notifications/ListNotifications/ListNotifications.cs:44-176`
- Modify: `tests/Toklong.Mobile.Core.Tests/TransactionPresentationTests.cs:5-55`
- Modify: `tests/Toklong.Domain.Tests/Transactions/SaleTransactionTests.cs:850-883`
- Modify: `tests/Toklong.Application.Tests/Notifications/NotificationContentTests.cs:6-33`

**Interfaces:**
- Consumes: existing internal states/events and `NotificationOutboxMessage.Create(..., actionDeadlineAt)`.
- Produces: readiness-oriented Thai labels and a `seller_accepted` message whose deadline equals `BuyerPaymentDeadlineAt`.

- [ ] **Step 1: Write failing presentation tests**

```csharp
Assert.Equal("รอผู้ขายเตรียมขาย", buyer.StatusLabel);
Assert.Equal("มีรายการรอเตรียมขาย", seller.StatusLabel);
Assert.Equal("เตรียมขาย", seller.PrimaryActionLabel);
Assert.Equal("ผู้ขายพร้อมขายแล้ว", readyBuyer.StatusLabel);
Assert.Equal("ตรวจยอดและชำระ", readyBuyer.PrimaryActionLabel);
```

Also assert `AppTransaction.StatusGuidance` contains
`รอผู้ขายตรวจสอบและเตรียมขาย` when awaiting and
`ผู้ขายพร้อมขายแล้ว` plus the exact deadline when ready.

- [ ] **Step 2: Write failing outbox and notification tests**

After seller acceptance:

```csharp
var readyNotice = Assert.Single(
    transaction.Notifications,
    item => item.Template == "seller_accepted");
Assert.Equal(
    transaction.BuyerPaymentDeadlineAt,
    readyNotice.ActionDeadlineAt);
```

Add:

```csharp
[Fact]
public void Seller_ready_notification_contains_exact_bangkok_deadline()
{
    var deadline = new DateTimeOffset(
        2026, 8, 2, 10, 0, 0, TimeSpan.Zero);
    var notification = NotificationContent.From(
        new NotificationInboxRecord(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "seller_accepted",
            "ไอดีเกม Mythic Arena",
            7_200_00,
            "THB",
            "public-token",
            deadline.AddMinutes(-5),
            ActionDeadlineAt: deadline));

    Assert.Equal("ผู้ขายพร้อมขายแล้ว", notification.Title);
    Assert.Equal(
        "ไอดีเกม Mythic Arena · ตรวจยอดและชำระภายใน 2 ส.ค. 2569 17:00 น.",
        notification.Body);
}
```

- [ ] **Step 3: Run focused tests and verify RED**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore --filter FullyQualifiedName~TransactionPresentationTests
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~SaleTransactionTests
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj --no-restore --filter FullyQualifiedName~NotificationContentTests
```

Expected: FAIL on old copy and absent notification deadline.

- [ ] **Step 4: Update presentation without changing actions or states**

```csharp
"AwaitingSellerAcceptance" when role == AppTransactionRole.Buyer =>
    Progress("รอผู้ขายเตรียมขาย"),
"AwaitingSellerAcceptance" =>
    Action(
        "มีรายการรอเตรียมขาย",
        "เตรียมขาย",
        TransactionAction.ReviewSellerOffer),
"SellerAcceptedAwaitingPayment" when role == AppTransactionRole.Buyer =>
    Action(
        "ผู้ขายพร้อมขายแล้ว",
        "ตรวจยอดและชำระ",
        TransactionAction.ReviewAndPay),
```

Use `รอผู้ขายตรวจสอบและเตรียมขายถึง {ExactDeadline()}` and
`ผู้ขายพร้อมขายแล้ว ชำระภายใน {ExactDeadline()}` in `StatusGuidance`.
Use `รอผู้ขายเตรียมขาย` and `ผู้ขายพร้อมขายแล้ว · รอผู้ซื้อชำระ` in
`TransactionView.ThaiStateLabel`.

- [ ] **Step 5: Attach the existing deadline to the existing event**

In `QueueTransitionNotifications`:

```csharp
var actionDeadline = eventName == "buyer_offer.seller_accepted"
    ? BuyerPaymentDeadlineAt
    : null;
```

Pass `actionDeadlineAt: actionDeadline` to
`NotificationOutboxMessage.Create`. Do not change template or audit event names.

- [ ] **Step 6: Render exact Bangkok notification copy**

```csharp
"seller_accepted" => (
    "ผู้ขายพร้อมขายแล้ว",
    SellerReadyBody(record),
    $"toklong://transaction/{record.TransactionId:N}"),
```

Add:

```csharp
private static string SellerReadyBody(NotificationInboxRecord record) =>
    record.ActionDeadlineAt.HasValue
        ? $"{record.ProductName} · ตรวจยอดและชำระภายใน {FormatBangkokDateTime(record.ActionDeadlineAt.Value)} น."
        : $"{record.ProductName} · ตรวจยอดและชำระเงิน";

private static string FormatBangkokDateTime(DateTimeOffset value)
{
    var bangkok = TimeZoneInfo.ConvertTime(
        value,
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok"));
    return bangkok.ToString("d MMM yyyy HH:mm", ThaiCulture);
}
```

Reuse `FormatBangkokDateTime` in evidence-deadline copy.

- [ ] **Step 7: Verify GREEN**

Run the three commands from Step 3. Expected: all PASS; internal states,
authorization, hashes, and audit names remain unchanged.

- [ ] **Step 8: Commit Task 3**

```bash
git add -- src/Toklong.Mobile/Core/TransactionStatePresenter.cs src/Toklong.Mobile/Core/AppTransaction.cs src/Toklong.Application/Transactions/TransactionView.cs src/Toklong.Domain/Transactions/SaleTransaction.cs src/Toklong.Application/Features/Notifications/ListNotifications/ListNotifications.cs tests/Toklong.Mobile.Core.Tests/TransactionPresentationTests.cs tests/Toklong.Domain.Tests/Transactions/SaleTransactionTests.cs tests/Toklong.Application.Tests/Notifications/NotificationContentTests.cs
git commit -m "feat: present accepted offers as ready to sell"
```

### Task 4: Documentation and Full Safety Verification

**Files:**
- Modify: `docs/02_UI_UX_AND_CONTENT_SPEC.md`
- Modify: `docs/05_ACCEPTANCE_TESTS.md`

**Interfaces:**
- Consumes: Tasks 1–3 behavior and unchanged state mapping.
- Produces: normative UX and acceptance documentation matching implementation.

- [ ] **Step 1: Update UX specification**

Document exact seller titles/actions, buyer passive consent copy and
`ยืนยันและชำระ ฿[ยอดทั้งหมด]`, preservation of acceptance evidence, and the
verified-payment fulfillment gate. Keep domain descriptions of acceptance;
only the separate consumer experience disappears.

- [ ] **Step 2: Add exact acceptance scenarios**

Add scenarios asserting:

```markdown
**Given** a buyer-created offer awaits the intended seller
**When** the seller opens the offer
**Then** the page is titled `เตรียมขาย`
**And** applicable physical or digital preparation is shown
**And** the only primary action is `ยืนยันพร้อมขาย`.

**When** the intended seller confirms readiness
**Then** existing seller acceptance evidence and audit event are written
**And** the buyer sees `ผู้ขายพร้อมขายแล้ว`
**And** the exact one-hour payment deadline is visible.

**When** the buyer reviews every material term
**Then** no standalone acceptance checkbox is shown
**And** `ยืนยันและชำระ ฿[ยอดทั้งหมด]` records consent and starts payment
**And** no fulfillment action appears before provider-confirmed payment.
```

- [ ] **Step 3: Validate source and forbidden consumer bindings**

```bash
git diff --check
xmllint --noout src/Toklong.Mobile/App.xaml src/Toklong.Mobile/Pages/SellerOfferPage.xaml src/Toklong.Mobile/Pages/TransactionDetailPage.xaml
rg -n 'Text="ยอมรับข้อเสนอ"|Text="ยืนยันข้อเสนอ"|AcceptedTerms}' src/Toklong.Mobile/Pages src/Toklong.Mobile/ViewModels
```

Expected: diff/XML PASS. The search finds no old action labels or buyer
`AcceptedTerms` binding; seller legal-terms copy and API fields remain allowed.

- [ ] **Step 4: Run all relevant tests**

```bash
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj --no-restore
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj --no-restore
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj --no-restore
dotnet test tests/Toklong.Crm.Tests/Toklong.Crm.Tests.csproj --no-restore
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore
```

Expected: all PASS, including transition/authorization, payment webhook
signature/idempotency/replay, shipping timing, digital no-auto-release, and
dispute-blocks-payout coverage.

- [ ] **Step 5: Compile mobile targets**

```bash
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj -f net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64 --no-restore -t:Compile
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj -f net10.0-android --no-restore -t:Compile
```

Expected: 0 errors. This workstation currently lacks `maui-android`; do not
install it without explicit approval. If absent, report Android compilation as
environment-blocked rather than passed. The unrelated existing
`IMediaPicker.PickPhotoAsync` obsolete warning may remain on iOS.

- [ ] **Step 6: Commit documentation**

```bash
git add -- docs/02_UI_UX_AND_CONTENT_SPEC.md docs/05_ACCEPTANCE_TESTS.md
git commit -m "docs: specify confirm-and-prepare transaction flow"
```

- [ ] **Step 7: Produce the required completion report**

Report: changes; unchanged transitions and evidence; test pass counts;
assumption that consumer acceptance is combined rather than erased; provider or
Android-workload limitations; and the next smallest slice of device visual
review for physical readiness, digital readiness, and buyer payment.
