using System.Windows.Input;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.ViewModels;

public sealed class VerifyEmailChangeViewModel(
    IAuthenticationService authentication,
    IMobileAnalytics analytics,
    TimeProvider timeProvider,
    AuthenticatedSessionBoundary session,
    AccountEmailChangeCompletionState
        emailChangeCompletion) : ObservableViewModel
{
    private static readonly TimeSpan
        LocalResendCooldown =
            TimeSpan.FromSeconds(1);

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
    private AccountRecovery accountRecovery;
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
        !isVerified &&
        (IsExpired ||
         IsLocked);

    public bool RequiresPendingRefresh =>
        accountRecovery != AccountRecovery.None;

    public bool CanUseChallenge =>
        pending is not null &&
        !RequiresNewRequest &&
        !RequiresPendingRefresh &&
        !isVerified;

    public bool CanConfirm =>
        CanUseChallenge &&
        !IsBusy;

    public bool RequiresAccountReturn =>
        requiresAccountReturn;

    public bool CanReturnToAccount =>
        RequiresAccountReturn ||
        RequiresPendingRefresh;

    public string AccountReturnButtonText =>
        accountRecovery ==
        AccountRecovery.LatestPending
            ? "กลับไปยืนยันรหัสล่าสุด"
            : "กลับไปหน้าบัญชี";

    public string AccountReturnSemanticDescription =>
        accountRecovery ==
        AccountRecovery.LatestPending
            ? "กลับไปหน้าบัญชีเพื่อยืนยันรหัสล่าสุด"
            : RequiresPendingRefresh
                ? "กลับไปหน้าบัญชีเพื่อตรวจสอบข้อมูลล่าสุด"
                : "กลับไปหน้าบัญชีเพื่อตรวจสอบอีเมลล่าสุด";

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
        SetAccountRecovery(AccountRecovery.None);
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
        if (HasMessage &&
            (RequiresNewRequest ||
             RequiresPendingRefresh ||
             RequiresAccountReturn))
        {
            PresentError(
                RequiresNewRequest
                    ? EmailChangeErrorTarget
                        .NewRequestAction
                    : EmailChangeErrorTarget
                        .AccountReturnAction,
                Message);
        }
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
            !IsLocked &&
            !RequiresPendingRefresh &&
            !isVerified &&
            ExpirySecondsRemaining == 0;
        if (becameExpired)
        {
            IsExpired = true;
            Message =
                "รหัสหมดอายุแล้ว กรุณาขอรหัสใหม่";
            if (isActive)
            {
                PresentError(
                    EmailChangeErrorTarget.NewRequestAction,
                    Message);
            }
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

                RefreshTemporalState();
                if (RequiresNewRequest)
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
                        : error.RequiresPendingRefresh
                            ? EmailChangeErrorTarget
                                .AccountReturnAction
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
            ApplyVerificationSuccess();
            emailChangeCompletion.RecordCompletion();
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
                await NavigateToAccountAsync();
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
            SetAccountRecovery(AccountRecovery.None);
            SetVerified(false);
            SetRequiresAccountReturn(false);
            Code = "";
            Message = "";
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

            RefreshTemporalState();
            if (RequiresNewRequest)
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
                    : error.RequiresPendingRefresh
                        ? EmailChangeErrorTarget
                            .AccountReturnAction
                    : EmailChangeErrorTarget.ResendAction,
                Message);
            if (error.Kind ==
                AccountEmailChangeErrorKind.Cooldown)
            {
                ApplyResendCooldown(error);
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
            !CanReturnToAccount ||
            !lifetime.IsCurrent(operation.Value))
        {
            return;
        }

        var isPendingRefresh = RequiresPendingRefresh;
        try
        {
            await NavigateToAccountAsync();
            if (lifetime.IsCurrent(operation.Value) &&
                !isPendingRefresh)
            {
                SetRequiresAccountReturn(false);
            }
        }
        catch
        {
            if (lifetime.IsCurrent(operation.Value))
            {
                if (!isPendingRefresh)
                {
                    Message =
                        "ยืนยันอีเมลสำเร็จแล้ว กรุณากลับไปหน้าบัญชีเพื่อตรวจสอบอีเมลล่าสุด";
                }
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
            !CanUseChallenge)
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
                if (!CanUseChallenge)
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

    private void ApplyResendCooldown(
        AccountEmailChangeError error)
    {
        if (pending is null)
            return;

        var retryAt =
            timeProvider.GetUtcNow() +
            (error.RetryAfter ??
             LocalResendCooldown);
        if (pending.ResendAvailableAt > retryAt)
            retryAt = pending.ResendAvailableAt;

        pending = pending with
        {
            ResendAvailableAt = retryAt
        };
        RestartCountdown();
    }

    private void ApplyVerificationSuccess()
    {
        SetVerified(true);
        IsExpired = false;
        IsLocked = false;
        SetAccountRecovery(AccountRecovery.None);
        SetRequiresAccountReturn(false);
        Message = "";
        StopCountdown();
    }

    private void ApplyChallengeError(
        AccountEmailChangeError error)
    {
        if (error.Kind ==
            AccountEmailChangeErrorKind.Expired)
        {
            IsExpired = true;
        }
        else if (error.Kind ==
                 AccountEmailChangeErrorKind.Locked)
        {
            IsLocked = true;
        }
        else if (error.Kind ==
                 AccountEmailChangeErrorKind.Superseded)
        {
            SetAccountRecovery(
                AccountRecovery.LatestPending);
        }
        else if (error.Kind ==
                 AccountEmailChangeErrorKind.Missing)
        {
            SetAccountRecovery(
                AccountRecovery.Account);
        }
    }

    private void SetAccountRecovery(
        AccountRecovery value)
    {
        if (accountRecovery == value)
            return;

        accountRecovery = value;
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
        OnPropertyChanged(nameof(CanReturnToAccount));
        OnPropertyChanged(
            nameof(AccountReturnButtonText));
        OnPropertyChanged(
            nameof(AccountReturnSemanticDescription));
    }

    private void RaiseActionState()
    {
        OnPropertyChanged(nameof(RequiresNewRequest));
        OnPropertyChanged(nameof(RequiresPendingRefresh));
        OnPropertyChanged(nameof(CanUseChallenge));
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(CanResend));
        OnPropertyChanged(nameof(CanReturnToAccount));
        OnPropertyChanged(
            nameof(AccountReturnButtonText));
        OnPropertyChanged(
            nameof(AccountReturnSemanticDescription));
    }

    private void PresentError(
        EmailChangeErrorTarget target,
        string value) =>
        ErrorPresented?.Invoke(
            this,
            new EmailChangeErrorNotice(
                target,
                value));

    private static Task NavigateToAccountAsync() =>
        Shell.Current.GoToAsync("//main/account");

    private enum AccountRecovery
    {
        None,
        LatestPending,
        Account
    }
}
