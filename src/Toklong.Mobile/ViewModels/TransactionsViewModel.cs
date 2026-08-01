using System.Collections.ObjectModel;
using System.Windows.Input;
using Toklong.Mobile.Core;
using Toklong.Mobile.Pages;

namespace Toklong.Mobile.ViewModels;

public sealed class TransactionsViewModel : ObservableViewModel
{
    private readonly ITransactionService transactionService;
    private readonly IDeepLinkCoordinator deepLinks;
    private readonly SellerWorkspaceState sellerState = new();
    private readonly IMobileAnalytics analytics;
    private readonly AuthenticatedSessionBoundary session;
    private readonly SpotlightEmptyStatePresentation spotlightEmptyState;
    private IReadOnlyList<AppTransaction> allTransactions = [];
    private bool hasSuccessfulLoad;
    private readonly RoleFilter roleFilter;
    private BucketFilter bucketFilter;
    private bool isBusy;
    private bool isRefreshing;
    private string emptyText = "";
    private string errorText = "";
    private AppTransaction? spotlightTransaction;

    public TransactionsViewModel(
        ITransactionService transactionService,
        IDeepLinkCoordinator deepLinks,
        IMobileAnalytics analytics,
        AuthenticatedSessionBoundary session,
        RoleFilter role)
    {
        if (role is not (RoleFilter.Buying or RoleFilter.Selling))
            throw new ArgumentOutOfRangeException(nameof(role));

        this.transactionService = transactionService;
        this.deepLinks = deepLinks;
        this.analytics = analytics;
        this.session = session;
        session.ResetRequested +=
            (_, _) => ResetForSessionBoundary();
        roleFilter = role;
        spotlightEmptyState = new(
            roleFilter,
            hasSpotlight: false);
        spotlightEmptyState.PropertyChanged +=
            (_, eventArgs) =>
                OnPropertyChanged(eventArgs.PropertyName);
        SelectAllBucketsCommand = new Command(() => SelectBucket(BucketFilter.All));
        SelectActionCommand = new Command(() => SelectBucket(BucketFilter.ActionRequired));
        SelectProgressCommand = new Command(() => SelectBucket(BucketFilter.InProgress));
        SelectCompletedCommand = new Command(() => SelectBucket(BucketFilter.Completed));
        SelectSellerNewOffersCommand = new Command(
            () => SelectSellerWork(SellerWorkCategory.NewOffers));
        SelectSellerFulfillmentCommand = new Command(
            () => SelectSellerWork(SellerWorkCategory.FulfillmentRequired));
        SelectSellerInProgressCommand = new Command(
            () => SelectSellerWork(SellerWorkCategory.InProgress));
        SelectSellerProblemsCommand = new Command(
            () =>
            {
                SelectSellerWork(SellerWorkCategory.Problems);
                analytics.Track(
                    SellerWorkspaceAnalytics.ProblemBannerOpened(
                        sellerState.Snapshot.ProblemCount));
            });
        SelectAllSellerWorkCommand = new Command(
            () => SelectSellerWork(SellerWorkCategory.All));
        OpenTransactionCommand = new Command<AppTransaction>(
            async item => await OpenTransactionAsync(item));
        CreateOfferCommand = new Command(
            async () => await Shell.Current.GoToAsync(nameof(CreateOfferPage)));
        RefreshCommand = new AsyncCommand(RefreshAsync);
    }

    public ObservableCollection<AppTransaction> Transactions { get; } = [];

    public ICommand SelectAllBucketsCommand { get; }
    public ICommand SelectActionCommand { get; }
    public ICommand SelectProgressCommand { get; }
    public ICommand SelectCompletedCommand { get; }
    public ICommand SelectSellerNewOffersCommand { get; }
    public ICommand SelectSellerFulfillmentCommand { get; }
    public ICommand SelectSellerInProgressCommand { get; }
    public ICommand SelectSellerProblemsCommand { get; }
    public ICommand SelectAllSellerWorkCommand { get; }
    public ICommand OpenTransactionCommand { get; }
    public ICommand CreateOfferCommand { get; }
    public ICommand RefreshCommand { get; }

