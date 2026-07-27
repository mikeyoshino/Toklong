using System.Windows.Input;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.ViewModels;

public sealed record VerificationRequest(
    string ChallengeId,
    string MaskedPhoneNumber,
    string? DevelopmentCode,
    string PhoneNumber,
    AuthenticationMode Mode,
    string? FullName,
    string? Email);

public sealed class VerifyCodeViewModel(
    IAuthenticationService authentication,
    IDeepLinkCoordinator deepLinks,
    IPushRegistrationService pushRegistration)
    : ObservableViewModel
{
    private VerificationRequest? request;
    private string code = "";
    private string message = "";
    private bool isBusy;

    public string Code
    {
        get => code;
        set => SetProperty(
            ref code,
            new((value ?? "")
                .Where(char.IsAsciiDigit)
                .Take(6)
                .ToArray()));
    }

    public string MaskedPhoneNumber => request?.MaskedPhoneNumber ?? "";

    public string DevelopmentHint =>
        string.IsNullOrWhiteSpace(request?.DevelopmentCode)
            ? ""
            : $"รหัสสำหรับทดสอบ: {request.DevelopmentCode}";

    public bool HasDevelopmentHint =>
        !string.IsNullOrWhiteSpace(DevelopmentHint);

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

    public ICommand ConfirmCommand => new AsyncCommand(ConfirmAsync);
    public ICommand ResendCommand => new AsyncCommand(ResendAsync);

    public void Apply(VerificationRequest value)
    {
        request = value;
        Code = "";
        Message = "";
        OnPropertyChanged(nameof(MaskedPhoneNumber));
        OnPropertyChanged(nameof(DevelopmentHint));
        OnPropertyChanged(nameof(HasDevelopmentHint));
    }

    private async Task ConfirmAsync()
    {
        if (request is null)
            return;
        if (IsBusy)
            return;
        if (Code.Length != 6 || Code.Any(character => !char.IsDigit(character)))
        {
            Message = "กรอกรหัสยืนยัน 6 หลัก";
            return;
        }

        IsBusy = true;
        Message = "";
        try
        {
            await authentication.VerifyCodeAsync(
                request.ChallengeId,
                Code,
                request.Mode,
                request.FullName,
                request.Email);
            await pushRegistration.InitializeAsync();
            await Shell.Current.GoToAsync("//transactions");
            await deepLinks.ResumePendingAsync();
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

    private async Task ResendAsync()
    {
        if (request is null || IsBusy)
            return;

        IsBusy = true;
        Message = "";
        try
        {
            var challenge = await authentication.RequestCodeAsync(
                request.PhoneNumber,
                request.Mode,
                request.FullName,
                request.Email);
            request = request with
            {
                ChallengeId = challenge.ChallengeId,
                MaskedPhoneNumber = challenge.MaskedPhoneNumber,
                DevelopmentCode = challenge.DevelopmentCode
            };
            Code = "";
            OnPropertyChanged(nameof(MaskedPhoneNumber));
            OnPropertyChanged(nameof(DevelopmentHint));
            OnPropertyChanged(nameof(HasDevelopmentHint));
            Message = "ส่งรหัสใหม่แล้ว";
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
