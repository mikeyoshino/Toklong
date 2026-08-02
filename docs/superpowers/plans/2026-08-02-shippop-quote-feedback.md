# SHIPPOP Quote Feedback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the seller shipping-quote action send SHIPPOP's documented Check Price shape and always show loading, result, or safe retry feedback beside the action.

**Architecture:** Keep SHIPPOP request correction inside `ShippopShippingProvider`, and keep quote-interaction state inside `SellerOfferViewModel` so unrelated page actions retain the existing shared message. `SellerOfferPage.xaml` only binds the dedicated state and preserves the current theme and layout. Provider rejection remains fail-closed with no local quote fallback and no transaction-state change.

**Tech Stack:** .NET 10, C#, .NET MAUI XAML, xUnit, `System.Text.Json`, existing TOKLONG shipping-provider and mobile MVVM abstractions.

## Global Constraints

- Preserve the existing buyer-first state machine, seller acceptance, payment, booking, tracking, dispute, refund, and payout behavior.
- Use only server-authoritative SHIPPOP quotes; never fall back to deterministic or client-computed pricing in `ShippopSandbox` mode.
- Never expose an API key, raw provider response, address, phone number, or provider-internal error in mobile UI, normal logs, tests, or commits.
- Preserve the current theme, colors, typography, form spacing, and minimum 44-point touch targets.
- Consumer copy is exactly `กำลังดูค่าจัดส่ง…`, `ยังไม่พบตัวเลือกจัดส่งสำหรับพัสดุนี้`, and `ยังดูค่าจัดส่งไม่ได้ กรุณาลองอีกครั้ง` for the applicable states.
- Editing origin or parcel measurements invalidates the selected quote and clears stale quote feedback.
- Quote retrieval creates no booking, PaymentIntent, immutable paid snapshot, audit transition, or financial state transition.
- Follow RED-GREEN-REFACTOR: every production behavior change must be preceded by a focused failing test that is observed failing for the intended reason.

---

### Task 1: Match the documented SHIPPOP Check Price request

**Files:**
- Modify: `tests/Toklong.Application.Tests/Shipping/ShippopShippingProviderTests.cs:392-455`
- Modify: `src/Toklong.Infrastructure/Services/ShippopShippingProvider.cs:235-264`
- Modify: `docs/05_ACCEPTANCE_TESTS.md` in `B0.1 — Seller locks origin, parcel, quote, and may save one origin`

**Interfaces:**
- Consumes: existing `ShipmentPayload(ShippingQuoteRequest, string, string?, bool)` serializer and `ShippopShippingOptions.ServiceCodes` allow-list.
- Produces: `GetQuotesAsync(ShippingQuoteRequest, CancellationToken)` sends integer JSON property `showall: 1` on every `pricelist/` shipment.

- [ ] **Step 1: Write the failing provider-contract test**

Add this focused test next to `Quote_is_integer_satang_and_signed_for_exact_request`:

```csharp
[Fact]
public async Task Quote_request_includes_documented_showall_flag()
{
    string? body = null;
    var provider = Provider(async request =>
    {
        body = await request.Content!.ReadAsStringAsync();
        return Json(
            """
            {
              "status": true,
              "data": {
                "0": {
                  "EMST": {
                    "available": true,
                    "courier_code": "EMST",
                    "courier_name": "EMS Thailand Post",
                    "price": "52.00"
                  }
                }
              }
            }
            """);
    });

    await provider.GetQuotesAsync(Request(), default);

    Assert.NotNull(body);
    using var document = JsonDocument.Parse(body);
    var shipment = document.RootElement
        .GetProperty("data")
        .GetProperty("0");
    Assert.Equal(1, shipment.GetProperty("showall").GetInt32());
    Assert.Equal(
        "EMST",
        shipment.GetProperty("courier_code").GetString());
    Assert.Equal(
        1_200,
        shipment.GetProperty("parcel")
            .GetProperty("weight")
            .GetInt32());
}
```

- [ ] **Step 2: Run the test and verify RED**

