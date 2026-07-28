using System.Windows.Input;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.ViewModels;

public sealed class AuthenticatedHomeViewModel : ObservableViewModel
{
    private readonly ITransactionService transactions;
    private readonly IMobileAnalytics analytics;
    private readonly SellerWorkspaceState sellerState = new();
    private readonly AsyncCommand retryCommand;
    private bool isBusy;

    public AuthenticatedHomeViewModel(
        ITransactionService transactions,
        IMobileAnalytics analytics)
    {
        this.transactions = transactions;
        this.analytics = analytics;
        retryCommand = new AsyncCommand(LoadAsync);
    }

    public bool HasSellerSummary => sellerState.HasVisibleSummary;
    public bool HasNewOffers =>
        HasSellerSummary && sellerState.Snapshot.NewOfferCount > 0;
    public bool HasActionableSellerWork =>
        HasSellerSummary && sellerState.Snapshot.ActionableCount > 0;
    public string NewOfferBadgeText =>
        $"{sellerState.Snapshot.NewOfferCount} ข้อเสนอใหม่";
    public string ActionableSellerWorkText =>
        $"มี {sellerState.Snapshot.ActionableCount} รายการที่ต้องจัดการ";
    public string SellerCardSemanticText =>
        !HasSellerSummary
            ? "ขาย ตรวจข้อเสนอ ส่งสินค้า และติดตามยอดรับ"
            : $"ขาย {NewOfferBadgeText} {ActionableSellerWorkText}";
    public bool HasLoadError => sellerState.HasLoadError;
    public string LoadErrorText => sellerState.LoadErrorText;
    public ICommand RetryCommand => retryCommand;

    public async Task LoadAsync()
    {
        if (isBusy)
            return;

        isBusy = true;
        try
        {
            var loaded = await transactions.GetTransactionsAsync();
            sellerState.ReplaceSuccessful(loaded);
            RaiseSummaryProperties();
            analytics.Track(SellerWorkspaceAnalytics.HomeOpened(
                sellerState.Snapshot.NewOfferCount,
                sellerState.Snapshot.ActionableCount));
        }
        catch
        {
            sellerState.MarkLoadFailed();
            RaiseSummaryProperties();
        }
        finally
        {
            isBusy = false;
        }
    }

    private void RaiseSummaryProperties()
    {
        OnPropertyChanged(nameof(HasSellerSummary));
        OnPropertyChanged(nameof(HasNewOffers));
        OnPropertyChanged(nameof(HasActionableSellerWork));
        OnPropertyChanged(nameof(NewOfferBadgeText));
        OnPropertyChanged(nameof(ActionableSellerWorkText));
        OnPropertyChanged(nameof(SellerCardSemanticText));
        OnPropertyChanged(nameof(HasLoadError));
        OnPropertyChanged(nameof(LoadErrorText));
    }
}
