using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Toklong.Application.Abstractions;
using Toklong.Domain.Transactions;

namespace Toklong.Infrastructure.Services;

public sealed partial class ListingImportService(HttpClient httpClient) : IListingImportService
{
    private const int MaximumHtmlBytes = 1_500_000;
    private const int MaximumRedirects = 3;

    public async Task<ImportedListingDraft> ImportAsync(
        Uri sourceUrl,
        CancellationToken cancellationToken)
    {
        var currentUrl = sourceUrl;
        for (var redirect = 0; redirect <= MaximumRedirects; redirect++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, currentUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            using var response = await httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (IsRedirect(response.StatusCode))
            {
                if (redirect == MaximumRedirects || response.Headers.Location is null)
                    throw new InvalidOperationException("ประกาศเปลี่ยนเส้นทางหลายครั้งเกินไป");

                var redirected = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(currentUrl, response.Headers.Location);
                if (!PublicListingUrl.TryParse(redirected.ToString(), out var safeRedirect) ||
                    safeRedirect is null)
                    throw new InvalidOperationException("ประกาศเปลี่ยนเส้นทางไปยังที่อยู่ที่ระบบไม่อนุญาต");
                currentUrl = safeRedirect;
                continue;
            }

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
                        ? "เว็บไซต์นี้ไม่อนุญาตให้อ่านประกาศโดยตรง กรุณาเปิดประกาศเป็นสาธารณะหรือกรอกข้อมูลเอง"
                        : "ยังอ่านประกาศนี้ไม่ได้ กรุณาตรวจว่าลิงก์เปิดเป็นสาธารณะแล้วลองอีกครั้ง");

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null && !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("ลิงก์นี้ไม่ใช่หน้าประกาศสินค้า");
            if (response.Content.Headers.ContentLength > MaximumHtmlBytes)
                throw new InvalidOperationException("หน้าประกาศมีขนาดใหญ่เกินกว่าที่ระบบรองรับ");

            var html = await ReadLimitedAsync(response.Content, cancellationToken);
            return Extract(currentUrl, html);
        }

