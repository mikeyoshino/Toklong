using Toklong.Domain.Sellers;

namespace Toklong.Application.Abstractions;

public interface ISellerRepository
{
    Task<SellerAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<SellerAccount?> GetByPhoneAsync(string phoneNumber, CancellationToken cancellationToken);
    Task AddAsync(SellerAccount seller, CancellationToken cancellationToken);
    Task AddPayoutAccountAsync(
        SellerPayoutAccount account,
        CancellationToken cancellationToken);
}

public sealed record OtpChallenge(
    string ChallengeId,
    string MaskedPhoneNumber,
    string? DevelopmentCode);

public sealed record OtpChallengeRecovery(
    OtpChallenge Challenge,
    string ProviderRequestKey,
    OtpPurpose Purpose,
    string PhoneNumber,
    DateTimeOffset AcceptedAt,
    DateTimeOffset ExpiresAt);

public enum OtpPurpose
{
    MobileAuthentication,
    AccountNameChange
}

public sealed record OtpProviderCapabilities(
    bool SupportsAccountNameChange,
    TimeSpan? AccountNameChangeCodeLifetime,
    bool SupportsRequestLookup)
{
    public bool SupportsVerificationLookup { get; init; }

    public static OtpProviderCapabilities MobileAuthenticationOnly { get; } =
        new(false, null, false);
}

public enum OtpProviderVerificationOutcome
{
    Verified,
    Rejected
}

public sealed record OtpProviderVerificationEvidence(
    string VerificationRequestKey,
    string ChallengeId,
    OtpPurpose Purpose,
    string PhoneNumber,
    OtpProviderVerificationOutcome Outcome,
    DateTimeOffset RequestedAt,
    DateTimeOffset CompletedAt);

public interface IOtpVerificationProvider
{
    OtpProviderCapabilities Capabilities =>
        OtpProviderCapabilities.MobileAuthenticationOnly;

    Task<OtpChallenge> RequestAsync(
        string phoneNumber,
        OtpPurpose purpose,
        string providerRequestKey,
        CancellationToken cancellationToken);

    Task<OtpChallengeRecovery?> LookupAsync(
        string providerRequestKey,
        string phoneNumber,
        OtpPurpose purpose,
        CancellationToken cancellationToken) =>
        Task.FromResult<OtpChallengeRecovery?>(null);

    Task<string?> VerifyAsync(
        string challengeId,
        string code,
        OtpPurpose purpose,
        CancellationToken cancellationToken);

    Task<OtpProviderVerificationEvidence> VerifyIdempotentlyAsync(
        string challengeId,
        string code,
        OtpPurpose purpose,
        string verificationRequestKey,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "The OTP provider does not support idempotent verification.");

    Task<OtpProviderVerificationEvidence?> LookupVerificationAsync(
        string verificationRequestKey,
        string challengeId,
        string phoneNumber,
        OtpPurpose purpose,
        CancellationToken cancellationToken) =>
        Task.FromResult<OtpProviderVerificationEvidence?>(null);
}
