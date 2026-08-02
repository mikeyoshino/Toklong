using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Toklong.Application.Abstractions;
using Toklong.Domain.Common;
using Toklong.Domain.Transactions;
using QRCoder;

namespace Toklong.Infrastructure.Services;

public sealed class DevelopmentShippingQuoteProvider(
    IClock clock) : IShippingQuoteProvider,
    IParcelProtectionQuoteProvider,
    IShipmentProvider
{
    private readonly ConcurrentDictionary<
        string,
        StoredQuote> quotes = new();
    private readonly ConcurrentDictionary<
        string,
        StoredProtectionOption> protectionOptions = new();

    private const long IncludedCoverageLimitSatang = 100_000;
    private const string ProtectionTermsVersion =
        "development-parcel-protection-v1";

    public Task<IReadOnlyList<ShippingQuoteOption>> GetQuotesAsync(
        ShippingQuoteRequest request,
        CancellationToken cancellationToken)
    {
        ValidatePackage(request);
        var expiresAt = clock.UtcNow.AddHours(2);
        IReadOnlyList<ShippingQuoteOption> result =
        [
            Create(
                request,
                "THAIPOST",
                "EMS",
                "ไปรษณีย์ไทย EMS",
                4_500,
                expiresAt),
            Create(
                request,
                "FLASH",
                "STANDARD",
                "Flash Express Standard",
                5_000,
                expiresAt),
            Create(
                request,
                "KERRY",
                "STANDARD",
                "KEX Express Standard",
                5_500,
                expiresAt)
        ];
        return Task.FromResult(result);
    }

    public Task<ShippingQuoteOption> ValidateQuoteAsync(
        ShippingQuoteRequest request,
        string quoteReference,
        long disclosedFeeSatang,
        CancellationToken cancellationToken)
    {
        ValidatePackage(request);
        if (!quotes.TryGetValue(
                quoteReference.Trim(),
                out var stored) ||
            stored.Request != request ||
            stored.Option.FeeSatang != disclosedFeeSatang ||
            stored.Option.ExpiresAt <= clock.UtcNow)
            throw new DomainException(
                "ราคาค่าจัดส่งหมดอายุหรือข้อมูลพัสดุเปลี่ยน กรุณาดูราคาใหม่");
        return Task.FromResult(stored.Option);
    }

    private ShippingQuoteOption Create(
        ShippingQuoteRequest request,
        string carrierCode,
        string serviceCode,
        string serviceName,
        long baseFeeSatang,
        DateTimeOffset expiresAt)
    {
        var volumeWeightGrams = checked(
            (long)request.WidthCentimeters *
            request.LengthCentimeters *
            request.HeightCentimeters * 1_000 / 5_000);
        var billableWeight = Math.Max(
            request.WeightGrams,
            volumeWeightGrams);
        var additionalKilograms =
            Math.Max(
                0,
                (billableWeight - 1_000 + 999) /
                1_000);
        var feeSatang = checked(
            baseFeeSatang +
            additionalKilograms * 1_000L);
        var reference =
            $"dev-ship-{Guid.NewGuid():N}";
        var option = new ShippingQuoteOption(
            "development-shipping",
            reference,
            carrierCode,
            serviceCode,
            serviceName,
            feeSatang,
            0,
            0,
            null,
            expiresAt);
        quotes[reference] = new StoredQuote(
            request,
            option);
        return option;
    }

    private static void ValidatePackage(
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
    }

    private sealed record StoredQuote(
        ShippingQuoteRequest Request,
        ShippingQuoteOption Option);

    public Task<ParcelProtectionAvailability> GetAvailabilityAsync(
        ParcelProtectionQuoteRequest request,
        CancellationToken cancellationToken)
    {
        ValidateProtectionRequest(request);
        ValidateDeliveryQuote(request);
        if (request.BuyerPaymentDeadlineAt <= clock.UtcNow)
            throw new DomainException(
                "หมดเวลาชำระแล้ว กรุณาส่งข้อเสนอใหม่ให้ผู้ขายยืนยัน");
        if (request.ItemPriceSatang <= IncludedCoverageLimitSatang)
            return Task.FromResult(
                new ParcelProtectionAvailability(
                    IncludedCoverageLimitSatang,
                    null,
                    ProviderCapabilityCertified: true));

        var existing = protectionOptions.Values
            .Where(stored => stored.Request == request)
            .Select(stored => stored.Option)
            .FirstOrDefault(option => option.ExpiresAt > clock.UtcNow);
        if (existing is not null)
            return Task.FromResult(
                new ParcelProtectionAvailability(
                    IncludedCoverageLimitSatang,
                    existing,
                    ProviderCapabilityCertified: true));

        var quotedAt = clock.UtcNow;
        var expiresAt = quotedAt.AddHours(1) <
            request.BuyerPaymentDeadlineAt
                ? quotedAt.AddHours(1)
                : request.BuyerPaymentDeadlineAt;
        var option = new ProviderParcelProtectionOption(
            ProviderName,
            $"dev-protection-{Guid.NewGuid():N}",
            IncludedCoverageLimitSatang,
            request.ItemPriceSatang,
            Math.Max(100, request.ItemPriceSatang / 100),
            ProtectionTermsVersion,
            "DEV_PARCEL_PROTECTION",
            quotedAt,
            expiresAt);
        protectionOptions[option.OptionReference] = new(
            request,
            option);
        return Task.FromResult(
            new ParcelProtectionAvailability(
                IncludedCoverageLimitSatang,
                option,
                ProviderCapabilityCertified: true));
    }

    public Task<ProviderParcelProtectionOption> ValidateOptionAsync(
        ParcelProtectionQuoteRequest request,
        string optionReference,
        CancellationToken cancellationToken)
    {
        ValidateProtectionRequest(request);
        ValidateDeliveryQuote(request);
        if (!protectionOptions.TryGetValue(
                optionReference?.Trim() ?? "",
                out var stored) ||
            stored.Request != request)
            throw new ParcelProtectionOptionChangedException(
                "parcel-protection-option-changed");
        if (stored.Option.ExpiresAt <= clock.UtcNow)
            throw new ParcelProtectionOptionChangedException(
                "parcel-protection-option-changed");
        return Task.FromResult(stored.Option);
    }

    private void ValidateProtectionRequest(
        ParcelProtectionQuoteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidatePackage(request.Shipment);
        if (request.ItemPriceSatang <= 0 ||
            request.DeliveryQuoteExpiresAt == default ||
            request.BuyerPaymentDeadlineAt == default ||
            string.IsNullOrWhiteSpace(request.DeliveryQuoteReference) ||
            string.IsNullOrWhiteSpace(request.CarrierCode) ||
            string.IsNullOrWhiteSpace(request.ServiceCode))
            throw new DomainException("ข้อมูลความคุ้มครองพัสดุไม่ถูกต้อง");
    }

    private void ValidateDeliveryQuote(
        ParcelProtectionQuoteRequest request)
    {
        var reference = request.DeliveryQuoteReference.Trim();
        if (quotes.TryGetValue(reference, out var stored))
        {
            if (stored.Request == request.Shipment &&
                stored.Option.ExpiresAt > clock.UtcNow &&
                string.Equals(
                    stored.Option.CarrierCode,
                    request.CarrierCode,
                    StringComparison.Ordinal) &&
                string.Equals(
                    stored.Option.ServiceCode,
                    request.ServiceCode,
                    StringComparison.Ordinal))
                return;
        }
        else if (IsDevelopmentQuoteReference(reference) &&
                 request.DeliveryQuoteExpiresAt > clock.UtcNow)
        {
            // Protection requests are built from the immutable, previously
            // validated seller-acceptance snapshot. Rehydrate that accepted
            // development quote after a local API restart.
            return;
        }

        throw new DomainException(
            "ราคาค่าจัดส่งหมดอายุหรือข้อมูลพัสดุเปลี่ยน กรุณาดูราคาใหม่");
    }

    private static bool IsDevelopmentQuoteReference(string value)
    {
        if (value.Length != 41 ||
            !value.StartsWith("dev-ship-", StringComparison.Ordinal))
            return false;
        foreach (var character in value.AsSpan(9))
        {
            if (character is not (>= '0' and <= '9') and
                not (>= 'a' and <= 'f'))
                return false;
        }
        return true;
    }

    private sealed record StoredProtectionOption(
        ParcelProtectionQuoteRequest Request,
        ProviderParcelProtectionOption Option);

    public string ProviderName => "development-shipping";

    public Task<ShipmentReservation> ReserveAsync(
        ShipmentReservationRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                request.Quote.Provider,
                ProviderName,
                StringComparison.Ordinal))
            throw new DomainException(
                "ผู้ให้บริการขนส่งไม่ตรงกับราคาที่เลือก");
        var providerTracking =
            $"DEVSP{request.TransactionId:N}".ToUpperInvariant();
        var courierTracking = DevelopmentCourierTracking(
            request.Quote.CarrierCode,
            request.TransactionId);
        return Task.FromResult(
            new ShipmentReservation(
                ProviderName,
                $"dev-purchase-{request.TransactionId:N}",
                providerTracking,
                courierTracking,
                request.Quote.CarrierCode,
                request.Quote.ServiceCode,
                request.Quote.FeeSatang,
                request.Quote.InsuranceFeeSatang,
                request.Quote.DeclaredValueSatang,
                request.Quote.InsuranceCode,
                clock.UtcNow));
    }

    public Task<ShipmentTrackingUpdate> GetTrackingAsync(
        string providerTrackingCode,
        string carrierCode,
        CancellationToken cancellationToken) =>
        Task.FromResult(
            new ShipmentTrackingUpdate(
                providerTrackingCode,
                null,
                carrierCode,
                "booking",
                null,
                $"development:{providerTrackingCode}:booking",
                clock.UtcNow));

    public Task<ShipmentConfirmation> ConfirmAsync(
        string purchaseReference,
        string providerTrackingCode,
        string carrierCode,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(
                providerTrackingCode["DEVSP".Length..],
                "N",
                out var transactionId))
            throw new DomainException(
                "เลขอ้างอิงขนส่งจำลองไม่ถูกต้อง");
        return Task.FromResult(
            new ShipmentConfirmation(
                providerTrackingCode,
                DevelopmentCourierTracking(
                    carrierCode,
                    transactionId),
                carrierCode,
                "booking",
                clock.UtcNow));
    }

    public Task<string> GetLabelHtmlAsync(
        ShipmentLabelRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tracking = request.TrackingNumber
            .Trim()
            .ToUpperInvariant();
        var barcode = Code39Svg(tracking);
        var weightKilograms =
            (request.WeightGrams / 1_000m)
            .ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        var html = $$"""
            <!doctype html>
            <html lang="th">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <title>ใบปะหน้า {{Encode(tracking)}}</title>
              <style>
                @page { size: 4in 6in; margin: 0; }
                * { box-sizing: border-box; }
                html { background: #fff; }
                body {
                  width: 4in;
                  min-height: 6in;
                  margin: 0 auto;
                  padding: .18in;
                  color: #101828;
                  background: #fff;
                  font: 12px/1.35 -apple-system, BlinkMacSystemFont,
                    "Noto Sans Thai", "Segoe UI", sans-serif;
                }
                header {
                  display: flex;
                  align-items: center;
                  justify-content: space-between;
                  padding-bottom: .1in;
                  border-bottom: 2px solid #101828;
                }
                header strong { color: #6548c7; font-size: 25px; letter-spacing: .04em; }
                .service { padding: 6px 9px; color: #fff; background: #101828; font-weight: 700; }
                .row { display: grid; grid-template-columns: 1fr 1.18fr; border-bottom: 1px dashed #101828; }
                .cell { min-width: 0; padding: .12in .06in; }
                .cell + .cell { border-left: 1px solid #101828; }
                .label { margin-bottom: 4px; color: #475467; font-size: 10px; }
                .name { margin-bottom: 4px; font-size: 14px; font-weight: 700; }
                .address { min-height: 54px; overflow-wrap: anywhere; }
                .tracking { margin-bottom: 5px; font-size: 15px; font-weight: 700; letter-spacing: .02em; }
                .barcode { width: 100%; margin-top: 5px; }
                .barcode-text { text-align: center; font-size: 11px; letter-spacing: .08em; }
                .paid-row { display: grid; grid-template-columns: 1fr auto; align-items: center; gap: 12px; padding: .12in .06in; }
                .paid { width: fit-content; padding: 7px 10px; border: 1.5px solid #087c68; border-radius: 7px; color: #087c68; font-size: 15px; }
                .weight { text-align: right; font-size: 15px; }
                footer { display: flex; align-items: center; justify-content: space-between; gap: 12px; margin-top: .12in; padding: .1in; border: 1.5px solid #101828; }
                footer strong { display: block; margin-bottom: 3px; font-size: 13px; }
                footer span { color: #475467; font-size: 10px; }
                .dropoff { text-align: center; font-size: 12px; }
              </style>
            </head>
            <body>
              <header>
                <strong>TOKLONG</strong>
                <span class="service">{{Encode(request.ServiceName)}}</span>
              </header>
              <section class="row">
                <div class="cell">
                  <div class="label">จาก (Sender)</div>
                  <div class="name">{{Encode(request.Origin.Name)}}</div>
                  <div class="address">{{Address(request.Origin)}}</div>
                  <div>{{Encode(request.Origin.PhoneNumber)}}</div>
                </div>
                <div class="cell">
                  <div class="label">เลขพัสดุ (Tracking No.)</div>
                  <div class="tracking">{{Encode(tracking)}}</div>
                  <div class="barcode">{{barcode}}</div>
                  <div class="barcode-text">{{Encode(tracking)}}</div>
                </div>
              </section>
              <section class="row">
                <div class="cell">
                  <div class="label">ถึง (Recipient)</div>
                  <div class="name">{{Encode(request.Destination.Name)}}</div>
                  <div class="address">{{Address(request.Destination)}}</div>
                  <div>{{Encode(request.Destination.PhoneNumber)}}</div>
                </div>
                <div class="cell paid-row">
                  <div class="paid">ชำระแล้ว</div>
                  <div class="weight">
                    <div class="label">น้ำหนัก</div>
                    <strong>{{weightKilograms}} KG</strong>
                  </div>
                </div>
              </section>
              <footer>
                <div>
                  <strong>ส่งกับ {{Encode(request.ServiceName)}}</strong>
                  <span>ปฏิบัติตามวิธีนำส่งที่บริษัทขนส่งกำหนด</span>
                </div>
                <div class="dropoff">พัสดุพร้อมส่ง</div>
              </footer>
            </body>
            </html>
            """;
        return Task.FromResult(html);
    }

    public Task<CounterQrReadResult> GetCounterQrAsync(
        CounterQrRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TransactionId == Guid.Empty ||
            request.ManagedShipmentId == Guid.Empty ||
            !string.Equals(
                request.Provider,
                ProviderName,
                StringComparison.Ordinal))
            throw new DomainException(
                "ข้อมูล QR สำหรับจัดส่งไม่ถูกต้อง");
        var png = PngByteQRCodeHelper.GetQRCode(
            $"TOKLONG-DEVELOPMENT-COUNTER:{request.ManagedShipmentId:N}",
            QRCodeGenerator.ECCLevel.Q,
            12);
        var digest = Convert.ToHexString(
                SHA256.HashData(png))
            .ToLowerInvariant();
        return Task.FromResult(new CounterQrReadResult(
            CounterQrReadStatus.Ready,
            CounterQrRepresentation.ProviderPng,
            png,
            digest,
            null,
            clock.UtcNow,
            null));
    }

    public Task CancelAsync(
        string courierTrackingCode,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;

    private static string DevelopmentCourierTracking(
        string carrierCode,
        Guid transactionId)
    {
        var digest = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                transactionId.ToString("N")));
        var digits = string.Concat(
            digest.Take(12).Select(
                value => (char)('0' + value % 10)));
        return carrierCode.ToUpperInvariant() switch
        {
            "THAIPOST" => $"EF{digits[..9]}TH",
            "FLASH" => $"TH{digits}",
            "KERRY" => $"KEX{digits}",
            _ => $"DEV{digits}"
        };
    }

    private static string Address(
        ShippingContactAddress address) =>
        string.Join(
            " ",
            new[]
            {
                address.AddressLine,
                address.SubdistrictName,
                address.DistrictName,
                address.ProvinceName,
                address.PostalCode
            }.Where(value =>
                !string.IsNullOrWhiteSpace(value))
             .Select(Encode));

    private static string Encode(string value) =>
        HtmlEncoder.Default.Encode(value);

    private static string Code39Svg(string value)
    {
        if (value.Length is < 1 or > 64 ||
            value.Any(character =>
                Code39Pattern(character) is null))
            throw new DomainException(
                "เลขพัสดุไม่รองรับการสร้างบาร์โค้ดจำลอง");

        var encoded = $"*{value}*";
        const int narrow = 2;
        const int wide = 5;
        const int gap = 2;
        const int quiet = 14;
        var width = quiet * 2;
        foreach (var character in encoded)
            width += Code39Pattern(character)!
                .Sum(module => module == 'w' ? wide : narrow) + gap;

        var svg = new StringBuilder(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" " +
            $"viewBox=\"0 0 {width} 74\" preserveAspectRatio=\"none\" " +
            $"role=\"img\" aria-label=\"บาร์โค้ด {Encode(value)}\">");
        var x = quiet;
        foreach (var character in encoded)
        {
            var pattern = Code39Pattern(character)!;
            for (var index = 0; index < pattern.Length; index++)
            {
                var moduleWidth =
                    pattern[index] == 'w' ? wide : narrow;
                if (index % 2 == 0)
                    svg.Append(
                        $"<rect x=\"{x}\" y=\"0\" " +
                        $"width=\"{moduleWidth}\" height=\"74\"/>");
                x += moduleWidth;
            }
            x += gap;
        }
        svg.Append("</svg>");
        return svg.ToString();
    }

    private static string? Code39Pattern(char value) => value switch
    {
        '0' => "nnnwwnwnn",
        '1' => "wnnwnnnnw",
        '2' => "nnwwnnnnw",
        '3' => "wnwwnnnnn",
        '4' => "nnnwwnnnw",
        '5' => "wnnwwnnnn",
        '6' => "nnwwwnnnn",
        '7' => "nnnwnnwnw",
        '8' => "wnnwnnwnn",
        '9' => "nnwwnnwnn",
        'A' => "wnnnnwnnw",
        'B' => "nnwnnwnnw",
        'C' => "wnwnnwnnn",
        'D' => "nnnnwwnnw",
        'E' => "wnnnwwnnn",
        'F' => "nnwnwwnnn",
        'G' => "nnnnnwwnw",
        'H' => "wnnnnwwnn",
        'I' => "nnwnnwwnn",
        'J' => "nnnnwwwnn",
        'K' => "wnnnnnnww",
        'L' => "nnwnnnnww",
        'M' => "wnwnnnnwn",
        'N' => "nnnnwnnww",
        'O' => "wnnnwnnwn",
        'P' => "nnwnwnnwn",
        'Q' => "nnnnnnwww",
        'R' => "wnnnnnwwn",
        'S' => "nnwnnnwwn",
        'T' => "nnnnwnwwn",
        'U' => "wwnnnnnnw",
        'V' => "nwwnnnnnw",
        'W' => "wwwnnnnnn",
        'X' => "nwnnwnnnw",
        'Y' => "wwnnwnnnn",
        'Z' => "nwwnwnnnn",
        '-' => "nwnnnnwnw",
        '.' => "wwnnnnwnn",
        ' ' => "nwwnnnwnn",
        '$' => "nwnwnwnnn",
        '/' => "nwnwnnnwn",
        '+' => "nwnnnwnwn",
        '%' => "nnnwnwnwn",
        '*' => "nwnnwnwnn",
        _ => null
    };
}

