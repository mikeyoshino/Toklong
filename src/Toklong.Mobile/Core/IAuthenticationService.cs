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
    string? SavedDeliveryPostalCode = null,
    string? FirstName = null,
    string? LastName = null);

public sealed record PendingEmailChange(
    Guid ChallengeId,
    string MaskedEmail,
    DateTimeOffset ExpiresAt,
    DateTimeOffset ResendAvailableAt,
    int RemainingAttempts);

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
        string firstName,
        string lastName,
        string email,
        string termsVersion,
        CancellationToken cancellationToken = default);

    Task<MobileProfile> GetProfileAsync(
        CancellationToken cancellationToken = default);

    Task<AccountNameChangeEligibility> GetAccountNameChangeEligibilityAsync(
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<PendingAccountNameChange?> GetPendingAccountNameChangeAsync(
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<PendingAccountNameChange> RequestAccountNameChangeAsync(
        string firstName,
        string lastName,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<PendingAccountNameChange> ResendAccountNameChangeAsync(
        Guid challengeId,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<VerifiedAccountNameChange> VerifyAccountNameChangeAsync(
        Guid challengeId,
        string code,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<PendingEmailChange?> GetPendingEmailChangeAsync(
        CancellationToken cancellationToken = default);

    Task<PendingEmailChange> RequestEmailChangeAsync(
        string email,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<PendingEmailChange> ResendEmailChangeAsync(
        Guid challengeId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<string> VerifyEmailChangeAsync(
        Guid challengeId,
        string code,
        string idempotencyKey,
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
