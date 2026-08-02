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
