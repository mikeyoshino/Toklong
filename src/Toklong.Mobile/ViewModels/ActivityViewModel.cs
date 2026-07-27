using System.Collections.ObjectModel;
using System.Windows.Input;
using Toklong.Mobile.Core;
using Toklong.Mobile.Pages;

namespace Toklong.Mobile.ViewModels;

public sealed class ActivityViewModel(
    INotificationService notifications,
    IDeepLinkCoordinator deepLinks) : ObservableViewModel
{
    private bool isBusy;
    private bool isRefreshing;
    private string message = "";

    public ObservableCollection<AppNotification> Items { get; } = [];

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

    public string Message
    {
        get => message;
        private set
        {
            if (SetProperty(ref message, value))
                OnPropertyChanged(nameof(HasMessage));
        }
    }

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    public ICommand RefreshCommand =>
        new AsyncCommand(RefreshAsync);

    public ICommand OpenCommand =>
        new Command<AppNotification>(
            async item => await OpenAsync(item));

    public async Task LoadAsync()
    {
        if (IsBusy)
            return;
        IsBusy = true;
        Message = "";
        try
        {
            var latest =
                await notifications.GetNotificationsAsync();
            Synchronize(latest);
        }
        catch
        {
            Message =
                "โหลดการแจ้งเตือนไม่สำเร็จ กรุณาลองอีกครั้ง";
        }
        finally
        {
            IsBusy = false;
        }
    }

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

    private async Task OpenAsync(AppNotification? item)
    {
        if (item is null)
            return;
        if (item.EventType == "buyer_offer_received" &&
            Uri.TryCreate(
                item.DeepLink,
                UriKind.Absolute,
                out var offerUri))
        {
            await deepLinks.HandleAsync(offerUri);
            return;
        }

        await Shell.Current.GoToAsync(
            nameof(TransactionDetailPage),
            new Dictionary<string, object>
            {
                ["TransactionId"] = item.TransactionId
            });
    }

    private void Synchronize(
        IReadOnlyList<AppNotification> latest)
    {
        Items.Clear();
        foreach (var item in latest)
            Items.Add(item);
    }
}