    public bool IsBusy
    {
        get => isBusy;
        private set => SetProperty(ref isBusy, value);
    }

    public bool IsRefreshing
    {
        get => isRefreshing;
        set => SetProperty(ref isRefreshing, value);
    }

    public string EmptyText
    {
        get => emptyText;
        private set => SetProperty(ref emptyText, value);
    }

    public string ErrorText
    {
        get => errorText;
        private set
        {
            if (SetProperty(ref errorText, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public AppTransaction? SpotlightTransaction
    {
        get => spotlightTransaction;
        private set
        {
            if (SetProperty(ref spotlightTransaction, value))
            {
                SpotlightGradient.SetSpotlight(value);
                OnPropertyChanged(nameof(HasSpotlight));
                OnPropertyChanged(nameof(HasNoSpotlight));
                spotlightEmptyState.SetHasSpotlight(
                    value is not null);
                OnPropertyChanged(nameof(SpotlightAmountText));
            }
        }
    }

    public bool HasSpotlight => SpotlightTransaction is not null;
    public bool HasNoSpotlight => SpotlightTransaction is null;
    public SpotlightGradientPresentation SpotlightGradient { get; } =
        new(null);
    public bool ShowBuyerSpotlightEmptyState =>
        spotlightEmptyState.ShowBuyerSpotlightEmptyState;
    public bool ShowTransactionCollectionEmptyState =>
        hasSuccessfulLoad && Transactions.Count == 0;

    public bool IsBuying => roleFilter == RoleFilter.Buying;
    public bool IsSelling => roleFilter == RoleFilter.Selling;
    public RoleFilter Role => roleFilter;
    public string WorkspaceAccentColor => IsBuying
        ? "#2B7FFF"
        : SellerColorPalette.Role;
    public bool IsAllBuckets => bucketFilter == BucketFilter.All;
    public bool IsActionRequired => bucketFilter == BucketFilter.ActionRequired;
    public bool IsInProgress => bucketFilter == BucketFilter.InProgress;
    public bool IsCompleted => bucketFilter == BucketFilter.Completed;
    public bool HasSellerSummary =>
        IsSelling && sellerState.HasVisibleSummary;
    public string SellerTotalText =>
        $"รายการขายทั้งหมด {sellerState.Snapshot.TotalCount} รายการ";
    public string NewOfferCountText =>
        sellerState.Snapshot.NewOfferCount.ToString();
    public string FulfillmentCountText =>
        sellerState.Snapshot.FulfillmentRequiredCount.ToString();
    public string InProgressCountText =>
        sellerState.Snapshot.InProgressCount.ToString();
    public bool IsSellerNewOffersSelected =>
        sellerState.SelectedCategory == SellerWorkCategory.NewOffers;
    public bool IsSellerFulfillmentSelected =>
        sellerState.SelectedCategory ==
        SellerWorkCategory.FulfillmentRequired;
    public bool IsSellerInProgressSelected =>
        sellerState.SelectedCategory == SellerWorkCategory.InProgress;
    public string NewOfferSemanticText =>
        SellerSemanticText(
            "ข้อเสนอใหม่",
            NewOfferCountText,
            IsSellerNewOffersSelected);
    public string FulfillmentSemanticText =>
        SellerSemanticText(
            "ต้องส่ง",
            FulfillmentCountText,
            IsSellerFulfillmentSelected);
    public string InProgressSemanticText =>
        SellerSemanticText(
            "กำลังดำเนินการ",
            InProgressCountText,
            IsSellerInProgressSelected);
    public bool HasSellerProblems =>
        IsSelling && sellerState.Snapshot.ProblemCount > 0;
    public string SellerProblemText =>
        $"มี {sellerState.Snapshot.ProblemCount} รายการแจ้งปัญหา · " +
        "ยอดรับหยุดไว้ระหว่างตรวจสอบ";
    public string SpotlightAmountText =>
        SpotlightTransaction is null
            ? ""
            : IsSelling
                ? SpotlightTransaction.ItemPriceText
                : SpotlightTransaction.FormattedAmount;
    public string SellerPriorityExplanation =>
        sellerState.SelectedCategory switch
        {
            SellerWorkCategory.NewOffers => "ใกล้หมดเวลาตอบก่อน",
            SellerWorkCategory.FulfillmentRequired => "เร่งส่งก่อน",
            SellerWorkCategory.InProgress => "อัปเดตล่าสุดก่อน",
            SellerWorkCategory.Problems => "ปัญหาล่าสุดก่อน",
            _ => "เรียงตามสิ่งที่ต้องทำก่อน"
        };

    public string ModeTitle =>
        IsBuying ? "รายการซื้อ" : "รายการขาย";

    public string ModeSubtitle =>
        IsBuying
            ? "ดีลที่คุณเป็นผู้ซื้อ"
            : "ข้อเสนอและยอดขายของคุณ";

    public string ModeSectionTitle =>
        IsBuying
            ? "รายการซื้อทั้งหมด"
            : "รายการขายทั้งหมด";

    public int ActionRequiredCount =>
        TransactionFilter.Apply(
            allTransactions,
            roleFilter,
            BucketFilter.ActionRequired).Count;

    public string ActionSummary => ActionRequiredCount == 0
        ? "วันนี้ไม่มีรายการที่ต้องทำ"
        : $"มี {ActionRequiredCount} รายการรอคุณทำต่อ";

    public async Task LoadAsync()
    {
        if (IsBusy)
            return;

        var generation = session.Capture();
        IsBusy = true;
        ErrorText = "";
        try
        {
            var loaded = await transactionService.GetTransactionsAsync();
            if (!session.IsCurrent(generation))
                return;

            allTransactions = loaded;
            hasSuccessfulLoad = true;
            sellerState.ReplaceSuccessful(loaded);
            ApplyFilter();
            RaiseSellerSummaryProperties();
            OnPropertyChanged(nameof(ActionRequiredCount));
            OnPropertyChanged(nameof(ActionSummary));
        }
        catch
        {
            if (!session.IsCurrent(generation))
                return;

            sellerState.MarkLoadFailed();
            if (!sellerState.HasSuccessfulLoad)
            {
                allTransactions = [];
                ApplyFilter();
            }
            OnPropertyChanged(nameof(ActionRequiredCount));
            OnPropertyChanged(nameof(ActionSummary));
            ErrorText = sellerState.LoadErrorText;
        }
        finally
        {
            if (session.IsCurrent(generation))
                IsBusy = false;
        }
    }

    private async Task RefreshAsync()
    {
        var generation = session.Capture();
        IsRefreshing = true;
        try
        {
            await LoadAsync();
        }
        finally
        {
            if (session.IsCurrent(generation))
                IsRefreshing = false;
        }
    }

    private void SelectBucket(BucketFilter value)
    {
        bucketFilter = value;
        RaiseFilterProperties();
        ApplyFilter();
    }

    private void RaiseFilterProperties()
    {
        OnPropertyChanged(nameof(IsBuying));
        OnPropertyChanged(nameof(IsSelling));
        OnPropertyChanged(nameof(IsAllBuckets));
        OnPropertyChanged(nameof(IsActionRequired));
        OnPropertyChanged(nameof(IsInProgress));
        OnPropertyChanged(nameof(IsCompleted));
        RaiseSellerSummaryProperties();
    }

    private void ApplyFilter()
    {
        if (IsSelling)
        {
            var snapshot = sellerState.Snapshot;
            SpotlightTransaction = snapshot.Spotlight;
            TransactionCollectionSynchronizer.Synchronize(
                Transactions,
                snapshot.RemainingTransactions);
            EmptyText = SpotlightTransaction is null
                ? "ยังไม่มีรายการในสถานะนี้"
                : "ไม่มีรายการอื่นในสถานะนี้";
            OnPropertyChanged(
                nameof(ShowTransactionCollectionEmptyState));
            return;
        }

        var filtered = TransactionFilter.Apply(
            allTransactions,
            roleFilter,
            bucketFilter);
        SpotlightTransaction =
            TransactionFilter.FindActionRequired(filtered);

        TransactionCollectionSynchronizer.Synchronize(
            Transactions,
            filtered
                .Where(item => item.Id != SpotlightTransaction?.Id)
                .ToArray());

        EmptyText = SpotlightTransaction is null
            ? "ยังไม่มีรายการในสถานะนี้"
            : "ไม่มีรายการอื่นในสถานะนี้";
        OnPropertyChanged(
            nameof(ShowTransactionCollectionEmptyState));
    }

    private void ResetForSessionBoundary()
    {
        allTransactions = [];
        hasSuccessfulLoad = false;
        sellerState.Reset();
        bucketFilter = BucketFilter.All;
        IsBusy = false;
        IsRefreshing = false;
        ErrorText = "";
        EmptyText = "";
        SpotlightTransaction = null;
        TransactionCollectionSynchronizer.Synchronize(
            Transactions,
            []);
        RaiseFilterProperties();
        OnPropertyChanged(nameof(ActionRequiredCount));
        OnPropertyChanged(nameof(ActionSummary));
        OnPropertyChanged(
            nameof(ShowTransactionCollectionEmptyState));
    }

    private void SelectSellerWork(SellerWorkCategory category)
    {
        sellerState.Select(category);
        ApplyFilter();
        var snapshot = sellerState.Snapshot;
        analytics.Track(
            SellerWorkspaceAnalytics.FilterSelected(
                snapshot.SelectedCategory,
                snapshot.VisibleTransactions.Count));
        RaiseSellerSummaryProperties();
    }

    private void RaiseSellerSummaryProperties()
    {
        OnPropertyChanged(nameof(HasSellerSummary));
        OnPropertyChanged(nameof(SellerTotalText));
        OnPropertyChanged(nameof(NewOfferCountText));
        OnPropertyChanged(nameof(FulfillmentCountText));
        OnPropertyChanged(nameof(InProgressCountText));
        OnPropertyChanged(nameof(NewOfferSemanticText));
        OnPropertyChanged(nameof(FulfillmentSemanticText));
        OnPropertyChanged(nameof(InProgressSemanticText));
        OnPropertyChanged(nameof(HasSellerProblems));
        OnPropertyChanged(nameof(SellerProblemText));
        OnPropertyChanged(nameof(IsSellerNewOffersSelected));
        OnPropertyChanged(nameof(IsSellerFulfillmentSelected));
        OnPropertyChanged(nameof(IsSellerInProgressSelected));
        OnPropertyChanged(nameof(SpotlightAmountText));
        OnPropertyChanged(nameof(SellerPriorityExplanation));
    }

    private static string SellerSemanticText(
        string label,
        string count,
        bool selected) =>
        $"{label} {count} รายการ" +
        (selected ? " เลือกอยู่" : "");

    private async Task OpenTransactionAsync(
        AppTransaction? item)
    {
        if (item is null)
            return;

        if (IsSelling &&
            item.Id == SpotlightTransaction?.Id)
        {
            analytics.Track(
                SellerWorkspaceAnalytics.SpotlightOpened(
                    item.Presentation.PrimaryAction,
                    item.State));
        }

        if (item.Presentation.PrimaryAction ==
                TransactionAction.ReviewSellerOffer &&
            Uri.TryCreate(
                item.SellerInvitationUrl,
                UriKind.Absolute,
                out var offerUri) &&
            await deepLinks.HandleAsync(offerUri))
            return;

        await Shell.Current.GoToAsync(
            nameof(TransactionDetailPage),
            new Dictionary<string, object>
            {
                ["TransactionId"] = item.Id
            });
    }

}