public sealed class UnavailableShippingQuoteProvider
    : IShippingQuoteProvider,
    IParcelProtectionQuoteProvider,
    IShipmentProvider
{
    private const string Message =
        "ยังไม่ได้เชื่อมและอนุมัติผู้ให้บริการขนส่งสำหรับ production";

    public string ProviderName => "unavailable";

    public Task<IReadOnlyList<ShippingQuoteOption>> GetQuotesAsync(
        ShippingQuoteRequest request,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(Message);

    public Task<ShippingQuoteOption> ValidateQuoteAsync(
        ShippingQuoteRequest request,
        string quoteReference,
        long disclosedFeeSatang,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(Message);

    public Task<ParcelProtectionAvailability> GetAvailabilityAsync(
        ParcelProtectionQuoteRequest request,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(Message);

    public Task<ProviderParcelProtectionOption> ValidateOptionAsync(
        ParcelProtectionQuoteRequest request,
        string optionReference,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(Message);

    public Task<ShipmentReservation> ReserveAsync(
        ShipmentReservationRequest request,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(Message);

    public Task<ShipmentTrackingUpdate> GetTrackingAsync(
        string providerTrackingCode,
        string carrierCode,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(Message);

    public Task<ShipmentConfirmation> ConfirmAsync(
        string purchaseReference,
        string providerTrackingCode,
        string carrierCode,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(Message);

    public Task<string> GetLabelHtmlAsync(
        ShipmentLabelRequest request,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(Message);

    public Task CancelAsync(
        string courierTrackingCode,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(Message);
}