Run:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj --no-restore --filter FullyQualifiedName~Quote_request_includes_documented_showall_flag
```

Expected: FAIL because `shipment.GetProperty("showall")` cannot find the property. A compile error or unrelated failure does not satisfy RED.

- [ ] **Step 3: Implement the minimal provider correction**

Change only the quote call in `GetQuotesAsync`:

```csharp
data[(index++).ToString(CultureInfo.InvariantCulture)] =
    ShipmentPayload(
        request,
        serviceCode,
        includeReference: null,
        showAll: true);
```

Do not change booking/confirmation behavior, add retry, log provider bodies, or change response parsing.

- [ ] **Step 4: Run the focused and provider test suites and verify GREEN**

Run:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj --no-restore --filter FullyQualifiedName~ShippopShippingProviderTests
```

Expected: all `ShippopShippingProviderTests` pass, including the new request-contract assertion.

- [ ] **Step 5: Align the canonical acceptance criterion**

Append this requirement to `B0.1` after the backend quote assertion:

```markdown
**And** each SHIPPOP Check Price shipment uses the published request shape,
including integer `showall: 1`, without trusting a client-computed fee or
falling back to a simulated quote when SHIPPOP rejects the request.
```

- [ ] **Step 6: Commit the provider slice**

```bash
git add tests/Toklong.Application.Tests/Shipping/ShippopShippingProviderTests.cs src/Toklong.Infrastructure/Services/ShippopShippingProvider.cs docs/05_ACCEPTANCE_TESTS.md
git commit -m "fix: match SHIPPOP quote request contract"
```

---

### Task 2: Give shipping quotes dedicated mobile state

**Files:**
- Create: `tests/Toklong.Mobile.Core.Tests/SellerOfferShippingQuoteTests.cs`
- Modify: `src/Toklong.Mobile/ViewModels/SellerOfferViewModel.cs:20-29, 107-117, 228-265, 378-427, 529-544`

**Interfaces:**
- Consumes: `ISellerOfferService.GetShippingQuotesAsync(string, SellerShippingQuoteRequest, CancellationToken)` and existing origin/package properties.
- Produces: read-only properties `ShippingQuoteMessage`, `HasShippingQuoteMessage`, `IsLoadingShippingQuotes`, and `CanLoadShippingQuotes`; `LoadShippingQuotesCommand` continues to be the XAML command.

- [ ] **Step 1: Add focused failing ViewModel tests**

Create `SellerOfferShippingQuoteTests.cs` with these behaviors and focused stubs:

