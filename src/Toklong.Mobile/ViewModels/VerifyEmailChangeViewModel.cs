using System.Windows.Input;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.ViewModels;

public sealed class VerifyEmailChangeViewModel(
    IAuthenticationService authentication,
    IMobileAnalytics analytics,
    TimeProvider timeProvider) : ObservableViewModel
{
    private PendingEmailChange? pending;
    private string code = "";
    private string message = "";
    private string? verificationIdempotencyKey;
    private string? resendIdempotencyKey;
    private int resendSecondsRemaining;
    private bool isBusy;
    private bool isActive;
    private CancellationTokenSource? countdown;

    public string Code
    {
        get => code;
        set
        {
            var normalized = new string(
                (value ?? "")
                .Where(char.IsAsciiDigit)
                .Take(6)
                .ToArray());
            if (!SetProperty(ref code, normalized))
                return;
            verificationIdempotencyKey = null;
        }
    }

    public string MaskedEmail =>
        pending?.MaskedEmail ?? "";

    public string MaskedEmailSemanticDescription =>
        string.IsNullOrWhiteSpace(MaskedEmail)
            ? "ไม่พบอีเมลปลายทาง"
            : $"ส่งรหัสยืนยันไปที่อีเมล {MaskedEmail}";

    public string ExpiresText =>
        pending is null
            ? ""
            : pending.ExpiresAt
                .ToLocalTime()
                .ToString("dd/MM/yyyy HH:mm");

    public int ResendSecondsRemaining
    {
        get => resendSecondsRemaining;
        private set
        {
            if (!SetProperty(
                    ref resendSecondsRemaining,
                    value))
                return;
            OnPropertyChanged(nameof(CanResend));
            OnPropertyChanged(nameof(ResendButtonText));
            OnPropertyChanged(
                nameof(ResendSemanticDescription));
        }
    }

    public bool CanResend =>
        pending is not null &&
        ResendSecondsRemaining == 0 &&
        !IsBusy;

    public string ResendButtonText =>
        ResendSecondsRemaining > 0
            ? $"ส่งรหัสใหม่ได้ใน {ResendSecondsRemaining} วินาที"
            : "ส่งรหัสใหม่";

    public string ResendSemanticDescription =>
        ResendSecondsRemaining > 0
            ? $"ขอรหัสใหม่ได้ในอีก {ResendSecondsRemaining} วินาที"
            : "ขอรหัสยืนยันอีเมลใหม่";

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
        private set
        {
            if (SetProperty(ref isBusy, value))
                OnPropertyChanged(nameof(CanResend));
        }
    }

    public ICommand ConfirmCommand =>
        new AsyncCommand(ConfirmAsync);
    public ICommand ResendCommand =>
        new AsyncCommand(ResendAsync);

    public void Apply(PendingEmailChange value)
    {
        pending = value;
        verificationIdempotencyKey = null;
        resendIdempotencyKey = null;
        Code = "";
        Message = "";
        OnPropertyChanged(nameof(MaskedEmail));
        OnPropertyChanged(
            nameof(MaskedEmailSemanticDescription));
        OnPropertyChanged(nameof(ExpiresText));
        RestartCountdown();
    }

    public void Activate()
    {
        isActive = true;
        RestartCountdown();
    }

    public void Deactivate()
    {
        isActive = false;
        StopCountdown();
    }

    public void RefreshCountdown()
    {
        var remaining = pending is null
            ? TimeSpan.Zero
            : pending.ResendAvailableAt -
              timeProvider.GetUtcNow();
        ResendSecondsRemaining = remaining <= TimeSpan.Zero
            ? 0
            : (int)Math.Ceiling(
                remaining.TotalSeconds);
        OnPropertyChanged(nameof(CanResend));
        OnPropertyChanged(nameof(ResendButtonText));
        OnPropertyChanged(
            nameof(ResendSemanticDescription));
    }

    public async Task ConfirmAsync()
    {
        if (pending is null || IsBusy)
            return;
        if (Code.Length != 6)
        {
            Message = "กรอกรหัสยืนยัน 6 หลัก";
            analytics.Track(
                AccountEmailChangeAnalytics.Failed(
                    AccountEmailChangeFailureReason.Invalid));
            return;
        }

        verificationIdempotencyKey ??=
            Guid.NewGuid().ToString("N");
        IsBusy = true;
        Message = "";
        try
        {
            await authentication.VerifyEmailChangeAsync(
                pending.ChallengeId,
                Code,
                verificationIdempotencyKey);
            await authentication.GetProfileAsync();
            verificationIdempotencyKey = null;
            analytics.Track(
                AccountEmailChangeAnalytics.Verified());
            await Shell.Current.GoToAsync("//main/account");
        }
        catch (Exception exception)
        {
            var error =
                AccountEmailChangeErrorPresentation
                    .ForVerification(exception);
            Message = error.Message;
            analytics.Track(
                AccountEmailChangeAnalytics.Failed(
                    error.Reason));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ResendAsync()
    {
        if (pending is null ||
            IsBusy ||
            !CanResend)
            return;

        resendIdempotencyKey ??=
            Guid.NewGuid().ToString("N");
        IsBusy = true;
        Message = "";
        try
        {
            var replacement =
                await authentication.ResendEmailChangeAsync(
                    pending.ChallengeId,
                    resendIdempotencyKey);
            pending = replacement;
            resendIdempotencyKey = null;
            Code = "";
            OnPropertyChanged(nameof(MaskedEmail));
            OnPropertyChanged(
                nameof(MaskedEmailSemanticDescription));
            OnPropertyChanged(nameof(ExpiresText));
            RestartCountdown();
            analytics.Track(
                AccountEmailChangeAnalytics.CodeResent());
        }
        catch (Exception exception)
        {
            var error =
                AccountEmailChangeErrorPresentation
                    .ForVerification(exception);
            Message = error.Message;
            analytics.Track(
                AccountEmailChangeAnalytics.Failed(
                    error.Reason));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RestartCountdown()
    {
        StopCountdown();
        RefreshCountdown();
        if (!isActive ||
            pending is null ||
            ResendSecondsRemaining == 0)
        {
            return;
        }

        countdown = new CancellationTokenSource();
        _ = CountDownAsync(countdown.Token);
    }

    private async Task CountDownAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                RefreshCountdown();
                if (ResendSecondsRemaining == 0)
                    return;
                await Task.Delay(
                    TimeSpan.FromSeconds(1),
                    timeProvider,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void StopCountdown()
    {
        countdown?.Cancel();
        countdown?.Dispose();
        countdown = null;
    }
}
