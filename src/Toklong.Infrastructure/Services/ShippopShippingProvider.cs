using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Toklong.Application.Abstractions;
using Toklong.Domain.Common;

namespace Toklong.Infrastructure.Services;

public sealed class ShippopShippingOptions
{
    public const string SectionName = "Shippop";
    public static IReadOnlySet<string> SupportedServiceCodes { get; } =
        new HashSet<string>(
            ["EMST", "FLE", "KRYX", "KRYS"],
            StringComparer.Ordinal);

    public string BaseUrl { get; init; } =
        "https://mkpservice.shippop.com/";
    public string ApiKey { get; init; } = "";
    public string AccountEmail { get; init; } = "";
    public string QuoteSigningSecret { get; init; } = "";
    public int QuoteLifetimeMinutes { get; init; } = 120;
    public IReadOnlyList<string> ServiceCodes { get; init; } =
        ["EMST", "FLE", "KRYX", "KRYS"];

    public static ShippopShippingOptions From(
        IConfiguration configuration)
    {
        var configuredServices = configuration
            .GetSection($"{SectionName}:ServiceCodes")
            .GetChildren()
            .Select(child => child.Value?.Trim().ToUpperInvariant())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new ShippopShippingOptions
        {
            BaseUrl =
                configuration[$"{SectionName}:BaseUrl"]?.Trim() ??
                "https://mkpservice.shippop.com/",
            ApiKey =
                configuration[$"{SectionName}:ApiKey"]?.Trim() ?? "",
            AccountEmail =
                configuration[$"{SectionName}:AccountEmail"]?.Trim() ?? "",
            QuoteSigningSecret =
                configuration[
                    $"{SectionName}:QuoteSigningSecret"] ?? "",
            QuoteLifetimeMinutes = Math.Clamp(
                configuration.GetValue(
                    $"{SectionName}:QuoteLifetimeMinutes",
                    120),
                61,
                240),
            ServiceCodes = configuredServices.Length == 0
                ? SupportedServiceCodes.ToArray()
                : configuredServices
        };
    }
}

