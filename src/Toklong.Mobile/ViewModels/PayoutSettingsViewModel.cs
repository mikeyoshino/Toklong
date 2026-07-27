using System.Windows.Input;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.ViewModels;

public sealed class PayoutSettingsViewModel(
    ISellerOfferService sellerOffers) : ObservableViewModel
{
    private Guid? accountId;
    private BankOption? selectedBank;
    private string accountName = "";
    private string accountNumber = "";
    private string currentMaskedNumber = "";
    private string message = "";
    private bool isBusy;

    public IReadOnlyList<BankOption> Banks =>
        ThaiBankCatalog.Supported;

    public BankOption? SelectedBank
    {
        get => selectedBank;
        set => SetProperty(ref selectedBank, value);
    }

    public string AccountName
    {
        get => accountName;
        set => SetProperty(ref accountName, value);
    }

    public string AccountNumber
    {
        get => accountNumber;
        set => SetProperty(
            ref accountNumber,
            new string((value ?? "")
                .Where(char.IsDigit)
                .Take(15)
                .ToArray()));
    }

    public string CurrentMaskedNumber
    {
        get => currentMaskedNumber;
        private set
        {
            if (SetProperty(ref currentMaskedNumber, value))
                OnPropertyChanged(nameof(HasExistingAccount));
        }
    }

    public bool HasExistingAccount =>
        accountId.HasValue;

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

    public bool IsBusy
    {
        get => isBusy;
        private set => SetProperty(ref isBusy, value);
    }

    public ICommand SaveCommand =>
        new AsyncCommand(SaveAsync);

    public async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var account = (await sellerOffers
                    .GetPayoutAccountsAsync())
                .FirstOrDefault();
            accountId = account?.Id;
            CurrentMaskedNumber = account?.MaskedNumber ?? "";
            AccountName = account?.AccountName ?? "";
            SelectedBank = account is null
                ? null
                : Banks.FirstOrDefault(
                    bank => bank.Code == account.BankCode);
            AccountNumber = "";
        });
    }

    private async Task SaveAsync()
    {
        if (SelectedBank is null ||
            string.IsNullOrWhiteSpace(AccountName) ||
            (!HasExistingAccount &&
             AccountNumber.Length is < 10 or > 15) ||
            (HasExistingAccount &&
             AccountNumber.Length is > 0 and (< 10 or > 15)))
        {
            Message =
                "เลือกธนาคาร กรอกชื่อบัญชี และเลขบัญชี 10–15 หลักให้ครบ";
            return;
        }

        await RunAsync(async () =>
        {
            var account = (await sellerOffers
                    .SavePayoutAccountAsync(
                        accountId,
                        SelectedBank.Code,
                        AccountName.Trim(),
                        AccountNumber))
                .First();
            accountId = account.Id;
            CurrentMaskedNumber = account.MaskedNumber;
            AccountNumber = "";
            Message = "บันทึกบัญชีรับเงินแล้ว";
        });
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy)
            return;
        IsBusy = true;
        Message = "";
        try
        {
            await action();
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
}