```csharp
using Toklong.Mobile.Core;
using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Core.Tests;

public sealed class SellerOfferShippingQuoteTests
{
    [Fact]
    public async Task Invalid_package_shows_shipping_local_feedback_without_calling_api()
    {
        var service = new QuoteServiceStub(_ =>
            Task.FromResult<IReadOnlyList<MobileShippingQuote>>([]));
        var viewModel = await CreateAsync(service);

        viewModel.LoadShippingQuotesCommand.Execute(null);

        Assert.Equal(0, service.QuoteCalls);
        Assert.Equal(
            "กรอกน้ำหนักและขนาดพัสดุให้ครบ",
            viewModel.ShippingQuoteMessage);
        Assert.True(viewModel.HasShippingQuoteMessage);
        Assert.False(viewModel.HasMessage);
    }

    [Fact]
    public async Task Missing_new_origin_shows_shipping_local_feedback_without_calling_api()
    {
        var service = new QuoteServiceStub(
            _ => Task.FromResult<IReadOnlyList<MobileShippingQuote>>([]),
            hasSavedOrigin: false);
        var viewModel = await CreateAsync(service);
        FillPackage(viewModel);

        viewModel.LoadShippingQuotesCommand.Execute(null);

        Assert.Equal(0, service.QuoteCalls);
        Assert.Equal(
            "กรอกที่อยู่ต้นทางให้ครบ",
            viewModel.ShippingQuoteMessage);
    }

    [Fact]
    public async Task Quote_request_exposes_loading_blocks_overlap_and_selects_first_result()
    {
        var pending = new TaskCompletionSource<
            IReadOnlyList<MobileShippingQuote>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new QuoteServiceStub(_ => pending.Task);
        var viewModel = await CreateAsync(service);
        FillPackage(viewModel);

        viewModel.LoadShippingQuotesCommand.Execute(null);
        await WaitUntilAsync(() => service.QuoteCalls == 1);

        Assert.True(viewModel.IsLoadingShippingQuotes);
        Assert.False(viewModel.CanLoadShippingQuotes);
        viewModel.LoadShippingQuotesCommand.Execute(null);
        await Task.Delay(20);
        Assert.Equal(1, service.QuoteCalls);

        var first = Quote("EMST", 5_200);
        pending.SetResult([first, Quote("FLE", 4_900)]);
        await WaitUntilAsync(() => !viewModel.IsLoadingShippingQuotes);

        Assert.True(viewModel.CanLoadShippingQuotes);
        Assert.Equal(2, viewModel.ShippingQuotes.Count);
        Assert.Same(first, viewModel.SelectedShippingQuote);
        Assert.False(viewModel.HasShippingQuoteMessage);
    }

    [Fact]
    public async Task Provider_failure_is_sanitized_and_editing_input_clears_feedback()
    {
        var service = new QuoteServiceStub(_ =>
            Task.FromException<IReadOnlyList<MobileShippingQuote>>(
                new InvalidOperationException(
                    "raw SHIPPOP provider rejection")));
        var viewModel = await CreateAsync(service);
        FillPackage(viewModel);

        viewModel.LoadShippingQuotesCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.HasShippingQuoteMessage);

        Assert.Equal(
            "ยังดูค่าจัดส่งไม่ได้ กรุณาลองอีกครั้ง",
            viewModel.ShippingQuoteMessage);
        Assert.DoesNotContain(
            "SHIPPOP",
            viewModel.ShippingQuoteMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.HasMessage);

        viewModel.WeightGrams = "1300";

        Assert.False(viewModel.HasShippingQuoteMessage);
        Assert.Empty(viewModel.ShippingQuotes);
        Assert.Null(viewModel.SelectedShippingQuote);
    }

    [Fact]
    public async Task Quote_result_for_changed_input_is_discarded()
    {
        var pending = new TaskCompletionSource<
            IReadOnlyList<MobileShippingQuote>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new QuoteServiceStub(_ => pending.Task);
        var viewModel = await CreateAsync(service);
        FillPackage(viewModel);

        viewModel.LoadShippingQuotesCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.IsLoadingShippingQuotes);
        viewModel.WeightGrams = "1300";
        pending.SetResult([Quote("EMST", 5_200)]);
        await WaitUntilAsync(() => !viewModel.IsLoadingShippingQuotes);

        Assert.Empty(viewModel.ShippingQuotes);
        Assert.Null(viewModel.SelectedShippingQuote);
        Assert.False(viewModel.HasShippingQuoteMessage);
    }

    [Fact]
    public async Task Empty_provider_result_is_visible_beside_quote_action()
    {
        var service = new QuoteServiceStub(_ =>
            Task.FromResult<IReadOnlyList<MobileShippingQuote>>([]));
        var viewModel = await CreateAsync(service);
        FillPackage(viewModel);

        viewModel.LoadShippingQuotesCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.HasShippingQuoteMessage);

        Assert.Equal(
            "ยังไม่พบตัวเลือกจัดส่งสำหรับพัสดุนี้",
            viewModel.ShippingQuoteMessage);
        Assert.Empty(viewModel.ShippingQuotes);
    }

    private static async Task<SellerOfferViewModel> CreateAsync(
        QuoteServiceStub service)
    {
        var viewModel = new SellerOfferViewModel(
            service,
            new AddressServiceStub(),
            new NullAnalytics());
        await viewModel.LoadAsync("public-token");
        return viewModel;
    }

    private static void FillPackage(SellerOfferViewModel viewModel)
    {
        viewModel.WeightGrams = "1200";
        viewModel.WidthCentimeters = "20";
        viewModel.LengthCentimeters = "30";
        viewModel.HeightCentimeters = "15";
    }

    private static MobileShippingQuote Quote(
        string serviceCode,
        long feeSatang) =>
        new(
            "shippop",
            $"quote-{serviceCode}",
            serviceCode == "EMST" ? "THAIPOST" : "FLASH",
            serviceCode,
            serviceCode == "EMST" ? "ไปรษณีย์ไทย EMS" : "Flash Express",
            feeSatang,
            DateTimeOffset.Parse("2026-08-02T18:00:00+07:00"));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(2));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class NullAnalytics : IMobileAnalytics
    {
        public void Track(MobileAnalyticsEvent value)
        {
        }
    }

    private sealed class AddressServiceStub : IAddressService
    {
        public Task<IReadOnlyList<AddressOption>> GetProvincesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AddressOption>>([]);

        public Task<IReadOnlyList<AddressOption>> GetDistrictsAsync(
            int provinceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AddressOption>>([]);

        public Task<IReadOnlyList<SubdistrictOption>> GetSubdistrictsAsync(
            int districtId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubdistrictOption>>([]);
    }

    private sealed class QuoteServiceStub(
        Func<SellerShippingQuoteRequest,
            Task<IReadOnlyList<MobileShippingQuote>>> quote,
        bool hasSavedOrigin = true)
        : ISellerOfferService
    {
        private readonly MobileSavedShippingOrigin? origin =
            hasSavedOrigin
            ? new MobileSavedShippingOrigin(
                "คลองเตยเหนือ วัฒนา กรุงเทพมหานคร 10110",
                1,
                "กรุงเทพมหานคร",
                2,
                "วัฒนา",
                3,
                "คลองเตยเหนือ",
                "10110")
            : null;

        public int QuoteCalls { get; private set; }

        public Task<SellerOfferInvitation> GetAsync(
            string publicToken,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SellerOfferInvitation(
                new AppTransaction(
                    Guid.Parse("00000000-0000-0000-0000-000000000961"),
                    "กล้องมือสอง",
                    8_000_00,
                    "THB",
                    AppTransactionRole.Seller,
                    AppFulfillmentType.Physical,
                    "AwaitingSellerAcceptance",
                    DateTimeOffset.Parse("2026-08-02T10:00:00+07:00"),
                    DateTimeOffset.Parse("2026-08-03T10:00:00+07:00"),
                    "ผู้ซื้อ"),
                8_000_00,
                [],
                origin));

        public Task<IReadOnlyList<MobileShippingQuote>>
            GetShippingQuotesAsync(
                string publicToken,
                SellerShippingQuoteRequest request,
                CancellationToken cancellationToken = default)
        {
            QuoteCalls++;
            return quote(request);
        }

        public Task<IReadOnlyList<MobilePayoutAccount>>
            GetPayoutAccountsAsync(
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MobilePayoutAccount>>
            SavePayoutAccountAsync(
                Guid? accountId,
                string bankCode,
                string accountName,
                string accountNumber,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AppTransaction> AcceptAsync(
            string publicToken,
            Guid payoutAccountId,
            bool transferRightsAttested,
            bool sellerAcceptedTerms,
            SellerShippingSelection? shipping,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AppTransaction> DeclineAsync(
            string publicToken,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
```

