using System.Net;
using Toklong.Mobile.Core;
using Toklong.Mobile.Services;
using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Core.Tests;

public sealed class CounterQrViewModelTests
{
    [Fact]
    public async Task Ready_seller_loads_bounded_image_and_clears_sensitive_bytes()
    {
        var service = new CounterQrService();
        var analytics = new RecordingAnalytics();
        var viewModel = new TransactionDetailViewModel(
            service,
            new PaymentSheet(),
            analytics);

        await viewModel.LoadAsync(service.Transaction.Id);

        Assert.True(viewModel.ShowCounterQrCard);
        Assert.True(viewModel.IsCounterQrReady);
        Assert.True(viewModel.HasCounterQrImage);
        Assert.Equal(1, service.DownloadCalls);
        Assert.Contains(
            analytics.Events,
            item => item.Name == "counter_qr_ready_viewed" &&
                    item.Properties.Count == 0);

        viewModel.ClearSensitiveCounterQr();

        Assert.False(viewModel.HasCounterQrImage);
        Assert.Null(viewModel.CounterQrImageBytes);
    }

    [Fact]
    public async Task Detail_hiding_cancels_inflight_counter_qr_download()
    {
        var service = new CounterQrService { BlockDownload = true };
        var viewModel = new TransactionDetailViewModel(
            service,
            new PaymentSheet(),
            new RecordingAnalytics());

        var load = viewModel.LoadAsync(service.Transaction.Id);
        await service.DownloadStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2));

        Assert.True(service.DownloadToken.CanBeCanceled);
        viewModel.ClearSensitiveCounterQr();
        Assert.True(service.DownloadToken.IsCancellationRequested);
        service.CompleteDownload();
        await load;

        Assert.False(viewModel.HasCounterQrImage);
    }

    [Fact]
    public async Task Fullscreen_hiding_cancels_inflight_counter_qr_download()
    {
        var service = new CounterQrService { BlockDownload = true };
        var viewModel = new CounterQrViewModel(service);

        var load = viewModel.LoadAsync(service.Transaction.Id);
        await service.DownloadStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2));

        Assert.True(service.DownloadToken.CanBeCanceled);
        viewModel.Clear();
        Assert.True(service.DownloadToken.IsCancellationRequested);
        Assert.False(viewModel.IsBusy);
        service.CompleteDownload();
        await load;

        Assert.False(viewModel.HasImage);
    }

    [Fact]
    public async Task Detail_hiding_during_noncooperative_refresh_cannot_reload_qr()
    {
        var service = new CounterQrService();
        var viewModel = new TransactionDetailViewModel(
            service,
            new PaymentSheet(),
            new RecordingAnalytics());
        await viewModel.LoadAsync(service.Transaction.Id);
        service.BlockTransaction = true;

        using var lifetime = new CancellationTokenSource();
        var refresh = viewModel.RefreshAsync(
            service.Transaction.Id,
            lifetime.Token);
        await service.TransactionStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2));

        lifetime.Cancel();
        viewModel.ClearSensitiveCounterQr();
        service.CompleteTransaction();
        await refresh;

        Assert.False(viewModel.HasCounterQrImage);
        Assert.Equal(1, service.DownloadCalls);
    }

    [Fact]
    public async Task Session_reset_clears_detail_and_fullscreen_qr()
    {
        var service = new CounterQrService();
        var session = new AuthenticatedSessionBoundary();
        var detail = new TransactionDetailViewModel(
            service,
            new PaymentSheet(),
            new RecordingAnalytics(),
            session);
        var fullscreen = new CounterQrViewModel(
            service,
            TimeProvider.System,
            session);
        await detail.LoadAsync(service.Transaction.Id);
        await fullscreen.LoadAsync(service.Transaction.Id);

        session.Reset();

        Assert.False(detail.HasCounterQrImage);
        Assert.Null(detail.Transaction);
        Assert.False(fullscreen.HasImage);
        Assert.Null(fullscreen.Transaction);
    }

    [Fact]
    public async Task Session_reset_during_noncooperative_detail_load_cannot_restore_qr()
    {
        var service = new CounterQrService
        {
            BlockTransaction = true
        };
        var session = new AuthenticatedSessionBoundary();
        var viewModel = new TransactionDetailViewModel(
            service,
            new PaymentSheet(),
            new RecordingAnalytics(),
            session);

        var load = viewModel.LoadAsync(service.Transaction.Id);
        await service.TransactionStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2));
        session.Reset();
        service.CompleteTransaction();
        await load;

        Assert.Null(viewModel.Transaction);
        Assert.False(viewModel.HasCounterQrImage);
        Assert.Equal(0, service.DownloadCalls);
    }

    [Fact]
    public async Task Fullscreen_invalidates_image_at_authoritative_expiry()
    {
        var now = DateTimeOffset.UtcNow;
        var time = new ManualTimeProvider(now);
        var service = new CounterQrService(now.AddMinutes(5));
        var viewModel = new CounterQrViewModel(
            service,
            time);
        await viewModel.LoadAsync(service.Transaction.Id);
        Assert.True(viewModel.HasImage);

        time.Advance(TimeSpan.FromMinutes(6));
        viewModel.InvalidateExpiredCounterQr();

        Assert.False(viewModel.HasImage);
        Assert.Contains("หมดอายุ", viewModel.Message);
    }

    [Fact]
    public async Task Failed_session_refresh_resets_boundary_and_clears_qr()
    {
        var service = new CounterQrService();
        var session = new AuthenticatedSessionBoundary();
        var viewModel = new CounterQrViewModel(
            service,
            TimeProvider.System,
            session);
        await viewModel.LoadAsync(service.Transaction.Id);
        var store = new InMemoryMobileSessionStore();
        await store.SaveAsync(new StoredMobileSession(
            "expired-access",
            "expired-refresh",
            DateTimeOffset.UtcNow.AddMinutes(-1)));
        using var http = new HttpClient(
            new FixedHandler(HttpStatusCode.Unauthorized))
        {
            BaseAddress = new Uri("https://mobile.test/")
        };
        var api = new MobileApiClient(
            new SingleClientFactory(http),
            store,
            session);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => api.SendAuthenticatedAsync(
                () => new HttpRequestMessage(
                    HttpMethod.Get,
                    "api/mobile/transactions")));

        Assert.False(viewModel.HasImage);
        Assert.Null(viewModel.Transaction);
        Assert.Null(await store.GetAsync());
    }

    [Fact]
    public async Task Fullscreen_retry_cannot_reload_after_page_clear()
    {
        var service = new CounterQrService(
            counterQrStatus: "RetryableError")
        {
            BlockRetry = true
        };
        var viewModel = new CounterQrViewModel(service);
        await viewModel.LoadAsync(service.Transaction.Id);

        var retry = viewModel.RetryAsync();
        await service.RetryStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2));
        service.SetTransaction("Ready");
        viewModel.Clear();
        service.CompleteRetry();
        await retry;

        Assert.False(viewModel.HasImage);
        Assert.Equal(0, service.DownloadCalls);
    }

    [Fact]
    public async Task Fullscreen_authorization_refresh_revokes_resident_qr()
    {
        var service = new CounterQrService();
        var viewModel = new CounterQrViewModel(service);
        await viewModel.LoadAsync(service.Transaction.Id);
        Assert.True(viewModel.HasImage);
        service.SetTransaction(counterQrStatus: null);

        await viewModel.RefreshAuthorizationAsync();

        Assert.False(viewModel.HasImage);
        Assert.Contains("ไม่พร้อม", viewModel.Message);
    }

    private sealed class CounterQrService : ITransactionService
    {
        public CounterQrService(
            DateTimeOffset? expiresAt = null,
            string? counterQrStatus = "Ready")
        {
            Transaction = CreateTransaction(
                expiresAt,
                counterQrStatus);
        }

        private static AppTransaction CreateTransaction(
            DateTimeOffset? expiresAt,
            string? counterQrStatus) => new(
                Guid.NewGuid(),
                "กล้อง",
                120_000,
                "THB",
                AppTransactionRole.Seller,
                AppFulfillmentType.Physical,
                "TrackingSubmitted",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddHours(24),
                "ผู้ซื้อ")
            {
                ShippingManagedByProvider = true,
                ShippingLabelAvailable = true,
                TrackingNumber = "EF123456789TH",
                ShippingServiceName = "ไปรษณีย์ไทย EMS",
                CounterQrStatus = counterQrStatus,
                CounterQrExpiresAt = expiresAt
            };

        public AppTransaction Transaction { get; private set; }

        public int DownloadCalls { get; private set; }
        public bool BlockDownload { get; init; }
        public bool BlockTransaction { get; set; }
        public bool BlockRetry { get; set; }
        public CancellationToken DownloadToken { get; private set; }
        public TaskCompletionSource DownloadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<CounterQrImageFile> DownloadCompletion
        { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource TransactionStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<AppTransaction?> TransactionCompletion
        { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RetryStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource RetryCompletion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<AppTransaction?> GetTransactionAsync(
            Guid transactionId,
            CancellationToken cancellationToken = default)
        {
            if (!BlockTransaction)
                return Transaction;
            TransactionStarted.TrySetResult();
            return await TransactionCompletion.Task;
        }

        public void CompleteTransaction() =>
            TransactionCompletion.TrySetResult(Transaction);

        public void SetTransaction(string? counterQrStatus) =>
            Transaction = CreateTransaction(
                expiresAt: null,
                counterQrStatus);

        public async Task RetryCounterQrAsync(
            Guid transactionId,
            CancellationToken cancellationToken = default)
        {
            RetryStarted.TrySetResult();
            if (BlockRetry)
                await RetryCompletion.Task;
        }

        public void CompleteRetry() =>
            RetryCompletion.TrySetResult();

        public async Task<CounterQrImageFile> DownloadCounterQrAsync(
            Guid transactionId,
            CancellationToken cancellationToken = default)
        {
            DownloadCalls++;
            DownloadToken = cancellationToken;
            DownloadStarted.TrySetResult();
            if (BlockDownload)
                return await DownloadCompletion.Task.WaitAsync(
                    cancellationToken);
            var png = Enumerable.Repeat((byte)5, 64).ToArray();
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }
                .CopyTo(png, 0);
            return new CounterQrImageFile(png);
        }

        public void CompleteDownload()
        {
            var png = Enumerable.Repeat((byte)5, 64).ToArray();
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }
                .CopyTo(png, 0);
            DownloadCompletion.TrySetResult(
                new CounterQrImageFile(png));
        }

        public Task<BuyerCostPreview> GetBuyerCostPreviewAsync(
            long itemPriceSatang,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CarrierOption>>
            GetSupportedCarriersAsync(
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CarrierOption>>([]);

        public Task<IReadOnlyList<AppTransaction>>
            GetTransactionsAsync(
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AppTransaction>>(
                [Transaction]);

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

    private sealed class PaymentSheet : IStripePaymentSheetService
    {
        public Task<PaymentSheetOutcome> PresentAsync(
            Guid transactionId,
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingAnalytics : IMobileAnalytics
    {
        public List<MobileAnalyticsEvent> Events { get; } = [];

        public void Track(MobileAnalyticsEvent value) =>
            Events.Add(value);
    }

    private sealed class ManualTimeProvider(
        DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan amount) => now = now.Add(amount);
    }

    private sealed class SingleClientFactory(
        HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FixedHandler(
        HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }
}