public sealed class ShippopShippingProvider(
    HttpClient httpClient,
    ShippopShippingOptions options,
    IClock clock) : IShippingQuoteProvider, IShipmentProvider
{
    private const string Provider = "shippop";
    private const int MaximumProviderResponseBytes =
        5 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public string ProviderName => Provider;

    public async Task<IReadOnlyList<ShippingQuoteOption>> GetQuotesAsync(
        ShippingQuoteRequest request,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        ValidateRequest(request);

        var data = new Dictionary<string, object>();
        var index = 0;
        foreach (var serviceCode in options.ServiceCodes)
        {
            if (TryMapService(serviceCode) is null)
                continue;
            data[(index++).ToString(CultureInfo.InvariantCulture)] =
                ShipmentPayload(
                    request,
                    serviceCode,
                    includeReference: null,
                    showAll: false);
        }
        if (data.Count == 0)
            throw new InvalidOperationException(
                "ยังไม่ได้ตั้งค่าบริการ SHIPPOP ที่ระบบรองรับ");

        using var document = await PostJsonAsync(
            "pricelist/",
            new
            {
                api_key = options.ApiKey,
                data
            },
            cancellationToken);
        EnsureProviderSuccess(
            document.RootElement,
            "ตรวจสอบค่าจัดส่ง");
        if (!TryProperty(
                document.RootElement,
                "data",
                out var responseData))
            throw ProviderFailure("ตรวจสอบค่าจัดส่ง");

        var expiresAt = clock.UtcNow.AddMinutes(
            options.QuoteLifetimeMinutes);
        var quotes = new List<ShippingQuoteOption>();
        foreach (var candidate in DescendantObjects(responseData))
        {
            if (!TryString(
                    candidate,
                    "courier_code",
                    out var serviceCode) ||
                TryMapService(serviceCode) is not
                    { } service ||
                !options.ServiceCodes.Contains(
                    serviceCode,
                    StringComparer.OrdinalIgnoreCase) ||
                TryBoolean(
                    candidate,
                    "available",
                    defaultValue: true) == false ||
                !TryMoneySatang(
                    candidate,
                    "price",
                    out var feeSatang) ||
                feeSatang <= 0)
                continue;

            var serviceName = TryString(
                    candidate,
                    "courier_name",
                    out var courierName)
                ? courierName
                : service.DisplayName;
            quotes.Add(
                new ShippingQuoteOption(
                    Provider,
                    CreateQuoteReference(
                        request,
                        serviceCode,
                        feeSatang,
                        expiresAt),
                    service.CarrierCode,
                    serviceCode,
                    serviceName,
                    feeSatang,
                    expiresAt));
        }

        return quotes
            .GroupBy(
                quote => quote.ServiceCode,
                StringComparer.Ordinal)
            .Select(group => group
                .OrderBy(quote => quote.FeeSatang)
                .First())
            .OrderBy(quote => quote.FeeSatang)
            .ToArray();
    }

    public Task<ShippingQuoteOption> ValidateQuoteAsync(
        ShippingQuoteRequest request,
        string quoteReference,
        long disclosedFeeSatang,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        ValidateRequest(request);
        var cleanQuoteReference =
            quoteReference?.Trim() ?? "";
        var parts = cleanQuoteReference.Split(
            '.',
            StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 7 ||
            parts[0] != "sp1" ||
            !long.TryParse(
                parts[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var expiryUnix) ||
            !long.TryParse(
                parts[2],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var feeSatang) ||
            !SafeServiceCode(parts[3]) ||
            TryMapService(parts[3]) is not { } service ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(parts[4]),
                Encoding.ASCII.GetBytes(
                    RequestFingerprint(request))) ||
            !VerifyQuoteSignature(parts))
            throw ExpiredQuote();

        DateTimeOffset expiresAt;
        try
        {
            expiresAt =
                DateTimeOffset.FromUnixTimeSeconds(expiryUnix);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw ExpiredQuote();
        }
        if (expiresAt <= clock.UtcNow ||
            feeSatang <= 0 ||
            feeSatang != disclosedFeeSatang)
            throw ExpiredQuote();

        return Task.FromResult(
            new ShippingQuoteOption(
                Provider,
                cleanQuoteReference,
                service.CarrierCode,
                parts[3],
                service.DisplayName,
                feeSatang,
                expiresAt));
    }

    public async Task<ShipmentReservation> ReserveAsync(
        ShipmentReservationRequest request,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        ValidateRequest(request.Shipment);
        if (!string.Equals(
                request.Quote.Provider,
                Provider,
                StringComparison.Ordinal) ||
            TryMapService(request.Quote.ServiceCode) is not
                { } selectedService ||
            !string.Equals(
                selectedService.CarrierCode,
                request.Quote.CarrierCode,
                StringComparison.Ordinal))
            throw new DomainException(
                "ผู้ให้บริการขนส่งไม่ตรงกับราคาที่เลือก");

        using var document = await PostJsonAsync(
            "booking/",
            new
            {
                api_key = options.ApiKey,
                email = options.AccountEmail,
                data = new[]
                {
                    ShipmentPayload(
                        request.Shipment,
                        request.Quote.ServiceCode,
                        request.TransactionId.ToString("N"),
                        showAll: false)
                },
                force_confirm = 0
            },
            cancellationToken);
        EnsureProviderSuccess(
            document.RootElement,
            "สร้างรายการจัดส่ง");
        if (!TryStringOrNumber(
                document.RootElement,
                "purchase_id",
                out var purchaseReference) ||
            !TryProperty(
                document.RootElement,
                "data",
                out var responseData))
            throw ProviderFailure("สร้างรายการจัดส่ง");

        var row = DescendantObjects(responseData)
            .FirstOrDefault(candidate =>
                HasProperty(candidate, "tracking_code"));
        if (row.ValueKind != JsonValueKind.Object ||
            TryBoolean(row, "status", true) == false ||
            !TryString(
                row,
                "tracking_code",
                out var providerTrackingCode) ||
            !TryString(
                row,
                "courier_code",
                out var returnedServiceCode) ||
            TryMapService(returnedServiceCode) is not
                { } returnedService ||
            !TryMoneySatang(
                row,
                "price",
                out var reservedFeeSatang))
            throw ProviderFailure("สร้างรายการจัดส่ง");
        if (!string.Equals(
                returnedServiceCode,
                request.Quote.ServiceCode,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                returnedService.CarrierCode,
                request.Quote.CarrierCode,
                StringComparison.Ordinal) ||
            reservedFeeSatang != request.Quote.FeeSatang)
            throw new DomainException(
                "ค่าจัดส่งเปลี่ยนแล้ว กรุณาดูราคาและยืนยันใหม่");

        TryString(
            row,
            "courier_tracking_code",
            out var courierTrackingCode);
        return new ShipmentReservation(
            Provider,
            purchaseReference,
            providerTrackingCode,
            EmptyToNull(courierTrackingCode),
            returnedService.CarrierCode,
            returnedServiceCode,
            reservedFeeSatang,
            clock.UtcNow);
    }

    public async Task<ShipmentConfirmation> ConfirmAsync(
        string purchaseReference,
        string providerTrackingCode,
        string carrierCode,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var content = new MultipartFormDataContent
        {
            { new StringContent(options.ApiKey), "api_key" },
            {
                new StringContent(
                    Required(
                        purchaseReference,
                        "เลขอ้างอิงรายการขนส่ง")),
                "purchase_id"
            }
        };
        using var document = await PostAsync(
            "confirm/",
            content,
            cancellationToken);
        EnsureProviderSuccess(
            document.RootElement,
            "ยืนยันค่าจัดส่ง");
        if (!TryProperty(
                document.RootElement,
                "result",
                out var result))
            throw ProviderFailure("ยืนยันค่าจัดส่ง");
        var row = DescendantObjects(result)
            .FirstOrDefault(candidate =>
                HasProperty(candidate, "tracking_code"));
        if (row.ValueKind != JsonValueKind.Object ||
            TryBoolean(row, "status", false) == false ||
            !TryString(
                row,
                "tracking_code",
                out var returnedProviderTracking) ||
            !SecureEquals(
                returnedProviderTracking,
                providerTrackingCode) ||
            !TryString(
                row,
                "courier_tracking_code",
                out var courierTrackingCode) ||
            string.IsNullOrWhiteSpace(courierTrackingCode) ||
            !TryString(
                row,
                "courier_code",
                out var serviceCode) ||
            TryMapService(serviceCode) is not { } service ||
            !string.Equals(
                service.CarrierCode,
                carrierCode,
                StringComparison.Ordinal))
            throw ProviderFailure("ยืนยันค่าจัดส่ง");

        return new ShipmentConfirmation(
            returnedProviderTracking,
            courierTrackingCode,
            service.CarrierCode,
            "booking",
            clock.UtcNow);
    }

    public async Task<ShipmentTrackingUpdate> GetTrackingAsync(
        string providerTrackingCode,
        string carrierCode,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var document = await PostJsonAsync(
            "tracking/",
            new
            {
                tracking_code = Required(
                    providerTrackingCode,
                    "หมายเลขติดตาม SHIPPOP")
            },
            cancellationToken);
        EnsureProviderSuccess(
            document.RootElement,
            "ติดตามพัสดุ");
        if (!TryString(
                document.RootElement,
                "tracking_code",
                out var returnedTracking) ||
            !SecureEquals(
                returnedTracking,
                providerTrackingCode) ||
            !TryString(
                document.RootElement,
                "order_status",
                out var providerStatus))
            throw ProviderFailure("ติดตามพัสดุ");

        TryString(
            document.RootElement,
            "courier_tracking_code",
            out var courierTracking);
        var returnedCarrier = carrierCode;
        if (TryString(
                document.RootElement,
                "courier_code",
                out var serviceCode))
        {
            var mapped = TryMapService(serviceCode)
                ?? throw ProviderFailure("ติดตามพัสดุ");
            returnedCarrier = mapped.CarrierCode;
        }
        if (!string.Equals(
                returnedCarrier,
                carrierCode,
                StringComparison.Ordinal))
            throw new DomainException(
                "บริษัทขนส่งจาก SHIPPOP ไม่ตรงกับรายการ");

        var eventType = MapProviderStatus(providerStatus);
        var occurredAt = LatestEventTime(
            document.RootElement,
            eventType);
        if (eventType == "delivered" &&
            !occurredAt.HasValue)
            eventType = "unverified";
        var eventId = ProviderEventId(
            providerTrackingCode,
            providerStatus,
            courierTracking,
            occurredAt);
        return new ShipmentTrackingUpdate(
            returnedTracking,
            EmptyToNull(courierTracking),
            returnedCarrier,
            providerStatus.Trim().ToLowerInvariant(),
            eventType,
            eventId,
            occurredAt);
    }

    public async Task<string> GetLabelHtmlAsync(
        ShipmentLabelRequest request,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var document = await PostJsonAsync(
            "label/",
            new
            {
                api_key = options.ApiKey,
                purchase_id = Required(
                    request.PurchaseReference,
                    "เลขอ้างอิงรายการขนส่ง"),
                type = "html",
                size = "sticker4x6"
            },
            cancellationToken);
        EnsureProviderSuccess(
            document.RootElement,
            "สร้างใบปะหน้า");
        if (!TryString(
                document.RootElement,
                "html",
                out var html) ||
            html.Length is < 20 or > MaximumProviderResponseBytes ||
            !html.Contains(
                "<html",
                StringComparison.OrdinalIgnoreCase))
            throw ProviderFailure("สร้างใบปะหน้า");
        return html;
    }

    public async Task CancelAsync(
        string courierTrackingCode,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var document = await PostJsonAsync(
            "cancel/",
            new
            {
                api_key = options.ApiKey,
                courier_tracking_code = Required(
                    courierTrackingCode,
                    "หมายเลขพัสดุ")
            },
            cancellationToken);
        EnsureProviderSuccess(
            document.RootElement,
            "ยกเลิกรายการจัดส่ง");
    }

    private object ShipmentPayload(
        ShippingQuoteRequest request,
        string serviceCode,
        string? includeReference,
        bool showAll)
    {
        var payload = new Dictionary<string, object?>
        {
            ["from"] = AddressPayload(request.Origin!),
            ["to"] = AddressPayload(request.Destination!),
            ["parcel"] = new
            {
                name = request.ParcelName.Trim(),
                weight = request.WeightGrams,
                width = request.WidthCentimeters,
                length = request.LengthCentimeters,
                height = request.HeightCentimeters
            },
            ["courier_code"] = serviceCode
        };
        if (showAll)
            payload["showall"] = 1;
        if (!string.IsNullOrWhiteSpace(includeReference))
            payload["meta"] = new
            {
                ref_no_1 = includeReference
            };
        return payload;
    }

    private static object AddressPayload(
        ShippingContactAddress address) => new
    {
        name = address.Name.Trim(),
        address = address.AddressLine.Trim(),
        district = address.SubdistrictName.Trim(),
        state = address.DistrictName.Trim(),
        province = address.ProvinceName.Trim(),
        postcode = address.PostalCode.Trim(),
        tel = address.PhoneNumber.Trim()
    };

    private async Task<JsonDocument> PostJsonAsync(
        string relativeUrl,
        object payload,
        CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(
            payload,
            options: JsonOptions);
        return await PostAsync(
            relativeUrl,
            content,
            cancellationToken);
    }

    private async Task<JsonDocument> PostAsync(
        string relativeUrl,
        HttpContent content,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            relativeUrl)
        {
            Content = content
        };
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw ProviderFailure("ติดต่อ SHIPPOP");
        var declaredLength =
            response.Content.Headers.ContentLength;
        if (declaredLength >
            MaximumProviderResponseBytes)
            throw ProviderFailure(
                "อ่านคำตอบจาก SHIPPOP");
        await using var responseStream =
            await response.Content.ReadAsStreamAsync(
                cancellationToken);
        using var buffered = declaredLength is > 0
            ? new MemoryStream(
                (int)Math.Min(
                    declaredLength.Value,
                    MaximumProviderResponseBytes))
            : new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await responseStream.ReadAsync(
                buffer,
                cancellationToken);
            if (read == 0)
                break;
            if (buffered.Length + read >
                MaximumProviderResponseBytes)
                throw ProviderFailure(
                    "อ่านคำตอบจาก SHIPPOP");
            buffered.Write(buffer, 0, read);
        }
        try
        {
            return JsonDocument.Parse(
                buffered.GetBuffer()
                    .AsMemory(
                        0,
                        checked((int)buffered.Length)),
                new JsonDocumentOptions
                {
                    MaxDepth = 32
                });
        }
        catch (JsonException)
        {
            throw ProviderFailure("อ่านคำตอบจาก SHIPPOP");
        }
    }

    private string CreateQuoteReference(
        ShippingQuoteRequest request,
        string serviceCode,
        long feeSatang,
        DateTimeOffset expiresAt)
    {
        var unsigned = string.Join(
            '.',
            "sp1",
            expiresAt.ToUnixTimeSeconds()
                .ToString(CultureInfo.InvariantCulture),
            feeSatang.ToString(CultureInfo.InvariantCulture),
            serviceCode.ToUpperInvariant(),
            RequestFingerprint(request),
            Base64Url(RandomNumberGenerator.GetBytes(8)));
        return $"{unsigned}.{Sign(unsigned)}";
    }

    private bool VerifyQuoteSignature(
        IReadOnlyList<string> parts)
    {
        var unsigned = string.Join('.', parts.Take(6));
        var expected = Sign(unsigned);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(parts[6]));
    }

    private string Sign(string value)
    {
        using var hmac = new HMACSHA256(
            Encoding.UTF8.GetBytes(
                options.QuoteSigningSecret));
        return Base64Url(
            hmac.ComputeHash(
                Encoding.UTF8.GetBytes(value))[..16]);
    }

    private static string RequestFingerprint(
        ShippingQuoteRequest request)
    {
        var canonical = string.Join(
            '\n',
            request.OriginPostalCode.Trim(),
            request.DestinationPostalCode.Trim(),
            request.WeightGrams,
            request.WidthCentimeters,
            request.LengthCentimeters,
            request.HeightCentimeters,
            ContactFingerprint(request.Origin),
            ContactFingerprint(request.Destination),
            request.ParcelName.Trim());
        return Base64Url(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(canonical))[..16]);
    }

    private static string ContactFingerprint(
        ShippingContactAddress? value) =>
        value is null
            ? ""
            : string.Join(
                '|',
                value.Name.Trim(),
                value.PhoneNumber.Trim(),
                value.AddressLine.Trim(),
                value.SubdistrictName.Trim(),
                value.DistrictName.Trim(),
                value.ProvinceName.Trim(),
                value.PostalCode.Trim());

    private static string Base64Url(
        ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private void EnsureConfigured()
    {
        if (!Uri.TryCreate(
                options.BaseUrl,
                UriKind.Absolute,
                out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(options.ApiKey) ||
            string.IsNullOrWhiteSpace(options.AccountEmail) ||
            options.QuoteSigningSecret.Length < 32)
            throw new InvalidOperationException(
                "การตั้งค่า SHIPPOP ยังไม่ครบ");
    }

    private static void ValidateRequest(
        ShippingQuoteRequest request)
    {
        if (request.WeightGrams is < 1 or > 30_000)
            throw new DomainException(
                "น้ำหนักพัสดุต้องอยู่ระหว่าง 1 กรัมถึง 30 กิโลกรัม");
        if (request.WidthCentimeters is < 1 or > 200 ||
            request.LengthCentimeters is < 1 or > 200 ||
            request.HeightCentimeters is < 1 or > 200)
            throw new DomainException(
                "ขนาดพัสดุแต่ละด้านต้องอยู่ระหว่าง 1–200 ซม.");
        ValidateContact(request.Origin, "ต้นทาง");
        ValidateContact(request.Destination, "ปลายทาง");
        if (string.IsNullOrWhiteSpace(request.ParcelName) ||
            request.ParcelName.Trim().Length > 180)
            throw new DomainException(
                "ชื่อสินค้าสำหรับจัดส่งไม่ถูกต้อง");
    }

    private static void ValidateContact(
        ShippingContactAddress? address,
        string label)
    {
        if (address is null ||
            string.IsNullOrWhiteSpace(address.Name) ||
            string.IsNullOrWhiteSpace(address.PhoneNumber) ||
            string.IsNullOrWhiteSpace(address.AddressLine) ||
            string.IsNullOrWhiteSpace(address.SubdistrictName) ||
            string.IsNullOrWhiteSpace(address.DistrictName) ||
            string.IsNullOrWhiteSpace(address.ProvinceName) ||
            address.PostalCode.Length != 5 ||
            address.PostalCode.Any(character => !char.IsDigit(character)))
            throw new DomainException(
                $"ข้อมูลที่อยู่{label}สำหรับ SHIPPOP ไม่ครบ");
    }

    private static void EnsureProviderSuccess(
        JsonElement root,
        string action)
    {
        if (TryBoolean(root, "status", false) == false)
            throw ProviderFailure(action);
    }

    private static IEnumerable<JsonElement> DescendantObjects(
        JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            yield return element;
            foreach (var property in element.EnumerateObject())
                foreach (var child in DescendantObjects(
                             property.Value))
                    yield return child;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                foreach (var child in DescendantObjects(item))
                    yield return child;
        }
    }

    private static bool TryMoneySatang(
        JsonElement element,
        string name,
        out long satang)
    {
        satang = 0;
        if (!TryProperty(element, name, out var property))
            return false;
        decimal baht;
        if (property.ValueKind == JsonValueKind.Number)
        {
            if (!property.TryGetDecimal(out baht))
                return false;
        }
        else if (property.ValueKind == JsonValueKind.String)
        {
            if (!decimal.TryParse(
                    property.GetString(),
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out baht))
                return false;
        }
        else
        {
            return false;
        }
        if (baht <= 0)
            return false;
        try
        {
            satang = checked(
                (long)decimal.Round(
                    baht * 100m,
                    0,
                    MidpointRounding.AwayFromZero));
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool TryBoolean(
        JsonElement element,
        string name,
        bool defaultValue)
    {
        if (!TryProperty(element, name, out var property))
            return defaultValue;
        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number =>
                property.TryGetInt32(out var value) && value != 0,
            JsonValueKind.String =>
                bool.TryParse(
                    property.GetString(),
                    out var value) && value ||
                property.GetString() == "1",
            _ => defaultValue
        };
    }

    private static bool TryString(
        JsonElement element,
        string name,
        out string value)
    {
        value = "";
        if (!TryProperty(element, name, out var property) ||
            property.ValueKind != JsonValueKind.String)
            return false;
        value = property.GetString()?.Trim() ?? "";
        return value.Length > 0;
    }

    private static bool TryStringOrNumber(
        JsonElement element,
        string name,
        out string value)
    {
        if (TryString(element, name, out value))
            return true;
        value = "";
        if (!TryProperty(element, name, out var property) ||
            property.ValueKind != JsonValueKind.Number)
            return false;
        value = property.GetRawText();
        return value.Length > 0;
    }

    private static bool HasProperty(
        JsonElement element,
        string name) =>
        TryProperty(element, name, out _);

    private static bool TryProperty(
        JsonElement element,
        string name,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(name, out value))
            return true;
        value = default;
        return false;
    }

    private static ServiceMapping? TryMapService(
        string? serviceCode) =>
        (serviceCode ?? "").Trim().ToUpperInvariant() switch
        {
            "EMST" => new(
                "THAIPOST",
                "ไปรษณีย์ไทย EMS"),
            "FLE" => new(
                "FLASH",
                "Flash Express"),
            "KRYX" => new(
                "KERRY",
                "KEX Express"),
            "KRYS" => new(
                "KERRY",
                "KEX Shop"),
            _ => null
        };

    private static string? MapProviderStatus(
        string providerStatus) =>
        providerStatus.Trim().ToLowerInvariant() switch
        {
            "shipping" => "in_transit",
            "complete" => "delivered",
            "invalid" or "problem" or "return" or
                "return_shipping" or "return_problem" or
                "return_complete" or "return_return" or
                "return_close" => "unverified",
            _ => null
        };

    private static DateTimeOffset? LatestEventTime(
        JsonElement root,
        string? eventType)
    {
        if (eventType == "in_transit" &&
            TryString(
                root,
                "datetime_shipping",
                out var firstShippedAt))
        {
            var parsedFirstShippedAt =
                ParseThailandTime(firstShippedAt);
            if (parsedFirstShippedAt.HasValue)
                return parsedFirstShippedAt;
        }
        if (TryProperty(root, "states", out var states) &&
            states.ValueKind == JsonValueKind.Array)
        {
            var candidates = states.EnumerateArray()
                .Select(state => new
                {
                    State = state,
                    Time = TryString(
                            state,
                            "datetime",
                            out var value)
                        ? ParseThailandTime(value)
                        : null
                })
                .Where(item => item.Time.HasValue);
            if (eventType == "delivered")
            {
                var delivered = candidates
                    .Where(item =>
                        TryString(
                            item.State,
                            "status",
                            out var status) &&
                        status.Equals(
                            "POD",
                            StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(item => item.Time)
                    .FirstOrDefault();
                if (delivered?.Time is not null)
                    return delivered.Time;
                return null;
            }
            if (eventType == "in_transit")
            {
                var firstScan = candidates
                    .OrderBy(item => item.Time)
                    .FirstOrDefault();
                if (firstScan?.Time is not null)
                    return firstScan.Time;
            }
            var latest = candidates
                .OrderByDescending(item => item.Time)
                .FirstOrDefault();
            if (latest?.Time is not null)
                return latest.Time;
        }
        if (TryString(
                root,
                "datetime_shipping",
                out var shippedAt))
            return ParseThailandTime(shippedAt);
        return null;
    }

    private static DateTimeOffset? ParseThailandTime(
        string value)
    {
        if (!DateTime.TryParseExact(
                value,
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
            return null;
        return new DateTimeOffset(
            DateTime.SpecifyKind(
                parsed,
                DateTimeKind.Unspecified),
            TimeSpan.FromHours(7));
    }

    private static string ProviderEventId(
        string trackingCode,
        string providerStatus,
        string courierTrackingCode,
        DateTimeOffset? occurredAt)
    {
        var digest = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                string.Join(
                    '|',
                    trackingCode,
                    providerStatus.Trim().ToLowerInvariant(),
                    courierTrackingCode,
                    occurredAt.HasValue
                        ? occurredAt.Value
                            .ToUnixTimeSeconds()
                            .ToString(
                                CultureInfo.InvariantCulture)
                        : "missing-time")));
        return $"shippop-{Convert.ToHexString(digest[..16]).ToLowerInvariant()}";
    }

    private static bool SafeServiceCode(
        string value) =>
        value.Length is >= 2 and <= 20 &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) ||
            character == '_');

    private static string Required(
        string? value,
        string label)
    {
        var clean = value?.Trim() ?? "";
        if (clean.Length == 0)
            throw new DomainException($"กรุณาระบุ{label}");
        return clean;
    }

    private static string? EmptyToNull(
        string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static bool SecureEquals(
        string left,
        string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(
                   leftBytes,
                   rightBytes);
    }

    private static DomainException ExpiredQuote() =>
        new(
            "ราคาค่าจัดส่งหมดอายุหรือข้อมูลพัสดุเปลี่ยน กรุณาดูราคาใหม่");

    private static InvalidOperationException ProviderFailure(
        string action) =>
        new(
            $"{action}ผ่าน SHIPPOP ไม่สำเร็จ กรุณาลองใหม่");

    private sealed record ServiceMapping(
        string CarrierCode,
        string DisplayName);
}