- [ ] **Step 2: Run the ViewModel tests and verify RED**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore --filter FullyQualifiedName~SellerOfferShippingQuoteTests
```

Expected: compilation FAIL because `ShippingQuoteMessage`,
`HasShippingQuoteMessage`, `IsLoadingShippingQuotes`, and
`CanLoadShippingQuotes` do not exist. After adding only declarations, the
behavior assertions must still fail until the command is changed.

- [ ] **Step 3: Add the dedicated observable state**

Add fields and properties to `SellerOfferViewModel`:

```csharp
private string shippingQuoteMessage = "";
private bool isLoadingShippingQuotes;
private int shippingQuoteInputVersion;

public string ShippingQuoteMessage
{
    get => shippingQuoteMessage;
    private set
    {
        if (SetProperty(ref shippingQuoteMessage, value))
            OnPropertyChanged(nameof(HasShippingQuoteMessage));
    }
}

public bool HasShippingQuoteMessage =>
    !string.IsNullOrWhiteSpace(ShippingQuoteMessage);

public bool IsLoadingShippingQuotes
{
    get => isLoadingShippingQuotes;
    private set
    {
        if (SetProperty(ref isLoadingShippingQuotes, value))
            OnPropertyChanged(nameof(CanLoadShippingQuotes));
    }
}

