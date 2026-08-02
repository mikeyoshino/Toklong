using System.Net;
using System.Text;
using Toklong.Application.Abstractions;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Services;

namespace Toklong.Application.Tests.Shipping;

public sealed class CounterQrProviderContractTests
{
    [Fact]
    public async Task Development_provider_issues_a_bounded_png_independent_of_shipping_references()
    {
        var provider = new DevelopmentShippingQuoteProvider(
            new FixedClock());
        var shipmentId = Guid.NewGuid();
        var first = await provider.GetCounterQrAsync(
            Request(shipmentId),
            default);
        var changedReferences = await provider.GetCounterQrAsync(
            Request(shipmentId) with
            {
                PurchaseReference = "different-purchase",
                ProviderTrackingCode = "different-provider-track",
                CourierTrackingCode = "different-courier-track"
            },
            default);

        Assert.Equal(CounterQrReadStatus.Ready, first.Status);
        Assert.Equal(CounterQrRepresentation.ProviderPng, first.Representation);
        Assert.NotNull(first.Artifact);
        Assert.InRange(first.Artifact.Length, 100, 2 * 1024 * 1024);
        Assert.Equal(
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 },
            first.Artifact[..8]);
        Assert.Equal(first.Artifact, changedReferences.Artifact);
        Assert.Equal(64, first.ProviderResourceDigest.Length);
    }

    [Fact]
    public async Task Shippop_is_unavailable_without_certified_read_contract_and_makes_no_request()
    {
        var requestCount = 0;
        var provider = new ShippopShippingProvider(
            new HttpClient(new StubHandler(_ =>
            {
                requestCount++;
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK));
            }))
            {
                BaseAddress = new Uri(
                    "https://mkpservice.shippop.com/")
            },
            Options(counterQrEnabled: false),
            new FixedClock());

        var result = await provider.GetCounterQrAsync(
            Request(Guid.NewGuid()),
            default);

        Assert.Equal(CounterQrReadStatus.Unavailable, result.Status);
        Assert.Equal(
            "counter-qr-contract-not-certified",
            result.SanitizedErrorCode);
        Assert.Null(result.Artifact);
        Assert.Equal(0, requestCount);
    }

    [Fact]
    public async Task Shippop_quotes_exclude_service_without_counter_qr_certification()
    {
        var requestCount = 0;
        var provider = new ShippopShippingProvider(
            new HttpClient(new StubHandler(_ =>
            {
                requestCount++;
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK));
            }))
            {
                BaseAddress = new Uri(
                    "https://mkpservice.shippop.com/")
            },
            Options(counterQrEnabled: false),
            new FixedClock());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetQuotesAsync(QuoteRequest(), default));
        Assert.Equal(0, requestCount);
    }

    [Fact]
    public async Task Shippop_quotes_fail_closed_when_configured_service_has_no_profile()
    {
        var requestCount = 0;
        var provider = Provider(
            new ShippopShippingOptions
            {
                ApiKey = "test-api-key",
                AccountEmail = "shipping@toklong.test",
                QuoteSigningSecret = SigningSecret,
                ServiceCodes = ["EMST"]
            },
            _ =>
            {
                requestCount++;
                return Task.FromResult(Json("""{"status":true}"""));
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetQuotesAsync(QuoteRequest(), default));

        Assert.Equal(0, requestCount);
    }

    [Fact]
    public async Task Shippop_response_cannot_reintroduce_an_uncertified_service()
    {
        var profiles = new Dictionary<string, ShippopServiceProfile>(
            StringComparer.Ordinal)
        {
            ["EMST"] = Profile("EMST", counterQrEnabled: false),
            ["FLE"] = Profile("FLE", counterQrEnabled: true)
        };
        var provider = Provider(
            Options(["EMST", "FLE"], profiles),
            _ => Task.FromResult(Json(
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
                """)));

        var quotes = await provider.GetQuotesAsync(
            QuoteRequest(),
            default);

        Assert.Empty(quotes);
    }

    [Fact]
    public async Task Signed_quote_is_rejected_after_counter_qr_capability_is_removed()
    {
        var enabled = Provider(
            Options(counterQrEnabled: true),
            _ => Task.FromResult(Json(
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
                """)));
        var quote = Assert.Single(
            await enabled.GetQuotesAsync(QuoteRequest(), default));
        var disabled = Provider(
            Options(counterQrEnabled: false),
            _ => throw new InvalidOperationException(
                "Validation must not call SHIPPOP."));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            disabled.ValidateQuoteAsync(
                QuoteRequest(),
                quote.QuoteReference,
                quote.FeeSatang,
                default));
    }

    private static CounterQrRequest Request(Guid shipmentId) =>
        new(
            Guid.NewGuid(),
            shipmentId,
            "development-shipping",
            "purchase-ref",
            "provider-track",
            "courier-track",
            "THAIPOST",
            "EMS");

    private const string SigningSecret =
        "quote-signing-secret-longer-than-thirty-two-characters";

    private static ShippopShippingOptions Options(
        bool counterQrEnabled) =>
        Options(
            ["EMST"],
            new Dictionary<string, ShippopServiceProfile>(
                StringComparer.Ordinal)
            {
                ["EMST"] = Profile("EMST", counterQrEnabled)
            });

    private static ShippopShippingOptions Options(
        IReadOnlyList<string> serviceCodes,
        IReadOnlyDictionary<string, ShippopServiceProfile> profiles) =>
        new()
        {
            ApiKey = "test-api-key",
            AccountEmail = "shipping@toklong.test",
            QuoteSigningSecret = SigningSecret,
            ServiceCodes = serviceCodes,
            ServiceProfiles = profiles
        };

    private static ShippopServiceProfile Profile(
        string serviceCode,
        bool counterQrEnabled) =>
        new(
            serviceCode,
            QuoteEnabled: true,
            BookOutboundEnabled: true,
            ConfirmEnabled: true,
            ReturnEnabled: false,
            InsuranceEnabled: false,
            OperationLookupEnabled: true,
            HandoffMode: "DropOff",
            MaximumCoverageSatang: 0,
            CertificationReference: "test-cert",
            CounterQrEnabled: counterQrEnabled,
            CounterQrCertificationReference:
                counterQrEnabled ? "counter-cert" : "");

    private static ShippopShippingProvider Provider(
        ShippopShippingOptions options,
        Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) =>
        new(
            new HttpClient(new StubHandler(callback))
            {
                BaseAddress = new Uri(
                    "https://mkpservice.shippop.com/")
            },
            options,
            new FixedClock());

    private static HttpResponseMessage Json(string value) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                value,
                Encoding.UTF8,
                "application/json")
        };

    private static ShippingQuoteRequest QuoteRequest() =>
        new(
            "10110",
            "10500",
            1000,
            10,
            10,
            10,
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
                "10500"));

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow =>
            new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) :
        HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => callback(request);
    }
}
