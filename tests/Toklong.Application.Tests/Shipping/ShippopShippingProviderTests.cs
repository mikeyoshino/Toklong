using System.Net;
using System.Text;
using System.Text.Json;
using Toklong.Application.Abstractions;
using Toklong.Domain.Common;
using Toklong.Infrastructure.Services;

namespace Toklong.Application.Tests.Shipping;

public sealed class ShippopShippingProviderTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 26, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Quote_is_integer_satang_and_signed_for_exact_request()
    {
        string? body = null;
        var provider = Provider(async request =>
        {
            body = await request.Content!.ReadAsStringAsync();
            return Json(
                """
                {
                  "status": true,
                  "data": {
                    "0": {
                      "EMST": {
                        "available": true,
                        "courier_code": "EMST",
                        "courier_name": "EMS Thailand Post",
                        "price": "52.00"
                      }
                    }
                  }
                }
                """);
        });
        var shipment = Request();

        var quotes = await provider.GetQuotesAsync(
            shipment,
            default);

        var quote = Assert.Single(quotes);
        Assert.Equal(5_200, quote.FeeSatang);
        Assert.Equal("THAIPOST", quote.CarrierCode);
        Assert.Equal("EMST", quote.ServiceCode);
        Assert.StartsWith("sp1.", quote.QuoteReference);
        Assert.Contains("\"api_key\":\"api-key-for-tests\"", body);
        Assert.Contains("\"weight\":1200", body);

        var validated = await provider.ValidateQuoteAsync(
            shipment,
            quote.QuoteReference,
            5_200,
            default);
        Assert.Equal(quote.FeeSatang, validated.FeeSatang);
        await Assert.ThrowsAsync<DomainException>(() =>
            provider.ValidateQuoteAsync(
                shipment with
                {
                    WeightGrams = 1_201
                },
                quote.QuoteReference,
                5_200,
                default));
        await Assert.ThrowsAsync<DomainException>(() =>
            provider.ValidateQuoteAsync(
                shipment,
                quote.QuoteReference,
                5_201,
                default));
    }

    [Fact]
    public async Task Reservation_is_unconfirmed_and_price_must_match_quote()
    {
        string? body = null;
        var provider = Provider(async request =>
        {
            body = await request.Content!.ReadAsStringAsync();
            return Json(
                """
                {
                  "status": true,
                  "purchase_id": 452002,
                  "total_price": 52,
                  "data": {
                    "0": {
                      "status": true,
                      "tracking_code": "SP452045855",
                      "courier_tracking_code": "EF123456789TH",
                      "courier_code": "EMST",
                      "price": 52
                    }
                  }
                }
                """);
        });
        var shipment = Request();
        var quote = Quote();

        var reservation = await provider.ReserveAsync(
            new ShipmentReservationRequest(
                Guid.Parse(
                    "11111111-2222-3333-4444-555555555555"),
                shipment,
                quote),
            default);

        Assert.Equal("452002", reservation.PurchaseReference);
        Assert.Equal(
            "SP452045855",
            reservation.ProviderTrackingCode);
        Assert.Equal(
            "EF123456789TH",
            reservation.CourierTrackingCode);
        Assert.Equal(5_200, reservation.FeeSatang);
        Assert.Contains("\"force_confirm\":0", body);
        Assert.Contains(
            "\"ref_no_1\":\"11111111222233334444555555555555\"",
            body);
    }

    [Fact]
    public async Task Provider_delivery_maps_to_verified_delivery_time()
    {
        var provider = Provider(_ =>
            Task.FromResult(
                Json(
                    """
                    {
                      "status": true,
                      "order_status": "complete",
                      "courier_code": "EMST",
                      "tracking_code": "SP529189074",
                      "courier_tracking_code": "EF123456789TH",
                      "states": [
                        {
                          "status": "010",
                          "datetime": "2026-07-26 09:00:00"
                        },
                        {
                          "status": "POD",
                          "datetime": "2026-07-26 11:17:52"
                        }
                      ]
                    }
                    """)));

        var update = await provider.GetTrackingAsync(
            "SP529189074",
            "THAIPOST",
            default);

        Assert.Equal("delivered", update.EventType);
        Assert.Equal(
            new DateTimeOffset(
                2026,
                7,
                26,
                11,
                17,
                52,
                TimeSpan.FromHours(7)),
            update.OccurredAt);
        Assert.Equal("EF123456789TH", update.CourierTrackingCode);
        Assert.StartsWith("shippop-", update.EventId);
    }

    [Fact]
    public async Task Provider_shipping_uses_authoritative_first_scan_time()
    {
        var provider = Provider(_ =>
            Task.FromResult(
                Json(
                    """
                    {
                      "status": true,
                      "order_status": "shipping",
                      "courier_code": "FLE",
                      "tracking_code": "SP529189075",
                      "courier_tracking_code": "TH123456789012",
                      "datetime_shipping": "2026-07-26 08:15:10",
                      "states": [
                        {
                          "status": "010",
                          "datetime": "2026-07-26 08:15:10"
                        },
                        {
                          "status": "102",
                          "datetime": "2026-07-26 10:00:00"
                        }
                      ]
                    }
                    """)));

        var update = await provider.GetTrackingAsync(
            "SP529189075",
            "FLASH",
            default);

        Assert.Equal("in_transit", update.EventType);
        Assert.Equal(
            new DateTimeOffset(
                2026,
                7,
                26,
                8,
                15,
                10,
                TimeSpan.FromHours(7)),
            update.OccurredAt);
    }

    [Fact]
    public async Task Confirm_uses_purchase_and_returns_courier_tracking()
    {
        string? contentType = null;
        string? body = null;
        var provider = Provider(async request =>
        {
            contentType = request.Content?.Headers.ContentType
                ?.MediaType;
            body = await request.Content!.ReadAsStringAsync();
            return Json(
                """
                {
                  "status": true,
                  "result": {
                    "0": {
                      "status": true,
                      "tracking_code": "SP452045855",
                      "courier_tracking_code": "EF123456789TH",
                      "courier_code": "EMST"
                    }
                  }
                }
                """);
        });

        var confirmation = await provider.ConfirmAsync(
            "452002",
            "SP452045855",
            "THAIPOST",
            default);

        Assert.Equal(
            "multipart/form-data",
            contentType);
        Assert.Contains("452002", body);
        Assert.Equal(
            "EF123456789TH",
            confirmation.CourierTrackingCode);
    }

    private static ShippopShippingProvider Provider(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> response)
    {
        var httpClient = new HttpClient(
            new StubHandler(response))
        {
            BaseAddress = new Uri(
                "https://mkpservice.shippop.com/")
        };
        return new ShippopShippingProvider(
            httpClient,
            new ShippopShippingOptions
            {
                BaseUrl =
                    "https://mkpservice.shippop.com/",
                ApiKey = "api-key-for-tests",
                AccountEmail = "shipping@toklong.test",
                QuoteSigningSecret =
                    "quote-signing-secret-longer-than-thirty-two-characters",
                QuoteLifetimeMinutes = 120,
                ServiceCodes = ["EMST"]
            },
            new FixedClock());
    }

    private static ShippingQuoteRequest Request() =>
        new(
            "10110",
            "10500",
            1_200,
            20,
            30,
            15,
            new ShippingContactAddress(
                "ผู้ขาย ทดสอบ",
                "+66812345678",
                "123 ถนนสุขุมวิท",
                "คลองเตยเหนือ",
                "วัฒนา",
                "กรุงเทพมหานคร",
                "10110"),
            new ShippingContactAddress(
                "ผู้ซื้อ ทดสอบ",
                "+66899999999",
                "456 ถนนสีลม",
                "สีลม",
                "บางรัก",
                "กรุงเทพมหานคร",
                "10500"),
            "กล้องมือสอง");

    private static ShippingQuoteOption Quote() =>
        new(
            "shippop",
            "signed-quote",
            "THAIPOST",
            "EMST",
            "ไปรษณีย์ไทย EMS",
            5_200,
            Now.AddHours(2));

    private static HttpResponseMessage Json(
        string value) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                value,
                Encoding.UTF8,
                "application/json")
        };

    private sealed class StubHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            response(request);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
