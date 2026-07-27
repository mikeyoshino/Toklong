using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Pages;

public partial class TransactionsPage : ContentPage
{
    private readonly TransactionsViewModel viewModel;
    private CancellationTokenSource? refreshLoop;

    public TransactionsPage(TransactionsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        refreshLoop?.Cancel();
        refreshLoop?.Dispose();
        refreshLoop = new CancellationTokenSource();
        await viewModel.LoadAsync();
        _ = RefreshWhileVisibleAsync(
            refreshLoop.Token);
    }

    protected override void OnDisappearing()
    {
        refreshLoop?.Cancel();
        refreshLoop?.Dispose();
        refreshLoop = null;
        base.OnDisappearing();
    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        Dispatcher.Dispatch(RestoreRootChrome);
    }

    private void RestoreRootChrome()
    {
        // Shell can retain the pushed page's chrome after a pop. Toggle the
        // attached values so the native toolbar recalculates after navigation
        // has completed instead of reusing the previous page's state.
        Shell.SetNavBarIsVisible(this, true);
        Shell.SetNavBarIsVisible(this, false);
        Shell.SetTabBarIsVisible(this, false);
        Shell.SetTabBarIsVisible(this, true);
        InvalidateMeasure();
    }

    private async Task RefreshWhileVisibleAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(5),
                    cancellationToken);
                await viewModel.LoadAsync();
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