public bool CanLoadShippingQuotes => !IsLoadingShippingQuotes;
```

- [ ] **Step 4: Replace quote loading with a dedicated fail-closed flow**

Replace `LoadShippingQuotesAsync` with this behavior:

```csharp
private async Task LoadShippingQuotesAsync()
{
    if (IsLoadingShippingQuotes)
        return;
    ShippingQuoteMessage = "";
    if (!TryGetPackage(
            out var weight,
            out var width,
            out var length,
            out var height))
    {
        ShippingQuoteMessage =
            "กรอกน้ำหนักและขนาดพัสดุให้ครบ";
        return;
    }
    if (!UseSavedOrigin &&
        (string.IsNullOrWhiteSpace(OriginAddressLine) ||
         SelectedOriginProvince is null ||
         SelectedOriginDistrict is null ||
         SelectedOriginSubdistrict is null))
    {
        ShippingQuoteMessage = "กรอกที่อยู่ต้นทางให้ครบ";
        return;
    }

    var inputVersion = shippingQuoteInputVersion;
    ShippingQuotes.Clear();
    SelectedShippingQuote = null;
    OnPropertyChanged(nameof(HasShippingQuotes));
    IsLoadingShippingQuotes = true;
    try
    {
        var quotes = await sellerOffers.GetShippingQuotesAsync(
            publicToken,
            new SellerShippingQuoteRequest(
                UseSavedOrigin,
                UseSavedOrigin ? null : OriginAddressLine,
                UseSavedOrigin ? null : SelectedOriginProvince?.Id,
                UseSavedOrigin ? null : SelectedOriginDistrict?.Id,
                UseSavedOrigin ? null : SelectedOriginSubdistrict?.Id,
                weight,
                width,
                length,
                height));
        if (inputVersion != shippingQuoteInputVersion)
            return;

        foreach (var quote in quotes)
            ShippingQuotes.Add(quote);
        SelectedShippingQuote = ShippingQuotes.FirstOrDefault();
        OnPropertyChanged(nameof(HasShippingQuotes));
        ShippingQuoteMessage = quotes.Count == 0
            ? "ยังไม่พบตัวเลือกจัดส่งสำหรับพัสดุนี้"
            : "";
    }
    catch (Exception)
    {
        if (inputVersion == shippingQuoteInputVersion)
            ShippingQuoteMessage =
                "ยังดูค่าจัดส่งไม่ได้ กรุณาลองอีกครั้ง";
    }
    finally
    {
        IsLoadingShippingQuotes = false;
    }
}
```

Do not assign `exception.Message` to the shipping-local property.

- [ ] **Step 5: Invalidate stale feedback whenever shipping inputs change**

Route the `UseSavedOrigin` setter through `ResetQuotes()`:

```csharp
public bool UseSavedOrigin
{
    get => useSavedOrigin;
    set
    {
        if (SetProperty(ref useSavedOrigin, value))
        {
            ResetQuotes();
            OnPropertyChanged(nameof(ShowOriginEditor));
            OnPropertyChanged(nameof(ShowSavedOrigin));
        }
    }
}
```

Then change the helper to:

```csharp
private void ResetQuotes()
{
    shippingQuoteInputVersion++;
    ShippingQuotes.Clear();
    SelectedShippingQuote = null;
    ShippingQuoteMessage = "";
    OnPropertyChanged(nameof(HasShippingQuotes));
}
```

Keep the existing origin hierarchy loading and parcel normalization intact.

- [ ] **Step 6: Run the focused and full mobile-core suites and verify GREEN**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore --filter FullyQualifiedName~SellerOfferShippingQuoteTests
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore
```

Expected: all quote-state tests pass, followed by the complete mobile-core
suite with no regressions.

- [ ] **Step 7: Commit the ViewModel slice**

