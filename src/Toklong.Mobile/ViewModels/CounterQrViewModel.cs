using System.Security.Cryptography;
using System.Windows.Input;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.ViewModels;

public sealed class CounterQrViewModel : ObservableViewModel
{
    private readonly ITransactionService transactionService;
    private readonly TimeProvider timeProvider;
    private AppTransaction? transaction;
    private byte[]? imageBytes;
    private string message = "";
    private bool isBusy;
    private CancellationTokenSource? loadCancellation;
    private long loadGeneration;
    private int authorizationRefreshRunning;

    public CounterQrViewModel(
        ITransactionService transactionService,
        TimeProvider? timeProvider = null,
        AuthenticatedSessionBoundary? session = null)
    {
        this.transactionService = transactionService;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        if (session is not null)
            session.ResetRequested += OnSessionReset;
    }

    public AppTransaction? Transaction
    {
        get => transaction;
        private set
        {
            if (SetProperty(ref transaction, value))
            {
                OnPropertyChanged(nameof(ShippingServiceText));
                OnPropertyChanged(nameof(TrackingNumberText));
                OnPropertyChanged(nameof(ShipByText));
                OnPropertyChanged(nameof(ExpiryText));
                OnPropertyChanged(nameof(HasExpiry));
            }
        }
    }

    public byte[]? ImageBytes
    {
        get => imageBytes;
        private set
        {
            if (SetProperty(ref imageBytes, value))
                OnPropertyChanged(nameof(HasImage));
        }
    }

    public bool HasImage => ImageBytes is { Length: > 0 };

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

    public bool IsBusy
    {
        get => isBusy;
        private set => SetProperty(ref isBusy, value);
    }

    public string ShippingServiceText =>
        Transaction?.ShippingServiceText ?? "";

    public string TrackingNumberText =>
        Transaction?.TrackingNumberText ?? "";

    public string ShipByText => Transaction?.ShipByAt is { } shipBy
        ? $"ส่งภายใน {shipBy.ToLocalTime():d MMM yyyy · HH:mm} น."
        : "";

    public string ExpiryText =>
        Transaction?.CounterQrExpiryText ?? "";

    public bool HasExpiry =>
        Transaction?.CounterQrExpiresAt.HasValue == true;

    public ICommand RetryCommand =>
        new AsyncCommand(RetryAsync);

    public Task LoadAsync(Guid transactionId)
    {
        if (transactionId == Guid.Empty || IsBusy)
            return Task.CompletedTask;
        var (cancellation, generation) = BeginLoad();
        return ExecuteLoadAsync(
            transactionId,
            requestRetry: false,
            cancellation,
            generation);
    }

    public Task RetryAsync()
    {
        if (Transaction is null || IsBusy)
            return Task.CompletedTask;
        var transactionId = Transaction.Id;
        var requestRetry = Transaction.IsCounterQrError;
        var (cancellation, generation) = BeginLoad();
        return ExecuteLoadAsync(
            transactionId,
            requestRetry,
            cancellation,
            generation);
    }

    private async Task ExecuteLoadAsync(
        Guid transactionId,
        bool requestRetry,
        CancellationTokenSource cancellation,
        long generation)
    {
        IsBusy = true;
        Message = requestRetry
            ? "กำลังขอ QR ใหม่…"
            : "";
        ClearImage();
        try
        {
            if (requestRetry)
            {
                await transactionService.RetryCounterQrAsync(
                    transactionId,
                    cancellation.Token);
                if (!IsCurrent(cancellation, generation))
                    return;
            }
            var loadedTransaction = await transactionService
                .GetTransactionAsync(
                    transactionId,
                    cancellation.Token);
            if (!IsCurrent(cancellation, generation))
                return;
            Transaction = loadedTransaction;
            if (loadedTransaction is not
                {
                    Role: AppTransactionRole.Seller,
                    IsCounterQrReady: true
                })
                throw new InvalidOperationException(
                    "QR เคาน์เตอร์ยังไม่พร้อมใช้งาน");
            var image = await transactionService
                .DownloadCounterQrAsync(
                    transactionId,
                    cancellation.Token);
            if (!IsCurrent(cancellation, generation) ||
                Transaction?.Id != transactionId ||
                Transaction.IsCounterQrReady != true)
            {
                CryptographicOperations.ZeroMemory(image.Content);
                return;
            }
            ImageBytes = [.. image.Content];
            CryptographicOperations.ZeroMemory(image.Content);
            InvalidateExpiredCounterQr();
        }
        catch (OperationCanceledException)
            when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Message = exception.Message;
        }
        finally
        {
            if (generation == Volatile.Read(ref loadGeneration))
                IsBusy = false;
            Interlocked.CompareExchange(
                ref loadCancellation,
                null,
                cancellation);
            cancellation.Dispose();
        }
    }

    public async Task RefreshAuthorizationAsync(
        CancellationToken cancellationToken = default)
    {
        var current = Transaction;
        if (current is null || !HasImage ||
            Interlocked.Exchange(
                ref authorizationRefreshRunning,
                1) != 0)
            return;
        var generation = Volatile.Read(ref loadGeneration);
        try
        {
            var refreshed = await transactionService
                .GetTransactionAsync(
                    current.Id,
                    cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (generation != Volatile.Read(ref loadGeneration))
                return;
            if (refreshed is not
                {
                    Role: AppTransactionRole.Seller,
                    IsCounterQrReady: true
                })
            {
                Clear();
                Transaction = refreshed;
                Message =
                    "QR เคาน์เตอร์ไม่พร้อมใช้งานแล้ว กรุณากลับไปดูสถานะรายการ";
                return;
            }
            Transaction = refreshed;
            InvalidateExpiredCounterQr();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            if (generation == Volatile.Read(ref loadGeneration))
            {
                Clear();
                Message =
                    "ตรวจสอบสิทธิ์ใช้ QR ไม่สำเร็จ กรุณาลองโหลดใหม่";
            }
        }
        finally
        {
            Volatile.Write(ref authorizationRefreshRunning, 0);
        }
    }

    public void Clear()
    {
        Interlocked.Increment(ref loadGeneration);
        Interlocked.Exchange(ref loadCancellation, null)
            ?.Cancel();
        ClearImage();
        IsBusy = false;
    }

    public void InvalidateExpiredCounterQr()
    {
        if (Transaction?.CounterQrExpiresAt is not { } expiresAt ||
            expiresAt > timeProvider.GetUtcNow() ||
            !HasImage)
            return;
        Interlocked.Increment(ref loadGeneration);
        Interlocked.Exchange(ref loadCancellation, null)
            ?.Cancel();
        ClearImage();
        Message = "QR เคาน์เตอร์หมดอายุแล้ว กรุณาลองโหลดใหม่";
    }

    private void OnSessionReset(object? sender, EventArgs eventArgs)
    {
        Clear();
        Transaction = null;
        Message = "";
    }

    private void ClearImage()
    {
        if (imageBytes is not null)
            CryptographicOperations.ZeroMemory(imageBytes);
        ImageBytes = null;
    }

    private (CancellationTokenSource Source, long Generation)
        BeginLoad()
    {
        var source = new CancellationTokenSource();
        var generation = Interlocked.Increment(ref loadGeneration);
        Interlocked.Exchange(ref loadCancellation, source)
            ?.Cancel();
        return (source, generation);
    }

    private bool IsCurrent(
        CancellationTokenSource cancellation,
        long generation) =>
        !cancellation.IsCancellationRequested &&
        generation == Volatile.Read(ref loadGeneration);
}
