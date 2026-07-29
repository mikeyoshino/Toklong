using Toklong.Mobile.Core;
using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Pages;

public partial class TransactionsPage :
    ContentPage,
    IQueryAttributable
{
    private readonly TransactionsViewModel viewModel;
    private readonly RefreshLoopLifecycle refreshLoop = new();

    public TransactionsPage(TransactionsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }

    public void ApplyQueryAttributes(
        IDictionary<string, object> query)
    {
        if (query.TryGetValue("role", out var raw) &&
            AuthenticatedHomeRoutes.TryParseRole(
                raw?.ToString(),
                out var role))
        {
            viewModel.ApplyRoleNavigation(role);
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var refreshToken = refreshLoop.Begin();
        await viewModel.LoadAsync();
        if (refreshLoop.IsCurrent(refreshToken))
            _ = RefreshWhileVisibleAsync(refreshToken);
    }

    protected override void OnDisappearing()
    {
        refreshLoop.End();
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
