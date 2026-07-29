using Toklong.Mobile.Core;
using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Core.Tests;

public sealed class ViewModelSessionBoundaryTests
{
    [Fact]
    public async Task Account_switch_failure_never_exposes_previous_account_workspace()
    {
        Preferences.Default.Clear();
        Shell.Current = new Shell();
        var session = new AuthenticatedSessionBoundary();
        var transactions = new SequencedTransactionService();
        var accountA = new[]
        {
            Item(
                "00000000-0000-0000-0000-000000000811",
                "สินค้าของบัญชี A ที่ต้องตอบ",
                "ผู้ซื้อบัญชี A หนึ่ง",
                "AwaitingSellerAcceptance"),
            Item(
                "00000000-0000-0000-0000-000000000812",
                "สินค้าของบัญชี A ที่กำลังส่ง",
                "ผู้ซื้อบัญชี A สอง",
                "InTransit")
        };
        transactions.EnqueueResult(accountA);
        transactions.EnqueueResult(accountA);
        var lateHome = transactions.EnqueuePending();
        var lateList = transactions.EnqueuePending();
        transactions.EnqueueFailure();
        transactions.EnqueueFailure();
        var home = new AuthenticatedHomeViewModel(
            transactions,
            new RecordingAnalytics(),
            session);
        var list = new TransactionsViewModel(
            transactions,
            new NoOpDeepLinks(),
            new RecordingAnalytics(),
            session);
        await home.LoadAsync();
        await list.LoadAsync();
        list.ApplyRoleNavigation(TransactionRoleRoute.Selling);
        Assert.True(home.HasNewOffers);
        Assert.Equal("1 ข้อเสนอใหม่", home.NewOfferBadgeText);
        Assert.Equal(
            "สินค้าของบัญชี A ที่ต้องตอบ",
            list.SpotlightTransaction?.ProductName);
        Assert.Contains(
            list.Transactions,
            item =>
                item.ProductName == "สินค้าของบัญชี A ที่กำลังส่ง" &&
                item.CounterpartyName == "ผู้ซื้อบัญชี A สอง");

        var oldHomeLoad = home.LoadAsync();
        var oldListLoad = list.LoadAsync();
        var authentication = new SignOutAuthentication(() =>
        {
            Assert.False(home.HasSellerSummary);
            Assert.Empty(list.Transactions);
            Assert.Null(list.SpotlightTransaction);
            Assert.False(list.HasSellerSummary);
            Assert.False(list.HasError);
            Assert.False(list.ShowTransactionCollectionEmptyState);
        });
        var account = new AccountViewModel(
            authentication,
            session,
            new AccountEmailChangeCompletionState(
                session));

        await account.SignOutAsync();
        await home.LoadAsync();
        await list.LoadAsync();
        lateHome.SetResult(accountA);
        lateList.SetResult(accountA);
        await Task.WhenAll(oldHomeLoad, oldListLoad);

        Assert.Equal(["//welcome"], Shell.Current.Routes);
        Assert.True(authentication.SignedOut);
        Assert.False(home.HasSellerSummary);
        Assert.False(home.HasNewOffers);
        Assert.True(home.HasLoadError);
        Assert.Equal(
            "โหลดรายการไม่สำเร็จ · ลองอีกครั้ง",
            home.LoadErrorText);
        Assert.Empty(list.Transactions);
        Assert.Null(list.SpotlightTransaction);
        Assert.False(list.HasSellerSummary);
        Assert.True(list.HasError);
        Assert.False(list.ShowTransactionCollectionEmptyState);
        Assert.DoesNotContain(
            "บัญชี A",
            string.Join(
                " ",
                list.Transactions.SelectMany(
                    item => new[]
                    {
                        item.ProductName,
                        item.CounterpartyName
                    })));
    }

