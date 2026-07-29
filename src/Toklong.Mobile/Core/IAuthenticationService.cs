namespace Toklong.Mobile.Core;

public enum AuthenticationMode
{
    SignIn,
    SignUp
}

public sealed record OtpChallengeResult(
    string ChallengeId,
    string MaskedPhoneNumber,
    string? DevelopmentCode);

public abstract record AuthenticationVerificationResult;

public sealed record SessionVerificationResult
    : AuthenticationVerificationResult;

public sealed record RegistrationRequiredVerificationResult(
    PendingMobileRegistration Pending)
    : AuthenticationVerificationResult;

public sealed record MobileProfile(
    string DisplayName,
    string PhoneNumber,
    string? Email,
    string? SavedAddress,
    string? PayoutBankCode,
    string? PayoutMaskedNumber,
    bool CanBuy,
    bool CanSell,
    string? SavedDeliveryProvinceName = null,
    string? SavedDeliveryPostalCode = null);

public interface IAuthenticationService
{
    Task<bool> HasSessionAsync();

    Task<OtpChallengeResult> RequestCodeAsync(
        string phoneNumber,
        AuthenticationMode mode,
        CancellationToken cancellationToken = default);

    Task<AuthenticationVerificationResult> VerifyCodeAsync(
        string challengeId,
        string code,
        AuthenticationMode mode,
        CancellationToken cancellationToken = default);

    Task CompleteRegistrationAsync(
        string fullName,
        string email,
        string termsVersion,
        CancellationToken cancellationToken = default);

    Task<MobileProfile> GetProfileAsync(
        CancellationToken cancellationToken = default);

    Task SignOutAsync(CancellationToken cancellationToken = default);
}

public sealed record StoredMobileSession(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt);

public interface IMobileSessionStore
{
    Task<StoredMobileSession?> GetAsync();
    Task SaveAsync(StoredMobileSession session);
    void Clear();
}
