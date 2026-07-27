using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Pages;

public partial class TransactionDetailPage : ContentPage, IQueryAttributable
{
    private readonly TransactionDetailViewModel viewModel;
    private Guid? transactionId;
    private CancellationTokenSource? refreshCancellation;
    private Task? refreshTask;

    public TransactionDetailPage(TransactionDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("TransactionId", out var rawId) &&
            TryResolveId(rawId, out var resolvedId))
        {
            transactionId = resolvedId;
            await viewModel.LoadAsync(resolvedId);
            if (IsVisible)
                StartRefreshLoop();
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        StartRefreshLoop();
    }

    protected override void OnDisappearing()
    {
        StopRefreshLoop();
        base.OnDisappearing();
    }

    private void StartRefreshLoop()
    {
        if (!transactionId.HasValue ||
            refreshTask is { IsCompleted: false })
            return;

        refreshCancellation = new CancellationTokenSource();
        refreshTask = RefreshLoopAsync(
            transactionId.Value,
            refreshCancellation.Token);
    }

    private void StopRefreshLoop()
    {
        refreshCancellation?.Cancel();
        refreshCancellation?.Dispose();
        refreshCancellation = null;
        refreshTask = null;
    }

    private async Task RefreshLoopAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(
                       cancellationToken))
                await viewModel.RefreshAsync(id);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static bool TryResolveId(object value, out Guid id) =>
        value is Guid guid
            ? (id = guid) != Guid.Empty
            : Guid.TryParse(value?.ToString(), out id);
}
