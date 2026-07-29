using System.Windows.Input;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.ViewModels;

public sealed class AccountViewModel(
    IAuthenticationService authentication,
    AuthenticatedSessionBoundary session) : ObservableViewModel
{
    private MobileProfile? profile;
    private string message = "";
    private bool isBusy;

    public string DisplayName => profile?.DisplayName ?? "";

    public string Initials
    {
        get
        {
            var parts = DisplayName.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);
            return string.Concat(parts.Take(2).Select(part => part[0]));
        }
    }

    public string PhoneNumber => profile?.PhoneNumber ?? "";
    public bool CanBuy => profile?.CanBuy == true;
    public string Email => profile?.Email ?? "";
    public bool HasPayoutAccount =>
        !string.IsNullOrWhiteSpace(profile?.PayoutMaskedNumber);
    public string PayoutText => HasPayoutAccount
        ? $"{profile!.PayoutBankCode} · {profile.PayoutMaskedNumber}"
        : "ยังไม่ได้เพิ่มบัญชีรับเงิน";
    public string PayoutStatus => HasPayoutAccount
        ? "เพิ่มแล้ว"
        : "ยังไม่เพิ่ม";
    public string PayoutNote => HasPayoutAccount
        ? "บัญชีรับเงินที่บันทึกไว้"
        : "เพิ่มบัญชีก่อนยืนยันข้อเสนอขาย";
    public bool HasSavedAddress =>
        !string.IsNullOrWhiteSpace(profile?.SavedAddress);
    public string AddressText => HasSavedAddress
        ? profile!.SavedAddress!
        : "ยังไม่ได้บันทึกที่อยู่";

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

    public ICommand SignOutCommand => new AsyncCommand(SignOutAsync);
    public ICommand OpenPayoutSettingsCommand =>
        new AsyncCommand(() =>
            Shell.Current.GoToAsync(
                nameof(Pages.PayoutSettingsPage)));

    public async Task LoadAsync()
    {
        IsBusy = true;
        Message = "";
        try
        {
            profile = await authentication.GetProfileAsync();
            RaiseProfileChanged();
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

    internal async Task SignOutAsync()
    {
        IsBusy = true;
        Message = "";
        profile = null;
        RaiseProfileChanged();
        session.Reset();
        try
        {
            await authentication.SignOutAsync();
            await Shell.Current.GoToAsync("//welcome");
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

    private void RaiseProfileChanged()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Initials));
        OnPropertyChanged(nameof(PhoneNumber));
        OnPropertyChanged(nameof(CanBuy));
        OnPropertyChanged(nameof(Email));
        OnPropertyChanged(nameof(HasPayoutAccount));
        OnPropertyChanged(nameof(PayoutText));
        OnPropertyChanged(nameof(PayoutStatus));
        OnPropertyChanged(nameof(PayoutNote));
        OnPropertyChanged(nameof(HasSavedAddress));
        OnPropertyChanged(nameof(AddressText));
    }
}
