using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Pages;

public partial class TransactionDetailPage : ContentPage, IQueryAttributable
{
    private readonly TransactionDetailViewModel viewModel;
    private Guid? transactionId;
    private CancellationTokenSource? refreshCancellation;
    private Task? refreshTask;
    private bool isAppeared;
    private long appearanceGeneration;

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
            var observedAppearance = Volatile.Read(
                ref appearanceGeneration);
            transactionId = resolvedId;
            await viewModel.LoadAsync(
                resolvedId,
                EnsureRefreshCancellation().Token);
            if (isAppeared &&
                observedAppearance == Volatile.Read(
                    ref appearanceGeneration))
                StartRefreshLoop();
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        isAppeared = true;
        Interlocked.Increment(ref appearanceGeneration);
        if (transactionId.HasValue)
            _ = viewModel.RefreshAsync(
                transactionId.Value,
                EnsureRefreshCancellation().Token);
        StartRefreshLoop();
    }

    protected override void OnDisappearing()
    {
        isAppeared = false;
        Interlocked.Increment(ref appearanceGeneration);
        StopRefreshLoop();
        viewModel.ClearSensitiveCounterQr();
        base.OnDisappearing();
    }

    private void OnParcelProtectionToggled(
        object? sender,
        ToggledEventArgs eventArgs)
    {
        if (eventArgs.Value ==
                viewModel.IsParcelProtectionToggleOn ||
            !viewModel.CanToggleParcelProtection ||
            !viewModel.ToggleParcelProtectionCommand
                .CanExecute(null))
            return;

        viewModel.ToggleParcelProtectionCommand.Execute(null);
    }

    private void StartRefreshLoop()
    {
        if (!transactionId.HasValue ||
            refreshTask is { IsCompleted: false })
            return;

        var cancellation = EnsureRefreshCancellation();
        refreshTask = RefreshLoopAsync(
            transactionId.Value,
            cancellation.Token);
    }

    private CancellationTokenSource EnsureRefreshCancellation()
    {
        if (refreshCancellation is null ||
            refreshCancellation.IsCancellationRequested)
        {
            refreshCancellation?.Dispose();
            refreshCancellation = new CancellationTokenSource();
        }
        return refreshCancellation;
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
                await viewModel.RefreshAsync(id, cancellationToken);
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
