using System.Windows.Input;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.ViewModels;

public sealed record VerificationRequest(
    string ChallengeId,
    string MaskedPhoneNumber,
    string? DevelopmentCode,
    string PhoneNumber,
    AuthenticationMode Mode);

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
    private int resendSecondsRemaining;
    private CancellationTokenSource? resendCountdown;

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

    public string ConfirmButtonText =>
        request?.Mode == AuthenticationMode.SignUp
            ? "ยืนยันเบอร์มือถือ"
            : "ยืนยันและเข้าสู่ระบบ";

    public string ResendButtonText =>
        resendSecondsRemaining > 0
            ? $"ส่งใหม่ได้ใน {resendSecondsRemaining} วินาที"
            : "ส่งรหัสใหม่";

    public bool CanResend =>
        resendSecondsRemaining == 0 && !IsBusy;

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
        private set
        {
            if (SetProperty(ref isBusy, value))
                OnPropertyChanged(nameof(CanResend));
        }
    }

    public ICommand ConfirmCommand => new AsyncCommand(ConfirmAsync);
    public ICommand ResendCommand => new AsyncCommand(ResendAsync);
    public ICommand EditPhoneCommand => new AsyncCommand(
        () => Shell.Current.GoToAsync(".."));

    public void Apply(VerificationRequest value)
    {
        request = value;
        Code = "";
        Message = "";
        OnPropertyChanged(nameof(MaskedPhoneNumber));
        OnPropertyChanged(nameof(DevelopmentHint));
        OnPropertyChanged(nameof(HasDevelopmentHint));
        OnPropertyChanged(nameof(ConfirmButtonText));
        StartResendCountdown();
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
            var result = await authentication.VerifyCodeAsync(
                request.ChallengeId,
                Code,
                request.Mode);
            switch (result)
            {
                case SessionVerificationResult:
                    await pushRegistration.InitializeAsync();
                    await Shell.Current.GoToAsync("//transactions");
                    await deepLinks.ResumePendingAsync();
                    break;
                case RegistrationRequiredVerificationResult:
                    await Shell.Current.GoToAsync(
                        AuthenticationRoutes
                            .CompleteRegistration);
                    break;
                default:
                    throw new InvalidOperationException(
                        "ผลการยืนยันเบอร์ไม่ถูกต้อง");
            }
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
        if (!CanResend)
            return;

        IsBusy = true;
        Message = "";
        try
        {
            var challenge = await authentication.RequestCodeAsync(
                request.PhoneNumber,
                request.Mode);
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
            StartResendCountdown();
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

    private void StartResendCountdown()
    {
        resendCountdown?.Cancel();
        resendCountdown?.Dispose();
        resendCountdown = new CancellationTokenSource();
        _ = CountDownAsync(resendCountdown.Token);
    }

    private async Task CountDownAsync(
        CancellationToken cancellationToken)
    {
        resendSecondsRemaining = 30;
        NotifyResendState();
        try
        {
            while (resendSecondsRemaining > 0)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(1),
                    cancellationToken);
                resendSecondsRemaining--;
                NotifyResendState();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void NotifyResendState()
    {
        OnPropertyChanged(nameof(ResendButtonText));
        OnPropertyChanged(nameof(CanResend));
    }
}