        throw new InvalidOperationException("ไม่สามารถอ่านประกาศนี้ได้");
    }

    public static ImportedListingDraft Extract(Uri sourceUrl, string html)
    {
        var metadata = ParseMetadata(html);
        var productJson = FindProductJsonLd(html);

        var title = First(
            JsonString(productJson, "name"),
            Get(metadata, "og:title"),
            Get(metadata, "twitter:title"),
            TitleRegex().Match(html) is { Success: true } titleMatch
                ? WebUtility.HtmlDecode(titleMatch.Groups["value"].Value)
                : null);
        title = CleanText(title)
            .Replace(" | Facebook", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" - Facebook", "", StringComparison.OrdinalIgnoreCase);

        var description = CleanText(First(
            JsonString(productJson, "description"),
            Get(metadata, "og:description"),
            Get(metadata, "description"),
            Get(metadata, "twitter:description")));
        var image = First(
            JsonImage(productJson),
            Get(metadata, "og:image"),
            Get(metadata, "twitter:image"));
        if (!Uri.TryCreate(image, UriKind.Absolute, out var imageUri) ||
            imageUri.Scheme is not ("http" or "https"))
            image = "";

        var price = ParsePrice(First(
            JsonOfferString(productJson, "price"),
            Get(metadata, "product:price:amount"),
            Get(metadata, "og:price:amount")));

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(description))
            throw new InvalidOperationException(
                "หน้าเว็บไม่เปิดเผยรายละเอียดสินค้า อาจต้องเข้าสู่ระบบ กรุณากรอกข้อมูลเองหรือลองลิงก์ประกาศสาธารณะ");

        var combined = $"{title} {description}";
        var condition = InferCondition(
            First(JsonOfferString(productJson, "itemCondition"), combined));
        var category = InferCategory(combined);
        var fields = new List<string>();
        if (!string.IsNullOrWhiteSpace(title)) fields.Add("ชื่อสินค้า");
        if (!string.IsNullOrWhiteSpace(description)) fields.Add("รายละเอียด");
        if (!string.IsNullOrWhiteSpace(image)) fields.Add("รูปสินค้า");
        if (price.HasValue) fields.Add("ราคา");
        fields.Add("หมวดและสภาพ");

        return new ImportedListingDraft(
            SourceSite(sourceUrl.Host),
            Limit(title, 180),
            Limit(description, 2000),
            image ?? "",
            price,
            category,
            condition,
            fields);
    }

    public static SocketsHttpHandler CreateSafeHandler() => new()
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        ConnectCallback = async (context, cancellationToken) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(
                context.DnsEndPoint.Host, cancellationToken);
            var address = addresses.FirstOrDefault(PublicListingUrl.IsPublicAddress)
                ?? throw new HttpRequestException("ปลายทางของลิงก์ไม่ใช่เครือข่ายสาธารณะ");
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(
                    new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    };

    private static async Task<string> ReadLimitedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[16_384];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;
            if (destination.Length + read > MaximumHtmlBytes)
                throw new InvalidOperationException("หน้าประกาศมีขนาดใหญ่เกินกว่าที่ระบบรองรับ");
            destination.Write(buffer, 0, read);
        }
        destination.Position = 0;
        using var reader = new StreamReader(destination, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static Dictionary<string, string> ParseMetadata(string html)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match tag in MetaTagRegex().Matches(html))
        {
            var attributes = AttributeRegex().Matches(tag.Value)
                .ToDictionary(
                    match => match.Groups["name"].Value,
                    match => WebUtility.HtmlDecode(match.Groups["value"].Value),
                    StringComparer.OrdinalIgnoreCase);
            if ((attributes.TryGetValue("property", out var key) ||
                 attributes.TryGetValue("name", out key)) &&
                attributes.TryGetValue("content", out var value) &&
                !result.ContainsKey(key))
                result[key] = value;
        }
        return result;
    }

    private static JsonElement? FindProductJsonLd(string html)
    {
        foreach (Match match in JsonLdRegex().Matches(html))
        {
            try
            {
                using var document = JsonDocument.Parse(WebUtility.HtmlDecode(match.Groups["value"].Value));
                var product = FindProduct(document.RootElement);
                if (product.HasValue)
                    return product.Value.Clone();
            }
            catch (JsonException)
            {
                // Invalid third-party JSON-LD is ignored in favor of Open Graph metadata.
            }
        }
        return null;
    }

    private static JsonElement? FindProduct(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("@type", out var type) &&
                (type.ValueKind == JsonValueKind.String &&
                 type.GetString()?.Contains("Product", StringComparison.OrdinalIgnoreCase) == true))
                return element;
            foreach (var property in element.EnumerateObject())
            {
                var found = FindProduct(property.Value);
                if (found.HasValue) return found;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var found = FindProduct(item);
                if (found.HasValue) return found;
            }
        }
        return null;
    }

    private static string JsonString(JsonElement? element, string property) =>
        element is { ValueKind: JsonValueKind.Object } value &&
        value.TryGetProperty(property, out var result) &&
        result.ValueKind == JsonValueKind.String
            ? result.GetString() ?? ""
            : "";

    private static string JsonImage(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty("image", out var image))
            return "";
        if (image.ValueKind == JsonValueKind.String) return image.GetString() ?? "";
        if (image.ValueKind == JsonValueKind.Array && image.GetArrayLength() > 0)
            return image[0].ValueKind == JsonValueKind.String ? image[0].GetString() ?? "" : "";
        if (image.ValueKind == JsonValueKind.Object &&
            image.TryGetProperty("url", out var url) &&
            url.ValueKind == JsonValueKind.String)
            return url.GetString() ?? "";
        return "";
    }

    private static string JsonOfferString(JsonElement? element, string property)
    {
        if (element is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty("offers", out var offers))
            return "";
        if (offers.ValueKind == JsonValueKind.Array && offers.GetArrayLength() > 0)
            offers = offers[0];
        return offers.ValueKind == JsonValueKind.Object &&
               offers.TryGetProperty(property, out var result)
            ? result.ToString()
            : "";
    }

    private static decimal? ParsePrice(string? value)
    {
        var normalized = Regex.Replace(value ?? "", @"[^\d.,]", "").Replace(",", "");
        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var price) && price > 0
            ? decimal.Round(price, 2)
            : null;
    }

    private static ConditionCode InferCondition(string text)
    {
        var normalized = text.ToLowerInvariant();
        if (ContainsAny(normalized, "brand new", "สินค้าใหม่", "ของใหม่", "newcondition"))
            return ConditionCode.New;
        if (ContainsAny(normalized, "ตำหนิ", "ชำรุด", "เสีย", "defect", "damaged", "scratch"))
            return ConditionCode.UsedDefects;
        return ConditionCode.UsedGood;
    }

    private static string InferCategory(string text)
    {
        var normalized = text.ToLowerInvariant();
        if (ContainsAny(normalized, "กล้อง", "เลนส์", "camera", "lens")) return "กล้องและอุปกรณ์";
        if (ContainsAny(normalized, "รองเท้า", "เสื้อ", "กางเกง", "sneaker", "shirt")) return "รองเท้าและเสื้อผ้า";
        if (ContainsAny(normalized, "กระเป๋า", "นาฬิกา", "bag", "watch")) return "กระเป๋าและแฟชั่น";
        if (ContainsAny(normalized, "สะสม", "ฟิกเกอร์", "การ์ด", "figure", "collectible")) return "ของสะสม";
        if (ContainsAny(normalized, "โทรศัพท์", "มือถือ", "คอม", "หูฟัง", "phone", "laptop", "headphone")) return "อิเล็กทรอนิกส์";
        return "งานอดิเรกและของใช้";
    }

    private static string SourceSite(string host)
    {
        if (host.Contains("facebook.", StringComparison.OrdinalIgnoreCase)) return "Facebook Marketplace";
        if (host.Contains("instagram.", StringComparison.OrdinalIgnoreCase)) return "Instagram";
        if (host.Contains("kaidee.", StringComparison.OrdinalIgnoreCase)) return "Kaidee";
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }

    private static string Get(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : "";
    private static string First(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    private static string Limit(string value, int length) =>
        value.Length <= length ? value : value[..length].TrimEnd();
    private static string CleanText(string? value) =>
        Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(value ?? "", "<[^>]+>", " ")), @"\s+", " ").Trim();
    private static bool IsRedirect(HttpStatusCode code) =>
        code is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    [GeneratedRegex(@"<meta\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex MetaTagRegex();
    [GeneratedRegex(@"(?<name>[\w:-]+)\s*=\s*[""'](?<value>.*?)[""']", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AttributeRegex();
    [GeneratedRegex(@"<title\b[^>]*>(?<value>.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleRegex();
    [GeneratedRegex(@"<script\b[^>]*type\s*=\s*[""']application/ld\+json[""'][^>]*>(?<value>.*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex JsonLdRegex();
}