```bash
git add tests/Toklong.Mobile.Core.Tests/SellerOfferShippingQuoteTests.cs src/Toklong.Mobile/ViewModels/SellerOfferViewModel.cs
git commit -m "fix: expose seller quote request state"
```

---

### Task 3: Render accessible feedback beside the quote action

**Files:**
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs:1458-1542`
- Modify: `src/Toklong.Mobile/Pages/SellerOfferPage.xaml:329-342`
- Modify: `docs/02_UI_UX_AND_CONTENT_SPEC.md` in `Scene 2 — Seller prepares the sale` and `Seller prepare-sale screen`

**Interfaces:**
- Consumes: Task 2 properties `ShippingQuoteMessage`, `HasShippingQuoteMessage`, `IsLoadingShippingQuotes`, and `CanLoadShippingQuotes`.
- Produces: `LoadShippingQuotesButton`, `ShippingQuoteLoadingStatus`, and `ShippingQuoteMessage` XAML elements in document order directly before the quote picker.

- [ ] **Step 1: Write the failing XAML structure/accessibility test**

Add this test beside `SellerOffer_PreparesSaleAndShowsMaterialReadOnlyTerms`:

```csharp
[Fact]
public void SellerOffer_ShowsAccessibleQuoteFeedbackBesideQuoteAction()
{
    var page = Load("Ui", "Pages", "SellerOfferPage.xaml");
    var elements = page.Descendants().ToList();
    var button = elements.Single(element =>
        AttributeValue(element, "AutomationId") ==
            "LoadShippingQuotesButton");
    var loading = elements.Single(element =>
        AttributeValue(element, "AutomationId") ==
            "ShippingQuoteLoadingStatus");
    var message = elements.Single(element =>
        AttributeValue(element, "AutomationId") ==
            "ShippingQuoteMessage");
    var picker = page.Descendants(Maui + "Picker")
        .Single(element =>
            AttributeValue(element, "ItemsSource") ==
                "{Binding ShippingQuotes}");

    Assert.Equal(
        "{Binding LoadShippingQuotesCommand}",
        AttributeValue(button, "Command"));
    Assert.Equal(
        "{Binding CanLoadShippingQuotes}",
        AttributeValue(button, "IsEnabled"));
    Assert.False(string.IsNullOrWhiteSpace(
        AttributeValue(button, "SemanticProperties.Description")));
    Assert.Equal(
        "{Binding IsLoadingShippingQuotes}",
        AttributeValue(loading, "IsVisible"));
    Assert.Contains(
        loading.Descendants(Maui + "Label"),
        label => AttributeValue(label, "Text") ==
            "กำลังดูค่าจัดส่ง…");
    Assert.Equal(
        "{Binding HasShippingQuoteMessage}",
        AttributeValue(message, "IsVisible"));
    Assert.Contains(
        message.Descendants(Maui + "Label"),
        label => AttributeValue(label, "Text") ==
            "{Binding ShippingQuoteMessage}");
    Assert.True(elements.IndexOf(button) < elements.IndexOf(loading));
    Assert.True(elements.IndexOf(loading) < elements.IndexOf(message));
    Assert.True(elements.IndexOf(message) < elements.IndexOf(picker));
}
```

- [ ] **Step 2: Run the XAML test and verify RED**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore --filter FullyQualifiedName~SellerOffer_ShowsAccessibleQuoteFeedbackBesideQuoteAction
```

Expected: FAIL because the three `AutomationId` elements and bindings do not
exist.

- [ ] **Step 3: Add the loading and feedback presentation**

Replace the existing quote button block with:

