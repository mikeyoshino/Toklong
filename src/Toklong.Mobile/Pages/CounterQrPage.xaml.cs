using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Pages;

public partial class CounterQrPage : ContentPage, IQueryAttributable
{
    private readonly CounterQrViewModel viewModel;
    private bool previousKeepScreenOn;
    private bool keepScreenStateCaptured;
    private IDispatcherTimer? expiryTimer;
    private IDispatcherTimer? authorizationTimer;
    private CancellationTokenSource? pageCancellation;

    public CounterQrPage(CounterQrViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }

    public async void ApplyQueryAttributes(
        IDictionary<string, object> query)
    {
        if (query.TryGetValue("TransactionId", out var rawId) &&
            Guid.TryParse(rawId?.ToString(), out var transactionId))
            await viewModel.LoadAsync(transactionId);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            previousKeepScreenOn =
                DeviceDisplay.Current.KeepScreenOn;
            keepScreenStateCaptured = true;
            DeviceDisplay.Current.KeepScreenOn = true;
        }
        catch (NotSupportedException)
        {
        }
        viewModel.InvalidateExpiredCounterQr();
        pageCancellation?.Cancel();
        pageCancellation?.Dispose();
        pageCancellation = new CancellationTokenSource();
        expiryTimer ??= CreateExpiryTimer();
        expiryTimer.Start();
        authorizationTimer ??= CreateAuthorizationTimer();
        authorizationTimer.Start();
    }

    protected override void OnDisappearing()
    {
        viewModel.Clear();
        pageCancellation?.Cancel();
        pageCancellation?.Dispose();
        pageCancellation = null;
        expiryTimer?.Stop();
        authorizationTimer?.Stop();
        if (keepScreenStateCaptured)
        {
            try
            {
                DeviceDisplay.Current.KeepScreenOn =
                    previousKeepScreenOn;
            }
            catch (NotSupportedException)
            {
            }
            keepScreenStateCaptured = false;
        }
        base.OnDisappearing();
    }

    private IDispatcherTimer CreateExpiryTimer()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += (_, _) =>
            viewModel.InvalidateExpiredCounterQr();
        return timer;
    }

    private IDispatcherTimer CreateAuthorizationTimer()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(5);
        timer.Tick += OnAuthorizationTimerTick;
        return timer;
    }

    private async void OnAuthorizationTimerTick(
        object? sender,
        EventArgs eventArgs)
    {
        var cancellation = pageCancellation;
        if (cancellation is null ||
            cancellation.IsCancellationRequested)
            return;
        await viewModel.RefreshAuthorizationAsync(
            cancellation.Token);
    }
}
