using Toklong.Mobile.Core;
using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Core.Tests;

public sealed class AuthenticatedHomeViewModelTests
{
    [Fact]
    public async Task Successful_load_exposes_positive_counts_and_safe_analytics()
    {
        var transactions = new SequenceTransactionService();
        transactions.EnqueueResult(
            Item("AwaitingSellerAcceptance"),
            Item("PaidAwaitingShipment"),
            Item("InTransit"),
            Item("PaidOut"),
            Item(
                "AwaitingSellerAcceptance",
                AppTransactionRole.Buyer));
        var analytics = new RecordingAnalytics();
        var viewModel = new AuthenticatedHomeViewModel(
            transactions,
            analytics,
            new AuthenticatedSessionBoundary());

        await viewModel.LoadAsync();

        Assert.True(viewModel.HasSellerSummary);
        Assert.True(viewModel.HasNewOffers);
        Assert.Equal("1 ข้อเสนอใหม่", viewModel.NewOfferBadgeText);
        Assert.True(viewModel.HasActionableSellerWork);
        Assert.Equal(
            "มี 2 รายการที่ต้องจัดการ",
            viewModel.ActionableSellerWorkText);
        Assert.Equal(
            "ขาย 1 ข้อเสนอใหม่ มี 2 รายการที่ต้องจัดการ",
            viewModel.SellerCardSemanticText);
        Assert.False(viewModel.HasLoadError);
        Assert.Equal("", viewModel.LoadErrorText);

        var opened = Assert.Single(analytics.Events);
        Assert.Equal("seller_home_opened", opened.Name);
        Assert.Equal(2, opened.Properties.Count);
        Assert.Equal("1", opened.Properties["new_offer_count"]);
        Assert.Equal("2", opened.Properties["actionable_count"]);
    }

    [Fact]
    public async Task Successful_empty_refresh_clears_visible_counts()
    {
        var transactions = new SequenceTransactionService();
        transactions.EnqueueResult(
            Item("AwaitingSellerAcceptance"),
            Item("PaidAwaitingShipment"));
        transactions.EnqueueResult();
        var analytics = new RecordingAnalytics();
        var viewModel = new AuthenticatedHomeViewModel(
            transactions,
            analytics,
            new AuthenticatedSessionBoundary());
        await viewModel.LoadAsync();

        await viewModel.LoadAsync();

        Assert.False(viewModel.HasSellerSummary);
        Assert.False(viewModel.HasNewOffers);
        Assert.False(viewModel.HasActionableSellerWork);
        Assert.False(viewModel.HasLoadError);
        Assert.Equal(
            "ขาย ตรวจข้อเสนอ ส่งสินค้า และติดตามยอดรับ",
            viewModel.SellerCardSemanticText);
        Assert.Equal(2, analytics.Events.Count);
        Assert.Equal(
            "0",
            analytics.Events[1].Properties["new_offer_count"]);
        Assert.Equal(
            "0",
            analytics.Events[1].Properties["actionable_count"]);
    }

    [Fact]
    public async Task Initial_failure_has_retry_copy_without_false_summary()
    {
        var transactions = new SequenceTransactionService();
        transactions.EnqueueFailure();
        var analytics = new RecordingAnalytics();
        var viewModel = new AuthenticatedHomeViewModel(
            transactions,
            analytics,
            new AuthenticatedSessionBoundary());

        await viewModel.LoadAsync();

        Assert.False(viewModel.HasSellerSummary);
        Assert.False(viewModel.HasNewOffers);
        Assert.False(viewModel.HasActionableSellerWork);
        Assert.True(viewModel.HasLoadError);
        Assert.Equal(
            "โหลดรายการไม่สำเร็จ · ลองอีกครั้ง",
            viewModel.LoadErrorText);
        Assert.Empty(analytics.Events);
    }

    [Fact]
    public async Task Later_failure_retains_counts_and_marks_them_stale()
    {
        var transactions = new SequenceTransactionService();
        transactions.EnqueueResult(
            Item("AwaitingSellerAcceptance"),
            Item("PaidAwaitingShipment"));
        transactions.EnqueueFailure();
        var analytics = new RecordingAnalytics();
        var viewModel = new AuthenticatedHomeViewModel(
            transactions,
            analytics,
            new AuthenticatedSessionBoundary());
        await viewModel.LoadAsync();

        await viewModel.LoadAsync();

        Assert.True(viewModel.HasSellerSummary);
        Assert.True(viewModel.HasNewOffers);
        Assert.Equal("1 ข้อเสนอใหม่", viewModel.NewOfferBadgeText);
        Assert.True(viewModel.HasActionableSellerWork);
        Assert.Equal(
            "มี 2 รายการที่ต้องจัดการ",
            viewModel.ActionableSellerWorkText);
        Assert.True(viewModel.HasLoadError);
        Assert.Equal(
            "อัปเดตล่าสุดไม่สำเร็จ",
            viewModel.LoadErrorText);
        Assert.Single(analytics.Events);
    }

