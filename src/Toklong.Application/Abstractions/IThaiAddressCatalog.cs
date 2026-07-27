using Toklong.Domain.Buyers;

namespace Toklong.Application.Abstractions;

public interface IThaiAddressCatalog
{
    IReadOnlyList<ThaiProvinceOption> Provinces { get; }
    IReadOnlyList<ThaiDistrictOption> GetDistricts(int provinceId);
    IReadOnlyList<ThaiSubdistrictOption> GetSubdistricts(int districtId);
    ThaiDeliveryRegion ResolveRegion(
        int provinceId,
        int districtId,
        int subdistrictId);
    BuyerDeliveryAddress Resolve(
        string addressLine,
        int provinceId,
        int districtId,
        int subdistrictId);
}

public sealed record ThaiProvinceOption(int Id, string Name);
public sealed record ThaiDistrictOption(int Id, int ProvinceId, string Name);
public sealed record ThaiSubdistrictOption(
    int Id,
    int DistrictId,
    string Name,
    string PostalCode);
public sealed record ThaiDeliveryRegion(
    string ProvinceName,
    string PostalCode);
