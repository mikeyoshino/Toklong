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

public enum OtpPurpose
{
    MobileAuthentication,
    AccountNameChange
}

public interface IOtpVerificationProvider
{
    Task<OtpChallenge> RequestAsync(
        string phoneNumber,
        OtpPurpose purpose,
        CancellationToken cancellationToken);

    Task<string?> VerifyAsync(
        string challengeId,
        string code,
        OtpPurpose purpose,
        CancellationToken cancellationToken);
}
