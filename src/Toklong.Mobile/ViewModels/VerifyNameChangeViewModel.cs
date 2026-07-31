using System.Windows.Input;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.ViewModels;

public sealed class VerifyNameChangeViewModel : ObservableViewModel, IDisposable
{
    private static readonly TimeSpan LocalResendCooldown =
        TimeSpan.FromSeconds(1);
    private readonly IAuthenticationService authentication;
    private readonly IMobileAnalytics analytics;
    private readonly TimeProvider timeProvider;
    private readonly AuthenticatedSessionBoundary session;
    private readonly EmailChangePageLifetime lifetime;
    private readonly AccountNameChangeCompletionState completion;
    private PendingAccountNameChange? pending;
    private string code = "";
    private string message = "";
    private int resendSecondsRemaining;
    private int expirySecondsRemaining;
    private int remainingAttempts;
    private bool isBusy;
    private bool isActive;
    private bool isExpired;
    private bool isLocked;
    private bool requiresFreshRequest;
    private bool requiresAccountReturn;
    private bool isVerified;
    private CancellationTokenSource? countdown;
    private bool disposed;
    private bool mustReloadPending;

    public VerifyNameChangeViewModel(
        IAuthenticationService authentication,
        IMobileAnalytics analytics,
        TimeProvider timeProvider,
        AuthenticatedSessionBoundary session,
        AccountNameChangeCompletionState completion)
    {
        this.authentication = authentication;
        this.analytics = analytics;
        this.timeProvider = timeProvider;
        this.session = session;
        this.completion = completion;
        lifetime = new EmailChangePageLifetime(session);
        session.ResetRequested += OnSessionResetRequested;
    }

