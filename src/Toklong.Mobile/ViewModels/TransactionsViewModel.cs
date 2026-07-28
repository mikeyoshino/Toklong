using System.Collections.ObjectModel;
using System.Windows.Input;
using Toklong.Mobile.Core;
using Toklong.Mobile.Pages;

namespace Toklong.Mobile.ViewModels;

public sealed class TransactionsViewModel : ObservableViewModel
{
    private readonly ITransactionService transactionService;
    private readonly IDeepLinkCoordinator deepLinks;
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
        IDeepLinkCoordinator deepLinks)
    {
        this.transactionService = transactionService;
        this.deepLinks = deepLinks;
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
        SelectSellerReviewCommand =
            new Command(() => SelectBucket(BucketFilter.SellerReview));
        SelectSellerFulfillmentCommand =
            new Command(() => SelectBucket(BucketFilter.SellerFulfillment));
        SelectSellerPayoutCommand =
            new Command(() => SelectBucket(BucketFilter.SellerPayout));
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
    public ICommand SelectSellerReviewCommand { get; }
    public ICommand SelectSellerFulfillmentCommand { get; }
    public ICommand SelectSellerPayoutCommand { get; }
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
    public bool IsSellerReview =>
        bucketFilter == BucketFilter.SellerReview;
    public bool IsSellerFulfillment =>
        bucketFilter == BucketFilter.SellerFulfillment;
    public bool IsSellerPayout =>
        bucketFilter == BucketFilter.SellerPayout;

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
            allTransactions = await transactionService.GetTransactionsAsync();
            ApplyFilter();
            OnPropertyChanged(nameof(ActionRequiredCount));
            OnPropertyChanged(nameof(ActionSummary));
        }
        catch
        {
            allTransactions = [];
            ApplyFilter();
            OnPropertyChanged(nameof(ActionRequiredCount));
            OnPropertyChanged(nameof(ActionSummary));
            ErrorText = "โหลดรายการไม่สำเร็จ กรุณาลองอีกครั้ง";
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
        OnPropertyChanged(nameof(IsSellerReview));
        OnPropertyChanged(nameof(IsSellerFulfillment));
        OnPropertyChanged(nameof(IsSellerPayout));
    }

    private void ApplyFilter()
    {
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

    private async Task OpenTransactionAsync(
        AppTransaction? item)
    {
        if (item is null)
            return;

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
