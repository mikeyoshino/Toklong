using System.Net.Mail;
using System.Windows.Input;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.ViewModels;

public sealed class CompleteRegistrationViewModel(
    IAuthenticationService authentication,
    IPendingRegistrationStore pendingRegistrations,
    IPushRegistrationService pushRegistration,
    IDeepLinkCoordinator deepLinks)
    : ObservableViewModel
{
    private const string TermsVersion = "terms-mvp-v1";
    private const string TermsUrl =
        "https://toklong.co.th/terms";
    private const string PrivacyUrl =
        "https://toklong.co.th/privacy";
    private string firstName = "";
    private string lastName = "";
    private string email = "";
    private string maskedPhoneNumber = "";
    private string message = "";
    private bool isBusy;
    private bool initialized;

    public string FirstName
    {
        get => firstName;
        set => SetProperty(ref firstName, value);
    }

    public string LastName
    {
        get => lastName;
        set => SetProperty(ref lastName, value);
    }

    public string Email
    {
        get => email;
        set => SetProperty(ref email, value);
    }

    public string MaskedPhoneNumber
    {
        get => maskedPhoneNumber;
        private set => SetProperty(
            ref maskedPhoneNumber,
            value);
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

    public bool HasMessage =>
        !string.IsNullOrWhiteSpace(Message);

    public bool IsBusy
    {
        get => isBusy;
        private set => SetProperty(ref isBusy, value);
    }

    public ICommand CompleteCommand =>
        new AsyncCommand(CompleteAsync);
    public ICommand OpenTermsCommand =>
        new AsyncCommand(() => OpenUrlAsync(TermsUrl));
    public ICommand OpenPrivacyCommand =>
        new AsyncCommand(() => OpenUrlAsync(PrivacyUrl));

    public async Task InitializeAsync()
    {
        if (initialized)
            return;
        initialized = true;
        var pending = await pendingRegistrations.GetValidAsync(
            DateTimeOffset.UtcNow);
        if (pending is null)
        {
            Message =
                "การยืนยันเบอร์หมดอายุ กรุณายืนยันเบอร์ใหม่";
            return;
        }
        MaskedPhoneNumber = pending.MaskedPhoneNumber;
    }

    private async Task CompleteAsync()
    {
        if (IsBusy)
            return;
        var normalizedFirstName = NormalizeNamePart(FirstName);
        if (string.IsNullOrWhiteSpace(normalizedFirstName))
        {
            Message = "กรอกชื่อ";
            return;
        }

        var normalizedLastName = NormalizeNamePart(LastName);
        if (string.IsNullOrWhiteSpace(normalizedLastName))
        {
            Message = "กรอกนามสกุล";
            return;
        }

        var cleanEmail = Email.Trim();
        if (cleanEmail.Length > 254 ||
            !MailAddress.TryCreate(
                cleanEmail,
                out var parsedEmail) ||
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
            await authentication.CompleteRegistrationAsync(
                normalizedFirstName,
                normalizedLastName,
                cleanEmail,
                TermsVersion);
            await pushRegistration.InitializeAsync();
            await Shell.Current.GoToAsync(
                AuthenticatedHomeRoutes.Home);
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

    private static Task OpenUrlAsync(string url) =>
        Launcher.Default.OpenAsync(url);

    private static string NormalizeNamePart(string value) =>
        string.Join(
            ' ',
            (value ?? "").Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));
}
