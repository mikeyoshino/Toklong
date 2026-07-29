using System.Net.Mail;
using System.Windows.Input;
using Toklong.Mobile.Core;
using Toklong.Mobile.Pages;

namespace Toklong.Mobile.ViewModels;

public sealed class ChangeEmailViewModel(
    IAuthenticationService authentication,
    IMobileAnalytics analytics,
    AuthenticatedSessionBoundary session) : ObservableViewModel
{
    private readonly EmailChangePageLifetime lifetime =
        new(session);
    private string email = "";
    private string emailError = "";
    private string message = "";
    private string? requestIdempotencyKey;
    private bool isBusy;

    public event EventHandler<EmailChangeErrorNotice>?
        ErrorPresented;

    public string Email
    {
        get => email;
        set
        {
            var replacement = value ?? "";
            if (!SetProperty(ref email, replacement))
                return;

            requestIdempotencyKey = null;
            if (TryNormalizeEmail(replacement, out _))
            {
                requestIdempotencyKey =
                    Guid.NewGuid().ToString("N");
                EmailError = "";
            }
            else
            {
                EmailError = string.IsNullOrWhiteSpace(replacement)
                    ? ""
                    : "กรอกอีเมลให้ถูกต้อง";
            }
        }
    }

    public string EmailError
    {
        get => emailError;
        private set
        {
            if (SetProperty(ref emailError, value))
                OnPropertyChanged(nameof(HasEmailError));
        }
    }

    public bool HasEmailError =>
        !string.IsNullOrWhiteSpace(EmailError);

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
        private set => SetProperty(ref isBusy, value);
    }

    public ICommand SubmitCommand =>
        new AsyncCommand(SubmitAsync);

    public void Activate() =>
        lifetime.Activate();

    public void Deactivate()
    {
        lifetime.Deactivate();
        IsBusy = false;
    }

    public async Task SubmitAsync()
    {
        var operation = lifetime.Capture();
        if (operation is null || IsBusy)
            return;
        if (!TryNormalizeEmail(Email, out var normalizedEmail))
        {
            EmailError = "กรอกอีเมลให้ถูกต้อง";
            PresentError(
                EmailChangeErrorTarget.EmailInput,
                EmailError);
            analytics.Track(
                AccountEmailChangeAnalytics.Failed(
                    AccountEmailChangeFailureReason.Invalid));
            return;
        }

        requestIdempotencyKey ??=
            Guid.NewGuid().ToString("N");
        IsBusy = true;
        EmailError = "";
        Message = "";
        try
        {
            var pending =
                await authentication.RequestEmailChangeAsync(
                    normalizedEmail,
                    requestIdempotencyKey,
                    operation.Value.Token);
            if (!lifetime.IsCurrent(operation.Value))
                return;

            requestIdempotencyKey = null;
            analytics.Track(
                AccountEmailChangeAnalytics.Started());
            await Shell.Current.GoToAsync(
                nameof(VerifyEmailChangePage),
                new Dictionary<string, object>
                {
                    ["Pending"] = pending
                });
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
                AccountEmailChangeErrorPresentation.ForRequest(
                    exception);
            Message = error.Message;
            PresentError(
                EmailChangeErrorTarget.EmailInput,
                Message);
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

    private void PresentError(
        EmailChangeErrorTarget target,
        string value) =>
        ErrorPresented?.Invoke(
            this,
            new EmailChangeErrorNotice(
                target,
                value));

    private static bool TryNormalizeEmail(
        string value,
        out string normalized)
    {
        normalized = (value ?? "").Trim();
        return normalized.Length is > 0 and <= 254 &&
               MailAddress.TryCreate(
                   normalized,
                   out var parsed) &&
               string.Equals(
                   parsed.Address,
                   normalized,
                   StringComparison.OrdinalIgnoreCase);
    }
}