    [Fact]
    public async Task Load_notifies_every_bound_summary_property()
    {
        var transactions = new SequenceTransactionService();
        transactions.EnqueueResult(Item("AwaitingSellerAcceptance"));
        var viewModel = new AuthenticatedHomeViewModel(
            transactions,
            new RecordingAnalytics(),
            new AuthenticatedSessionBoundary());
        var changed = new List<string?>();
        viewModel.PropertyChanged +=
            (_, eventArgs) => changed.Add(eventArgs.PropertyName);

        await viewModel.LoadAsync();

        var expected = new[]
        {
            nameof(viewModel.HasSellerSummary),
            nameof(viewModel.HasNewOffers),
            nameof(viewModel.HasActionableSellerWork),
            nameof(viewModel.NewOfferBadgeText),
            nameof(viewModel.ActionableSellerWorkText),
            nameof(viewModel.SellerCardSemanticText),
            nameof(viewModel.HasLoadError),
            nameof(viewModel.LoadErrorText)
        };
        Assert.Equal(expected.Length, changed.Count);
        Assert.All(
            expected,
            propertyName => Assert.Contains(propertyName, changed));
    }

    [Fact]
    public async Task Retry_command_starts_a_new_load()
    {
        var transactions = new SequenceTransactionService();
        transactions.EnqueueFailure();
        var retryResponse = transactions.EnqueuePending();
        var viewModel = new AuthenticatedHomeViewModel(
            transactions,
            new RecordingAnalytics(),
            new AuthenticatedSessionBoundary());
        await viewModel.LoadAsync();
        var loaded = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName ==
                    nameof(viewModel.HasNewOffers) &&
                viewModel.HasNewOffers)
                loaded.TrySetResult();
        };

        viewModel.RetryCommand.Execute(null);
        retryResponse.SetResult(
            [Item("AwaitingSellerAcceptance")]);
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, transactions.LoadCount);
        Assert.True(viewModel.HasNewOffers);
        Assert.False(viewModel.HasLoadError);
    }

    private static AppTransaction Item(
        string state,
        AppTransactionRole role =
            AppTransactionRole.Seller) =>
        new(
            Guid.NewGuid(),
            "สินค้า",
            100_00,
            "THB",
            role,
            AppFulfillmentType.Physical,
            state,
            DateTimeOffset.Parse("2026-07-28T15:00:00+07:00"),
            DateTimeOffset.Parse("2026-07-29T15:00:00+07:00"),
            role == AppTransactionRole.Seller
                ? "ผู้ซื้อ"
                : "ผู้ขาย",
            ItemPriceSatang: 100_00,
            CreatedAt:
                DateTimeOffset.Parse("2026-07-28T14:00:00+07:00"));

    private sealed class RecordingAnalytics : IMobileAnalytics
    {
        public List<MobileAnalyticsEvent> Events { get; } = [];

        public void Track(MobileAnalyticsEvent value) =>
            Events.Add(value);
    }

    private sealed class SequenceTransactionService :
        ITransactionService
    {
        private readonly Queue<
            Func<Task<IReadOnlyList<AppTransaction>>>> responses = [];

        public int LoadCount { get; private set; }

        public void EnqueueResult(
            params AppTransaction[] transactions) =>
            responses.Enqueue(() =>
                Task.FromResult<
                    IReadOnlyList<AppTransaction>>(transactions));

        public void EnqueueFailure() =>
            responses.Enqueue(() =>
                Task.FromException<
                    IReadOnlyList<AppTransaction>>(
                    new InvalidOperationException("load failed")));

        public TaskCompletionSource<IReadOnlyList<AppTransaction>>
            EnqueuePending()
        {
            var response = new TaskCompletionSource<
                IReadOnlyList<AppTransaction>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            responses.Enqueue(() => response.Task);
            return response;
        }

        public Task<IReadOnlyList<AppTransaction>>
            GetTransactionsAsync(
                CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return responses.Dequeue()();
        }

        public Task<BuyerCostPreview> GetBuyerCostPreviewAsync(
            long itemPriceSatang,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CarrierOption>>
            GetSupportedCarriersAsync(
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AppTransaction?> GetTransactionAsync(
            Guid transactionId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AppTransaction> CreateBuyerOfferAsync(
            CreateBuyerOfferRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AppTransaction> SubmitTrackingAsync(
            Guid transactionId,
            string carrierCode,
            string trackingNumber,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AppTransaction> SubmitDigitalHandoffAsync(
            Guid transactionId,
            string statement,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AppTransaction> ConfirmReceiptAsync(
            Guid transactionId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AppTransaction> OpenDisputeAsync(
            Guid transactionId,
            AppDisputeReason reason,
            string statement,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
