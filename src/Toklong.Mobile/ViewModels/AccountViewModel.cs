using System.Windows.Input;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.ViewModels;

public sealed class AccountViewModel(
    IAuthenticationService authentication,
    AuthenticatedSessionBoundary session) : ObservableViewModel
{
    private MobileProfile? profile;
    private PendingEmailChange? pendingEmailChange;
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
    public string EmailSemanticDescription =>
        string.IsNullOrWhiteSpace(Email)
            ? "ยังไม่ได้เพิ่มอีเมลสำหรับใบเสร็จและการคืนเงิน"
            : $"อีเมลที่ยืนยันแล้ว {Email} สำหรับใบเสร็จและการคืนเงิน";
    public bool HasPendingEmailChange =>
        pendingEmailChange is not null;
    public string EmailStatus => HasPendingEmailChange
        ? "รอยืนยัน"
        : string.IsNullOrWhiteSpace(Email)
            ? "ยังไม่เพิ่ม"
            : "ยืนยันแล้ว";
    public string EmailActionText => HasPendingEmailChange
        ? "ยืนยันต่อ"
        : string.IsNullOrWhiteSpace(Email)
            ? "เพิ่ม"
            : "แก้ไข";
    public string EmailNote => HasPendingEmailChange
        ? $"รอยืนยันอีเมลใหม่ {pendingEmailChange!.MaskedEmail}"
        : "ใช้รับใบเสร็จและขั้นตอนคืนเงิน";
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
    public ICommand OpenEmailChangeCommand =>
        new AsyncCommand(OpenEmailChangeAsync);
    public ICommand OpenPayoutSettingsCommand =>
        new AsyncCommand(() =>
            Shell.Current.GoToAsync(
                nameof(Pages.PayoutSettingsPage)));

    public async Task LoadAsync()
    {
        var generation = session.Capture();
        IsBusy = true;
        Message = "";
        try
        {
            var profileRequest =
                authentication.GetProfileAsync();
            var pendingRequest =
                LoadPendingEmailChangeAsync();
            try
            {
                await Task.WhenAll(
                    profileRequest,
                    pendingRequest);
            }
            catch
            {
                // Apply each completed result below so a partial refresh
                // cannot leave obsolete account state on screen.
            }

            if (!session.IsCurrent(generation))
                return;

            profile = profileRequest.IsCompletedSuccessfully
                ? profileRequest.GetAwaiter().GetResult()
                : null;
            pendingEmailChange =
                pendingRequest.IsCompletedSuccessfully
                    ? pendingRequest.GetAwaiter().GetResult()
                    : null;
            RaiseProfileChanged();

            if (!profileRequest.IsCompletedSuccessfully ||
                !pendingRequest.IsCompletedSuccessfully)
            {
                Message =
                    "โหลดข้อมูลบัญชีไม่สำเร็จ กรุณาลองอีกครั้ง";
            }
        }
        catch
        {
            if (!session.IsCurrent(generation))
                return;

            profile = null;
            pendingEmailChange = null;
            RaiseProfileChanged();
            Message =
                "โหลดข้อมูลบัญชีไม่สำเร็จ กรุณาลองอีกครั้ง";
        }
        finally
        {
            if (session.IsCurrent(generation))
                IsBusy = false;
        }
    }

    internal async Task SignOutAsync()
    {
        IsBusy = true;
        Message = "";
        profile = null;
        pendingEmailChange = null;
        RaiseProfileChanged();
        session.Reset();
        try
        {
            await authentication.SignOutAsync();
            await Shell.Current.GoToAsync("//welcome");
        }
        catch
        {
            Message =
                "ออกจากระบบไม่สำเร็จ กรุณาลองอีกครั้ง";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task OpenEmailChangeAsync() =>
        pendingEmailChange is null
            ? Shell.Current.GoToAsync(
                nameof(Pages.ChangeEmailPage))
            : Shell.Current.GoToAsync(
                nameof(Pages.VerifyEmailChangePage),
                new Dictionary<string, object>
                {
                    ["Pending"] = pendingEmailChange
                });

    private async Task<PendingEmailChange?>
        LoadPendingEmailChangeAsync()
    {
        try
        {
            return await authentication
                .GetPendingEmailChangeAsync();
        }
        catch (InvalidOperationException exception) when (
            string.Equals(
                exception.Message,
                "บัญชีนี้ไม่มีสิทธิ์เปลี่ยนอีเมล",
                StringComparison.Ordinal))
        {
            return null;
        }
    }

    private void RaiseProfileChanged()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Initials));
        OnPropertyChanged(nameof(PhoneNumber));
        OnPropertyChanged(nameof(CanBuy));
        OnPropertyChanged(nameof(Email));
        OnPropertyChanged(nameof(EmailSemanticDescription));
        OnPropertyChanged(nameof(HasPendingEmailChange));
        OnPropertyChanged(nameof(EmailStatus));
        OnPropertyChanged(nameof(EmailActionText));
        OnPropertyChanged(nameof(EmailNote));
        OnPropertyChanged(nameof(HasPayoutAccount));
        OnPropertyChanged(nameof(PayoutText));
        OnPropertyChanged(nameof(PayoutStatus));
        OnPropertyChanged(nameof(PayoutNote));
        OnPropertyChanged(nameof(HasSavedAddress));
        OnPropertyChanged(nameof(AddressText));
    }
}
