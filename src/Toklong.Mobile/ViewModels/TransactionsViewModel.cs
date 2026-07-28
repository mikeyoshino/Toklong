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
    private IReadOnlyList<AppTransaction> allTransactions = [];
    private RoleFilter roleFilter;
    private BucketFilter bucketFilter;
    private bool isBusy;
    private bool isRefreshing;
    private string emptyText = "";
    private string errorText = "";
    private AppTransaction? spotlightTransaction;

    public TransactionsViewModel(
        ITransactionService transactionService,
        IDeepLinkCoordinator deepLinks,
        IMobileAnalytics analytics)
    {
        this.transactionService = transactionService;
        this.deepLinks = deepLinks;
        this.analytics = analytics;
        roleFilter =
            Preferences.Default.Get(
                "transactions.last-role",
                nameof(RoleFilter.Buying)) ==
            nameof(RoleFilter.Selling)
                ? RoleFilter.Selling
                : RoleFilter.Buying;
        SelectBuyingCommand = new Command(() => SelectRole(RoleFilter.Buying));
        SelectSellingCommand = new Command(() => SelectRole(RoleFilter.Selling));
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

    public ICommand SelectBuyingCommand { get; }
    public ICommand SelectSellingCommand { get; }
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
                OnPropertyChanged(nameof(HasSpotlight));
                OnPropertyChanged(nameof(HasNoSpotlight));
                OnPropertyChanged(nameof(SpotlightAmountText));
            }
        }
    }

    public bool HasSpotlight => SpotlightTransaction is not null;
    public bool HasNoSpotlight => SpotlightTransaction is null;

    public bool IsBuying => roleFilter == RoleFilter.Buying;
    public bool IsSelling => roleFilter == RoleFilter.Selling;
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
            "กำลังไปต่อ",
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

        IsBusy = true;
        ErrorText = "";
        try
        {
            var loaded = await transactionService.GetTransactionsAsync();
            allTransactions = loaded;
            sellerState.ReplaceSuccessful(loaded);
            ApplyFilter();
            RaiseSellerSummaryProperties();
            OnPropertyChanged(nameof(ActionRequiredCount));
            OnPropertyChanged(nameof(ActionSummary));
        }
        catch
        {
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
            IsBusy = false;
        }
    }

    public void ApplyRoleNavigation(TransactionRoleRoute role) =>
        SelectRole(AuthenticatedHomeRoutes.ToRoleFilter(role));

    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            await LoadAsync();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void SelectRole(RoleFilter value)
    {
        if (value is not
            (RoleFilter.Buying or RoleFilter.Selling))
            return;
        roleFilter = value;
        bucketFilter = BucketFilter.All;
        sellerState.Select(SellerWorkCategory.All);
        Preferences.Default.Set(
            "transactions.last-role",
            value.ToString());
        RaiseFilterProperties();
        ApplyFilter();
        OnPropertyChanged(nameof(ModeTitle));
        OnPropertyChanged(nameof(ModeSubtitle));
        OnPropertyChanged(nameof(ModeSectionTitle));
        OnPropertyChanged(nameof(ActionRequiredCount));
        OnPropertyChanged(nameof(ActionSummary));
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
