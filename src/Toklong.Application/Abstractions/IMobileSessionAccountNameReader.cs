namespace Toklong.Application.Abstractions;

public sealed record MobileSessionAccountName(
    Guid AccountId,
    string PhoneNumber,
    string FirstName,
    string LastName,
    string DisplayName);

public interface IMobileSessionAccountNameReader
{
    Task<MobileSessionAccountName?> GetBuyerAsync(
        Guid buyerId,
        CancellationToken cancellationToken);

    Task<MobileSessionAccountName?> GetSellerAsync(
        Guid sellerId,
        CancellationToken cancellationToken);
}
