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

public interface IOtpVerificationProvider
{
    Task<OtpChallenge> RequestAsync(
        string phoneNumber,
        CancellationToken cancellationToken);

    Task<string?> VerifyAsync(
        string challengeId,
        string code,
        CancellationToken cancellationToken);
}
