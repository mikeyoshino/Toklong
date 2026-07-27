using System.Reflection;
using System.Text.Json;
using Toklong.Application.Abstractions;
using Toklong.Domain.Buyers;
using Toklong.Domain.Common;

namespace Toklong.Infrastructure.Services;

public sealed class BundledThaiAddressCatalog : IThaiAddressCatalog
{
    private const string ResourceSuffix = "Data.thai-addresses.json";
    private readonly IReadOnlyList<ThaiProvinceOption> _provinces;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<ThaiDistrictOption>>
        _districtsByProvince;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<ThaiSubdistrictOption>>
        _subdistrictsByDistrict;
    private readonly IReadOnlyDictionary<int, SourceProvince> _provinceById;
    private readonly IReadOnlyDictionary<int, SourceDistrict> _districtById;
    private readonly IReadOnlyDictionary<int, SourceSubdistrict> _subdistrictById;

    public BundledThaiAddressCatalog()
    {
        var assembly = typeof(BundledThaiAddressCatalog).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(name =>
                name.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                "ไม่พบชุดข้อมูลที่อยู่ประเทศไทยในแอป");
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                "เปิดชุดข้อมูลที่อยู่ประเทศไทยไม่ได้");
        var source = JsonSerializer.Deserialize<List<SourceProvince>>(
            stream,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            }) ?? throw new InvalidOperationException(
                "อ่านชุดข้อมูลที่อยู่ประเทศไทยไม่ได้");

        _provinceById = source.ToDictionary(item => item.Id);
        _districtById = source
            .SelectMany(province => province.Districts)
            .ToDictionary(item => item.Id);
        _subdistrictById = source
            .SelectMany(province => province.Districts)
            .SelectMany(district => district.SubDistricts)
            .ToDictionary(item => item.Id);
        _provinces = source
            .Select(item => new ThaiProvinceOption(item.Id, item.NameTh))
            .ToList();
        _districtsByProvince = source.ToDictionary(
            province => province.Id,
            province => (IReadOnlyList<ThaiDistrictOption>)province.Districts
                .Select(district => new ThaiDistrictOption(
                    district.Id,
                    province.Id,
                    district.NameTh))
                .ToList());
        _subdistrictsByDistrict = source
            .SelectMany(province => province.Districts)
            .ToDictionary(
                district => district.Id,
                district => (IReadOnlyList<ThaiSubdistrictOption>)district
                    .SubDistricts
                    .Select(subdistrict => new ThaiSubdistrictOption(
                        subdistrict.Id,
                        district.Id,
                        subdistrict.NameTh,
                        subdistrict.ZipCode.ToString("00000")))
                    .ToList());
    }

    public IReadOnlyList<ThaiProvinceOption> Provinces => _provinces;

    public IReadOnlyList<ThaiDistrictOption> GetDistricts(int provinceId) =>
        _districtsByProvince.GetValueOrDefault(
            provinceId,
            Array.Empty<ThaiDistrictOption>());

    public IReadOnlyList<ThaiSubdistrictOption> GetSubdistricts(int districtId) =>
        _subdistrictsByDistrict.GetValueOrDefault(
            districtId,
            Array.Empty<ThaiSubdistrictOption>());

    public BuyerDeliveryAddress Resolve(
        string addressLine,
        int provinceId,
        int districtId,
        int subdistrictId)
    {
        var (province, district, subdistrict) =
            ResolveSource(
                provinceId,
                districtId,
                subdistrictId);

        return new BuyerDeliveryAddress(
            addressLine,
            province.Id,
            province.NameTh,
            district.Id,
            district.NameTh,
            subdistrict.Id,
            subdistrict.NameTh,
            subdistrict.ZipCode.ToString("00000"));
    }

    public ThaiDeliveryRegion ResolveRegion(
        int provinceId,
        int districtId,
        int subdistrictId)
    {
        var (province, _, subdistrict) =
            ResolveSource(
                provinceId,
                districtId,
                subdistrictId);
        return new ThaiDeliveryRegion(
            province.NameTh,
            subdistrict.ZipCode.ToString("00000"));
    }

    private (
        SourceProvince Province,
        SourceDistrict District,
        SourceSubdistrict Subdistrict) ResolveSource(
            int provinceId,
            int districtId,
            int subdistrictId)
    {
        if (!_provinceById.TryGetValue(provinceId, out var province))
            throw new DomainException("กรุณาเลือกจังหวัดจากรายการ");
        if (!_districtById.TryGetValue(districtId, out var district) ||
            !_districtsByProvince[provinceId].Any(item => item.Id == districtId))
            throw new DomainException("อำเภอหรือเขตไม่ตรงกับจังหวัดที่เลือก");
        if (!_subdistrictById.TryGetValue(subdistrictId, out var subdistrict) ||
            !_subdistrictsByDistrict[districtId]
                .Any(item => item.Id == subdistrictId))
            throw new DomainException("ตำบลหรือแขวงไม่ตรงกับอำเภอหรือเขตที่เลือก");
        return (province, district, subdistrict);
    }

    private sealed class SourceProvince
    {
        public int Id { get; init; }
        public string NameTh { get; init; } = "";
        public List<SourceDistrict> Districts { get; init; } = [];
    }

    private sealed class SourceDistrict
    {
        public int Id { get; init; }
        public string NameTh { get; init; } = "";
        public List<SourceSubdistrict> SubDistricts { get; init; } = [];
    }

    private sealed class SourceSubdistrict
    {
        public int Id { get; init; }
        public string NameTh { get; init; } = "";
        public int ZipCode { get; init; }
    }
}
