using System.Net.Mail;
using System.Windows.Input;
using Toklong.Mobile.Core;
using Toklong.Mobile.Pages;

namespace Toklong.Mobile.ViewModels;

public sealed class SignUpViewModel(
    IAuthenticationService authentication) : ObservableViewModel
{
    private string fullName = "";
    private string email = "";
    private string phoneNumber = "";
    private string message = "";
    private bool isBusy;

    public string FullName
    {
        get => fullName;
        set => SetProperty(ref fullName, value);
    }

    public string PhoneNumber
    {
        get => phoneNumber;
        set => SetProperty(
            ref phoneNumber,
            ThaiMobilePhoneInput.Format(value));
    }

    public string Email
    {
        get => email;
        set => SetProperty(ref email, value);
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

    public bool IsBusy
    {
        get => isBusy;
        private set => SetProperty(ref isBusy, value);
    }

    public ICommand ContinueCommand => new AsyncCommand(ContinueAsync);

    private async Task ContinueAsync()
    {
        if (FullName.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries).Length < 2)
        {
            Message = "กรอกชื่อและนามสกุล";
            return;
        }
        if (!ThaiMobilePhoneInput.IsValid(PhoneNumber))
        {
            Message = "กรอกเบอร์มือถือไทย 10 หลัก เช่น 081-234-5678";
            return;
        }
        var cleanEmail = Email.Trim();
        if (cleanEmail.Length > 254 ||
            !MailAddress.TryCreate(cleanEmail, out var parsedEmail) ||
            !string.Equals(
                parsedEmail.Address,
                cleanEmail,
                StringComparison.OrdinalIgnoreCase))
        {
            Message = "กรอกอีเมลให้ถูกต้อง";
            return;
        }

        IsBusy = true;
        Message = "";
        try
        {
            var challenge = await authentication.RequestCodeAsync(
                PhoneNumber,
                AuthenticationMode.SignUp,
                FullName,
                cleanEmail);
            await Shell.Current.GoToAsync(
                nameof(VerifyCodePage),
                new Dictionary<string, object>
                {
                    ["Request"] = new VerificationRequest(
                        challenge.ChallengeId,
                        challenge.MaskedPhoneNumber,
                        challenge.DevelopmentCode,
                        PhoneNumber,
                        AuthenticationMode.SignUp,
                        FullName.Trim(),
                        cleanEmail)
                });
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
