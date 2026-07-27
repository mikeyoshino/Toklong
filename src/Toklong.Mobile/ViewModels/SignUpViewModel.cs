using System.Windows.Input;
using Toklong.Mobile.Core;
using Toklong.Mobile.Pages;

namespace Toklong.Mobile.ViewModels;

public sealed class SignUpViewModel(
    IAuthenticationService authentication) : ObservableViewModel
{
    private string phoneNumber = "";
    private string message = "";
    private bool isBusy;

    public string PhoneNumber
    {
        get => phoneNumber;
        set => SetProperty(
            ref phoneNumber,
            ThaiMobilePhoneInput.Format(value));
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
        if (!ThaiMobilePhoneInput.IsValid(PhoneNumber))
        {
            Message = "กรอกเบอร์มือถือไทย 10 หลัก เช่น 081-234-5678";
            return;
        }
        IsBusy = true;
        Message = "";
        try
        {
            var challenge = await authentication.RequestCodeAsync(
                PhoneNumber,
                AuthenticationMode.SignUp);
            await Shell.Current.GoToAsync(
                nameof(VerifyCodePage),
                new Dictionary<string, object>
                {
                    ["Request"] = new VerificationRequest(
                        challenge.ChallengeId,
                        challenge.MaskedPhoneNumber,
                        challenge.DevelopmentCode,
                        PhoneNumber,
                        AuthenticationMode.SignUp)
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
