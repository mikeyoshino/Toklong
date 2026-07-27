namespace Toklong.Mobile.Core;

public sealed record AddressOption(int Id, string Name);

public sealed record SubdistrictOption(
    int Id,
    int DistrictId,
    string Name,
    string PostalCode);

public interface IAddressService
{
    Task<IReadOnlyList<AddressOption>> GetProvincesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AddressOption>> GetDistrictsAsync(
        int provinceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubdistrictOption>> GetSubdistrictsAsync(
        int districtId,
        CancellationToken cancellationToken = default);
}
