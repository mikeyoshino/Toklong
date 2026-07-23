using System.Net;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Abstractions;

public sealed record ImportedListingDraft(
    string SourceSite,
    string ProductName,
    string Description,
    string PhotoUrl,
    decimal? PriceBaht,
    string Category,
    ConditionCode Condition,
    IReadOnlyList<string> ExtractedFields);

public interface IListingImportService
{
    Task<ImportedListingDraft> ImportAsync(Uri sourceUrl, CancellationToken cancellationToken);
}

public static class PublicListingUrl
{
    public static bool TryParse(string? value, out Uri? uri)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out uri) ||
            uri.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            uri.UserInfo.Length > 0 ||
            uri.Port is < 1 or > 65535)
        {
            uri = null;
            return false;
        }

        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
        {
            uri = null;
            return false;
        }

        if (IPAddress.TryParse(uri.Host, out var address) && !IsPublicAddress(address))
        {
            uri = null;
            return false;
        }

        return true;
    }

    public static bool IsPublicAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return false;

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] switch
            {
                0 or 10 or 127 => false,
                100 when bytes[1] is >= 64 and <= 127 => false,
                169 when bytes[1] == 254 => false,
                172 when bytes[1] is >= 16 and <= 31 => false,
                192 when bytes[1] is 0 or 168 => false,
                198 when bytes[1] is 18 or 19 or 51 => false,
                203 when bytes[1] == 0 && bytes[2] == 113 => false,
                >= 224 => false,
                _ => true
            };
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            var isUniqueLocal = (bytes[0] & 0xfe) == 0xfc;
            var isLinkLocal = bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80;
            var isDocumentation = bytes[0] == 0x20 && bytes[1] == 0x01 &&
                                  bytes[2] == 0x0d && bytes[3] == 0xb8;
            return !address.Equals(IPAddress.IPv6None) &&
                   !address.Equals(IPAddress.IPv6Any) &&
                   !address.Equals(IPAddress.IPv6Loopback) &&
                   !isUniqueLocal &&
                   !isLinkLocal &&
                   !isDocumentation &&
                   !address.IsIPv6Multicast;
        }

        return false;
    }
}
