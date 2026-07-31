using System.Globalization;
using System.Text;
using System.Windows.Input;
using Toklong.Mobile.Core;
using Toklong.Mobile.Pages;

namespace Toklong.Mobile.ViewModels;

public sealed class ChangeNameViewModel : ObservableViewModel, IDisposable
{
    private readonly IAuthenticationService authentication;
    private readonly IMobileAnalytics analytics;
    private readonly AuthenticatedSessionBoundary session;
    private readonly EmailChangePageLifetime lifetime;
    private string currentFirstName = "";
    private string currentLastName = "";
    private string firstName = "";
    private string lastName = "";
    private string firstNameError = "";
    private string lastNameError = "";
    private string message = "";
    private PendingAccountNameChange? acceptedPending;
    private bool isBusy;
    private bool hasCurrentName;
    private bool disposed;

    public ChangeNameViewModel(
        IAuthenticationService authentication,
        IMobileAnalytics analytics,
        AuthenticatedSessionBoundary session)
    {
        this.authentication = authentication;
        this.analytics = analytics;
        this.session = session;
        lifetime = new EmailChangePageLifetime(session);
        session.ResetRequested += OnSessionResetRequested;
    }

    public event EventHandler<AccountNameChangeErrorNotice>?
        ErrorPresented;
    public event EventHandler<AccountNameChangeBlockedNotice>?
        NameChangeBlocked;
    public event EventHandler<AccountNameChangeModalNotice>?
        ActionBlocked;

    public string FirstName
    {
        get => firstName;
        set
        {
            if (!SetProperty(ref firstName, value ?? ""))
                return;
            FirstNameError = "";
            Message = "";
        }
    }

    public string LastName
    {
        get => lastName;
        set
        {
            if (!SetProperty(ref lastName, value ?? ""))
                return;
            LastNameError = "";
            Message = "";
        }
    }

    public string FirstNameError
    {
        get => firstNameError;
        private set
        {
            if (SetProperty(ref firstNameError, value))
                OnPropertyChanged(nameof(HasFirstNameError));
        }
    }

    public bool HasFirstNameError =>
        !string.IsNullOrWhiteSpace(FirstNameError);

    public string LastNameError
    {
        get => lastNameError;
        private set
        {
            if (SetProperty(ref lastNameError, value))
                OnPropertyChanged(nameof(HasLastNameError));
        }
    }

    public bool HasLastNameError =>
        !string.IsNullOrWhiteSpace(LastNameError);

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

    public bool CanEditName =>
        acceptedPending is null && !IsBusy;

    public string SubmitButtonText =>
        acceptedPending is null
            ? "ส่งรหัสยืนยัน"
            : "ไปกรอกรหัสยืนยัน";

