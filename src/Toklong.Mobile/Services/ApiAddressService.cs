using System.Net.Http.Json;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Services;

public sealed class ApiAddressService(MobileApiClient api) : IAddressService
{
    public Task<IReadOnlyList<AddressOption>> GetProvincesAsync(
        CancellationToken cancellationToken = default) =>
        GetAsync<AddressOption>(
            "api/mobile/addresses/provinces",
            cancellationToken);

    public Task<IReadOnlyList<AddressOption>> GetDistrictsAsync(
        int provinceId,
        CancellationToken cancellationToken = default) =>
        GetAsync<AddressOption>(
            $"api/mobile/addresses/districts/{provinceId}",
            cancellationToken);

    public Task<IReadOnlyList<SubdistrictOption>> GetSubdistrictsAsync(
        int districtId,
        CancellationToken cancellationToken = default) =>
        GetAsync<SubdistrictOption>(
            $"api/mobile/addresses/subdistricts/{districtId}",
            cancellationToken);

    private async Task<IReadOnlyList<T>> GetAsync<T>(
        string path,
        CancellationToken cancellationToken)
    {
        using var response = await api.SendAuthenticatedAsync(
            () => new HttpRequestMessage(HttpMethod.Get, path),
            cancellationToken);
        await MobileApiClient.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<T>>(
                   cancellationToken: cancellationToken)
               ?? [];
    }
}