    [Fact]
    public async Task Collection_empty_state_requires_a_successful_load()
    {
        Preferences.Default.Clear();
        var session = new AuthenticatedSessionBoundary();
        var transactions = new SequencedTransactionService();
        transactions.EnqueueFailure();
        transactions.EnqueueResult([]);
        transactions.EnqueueFailure();
        var viewModel = new TransactionsViewModel(
            transactions,
            new NoOpDeepLinks(),
            new RecordingAnalytics(),
            session);

        Assert.False(viewModel.ShowTransactionCollectionEmptyState);

        await viewModel.LoadAsync();

        Assert.True(viewModel.HasError);
        Assert.False(viewModel.ShowTransactionCollectionEmptyState);

        await viewModel.LoadAsync();

        Assert.False(viewModel.HasError);
        Assert.True(viewModel.ShowTransactionCollectionEmptyState);

        await viewModel.LoadAsync();

        Assert.True(viewModel.HasError);
        Assert.True(viewModel.ShowTransactionCollectionEmptyState);

        session.Reset();

        Assert.False(viewModel.HasError);
        Assert.False(viewModel.ShowTransactionCollectionEmptyState);
    }

    private static AppTransaction Item(
        string id,
        string product,
        string counterparty,
        string state) =>
        new(
            Guid.Parse(id),
            product,
            1_000_00,
            "THB",
            AppTransactionRole.Seller,
            AppFulfillmentType.Physical,
            state,
            DateTimeOffset.Parse("2026-07-28T15:00:00+07:00"),
            DateTimeOffset.Parse("2026-07-29T15:00:00+07:00"),
            counterparty,
            ItemPriceSatang: 1_000_00,
            CreatedAt:
                DateTimeOffset.Parse("2026-07-28T14:00:00+07:00"));

    private sealed class RecordingAnalytics : IMobileAnalytics
    {
        public List<MobileAnalyticsEvent> Events { get; } = [];

        public void Track(MobileAnalyticsEvent value) =>
            Events.Add(value);
    }

    private sealed class NoOpDeepLinks : IDeepLinkCoordinator
    {
        public Task<bool> HandleAsync(Uri uri) =>
            Task.FromResult(false);

        public Task ResumePendingAsync() =>
            Task.CompletedTask;
    }

    private sealed class SequencedTransactionService :
        ITransactionService
    {
        private readonly Queue<
            Func<Task<IReadOnlyList<AppTransaction>>>> responses = [];

        public void EnqueueResult(
            params AppTransaction[] value) =>
            responses.Enqueue(() =>
                Task.FromResult<
                    IReadOnlyList<AppTransaction>>(value));

        public TaskCompletionSource<IReadOnlyList<AppTransaction>>
            EnqueuePending()
        {
            var pending = new TaskCompletionSource<
                IReadOnlyList<AppTransaction>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            responses.Enqueue(() => pending.Task);
            return pending;
        }

        public void EnqueueFailure() =>
            responses.Enqueue(() =>
                Task.FromException<
                    IReadOnlyList<AppTransaction>>(
                    new InvalidOperationException("load failed")));

        public Task<IReadOnlyList<AppTransaction>>
            GetTransactionsAsync(
                CancellationToken cancellationToken = default) =>
            responses.Dequeue()();

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

    private sealed class SignOutAuthentication(
        Action onSignOut) : IAuthenticationService
    {
        public bool SignedOut { get; private set; }

        public Task SignOutAsync(
            CancellationToken cancellationToken = default)
        {
            onSignOut();
            SignedOut = true;
            return Task.CompletedTask;
        }

        public Task<bool> HasSessionAsync() =>
            throw new NotSupportedException();

        public Task<OtpChallengeResult> RequestCodeAsync(
            string phoneNumber,
            AuthenticationMode mode,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AuthenticationVerificationResult> VerifyCodeAsync(
            string challengeId,
            string code,
            AuthenticationMode mode,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CompleteRegistrationAsync(
            string fullName,
            string email,
            string termsVersion,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MobileProfile> GetProfileAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PendingEmailChange?> GetPendingEmailChangeAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PendingEmailChange> RequestEmailChangeAsync(
            string email,
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PendingEmailChange> ResendEmailChangeAsync(
            Guid challengeId,
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string> VerifyEmailChangeAsync(
            Guid challengeId,
            string code,
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

    }
}
