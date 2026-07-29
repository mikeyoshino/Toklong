using System.Windows.Input;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.ViewModels;

public sealed class VerifyEmailChangeViewModel(
    IAuthenticationService authentication,
    IMobileAnalytics analytics,
    TimeProvider timeProvider,
    AuthenticatedSessionBoundary session) : ObservableViewModel
{
    private readonly EmailChangePageLifetime lifetime =
        new(session);
    private PendingEmailChange? pending;
    private string code = "";
    private string message = "";
    private string? verificationIdempotencyKey;
    private string? resendIdempotencyKey;
    private int resendSecondsRemaining;
    private int expirySecondsRemaining;
    private bool isBusy;
    private bool isActive;
    private bool isExpired;
    private bool isLocked;
    private bool isObsolete;
    private bool isVerified;
    private bool requiresAccountReturn;
    private CancellationTokenSource? countdown;

    public event EventHandler<EmailChangeErrorNotice>?
        ErrorPresented;

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

    public int ExpirySecondsRemaining
    {
        get => expirySecondsRemaining;
        private set => SetProperty(
            ref expirySecondsRemaining,
            value);
    }

    public bool CanResend =>
        CanUseChallenge &&
        ResendSecondsRemaining == 0 &&
        !IsBusy;

    public bool IsExpired
    {
        get => isExpired;
        private set
        {
            if (SetProperty(ref isExpired, value))
                RaiseActionState();
        }
    }

    public bool IsLocked
    {
        get => isLocked;
        private set
        {
            if (SetProperty(ref isLocked, value))
                RaiseActionState();
        }
    }

    public bool RequiresNewRequest =>
        IsExpired ||
        IsLocked ||
        isObsolete;

    public bool CanUseChallenge =>
        pending is not null &&
        !RequiresNewRequest &&
        !isVerified;

    public bool CanConfirm =>
        CanUseChallenge &&
        !IsBusy;

    public bool RequiresAccountReturn =>
        requiresAccountReturn;

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
            {
                OnPropertyChanged(nameof(CanResend));
                OnPropertyChanged(nameof(CanConfirm));
            }
        }
    }

    public ICommand ConfirmCommand =>
        new AsyncCommand(ConfirmAsync);
    public ICommand ResendCommand =>
        new AsyncCommand(ResendAsync);
    public ICommand StartNewRequestCommand =>
        new AsyncCommand(StartNewRequestAsync);
    public ICommand ReturnToAccountCommand =>
        new AsyncCommand(ReturnToAccountAsync);

    public void Apply(PendingEmailChange value)
    {
        pending = value;
        verificationIdempotencyKey = null;
        resendIdempotencyKey = null;
        IsExpired = false;
        IsLocked = value.RemainingAttempts <= 0;
        SetObsolete(false);
        SetVerified(false);
        SetRequiresAccountReturn(false);
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
        lifetime.Activate();
        isActive = true;
        RestartCountdown();
    }

    public void Deactivate()
    {
        lifetime.Deactivate();
        isActive = false;
        StopCountdown();
        IsBusy = false;
    }

    private void RefreshTemporalState()
    {
        var now = timeProvider.GetUtcNow();
        var remaining = pending is null
            ? TimeSpan.Zero
            : pending.ResendAvailableAt -
              now;
        ResendSecondsRemaining = remaining <= TimeSpan.Zero
            ? 0
            : (int)Math.Ceiling(
                remaining.TotalSeconds);

        var expiryRemaining = pending is null
            ? TimeSpan.Zero
            : pending.ExpiresAt - now;
        ExpirySecondsRemaining =
            expiryRemaining <= TimeSpan.Zero
                ? 0
                : (int)Math.Ceiling(
                    expiryRemaining.TotalSeconds);
        var becameExpired =
            pending is not null &&
            !IsExpired &&
            ExpirySecondsRemaining == 0;
        IsExpired =
            pending is not null &&
            ExpirySecondsRemaining == 0;
        if (becameExpired &&
            !IsLocked &&
            !isObsolete &&
            !isVerified)
        {
            Message =
                "รหัสหมดอายุแล้ว กรุณาเริ่มเปลี่ยนอีเมลใหม่";
            PresentError(
                EmailChangeErrorTarget.NewRequestAction,
                Message);
        }

        OnPropertyChanged(nameof(CanResend));
        OnPropertyChanged(nameof(ResendButtonText));
        OnPropertyChanged(
            nameof(ResendSemanticDescription));
    }

    public async Task ConfirmAsync()
    {
        var operation = lifetime.Capture();
        if (operation is null ||
            pending is null ||
            !CanConfirm)
            return;
        if (Code.Length != 6)
        {
            Message = "กรอกรหัสยืนยัน 6 หลัก";
            PresentError(
                EmailChangeErrorTarget.CodeInput,
                Message);
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
            try
            {
                await authentication.VerifyEmailChangeAsync(
                    pending.ChallengeId,
                    Code,
                    verificationIdempotencyKey,
                    operation.Value.Token);
            }
            catch (OperationCanceledException) when (
                operation.Value.Token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                if (!lifetime.IsCurrent(operation.Value))
                    return;

                var error =
                    AccountEmailChangeErrorPresentation
                        .ForVerification(exception);
                Message = error.Message;
                ApplyChallengeError(error);
                PresentError(
                    error.RequiresNewRequest
                        ? EmailChangeErrorTarget
                            .NewRequestAction
                        : EmailChangeErrorTarget.CodeInput,
                    Message);
                analytics.Track(
                    AccountEmailChangeAnalytics.Failed(
                        error.Reason));
                return;
            }

            if (!lifetime.IsCurrent(operation.Value))
                return;

            verificationIdempotencyKey = null;
            SetVerified(true);
            analytics.Track(
                AccountEmailChangeAnalytics.Verified());

            try
            {
                await authentication.GetProfileAsync(
                    operation.Value.Token);
            }
            catch (OperationCanceledException) when (
                operation.Value.Token.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // Account reload remains the recovery source of truth.
            }

            if (!lifetime.IsCurrent(operation.Value))
                return;

            try
            {
                await Shell.Current.GoToAsync(
                    "//main/account");
            }
            catch
            {
                if (!lifetime.IsCurrent(operation.Value))
                    return;

                SetRequiresAccountReturn(true);
                Message =
                    "ยืนยันอีเมลสำเร็จแล้ว กรุณากลับไปหน้าบัญชีเพื่อตรวจสอบอีเมลล่าสุด";
                PresentError(
                    EmailChangeErrorTarget
                        .AccountReturnAction,
                    Message);
            }
        }
        finally
        {
            if (lifetime.IsCurrent(operation.Value))
                IsBusy = false;
        }
    }

    public async Task ResendAsync()
    {
        var operation = lifetime.Capture();
        if (operation is null ||
            pending is null ||
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
                    resendIdempotencyKey,
                    operation.Value.Token);
            if (!lifetime.IsCurrent(operation.Value))
                return;

            pending = replacement;
            resendIdempotencyKey = null;
            IsExpired = false;
            IsLocked =
                replacement.RemainingAttempts <= 0;
            SetObsolete(false);
            SetVerified(false);
            SetRequiresAccountReturn(false);
            Code = "";
            OnPropertyChanged(nameof(MaskedEmail));
            OnPropertyChanged(
                nameof(MaskedEmailSemanticDescription));
            OnPropertyChanged(nameof(ExpiresText));
            RestartCountdown();
            analytics.Track(
                AccountEmailChangeAnalytics.CodeResent());
        }
        catch (OperationCanceledException) when (
            operation.Value.Token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!lifetime.IsCurrent(operation.Value))
                return;

            var error =
                AccountEmailChangeErrorPresentation
                    .ForResend(exception);
            Message = error.Message;
            ApplyChallengeError(error);
            PresentError(
                error.RequiresNewRequest
                    ? EmailChangeErrorTarget
                        .NewRequestAction
                    : EmailChangeErrorTarget.ResendAction,
                Message);
            if (error.RetryAfter is { } retryAfter &&
                pending is not null)
            {
                pending = pending with
                {
                    ResendAvailableAt =
                        timeProvider.GetUtcNow() +
                        retryAfter
                };
                RestartCountdown();
            }
            analytics.Track(
                AccountEmailChangeAnalytics.Failed(
                    error.Reason));
        }
        finally
        {
            if (lifetime.IsCurrent(operation.Value))
                IsBusy = false;
        }
    }

    public async Task StartNewRequestAsync()
    {
        var operation = lifetime.Capture();
        if (operation is null ||
            !RequiresNewRequest ||
            !lifetime.IsCurrent(operation.Value))
        {
            return;
        }

        await Shell.Current.GoToAsync(
            nameof(Pages.ChangeEmailPage));
    }

    public async Task ReturnToAccountAsync()
    {
        var operation = lifetime.Capture();
        if (operation is null ||
            !RequiresAccountReturn ||
            !lifetime.IsCurrent(operation.Value))
        {
            return;
        }

        try
        {
            await Shell.Current.GoToAsync(
                "//main/account");
            if (lifetime.IsCurrent(operation.Value))
                SetRequiresAccountReturn(false);
        }
        catch
        {
            if (lifetime.IsCurrent(operation.Value))
            {
                Message =
                    "ยืนยันอีเมลสำเร็จแล้ว กรุณากลับไปหน้าบัญชีเพื่อตรวจสอบอีเมลล่าสุด";
                PresentError(
                    EmailChangeErrorTarget
                        .AccountReturnAction,
                    Message);
            }
        }
    }

    private void RestartCountdown()
    {
        StopCountdown();
        RefreshTemporalState();
        if (!isActive ||
            pending is null ||
            RequiresNewRequest ||
            isVerified)
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
                RefreshTemporalState();
                if (RequiresNewRequest ||
                    isVerified)
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

    private void ApplyChallengeError(
        AccountEmailChangeError error)
    {
        IsExpired =
            error.Kind ==
            AccountEmailChangeErrorKind.Expired;
        IsLocked =
            error.Kind ==
            AccountEmailChangeErrorKind.Locked;
        if (error.Kind ==
            AccountEmailChangeErrorKind.Superseded)
        {
            SetObsolete(true);
        }
    }

    private void SetObsolete(bool value)
    {
        if (isObsolete == value)
            return;

        isObsolete = value;
        RaiseActionState();
    }

    private void SetVerified(bool value)
    {
        if (isVerified == value)
            return;

        isVerified = value;
        RaiseActionState();
    }

    private void SetRequiresAccountReturn(bool value)
    {
        if (requiresAccountReturn == value)
            return;

        requiresAccountReturn = value;
        OnPropertyChanged(nameof(RequiresAccountReturn));
    }

    private void RaiseActionState()
    {
        OnPropertyChanged(nameof(RequiresNewRequest));
        OnPropertyChanged(nameof(CanUseChallenge));
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(CanResend));
    }

    private void PresentError(
        EmailChangeErrorTarget target,
        string value) =>
        ErrorPresented?.Invoke(
            this,
            new EmailChangeErrorNotice(
                target,
                value));
}