    public string SubmitSemanticDescription =>
        acceptedPending is null
            ? "ส่งรหัสยืนยันไปยังเบอร์โทรศัพท์เดิม"
            : "ไปหน้ากรอกรหัสยืนยันที่ส่งแล้ว";

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
                OnPropertyChanged(nameof(CanEditName));
        }
    }

    public ICommand SubmitCommand => new AsyncCommand(SubmitAsync);

    public void ApplyCurrentName(string? currentFirstName, string? currentLastName)
    {
        if (hasCurrentName)
            return;

        this.currentFirstName = NormalizeWhitespace(currentFirstName ?? "");
        this.currentLastName = NormalizeWhitespace(currentLastName ?? "");
        hasCurrentName = true;
        FirstName = this.currentFirstName;
        LastName = this.currentLastName;
    }

    public void Activate() => lifetime.Activate();

    public async Task LoadCurrentNameAsync()
    {
        if (hasCurrentName)
            return;

        var operation = lifetime.Capture();
        if (operation is null)
            return;

        try
        {
            var profile = await authentication.GetProfileAsync(
                operation.Value.Token);
            if (!lifetime.IsCurrent(operation.Value))
                return;

            ApplyCurrentName(profile.FirstName, profile.LastName);
        }
        catch (OperationCanceledException) when (
            operation.Value.Token.IsCancellationRequested)
        {
        }
        catch
        {
            if (!lifetime.IsCurrent(operation.Value))
                return;

            Message = "โหลดชื่อบัญชีไม่สำเร็จ กรุณาลองอีกครั้ง";
            PresentError(
                AccountNameChangeErrorTarget.RequestAction,
                Message);
        }
    }

    public void Deactivate()
    {
        lifetime.Deactivate();
        IsBusy = false;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        session.ResetRequested -= OnSessionResetRequested;
        lifetime.Deactivate();
    }

    public async Task SubmitAsync()
    {
        var operation = lifetime.Capture();
        if (operation is null || IsBusy)
            return;

        if (acceptedPending is not null)
        {
            await NavigateToVerificationAsync(
                acceptedPending,
                operation.Value);
            return;
        }

        var validFirstName = TryNormalizePart(
            FirstName,
            "ชื่อ",
            out var normalizedFirstName,
            out var firstError);
        var validLastName = TryNormalizePart(
            LastName,
            "นามสกุล",
            out var normalizedLastName,
            out var lastError);
        FirstNameError = firstError;
        LastNameError = lastError;
        if (!validFirstName || !validLastName)
        {
            var target = !validFirstName
                ? AccountNameChangeErrorTarget.FirstNameInput
                : AccountNameChangeErrorTarget.LastNameInput;
            PresentError(
                target,
                !validFirstName ? firstError : lastError);
            analytics.Track(AccountNameChangeAnalytics.Failed(
                AccountNameChangeFailureReason.Invalid));
            return;
        }

        if (normalizedFirstName.Length + 1 +
            normalizedLastName.Length > 120)
        {
            LastNameError =
                "ชื่อและนามสกุลยาวเกิน 120 ตัวอักษร";
            PresentError(
                AccountNameChangeErrorTarget.LastNameInput,
                LastNameError);
            analytics.Track(AccountNameChangeAnalytics.Failed(
                AccountNameChangeFailureReason.Invalid));
            return;
        }

        if (string.Equals(
                normalizedFirstName,
                currentFirstName,
                StringComparison.Ordinal) &&
            string.Equals(
                normalizedLastName,
                currentLastName,
                StringComparison.Ordinal))
        {
            Message = "ชื่อนี้เป็นชื่อปัจจุบันของคุณแล้ว";
            PresentError(
                AccountNameChangeErrorTarget.RequestAction,
                Message);
            analytics.Track(AccountNameChangeAnalytics.Failed(
                AccountNameChangeFailureReason.Unchanged));
            return;
        }

        IsBusy = true;
        Message = "";
        try
        {
            PendingAccountNameChange pending;
            try
            {
                pending = await authentication
                    .RequestAccountNameChangeAsync(
                        normalizedFirstName,
                        normalizedLastName,
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

                var error = AccountNameChangeErrorPresentation
                    .ForRequest(exception);
                ApplyRequestError(error);
                return;
            }

            if (!lifetime.IsCurrent(operation.Value))
                return;

            SetAcceptedPending(pending);
            analytics.Track(AccountNameChangeAnalytics.Started());
            await NavigateToVerificationAsync(
                pending,
                operation.Value);
        }
        finally
        {
            if (lifetime.IsCurrent(operation.Value))
                IsBusy = false;
        }
    }

    private void ApplyRequestError(AccountNameChangeErrorNotice error)
    {
        if (error.Kind == AccountNameChangeErrorKind.Cooldown &&
            error.NextAllowedAt is { } nextAllowedAt)
        {
            Message = "";
            analytics.Track(AccountNameChangeAnalytics.Blocked(
                AccountNameChangeBlockReason.Cooldown));
            NameChangeBlocked?.Invoke(
                this,
                new AccountNameChangeBlockedNotice(nextAllowedAt));
            return;
        }

        if (error.Kind is AccountNameChangeErrorKind.Cooldown or
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
            Message = error.Message;
        }

        if (error.Target == AccountNameChangeErrorTarget.FirstNameInput)
            FirstNameError = error.Message;
        if (error.Target == AccountNameChangeErrorTarget.LastNameInput)
            LastNameError = error.Message;
        if (!string.IsNullOrWhiteSpace(Message))
            PresentError(error.Target, Message);
        analytics.Track(AccountNameChangeAnalytics.Failed(
            FailureReason(error.Kind)));
    }

    private async Task NavigateToVerificationAsync(
        PendingAccountNameChange pending,
        EmailChangeOperation operation)
    {
        try
        {
            await Shell.Current.GoToAsync(
                nameof(VerifyNameChangePage),
                new Dictionary<string, object>
                {
                    ["Pending"] = pending
                });
        }
        catch
        {
            if (!lifetime.IsCurrent(operation))
                return;

            Message =
                "ส่งรหัสยืนยันแล้ว กรุณาไปหน้ากรอกรหัสเพื่อดำเนินการต่อ";
            PresentError(
                AccountNameChangeErrorTarget.VerificationAction,
                Message);
        }
    }

    private void SetAcceptedPending(PendingAccountNameChange value)
    {
        acceptedPending = value;
        OnPropertyChanged(nameof(CanEditName));
        OnPropertyChanged(nameof(SubmitButtonText));
        OnPropertyChanged(nameof(SubmitSemanticDescription));
    }

    private void OnSessionResetRequested(object? sender, EventArgs eventArgs)
    {
        lifetime.Deactivate();
        currentFirstName = "";
        currentLastName = "";
        hasCurrentName = false;
        acceptedPending = null;
        FirstName = "";
        LastName = "";
        FirstNameError = "";
        LastNameError = "";
        Message = "";
        IsBusy = false;
        OnPropertyChanged(nameof(CanEditName));
        OnPropertyChanged(nameof(SubmitButtonText));
        OnPropertyChanged(nameof(SubmitSemanticDescription));
    }

    private void PresentError(
        AccountNameChangeErrorTarget target,
        string value) =>
        ErrorPresented?.Invoke(
            this,
            new AccountNameChangeErrorNotice(
                AccountNameChangeErrorKind.Invalid,
                target,
                value));

    private static bool TryNormalizePart(
        string value,
        string label,
        out string normalized,
        out string error)
    {
        normalized = NormalizeWhitespace(value);
        if (normalized.Length == 0)
        {
            error = $"กรุณากรอก{label}";
            return false;
        }
        if (normalized.Length > 60)
        {
            error = $"{label}ยาวเกิน 60 ตัวอักษร";
            return false;
        }

        var hasLetter = false;
        var previousWasSeparator = true;
        foreach (var rune in normalized.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.UppercaseLetter or
                UnicodeCategory.LowercaseLetter or
                UnicodeCategory.TitlecaseLetter or
                UnicodeCategory.ModifierLetter or
                UnicodeCategory.OtherLetter)
            {
                hasLetter = true;
                previousWasSeparator = false;
                continue;
            }
            if (category is UnicodeCategory.NonSpacingMark or
                UnicodeCategory.SpacingCombiningMark)
            {
                if (!previousWasSeparator)
                    continue;
                error = $"{label}มีอักขระที่ไม่รองรับ";
                return false;
            }
            if (rune.Value is ' ' or '-' or '\'' or 0x2019)
            {
                if (!previousWasSeparator)
                {
                    previousWasSeparator = true;
                    continue;
                }
                error = $"{label}มีอักขระที่ไม่รองรับ";
                return false;
            }

            error = $"{label}มีอักขระที่ไม่รองรับ";
            return false;
        }

        if (!hasLetter || previousWasSeparator)
        {
            error = $"{label}มีอักขระที่ไม่รองรับ";
            return false;
        }

        error = "";
        return true;
    }

    private static string NormalizeWhitespace(string value) =>
        string.Join(
            ' ',
            (value ?? "").Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));

    private static AccountNameChangeFailureReason FailureReason(
        AccountNameChangeErrorKind kind) =>
        kind switch
        {
            AccountNameChangeErrorKind.Cooldown =>
                AccountNameChangeFailureReason.Cooldown,
            AccountNameChangeErrorKind.SendLimit or
            AccountNameChangeErrorKind.RateLimited =>
                AccountNameChangeFailureReason.SendLimit,
            AccountNameChangeErrorKind.Unchanged =>
                AccountNameChangeFailureReason.Unchanged,
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
