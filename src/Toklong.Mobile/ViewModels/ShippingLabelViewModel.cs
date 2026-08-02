using System.Text;
using System.Windows.Input;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.ViewModels;

public sealed class ShippingLabelViewModel : ObservableViewModel
{
    private readonly ITransactionService transactionService;
    private readonly AsyncCommand saveCommand;
    private readonly AsyncCommand shareOrPrintCommand;
    private readonly AsyncCommand retryCommand;
    private Guid transactionId;
    private bool returnLabel;
    private ShippingLabelFile? labelFile;
    private HtmlWebViewSource? labelSource;
    private AppTransaction? transaction;
    private string message = "";
    private bool isBusy;

    public ShippingLabelViewModel(
        ITransactionService transactionService)
    {
        this.transactionService = transactionService;
        saveCommand = new AsyncCommand(
            () => ShareLabelAsync(
                "บันทึกใบปะหน้าลงเครื่อง"),
            () => CanUseLabel);
        shareOrPrintCommand = new AsyncCommand(
            () => ShareLabelAsync(
                "แชร์หรือพิมพ์ใบปะหน้า"),
            () => CanUseLabel);
        retryCommand = new AsyncCommand(
            () => LoadAsync(
                transactionId,
                returnLabel),
            () => transactionId != Guid.Empty &&
                  !IsBusy);
    }

    public AppTransaction? Transaction
    {
        get => transaction;
        private set
        {
            if (SetProperty(ref transaction, value))
            {
                OnPropertyChanged(nameof(TrackingNumberText));
                OnPropertyChanged(nameof(ShippingServiceText));
            }
        }
    }

    public HtmlWebViewSource? LabelSource
    {
        get => labelSource;
        private set
        {
            if (SetProperty(ref labelSource, value))
            {
                OnPropertyChanged(nameof(HasLabel));
                OnPropertyChanged(nameof(CanUseLabel));
                RaiseCommandState();
            }
        }
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

    public bool HasMessage =>
        !string.IsNullOrWhiteSpace(Message);

    public bool HasLabel =>
        LabelSource is not null;

    public bool CanUseLabel =>
        labelFile is not null &&
        HasLabel &&
        !IsBusy;

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (!SetProperty(ref isBusy, value))
                return;
            OnPropertyChanged(nameof(CanUseLabel));
            RaiseCommandState();
        }
    }

    public string TrackingNumberText =>
        Transaction?.TrackingNumberText ?? "";

    public string ShippingServiceText =>
        Transaction?.ShippingServiceText ?? "";

    public ICommand SaveCommand => saveCommand;

    public ICommand ShareOrPrintCommand =>
        shareOrPrintCommand;

    public ICommand RetryCommand => retryCommand;

    public Task LoadAsync(Guid id) =>
        LoadAsync(id, isReturn: true);

    public async Task LoadAsync(
        Guid id,
        bool isReturn)
    {
        if (id == Guid.Empty ||
            IsBusy)
            return;

        transactionId = id;
        returnLabel = true;
        IsBusy = true;
        Message = "";
        LabelSource = null;
        labelFile = null;
        try
        {
            Transaction = await transactionService
                .GetTransactionAsync(id);
            if (Transaction is null)
                throw new InvalidOperationException(
                    "ไม่พบรายการนี้");
            if (Transaction.Role !=
                    AppTransactionRole.Buyer)
                throw new InvalidOperationException(
                    "เฉพาะผู้ซื้อของรายการนี้ที่เปิดใบปะหน้าส่งคืนได้");
            if (!Transaction.ReturnShippingLabelAvailable)
                throw new InvalidOperationException(
                    "กำลังออกใบปะหน้าส่งคืน กรุณารอสักครู่แล้วลองใหม่");

            labelFile = await transactionService
                .DownloadReturnShippingLabelAsync(id);
            var html = Encoding.UTF8.GetString(
                labelFile.Content);
            LabelSource = new HtmlWebViewSource
            {
                Html = ShippingLabelHtmlPresenter
                    .PreparePreview(html)
            };
        }
        catch (Exception exception)
        {
            Message = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ShareLabelAsync(
        string title)
    {
        if (labelFile is null ||
            !CanUseLabel)
            return;

        IsBusy = true;
        Message = "";
        string? path = null;
        try
        {
            var fileName = SafeHtmlFileName(
                labelFile.FileName,
                transactionId);
            path = Path.Combine(
                FileSystem.CacheDirectory,
                fileName);
            await File.WriteAllBytesAsync(
                path,
                labelFile.Content);
            await Share.Default.RequestAsync(
                new ShareFileRequest(
                    title,
                    new ShareFile(
                        path,
                        "text/html")));
        }
        catch (Exception exception)
        {
            Message = exception.Message;
        }
        finally
        {
            DeleteTemporaryFile(path);
            IsBusy = false;
        }
    }

    private void RaiseCommandState()
    {
        saveCommand.RaiseCanExecuteChanged();
        shareOrPrintCommand.RaiseCanExecuteChanged();
        retryCommand.RaiseCanExecuteChanged();
    }

    private static string SafeHtmlFileName(
        string fileName,
        Guid id)
    {
        var safe = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safe))
            safe = $"TOKLONG-label-{id:N}.html";
        if (!string.Equals(
                Path.GetExtension(safe),
                ".html",
                StringComparison.OrdinalIgnoreCase))
            safe = $"{Path.GetFileNameWithoutExtension(safe)}.html";
        return safe;
    }

    private static void DeleteTemporaryFile(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
