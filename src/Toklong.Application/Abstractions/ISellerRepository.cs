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
    public static OtpProviderCapabilities MobileAuthenticationOnly { get; } =
        new(false, null, false);
}

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
}