```xml
<Button
    AutomationId="LoadShippingQuotesButton"
    Style="{StaticResource RefinedSecondaryActionButton}"
    Command="{Binding LoadShippingQuotesCommand}"
    IsEnabled="{Binding CanLoadShippingQuotes}"
    SemanticProperties.Description="ตรวจสอบค่าจัดส่งจากต้นทางและขนาดพัสดุ"
    Text="ดูค่าจัดส่ง" />

<HorizontalStackLayout
    AutomationId="ShippingQuoteLoadingStatus"
    IsVisible="{Binding IsLoadingShippingQuotes}"
    Spacing="{StaticResource SpacingXs}">
    <ActivityIndicator
        AutomationProperties.IsInAccessibleTree="False"
        Color="{StaticResource BrandBlue}"
        HeightRequest="18"
        IsRunning="{Binding IsLoadingShippingQuotes}"
        WidthRequest="18" />
    <Label
        Style="{StaticResource RefinedHelperText}"
        Text="กำลังดูค่าจัดส่ง…"
        VerticalOptions="Center" />
</HorizontalStackLayout>

<Border
    AutomationId="ShippingQuoteMessage"
    IsVisible="{Binding HasShippingQuoteMessage}"
    Style="{StaticResource RefinedValidationBorder}">
    <Label
        Style="{StaticResource RefinedValidationText}"
        Text="{Binding ShippingQuoteMessage}" />
</Border>
```

Leave the quote picker and item/shipping breakdown immediately after these
elements. Do not move the page-level message or confirmation actions.

- [ ] **Step 4: Update canonical mobile UX wording**

Add to both physical prepare-sale descriptions in
`docs/02_UI_UX_AND_CONTENT_SPEC.md`:

```markdown
- `ดูค่าจัดส่ง` disables repeat submission while loading and shows
  `กำลังดูค่าจัดส่ง…` immediately below the action. The same location shows
  empty or consumer-safe retry feedback; quote failures are never deferred to
  the bottom of the page or replaced by a simulated price.
```

- [ ] **Step 5: Run focused and full mobile-core suites and verify GREEN**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore --filter FullyQualifiedName~SellerOffer_ShowsAccessibleQuoteFeedbackBesideQuoteAction
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore
```

Expected: both the focused XAML assertion and full suite pass.

- [ ] **Step 6: Commit the mobile presentation slice**

```bash
git add tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs src/Toklong.Mobile/Pages/SellerOfferPage.xaml docs/02_UI_UX_AND_CONTENT_SPEC.md
git commit -m "fix: show shipping quote feedback inline"
```

---

### Task 4: Verify the complete correction and hand off Sandbox validation

**Files:**
- Verify only; no production file changes are expected.

**Interfaces:**
- Consumes: Tasks 1-3.
- Produces: fresh test evidence and an explicit SHIPPOP Sandbox result or provider capability blocker.

- [ ] **Step 1: Run all directly affected automated suites**

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj --no-restore
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --no-restore
```

Expected: both suites pass with zero failures.

- [ ] **Step 2: Verify repository hygiene**

```bash
git diff --check
git status --short
```

Expected: `git diff --check` has no output. `git status --short` is empty after
the task commits; if unrelated user changes exist, report them and do not stage
or modify them.

- [ ] **Step 3: Restart the user-owned Sandbox process and exercise the UI**

In the terminal that owns the exported Test credentials:

```bash
./scripts/stop-local-dual-sim.sh
TOKLONG_SHIPPING_MODE=ShippopSandbox ./scripts/run-local-dual-sim.sh
```

Open the seller's physical offer, enter valid parcel measurements, and tap
`ดูค่าจัดส่ง` once.

Expected UI sequence:

```text
ดูค่าจัดส่ง
-> กำลังดูค่าจัดส่ง… (button disabled)
-> real SHIPPOP quote rows OR ยังดูค่าจัดส่งไม่ได้ กรุณาลองอีกครั้ง
```

If a real quote appears, select it and confirm that changing one measurement
clears the selection. If the safe error appears, inspect
`${TMPDIR:-/tmp}/toklong-local-dual-sim/backend.log`; an HTTP 200 with provider
`status=false` after the corrected payload is an external Sandbox
key/service-activation or account-contract blocker, not permission to add a
simulated quote.

- [ ] **Step 4: Produce the completion report**

Report:

1. provider request and mobile UI changes;
2. confirmation that no transaction state transition changed;
3. tests added and their fresh pass counts;
4. assumption that published `showall: 1` applies to this Sandbox account;
5. whether SHIPPOP returned a real quote or remains an account/provider blocker;
6. next smallest vertical slice: certify the enabled Sandbox service through quote, booking, confirmation, label, and tracking without enabling production flags.