    public event EventHandler<AccountNameChangeErrorNotice>?
        ErrorPresented;
    public event EventHandler<AccountNameChangeModalNotice>?
        ActionBlocked;

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
            SetProperty(ref code, normalized);
        }
    }

    public string MaskedPhoneNumber =>
        pending?.MaskedPhoneNumber ?? "";

    public string MaskedPhoneSemanticDescription =>
        string.IsNullOrWhiteSpace(MaskedPhoneNumber)
            ? "ไม่พบเบอร์โทรศัพท์ปลายทาง"
            : $"ส่งรหัสยืนยันไปยังเบอร์ {MaskedPhoneNumber}";

    public string PendingDisplayName =>
        pending is null
            ? ""
            : $"{pending.FirstName} {pending.LastName}";

    public string PendingNameSemanticDescription =>
        string.IsNullOrWhiteSpace(PendingDisplayName)
            ? "ไม่พบชื่อใหม่"
            : $"ชื่อใหม่ {PendingDisplayName}";

    public string ExpiresText =>
        pending is null
            ? ""
            : pending.ExpiresAt.ToLocalTime()
                .ToString("dd/MM/yyyy HH:mm");

    public int ResendSecondsRemaining
    {
        get => resendSecondsRemaining;
        private set
        {
            if (!SetProperty(ref resendSecondsRemaining, value))
                return;
            OnPropertyChanged(nameof(CanResend));
            OnPropertyChanged(nameof(ResendButtonText));
            OnPropertyChanged(nameof(ResendSemanticDescription));
        }
    }

    public int ExpirySecondsRemaining
    {
        get => expirySecondsRemaining;
        private set => SetProperty(ref expirySecondsRemaining, value);
    }

    public int RemainingAttempts
    {
        get => remainingAttempts;
        private set => SetProperty(ref remainingAttempts, value);
    }

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
        (IsExpired || IsLocked || requiresFreshRequest);

    public bool CanUseChallenge =>
        pending is not null &&
        !RequiresNewRequest &&
        !RequiresAccountReturn &&
        !isVerified;

    public bool CanConfirm =>
        CanUseChallenge && !IsBusy;

    public bool CanResend =>
        CanUseChallenge &&
        ResendSecondsRemaining == 0 &&
        !IsBusy;

    public bool RequiresAccountReturn => requiresAccountReturn;

    public bool CanReturnToAccount => RequiresAccountReturn;

    public string ResendButtonText =>
        ResendSecondsRemaining > 0
            ? $"ส่งรหัสใหม่ได้ใน {ResendSecondsRemaining} วินาที"
            : "ส่งรหัสใหม่";

    public string ResendSemanticDescription =>
        ResendSecondsRemaining > 0
            ? $"ขอรหัสใหม่ได้ในอีก {ResendSecondsRemaining} วินาที"
            : "ขอรหัสยืนยันการเปลี่ยนชื่อใหม่";

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
            if (!SetProperty(ref isBusy, value))
                return;
            OnPropertyChanged(nameof(CanConfirm));
            OnPropertyChanged(nameof(CanResend));
        }
    }

    public ICommand ConfirmCommand => new AsyncCommand(ConfirmAsync);
    public ICommand ResendCommand => new AsyncCommand(ResendAsync);
    public ICommand StartNewRequestCommand =>
        new AsyncCommand(StartNewRequestAsync);
    public ICommand ReturnToAccountCommand =>
        new AsyncCommand(ReturnToAccountAsync);

    public void Apply(PendingAccountNameChange value)
    {
        if (mustReloadPending)
            return;

        pending = value;
        RemainingAttempts = value.RemainingAttempts;
        IsExpired = false;
        IsLocked = value.RemainingAttempts <= 0;
        SetRequiresFreshRequest(false);
        SetVerified(false);
        SetRequiresAccountReturn(false);
        Code = "";
        Message = "";
        OnPropertyChanged(nameof(MaskedPhoneNumber));
        OnPropertyChanged(nameof(MaskedPhoneSemanticDescription));
        OnPropertyChanged(nameof(PendingDisplayName));
        OnPropertyChanged(nameof(PendingNameSemanticDescription));
        OnPropertyChanged(nameof(ExpiresText));
        RestartCountdown();
        RaiseActionState();
    }

    public void Activate()
    {
        lifetime.Activate();
        isActive = true;
        RestartCountdown();
        if (HasMessage && (RequiresNewRequest || RequiresAccountReturn))
        {
            PresentError(
                RequiresNewRequest
                    ? AccountNameChangeErrorTarget.NewRequestAction
                    : AccountNameChangeErrorTarget.AccountReturnAction,
                Message,
                RequiresNewRequest
                    ? IsLocked
                        ? AccountNameChangeErrorKind.Locked
                        : AccountNameChangeErrorKind.Expired
                    : AccountNameChangeErrorKind.Missing);
        }
    }

    public async Task LoadPendingAfterResetAsync()
    {
        if (!mustReloadPending)
            return;

        var operation = lifetime.Capture();
        if (operation is null)
            return;

        try
        {
            var current = await authentication
                .GetPendingAccountNameChangeAsync(operation.Value.Token);
            if (!lifetime.IsCurrent(operation.Value))
                return;

            mustReloadPending = false;
            if (current is not null)
            {
                Apply(current);
                return;
            }

            SetRequiresAccountReturn(true);
        }
        catch (OperationCanceledException) when (
            operation.Value.Token.IsCancellationRequested)
        {
        }
        catch
        {
            if (!lifetime.IsCurrent(operation.Value))
                return;

            Message =
                "โหลดคำขอเปลี่ยนชื่อไม่สำเร็จ กรุณากลับไปหน้าบัญชี";
            SetRequiresAccountReturn(true);
            PresentError(
                AccountNameChangeErrorTarget.AccountReturnAction,
                Message,
                AccountNameChangeErrorKind.Network);
        }
    }

    public void Deactivate()
    {
        lifetime.Deactivate();
        isActive = false;
        StopCountdown();
        IsBusy = false;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        session.ResetRequested -= OnSessionResetRequested;
        lifetime.Deactivate();
        StopCountdown();
    }

    public async Task ConfirmAsync()
    {
        var operation = lifetime.Capture();
        if (operation is null || pending is null || !CanConfirm)
            return;
        if (Code.Length != 6)
        {
            Message = "กรอกรหัสยืนยัน 6 หลัก";
            PresentError(
                AccountNameChangeErrorTarget.CodeInput,
                Message,
                AccountNameChangeErrorKind.Invalid);
            analytics.Track(AccountNameChangeAnalytics.Failed(
                AccountNameChangeFailureReason.Invalid));
            return;
        }

        IsBusy = true;
        Message = "";
        try
        {
            VerifiedAccountNameChange verified;
            try
            {
                verified = await authentication
                    .VerifyAccountNameChangeAsync(
                        pending.ChallengeId,
                        Code,
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

                var error = AccountNameChangeErrorPresentation
                    .ForVerification(exception);
                if (!TryHandleAccountCooldown(error))
                    ApplyChallengeError(error);
                analytics.Track(AccountNameChangeAnalytics.Failed(
                    FailureReason(error.Kind)));
                return;
            }

            if (!lifetime.IsCurrent(operation.Value))
                return;

            ApplyVerificationSuccess();
            if (!lifetime.IsCurrent(operation.Value))
                return;

            completion.RecordCompletion(
                operation.Value.SessionGeneration);
            if (!lifetime.IsCurrent(operation.Value))
                return;

            analytics.Track(AccountNameChangeAnalytics.Verified());

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
                // The account page performs the authoritative refresh.
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
                    "บันทึกชื่อสำเร็จแล้ว กรุณากลับไปหน้าบัญชีเพื่อตรวจสอบชื่อใหม่";
                PresentError(
                    AccountNameChangeErrorTarget.AccountReturnAction,
                    Message,
                    AccountNameChangeErrorKind.Missing);
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
        if (operation is null || pending is null || !CanResend)
            return;

        IsBusy = true;
        Message = "";
        try
        {
            try
            {
                var replacement = await authentication
                    .ResendAccountNameChangeAsync(
                        pending.ChallengeId,
                        operation.Value.Token);
                if (!lifetime.IsCurrent(operation.Value))
                    return;

                Apply(replacement);
                analytics.Track(AccountNameChangeAnalytics.CodeResent());
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

                var error = AccountNameChangeErrorPresentation
                    .ForResend(exception);
                if (!TryHandleAccountCooldown(error))
                {
                    if (error.Kind is
                        AccountNameChangeErrorKind.SendLimit or
                        AccountNameChangeErrorKind.RateLimited)
                    {
                        Message = "";
                        ActionBlocked?.Invoke(
                            this,
                            AccountNameChangeModalPresenter.SendLimit(error));
                    }
                    else
                    {
                        ApplyChallengeError(error);
                    }
                }
                analytics.Track(AccountNameChangeAnalytics.Failed(
                    FailureReason(error.Kind)));
            }
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
        if (operation is null || !RequiresNewRequest ||
            !lifetime.IsCurrent(operation.Value))
            return;

        try
        {
            await Shell.Current.GoToAsync(
                nameof(Pages.ChangeNamePage));
        }
        catch
        {
            if (!lifetime.IsCurrent(operation.Value))
                return;

            Message =
                "เปิดหน้าแก้ไขชื่อไม่สำเร็จ กรุณาลองอีกครั้ง";
            PresentError(
                AccountNameChangeErrorTarget.NewRequestAction,
                Message,
                AccountNameChangeErrorKind.Network);
        }
    }

    public async Task ReturnToAccountAsync()
    {
        var operation = lifetime.Capture();
        if (operation is null || !CanReturnToAccount ||
            !lifetime.IsCurrent(operation.Value))
            return;

        try
        {
            await NavigateToAccountAsync();
            if (lifetime.IsCurrent(operation.Value))
                SetRequiresAccountReturn(false);
        }
        catch
        {
            if (!lifetime.IsCurrent(operation.Value))
                return;

            Message =
                "บันทึกชื่อสำเร็จแล้ว กรุณากลับไปหน้าบัญชีเพื่อตรวจสอบชื่อใหม่";
            PresentError(
                AccountNameChangeErrorTarget.AccountReturnAction,
                Message,
                AccountNameChangeErrorKind.Missing);
        }
    }

    private void RefreshTemporalState()
    {
        var now = timeProvider.GetUtcNow();
        var resendRemaining = pending is null
            ? TimeSpan.Zero
            : pending.ResendAvailableAt - now;
        ResendSecondsRemaining = resendRemaining <= TimeSpan.Zero
            ? 0
            : (int)Math.Ceiling(resendRemaining.TotalSeconds);

        var expiryRemaining = pending is null
            ? TimeSpan.Zero
            : pending.ExpiresAt - now;
        ExpirySecondsRemaining = expiryRemaining <= TimeSpan.Zero
            ? 0
            : (int)Math.Ceiling(expiryRemaining.TotalSeconds);
        if (pending is not null &&
            !IsExpired &&
            !IsLocked &&
            !isVerified &&
            ExpirySecondsRemaining == 0)
        {
            IsExpired = true;
            Message = "รหัสยืนยันหมดอายุแล้ว กรุณาขอรหัสใหม่";
            if (isActive)
            {
                PresentError(
                    AccountNameChangeErrorTarget.NewRequestAction,
                    Message,
                    AccountNameChangeErrorKind.Expired);
            }
        }
    }

    private void RestartCountdown()
    {
        StopCountdown();
        RefreshTemporalState();
        if (!isActive || pending is null || !CanUseChallenge)
            return;

        countdown = new CancellationTokenSource();
        _ = CountDownAsync(countdown.Token);
    }

    private async Task CountDownAsync(CancellationToken cancellationToken)
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

    private void ApplyChallengeError(AccountNameChangeErrorNotice error)
    {
        if (error.RemainingAttempts is { } attempts)
        {
            RemainingAttempts = Math.Max(0, attempts);
            if (RemainingAttempts == 0)
                IsLocked = true;
        }

        if (error.Kind == AccountNameChangeErrorKind.Expired)
            IsExpired = true;
        else if (error.Kind == AccountNameChangeErrorKind.Locked)
            IsLocked = true;
        else if (error.Kind == AccountNameChangeErrorKind.Missing)
            SetRequiresAccountReturn(true);
        else if (error.Kind == AccountNameChangeErrorKind.Cooldown &&
                 pending is not null)
        {
            var retryAt = timeProvider.GetUtcNow() +
                (error.RetryAfter ?? LocalResendCooldown);
            if (pending.ResendAvailableAt > retryAt)
                retryAt = pending.ResendAvailableAt;
            pending = pending with { ResendAvailableAt = retryAt };
            RestartCountdown();
        }

        Message = error.RemainingAttempts is > 0 &&
                  error.Target == AccountNameChangeErrorTarget.CodeInput
            ? $"{error.Message} เหลือ {error.RemainingAttempts} ครั้ง"
            : error.Message;
        PresentError(
            error.Target,
            Message,
            error.Kind,
            error.RetryAfter,
            error.RemainingAttempts,
            error.NextAllowedAt);
    }

    private bool TryHandleAccountCooldown(
        AccountNameChangeErrorNotice error)
    {
        if (error.Kind != AccountNameChangeErrorKind.Cooldown ||
            error.Target != AccountNameChangeErrorTarget.BlockedAction ||
            error.NextAllowedAt is not { } nextAllowedAt)
        {
            return false;
        }

        StopCountdown();
        pending = null;
        Code = "";
        Message = "";
        ResendSecondsRemaining = 0;
        ExpirySecondsRemaining = 0;
        RemainingAttempts = 0;
        IsExpired = false;
        IsLocked = false;
        SetRequiresFreshRequest(false);
        SetVerified(false);
        SetRequiresAccountReturn(true);
        OnPropertyChanged(nameof(MaskedPhoneNumber));
        OnPropertyChanged(nameof(MaskedPhoneSemanticDescription));
        OnPropertyChanged(nameof(PendingDisplayName));
        OnPropertyChanged(nameof(PendingNameSemanticDescription));
        OnPropertyChanged(nameof(ExpiresText));
        RaiseActionState();
        ActionBlocked?.Invoke(
            this,
            AccountNameChangeModalPresenter.Cooldown(
                new AccountNameChangeBlockedNotice(nextAllowedAt)));
        return true;
    }

    private void OnSessionResetRequested(object? sender, EventArgs eventArgs)
    {
        lifetime.Deactivate();
        isActive = false;
        StopCountdown();
        pending = null;
        mustReloadPending = true;
        Code = "";
        Message = "";
        ResendSecondsRemaining = 0;
        ExpirySecondsRemaining = 0;
        RemainingAttempts = 0;
        IsBusy = false;
        IsExpired = false;
        IsLocked = false;
        SetRequiresFreshRequest(false);
        SetRequiresAccountReturn(false);
        SetVerified(false);
        OnPropertyChanged(nameof(MaskedPhoneNumber));
        OnPropertyChanged(nameof(MaskedPhoneSemanticDescription));
        OnPropertyChanged(nameof(PendingDisplayName));
        OnPropertyChanged(nameof(PendingNameSemanticDescription));
        OnPropertyChanged(nameof(ExpiresText));
        RaiseActionState();
    }

    private void ApplyVerificationSuccess()
    {
        SetVerified(true);
        IsExpired = false;
        IsLocked = false;
        SetRequiresFreshRequest(false);
        SetRequiresAccountReturn(false);
        Message = "";
        StopCountdown();
    }

    private void SetRequiresFreshRequest(bool value)
    {
        if (requiresFreshRequest == value)
            return;
        requiresFreshRequest = value;
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
        if (!SetProperty(ref requiresAccountReturn, value,
                nameof(RequiresAccountReturn)))
            return;
        RaiseActionState();
    }

    private void RaiseActionState()
    {
        OnPropertyChanged(nameof(RequiresNewRequest));
        OnPropertyChanged(nameof(CanUseChallenge));
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(CanResend));
        OnPropertyChanged(nameof(CanReturnToAccount));
    }

    private void PresentError(
        AccountNameChangeErrorTarget target,
        string value,
        AccountNameChangeErrorKind kind,
        TimeSpan? retryAfter = null,
        int? remainingAttempts = null,
        DateTimeOffset? nextAllowedAt = null) =>
        ErrorPresented?.Invoke(
            this,
            new AccountNameChangeErrorNotice(
                kind,
                target,
                value,
                retryAfter,
                remainingAttempts,
                nextAllowedAt));

    private static Task NavigateToAccountAsync() =>
        Shell.Current.GoToAsync("//main/account");

    private static AccountNameChangeFailureReason FailureReason(
        AccountNameChangeErrorKind kind) =>
        kind switch
        {
            AccountNameChangeErrorKind.Cooldown =>
                AccountNameChangeFailureReason.Cooldown,
            AccountNameChangeErrorKind.SendLimit or
            AccountNameChangeErrorKind.RateLimited =>
                AccountNameChangeFailureReason.SendLimit,
            AccountNameChangeErrorKind.Expired =>
                AccountNameChangeFailureReason.Expired,
            AccountNameChangeErrorKind.Locked =>
                AccountNameChangeFailureReason.Locked,
            AccountNameChangeErrorKind.Network =>
                AccountNameChangeFailureReason.Network,
            AccountNameChangeErrorKind.Unavailable =>
                AccountNameChangeFailureReason.Provider,
            _ => AccountNameChangeFailureReason.Invalid
        };
}
