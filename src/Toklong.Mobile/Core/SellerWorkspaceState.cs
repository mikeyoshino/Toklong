namespace Toklong.Mobile.Core;

public sealed class SellerWorkspaceState
{
    private IReadOnlyList<AppTransaction> transactions = [];

    public bool HasSuccessfulLoad { get; private set; }
    public bool HasVisibleSummary =>
        HasSuccessfulLoad && Snapshot.TotalCount > 0;
    public string LoadErrorText { get; private set; } = "";
    public bool HasLoadError =>
        !string.IsNullOrWhiteSpace(LoadErrorText);
    public SellerWorkCategory SelectedCategory { get; private set; }
        = SellerWorkCategory.All;
    public SellerWorkSnapshot Snapshot { get; private set; }
        = SellerWorkSummary.Create([]);
    public IReadOnlyList<AppTransaction> Transactions => transactions;

    public void Select(SellerWorkCategory category)
    {
        SelectedCategory = category;
        Rebuild();
    }

    public void ReplaceSuccessful(
        IReadOnlyList<AppTransaction> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        transactions = value;
        HasSuccessfulLoad = true;
        LoadErrorText = "";
        Rebuild();
        if (SelectedCategory != SellerWorkCategory.All &&
            Snapshot.VisibleTransactions.Count == 0)
        {
            SelectedCategory = SellerWorkCategory.All;
            Rebuild();
        }
    }

    public void MarkLoadFailed() =>
        LoadErrorText = HasSuccessfulLoad
            ? "อัปเดตล่าสุดไม่สำเร็จ"
            : "โหลดรายการไม่สำเร็จ · ลองอีกครั้ง";

    public void Reset()
    {
        transactions = [];
        HasSuccessfulLoad = false;
        LoadErrorText = "";
        SelectedCategory = SellerWorkCategory.All;
        Rebuild();
    }

    private void Rebuild() =>
        Snapshot = SellerWorkSummary.Create(
            transactions,
            SelectedCategory);
}
