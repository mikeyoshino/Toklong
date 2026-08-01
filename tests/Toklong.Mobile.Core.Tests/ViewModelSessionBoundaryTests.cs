using Toklong.Mobile.Core;
using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Core.Tests;

public sealed class ViewModelSessionBoundaryTests :
    AccountNameChangeViewModelTestBase
{
    [Fact]
    public async Task Name_form_reset_immediately_clears_account_A_and_reactivation_loads_account_B()
    {
        Shell.Current = new Shell
        {
            Navigate = _ => Task.FromException(
                new InvalidOperationException("private route detail"))
        };
        var session = new AuthenticatedSessionBoundary();
        var authentication = new RecordingAuthentication
        {
            GetProfile = () => Task.FromResult(Profile("บัญชีเอ", "เดิม")),
            RequestName = (_, _) => Task.FromResult(Pending())
        };
        var viewModel = new ChangeNameViewModel(
            authentication,
            new RecordingAnalytics(),
            session);
        viewModel.Activate();
        await viewModel.LoadCurrentNameAsync();
        viewModel.FirstName = "ชื่อใหม่เอ";
        await viewModel.SubmitAsync();

        session.Reset();

        Assert.Equal("", viewModel.FirstName);
        Assert.Equal("", viewModel.LastName);
        Assert.False(viewModel.HasFirstNameError);
        Assert.False(viewModel.HasLastNameError);
        Assert.False(viewModel.HasMessage);
        Assert.True(viewModel.CanEditName);
        Assert.Equal("ส่งรหัสยืนยัน", viewModel.SubmitButtonText);

        authentication.GetProfile = () =>
            Task.FromResult(Profile("บัญชีบี", "ใหม่"));
        viewModel.Activate();
        await viewModel.LoadCurrentNameAsync();

        Assert.Equal("บัญชีบี", viewModel.FirstName);
        Assert.Equal("ใหม่", viewModel.LastName);
        Assert.Equal(2, authentication.ProfileCalls);
        Assert.IsAssignableFrom<IDisposable>(viewModel).Dispose();
    }

    [Fact]
    public async Task Name_verification_reset_immediately_clears_account_A_challenge_presentation()
    {
        var session = new AuthenticatedSessionBoundary();
        var authentication = new RecordingAuthentication
        {
            VerifyName = (_, _) =>
                Task.FromException<VerifiedAccountNameChange>(
                    Problem(
                        "name_change_code_incorrect",
                        remainingAttempts: 2))
        };
        var viewModel = new VerifyNameChangeViewModel(
            authentication,
            new RecordingAnalytics(),
            new FixedTimeProvider(Now),
            session,
            new AccountNameChangeCompletionState(session));
        viewModel.Activate();
        viewModel.Apply(Pending(remainingAttempts: 3));
        viewModel.Code = "123456";
        await viewModel.ConfirmAsync();
        Assert.True(viewModel.HasMessage);

        session.Reset();

        Assert.Equal("", viewModel.MaskedPhoneNumber);
        Assert.Equal("", viewModel.PendingDisplayName);
        Assert.Equal("", viewModel.Code);
        Assert.Equal(0, viewModel.RemainingAttempts);
        Assert.Equal(0, viewModel.ResendSecondsRemaining);
        Assert.Equal(0, viewModel.ExpirySecondsRemaining);
        Assert.False(viewModel.HasMessage);
        Assert.False(viewModel.CanUseChallenge);
        Assert.False(viewModel.RequiresNewRequest);
        Assert.False(viewModel.RequiresAccountReturn);
        Assert.IsAssignableFrom<IDisposable>(viewModel).Dispose();
    }

    [Fact]
    public async Task Name_verification_reactivation_loads_only_account_B_pending_state()
    {
        var accountBPending = Pending(
            challengeId: Guid.Parse(
                "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")) with
        {
            MaskedPhoneNumber = "09x-xxx-9876",
            FirstName = "บัญชีบี",
            LastName = "ใหม่"
        };
        var session = new AuthenticatedSessionBoundary();
        var authentication = new RecordingAuthentication
        {
            GetPendingName = () =>
                Task.FromResult<PendingAccountNameChange?>(accountBPending)
        };
        var viewModel = new VerifyNameChangeViewModel(
            authentication,
            new RecordingAnalytics(),
            new FixedTimeProvider(Now),
            session,
            new AccountNameChangeCompletionState(session));
        viewModel.Activate();
        viewModel.Apply(Pending());

        session.Reset();
        viewModel.Activate();
        await viewModel.LoadPendingAfterResetAsync();

        Assert.Equal("09x-xxx-9876", viewModel.MaskedPhoneNumber);
        Assert.Equal("บัญชีบี ใหม่", viewModel.PendingDisplayName);
        Assert.True(viewModel.CanUseChallenge);
        Assert.Equal(1, authentication.PendingNameCalls);
        Assert.IsAssignableFrom<IDisposable>(viewModel).Dispose();
    }

    [Fact]
    public async Task Reset_during_form_navigation_rejects_account_A_route_names_before_account_B_page_is_constructed()
    {
        Shell.Current = new Shell();
        var session = new AuthenticatedSessionBoundary();
        var authentication = new RecordingAuthentication
        {
            GetProfile = () =>
                Task.FromResult(Profile("บัญชีเอ", "เดิม"))
        };
        var source = new AccountViewModel(
            authentication,
            new RecordingAnalytics(),
            session,
            new AccountEmailChangeCompletionState(session),
            new AccountNameChangeCompletionState(session));
        await source.LoadAsync();
        var resetDuringRoute = false;
        Shell.Current.Navigate = route =>
        {
            if (route == "ChangeNamePage" && !resetDuringRoute)
            {
                resetDuringRoute = true;
                session.Reset();
                authentication.GetProfile = () =>
                    Task.FromResult(Profile("บัญชีบี", "ใหม่"));
            }
            return Task.CompletedTask;
        };

        await source.OpenNameChangeAsync();

        var route = Assert.Single(Shell.Current.ParameterizedRoutes);
        var destination = new ChangeNameViewModel(
            authentication,
            new RecordingAnalytics(),
            session);
        destination.ApplyRouteName(
            (string)route.Parameters["FirstName"],
            (string)route.Parameters["LastName"],
            (long)route.Parameters["SessionGeneration"]);
        destination.Activate();
        await destination.LoadCurrentNameAsync();

        Assert.Equal("บัญชีบี", destination.FirstName);
        Assert.Equal("ใหม่", destination.LastName);
        Assert.DoesNotContain("บัญชีเอ", destination.FirstName);
        Assert.Equal("//main/account", Shell.Current.Routes[^1]);
        destination.Dispose();
    }

    [Fact]
    public async Task Reset_during_pending_navigation_rejects_account_A_challenge_before_account_B_page_is_constructed()
    {
        Shell.Current = new Shell();
        var accountAPending = Pending();
        var accountBPending = Pending(
            challengeId: Guid.Parse(
                "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")) with
        {
            MaskedPhoneNumber = "09x-xxx-9876",
            FirstName = "บัญชีบี",
            LastName = "ใหม่"
        };
        var session = new AuthenticatedSessionBoundary();
        var authentication = new RecordingAuthentication
        {
            GetPendingName = () =>
                Task.FromResult<PendingAccountNameChange?>(accountAPending)
        };
        var source = new AccountViewModel(
            authentication,
            new RecordingAnalytics(),
            session,
            new AccountEmailChangeCompletionState(session),
            new AccountNameChangeCompletionState(session));
        await source.LoadAsync();
        var resetDuringRoute = false;
        Shell.Current.Navigate = route =>
        {
            if (route == "VerifyNameChangePage" && !resetDuringRoute)
            {
                resetDuringRoute = true;
                session.Reset();
                authentication.GetPendingName = () =>
                    Task.FromResult<PendingAccountNameChange?>(
                        accountBPending);
            }
            return Task.CompletedTask;
        };

        await source.OpenNameChangeAsync();

        var route = Assert.Single(Shell.Current.ParameterizedRoutes);
        var destination = new VerifyNameChangeViewModel(
            authentication,
            new RecordingAnalytics(),
            new FixedTimeProvider(Now),
            session,
            new AccountNameChangeCompletionState(session));
        destination.ApplyRoutePending(
            (PendingAccountNameChange)route.Parameters["Pending"],
            (long)route.Parameters["SessionGeneration"]);
        destination.Activate();
        await destination.LoadPendingAfterResetAsync();

        Assert.Equal("09x-xxx-9876", destination.MaskedPhoneNumber);
        Assert.Equal("บัญชีบี ใหม่", destination.PendingDisplayName);
        Assert.DoesNotContain("08x-xxx-1234", destination.MaskedPhoneNumber);
        Assert.Equal("//main/account", Shell.Current.Routes[^1]);
        destination.Dispose();
    }

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
            session,
            RoleFilter.Selling);
        await home.LoadAsync();
        await list.LoadAsync();
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
        var workspaceRoles = new WorkspaceRolePreference(session);
        workspaceRoles.SavePreferredRole(TransactionRoleRoute.Selling);

        await account.SignOutAsync();
        await home.LoadAsync();
        await list.LoadAsync();
        lateHome.SetResult(accountA);
        lateList.SetResult(accountA);
        await Task.WhenAll(oldHomeLoad, oldListLoad);

        Assert.Equal(["//welcome"], Shell.Current.Routes);
        Assert.Equal(
            TransactionRoleRoute.Buying,
            workspaceRoles.GetPreferredRole());
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
    public async Task Fixed_role_workspaces_never_share_records_or_filters()
    {
        Preferences.Default.Clear();
        var session = new AuthenticatedSessionBoundary();
        var service = new SequencedTransactionService();
        var buyerItem = BuyerItem(
            "00000000-0000-0000-0000-000000000901");
        var sellerItem = Item(
            "00000000-0000-0000-0000-000000000902",
            "กล้องของผู้ขาย",
            "ผู้ซื้อ",
            "AwaitingSellerAcceptance");
        service.EnqueueResult(buyerItem, sellerItem);
        service.EnqueueResult(buyerItem, sellerItem);

        var buyer = new TransactionsViewModel(
            service,
            new NoOpDeepLinks(),
            new RecordingAnalytics(),
            session,
            RoleFilter.Buying);
        var seller = new TransactionsViewModel(
            service,
            new NoOpDeepLinks(),
            new RecordingAnalytics(),
            session,
            RoleFilter.Selling);

        await buyer.LoadAsync();
        await seller.LoadAsync();

        Assert.True(buyer.IsBuying);
        Assert.Equal("#2B7FFF", buyer.WorkspaceAccentColor);
        Assert.All(
            buyer.Transactions.Append(buyer.SpotlightTransaction!),
            item => Assert.Equal(AppTransactionRole.Buyer, item.Role));
        Assert.True(seller.IsSelling);
        Assert.Equal(
            SellerColorPalette.Role,
            seller.WorkspaceAccentColor);
        Assert.All(
            seller.Transactions.Append(seller.SpotlightTransaction!),
            item => Assert.Equal(AppTransactionRole.Seller, item.Role));
    }

    [Fact]
    public async Task Late_buyer_response_never_replaces_seller_workspace()
    {
        var session = new AuthenticatedSessionBoundary();
        var service = new SequencedTransactionService();
        var buyerPending = service.EnqueuePending();
        var sellerPending = service.EnqueuePending();
        var buyer = new TransactionsViewModel(
            service,
            new NoOpDeepLinks(),
            new RecordingAnalytics(),
            session,
            RoleFilter.Buying);
        var seller = new TransactionsViewModel(
            service,
            new NoOpDeepLinks(),
            new RecordingAnalytics(),
            session,
            RoleFilter.Selling);

        var buyerLoad = buyer.LoadAsync();
        var sellerLoad = seller.LoadAsync();
        var sellerItem = Item(
            "00000000-0000-0000-0000-000000000903",
            "รายการขาย",
            "ผู้ซื้อ",
            "AwaitingSellerAcceptance");
        sellerPending.SetResult([sellerItem]);
        await sellerLoad;
        buyerPending.SetResult([
            BuyerItem("00000000-0000-0000-0000-000000000904")
        ]);
        await buyerLoad;

        Assert.Equal(
            "รายการขาย",
            seller.SpotlightTransaction?.ProductName);
        Assert.All(
            seller.Transactions.Append(seller.SpotlightTransaction!),
            item => Assert.Equal(AppTransactionRole.Seller, item.Role));
    }

    [Fact]
    public async Task Empty_root_workspaces_name_the_selected_role()
    {
        var session = new AuthenticatedSessionBoundary();
        var service = new SequencedTransactionService();
        service.EnqueueResult([]);
        service.EnqueueResult([]);
        var buyer = new TransactionsViewModel(
            service,
            new NoOpDeepLinks(),
            new RecordingAnalytics(),
            session,
            RoleFilter.Buying);
        var seller = new TransactionsViewModel(
            service,
            new NoOpDeepLinks(),
            new RecordingAnalytics(),
            session,
            RoleFilter.Selling);

        await buyer.LoadAsync();
        await seller.LoadAsync();

        Assert.Equal("ยังไม่มีรายการซื้อ", buyer.EmptyText);
        Assert.Equal("ยังไม่มีรายการขาย", seller.EmptyText);
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
            session,
            RoleFilter.Buying);

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

    private static AppTransaction BuyerItem(string id) =>
        new(
            Guid.Parse(id),
            "รายการซื้อ",
            2_500_00,
            "THB",
            AppTransactionRole.Buyer,
            AppFulfillmentType.Physical,
            "SellerAcceptedAwaitingPayment",
            DateTimeOffset.Parse("2026-08-01T10:00:00+07:00"),
            DateTimeOffset.Parse("2026-08-02T10:00:00+07:00"),
            "ผู้ขาย");

    private new sealed class RecordingAnalytics : IMobileAnalytics
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
            string firstName,
            string lastName,
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
