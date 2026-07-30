using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Toklong.Application.Abstractions;
using Toklong.Domain.Common;
using Toklong.Infrastructure.Services;

namespace Toklong.Application.Tests.Shipping;

public sealed class ShippopShippingProviderTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 26, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Options_without_explicit_certification_keep_all_services_disabled()
    {
        var configuration =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>())
                .Build();

        var options = ShippopShippingOptions.From(
            configuration);

        Assert.Empty(options.ServiceCodes);
        Assert.False(options.AllowInsecureHttp);
    }

    [Fact]
    public void Options_read_explicit_insecure_http_opt_in()
    {
        var configuration =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Shippop:AllowInsecureHttp"] = "true"
                    })
                .Build();

        var options = ShippopShippingOptions.From(
            configuration);

        Assert.True(options.AllowInsecureHttp);
    }

    [Fact]
    public async Task Development_provider_returns_no_add_on_within_included_limit()
    {
        var provider = new DevelopmentShippingQuoteProvider(
            new FixedClock());

        var availability = await provider.GetAvailabilityAsync(
            await ProtectionRequest(provider, 90_000),
            default);

        Assert.Equal(100_000, availability.IncludedCoverageLimitSatang);
        Assert.Null(availability.AddOn);
    }

    [Fact]
    public async Task Development_provider_rejects_forged_delivery_quote_reference()
    {
        var provider = new DevelopmentShippingQuoteProvider(
            new FixedClock());

        await Assert.ThrowsAsync<DomainException>(() =>
            provider.GetAvailabilityAsync(
                new ParcelProtectionQuoteRequest(
                    Request() with { DeclaredValueSatang = 90_000 },
                    "THAIPOST",
                    "EMS",
                    "forged-delivery-quote",
                    90_000),
                default));
    }

    [Fact]
    public async Task Development_provider_rejects_delivery_quote_for_other_service()
    {
        var provider = new DevelopmentShippingQuoteProvider(
            new FixedClock());
        var request = await ProtectionRequest(provider, 90_000);

        await Assert.ThrowsAsync<DomainException>(() =>
            provider.GetAvailabilityAsync(
                request with { ServiceCode = "STANDARD" },
                default));
    }

    [Fact]
    public async Task Development_provider_rejects_expired_delivery_quote()
    {
        var clock = new MutableClock(Now);
        var provider = new DevelopmentShippingQuoteProvider(clock);
        var request = await ProtectionRequest(provider, 90_000);
        clock.UtcNow = Now.AddHours(2);

        await Assert.ThrowsAsync<DomainException>(() =>
            provider.GetAvailabilityAsync(request, default));
    }

    [Fact]
    public async Task Development_provider_returns_signed_add_on_above_limit()
    {
        var provider = new DevelopmentShippingQuoteProvider(
            new FixedClock());

        var availability = await provider.GetAvailabilityAsync(
            await ProtectionRequest(provider, 450_000),
            default);

        Assert.Equal(450_000, availability.AddOn!.SelectedCoverageLimitSatang);
        Assert.Equal(4_500, availability.AddOn.ProviderCostSatang);
    }

    [Fact]
    public async Task Development_provider_reuses_prepared_add_on_for_the_same_request()
    {
        var provider = new DevelopmentShippingQuoteProvider(new FixedClock());
        var request = await ProtectionRequest(provider, 450_000);

        var first = (await provider.GetAvailabilityAsync(request, default)).AddOn!;
        var resumed = (await provider.GetAvailabilityAsync(request, default)).AddOn!;

        Assert.Equal(first.OptionReference, resumed.OptionReference);
        Assert.Equal(first.QuotedAt, resumed.QuotedAt);
        Assert.Equal(first.ExpiresAt, resumed.ExpiresAt);
    }

    [Fact]
    public async Task Development_protection_option_binds_complete_request()
    {
        var provider = new DevelopmentShippingQuoteProvider(
            new FixedClock());
        var request = await ProtectionRequest(provider, 450_000);
        var option = (await provider.GetAvailabilityAsync(
            request,
            default)).AddOn!;

        var validated = await provider.ValidateOptionAsync(
            request,
            option.OptionReference,
            default);

        Assert.Equal(option.ProviderCostSatang, validated.ProviderCostSatang);
        Assert.Equal(option.IncludedCoverageLimitSatang, validated.IncludedCoverageLimitSatang);
        Assert.Equal(option.SelectedCoverageLimitSatang, validated.SelectedCoverageLimitSatang);
        Assert.Equal(option.TermsVersion, validated.TermsVersion);
        Assert.Equal(option.InsuranceCode, validated.InsuranceCode);
        await Assert.ThrowsAsync<ParcelProtectionOptionChangedException>(() =>
            provider.ValidateOptionAsync(
                request with { ItemPriceSatang = 450_001 },
                option.OptionReference,
                default));
        await Assert.ThrowsAsync<DomainException>(() =>
            provider.ValidateOptionAsync(
                request with { ServiceCode = "OTHER" },
                option.OptionReference,
                default));
        await Assert.ThrowsAsync<DomainException>(() =>
            provider.ValidateOptionAsync(
                request with { DeliveryQuoteReference = "other-delivery-quote" },
                option.OptionReference,
                default));
        await Assert.ThrowsAsync<DomainException>(() =>
            provider.ValidateOptionAsync(
                request with { Shipment = request.Shipment with { WeightGrams = 1_201 } },
                option.OptionReference,
                default));
        await Assert.ThrowsAsync<ParcelProtectionOptionChangedException>(() =>
            provider.ValidateOptionAsync(
                request,
                $"forged-{option.OptionReference}",
                default));
    }

    [Fact]
    public async Task Expired_development_protection_option_requests_a_price_refresh()
    {
        var clock = new MutableClock(Now);
        var provider = new DevelopmentShippingQuoteProvider(clock);
        var request = await ProtectionRequest(provider, 450_000);
        var option = (await provider.GetAvailabilityAsync(
            request,
            default)).AddOn!;
        clock.UtcNow = option.ExpiresAt;

        var exception = await Assert.ThrowsAsync<
            ParcelProtectionOptionChangedException>(() =>
            provider.ValidateOptionAsync(
                request,
                option.OptionReference,
                default));

        Assert.Equal(
            "parcel-protection-option-changed",
            exception.Message);
    }

    [Fact]
    public async Task Shippop_uncertified_protection_fails_closed_without_blocking_delivery()
    {
        var profile = new ShippopServiceProfile(
            "EMST",
            QuoteEnabled: true,
            BookOutboundEnabled: false,
            ConfirmEnabled: false,
            ReturnEnabled: false,
            InsuranceEnabled: false,
            OperationLookupEnabled: false,
            "DropOff",
            500_000,
            "CERT-DELIVERY-ONLY",
            IncludedCoverageSatang: 100_000,
            OptionalProtectionEnabled: false);
        var shippop = Provider(_ => Task.FromResult(
            Json(
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
                """)), profile);
        var shipment = Request() with { DeclaredValueSatang = 450_000 };
        var deliveryQuote = Assert.Single(
            await shippop.GetQuotesAsync(shipment, default));

        var request = new ParcelProtectionQuoteRequest(
            shipment,
            deliveryQuote.CarrierCode,
            deliveryQuote.ServiceCode,
            deliveryQuote.QuoteReference,
            450_000);
        var availability = await shippop.GetAvailabilityAsync(
            request,
            default);

        Assert.False(availability.ProviderCapabilityCertified);
        Assert.Equal(0, availability.IncludedCoverageLimitSatang);
        Assert.Null(availability.AddOn);
        await Assert.ThrowsAsync<ParcelProtectionOptionChangedException>(() =>
            shippop.ValidateOptionAsync(
                request,
                "legacy-optional-protection",
                default));
        await Assert.ThrowsAsync<DomainException>(() =>
            shippop.GetAvailabilityAsync(
                request with
                {
                    DeliveryQuoteReference =
                        $"forged-{request.DeliveryQuoteReference}"
                },
                default));
        await Assert.ThrowsAsync<DomainException>(() =>
            shippop.GetAvailabilityAsync(
                request with
                {
                    Shipment = shipment with { WeightGrams = 1_201 }
                },
                default));
    }

    [Fact]
    public async Task Http_base_url_is_rejected_without_explicit_opt_in()
    {
        var requestWasSent = false;
        var provider = Provider(
            _ =>
            {
                requestWasSent = true;
                return Task.FromResult(Json("""{"status":true}"""));
            },
            baseUrl: "http://mkpservice.shippop.dev/");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetQuotesAsync(Request(), default));

        Assert.False(requestWasSent);
    }

    [Fact]
    public async Task Http_base_url_is_allowed_with_explicit_opt_in()
    {
        var provider = Provider(
            _ => Task.FromResult(
                Json(
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
                    """)),
            baseUrl: "http://mkpservice.shippop.dev/",
            allowInsecureHttp: true);

        var quote = Assert.Single(
            await provider.GetQuotesAsync(Request(), default));

        Assert.Equal("EMST", quote.ServiceCode);
    }

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
        Assert.StartsWith("sp2.", quote.QuoteReference);
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
        var parts = quote.QuoteReference.Split('.');
        parts[3] = "1100";
        await Assert.ThrowsAsync<DomainException>(() =>
            provider.ValidateQuoteAsync(
                shipment,
                string.Join('.', parts),
                5_200,
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
                quote,
                Guid.Empty,
                false,
                "checkout:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
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
            "\"ref_no_1\":\"checkout:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"",
            body);
    }

    [Fact]
    public async Task Return_booking_uses_distinct_managed_shipment_reference()
    {
        string? body = null;
        var provider = Provider(async request =>
        {
            body = await request.Content!.ReadAsStringAsync();
            return Json(
                """
                {
                  "status": true,
                  "purchase_id": 452003,
                  "data": {
                    "0": {
                      "status": true,
                      "tracking_code": "SP-RETURN-001",
                      "courier_code": "EMST",
                      "price": 52
                    }
                  }
                }
                """);
        });
        var shipmentId = Guid.Parse(
            "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        await provider.ReserveAsync(
            new ShipmentReservationRequest(
                Guid.Parse(
                    "11111111-2222-3333-4444-555555555555"),
                Request(),
                Quote(),
                shipmentId,
                IsReturn: true,
                OperationReference: $"return:{shipmentId:N}"),
            default);

        Assert.Contains(
            $"\"ref_no_1\":\"return:{shipmentId:N}\"",
            body);
        Assert.DoesNotContain(
            "\"ref_no_1\":\"11111111222233334444555555555555\"",
            body);
    }

    [Fact]
    public async Task Booking_is_blocked_without_safe_lookup()
    {
        var providerCalls = 0;
        var profile = new ShippopServiceProfile(
            "EMST",
            QuoteEnabled: true,
            BookOutboundEnabled: true,
            ConfirmEnabled: true,
            ReturnEnabled: true,
            InsuranceEnabled: false,
            OperationLookupEnabled: false,
            "DropOff",
            300_000,
            "CERT-TEST");
        var provider = Provider(
            _ =>
            {
                providerCalls++;
                return Task.FromResult(Json("""{"status":true}"""));
            },
            profile);

        var lookupException =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.ReserveAsync(
                    new ShipmentReservationRequest(
                        Guid.NewGuid(),
                        Request(),
                        Quote(),
                        Guid.Empty,
                        false,
                        "test-operation"),
                    default));

        Assert.Contains(
            "operation lookup",
            lookupException.Message);
        Assert.Equal(0, providerCalls);
    }

    [Fact]
    public async Task Booking_without_add_on_does_not_require_insurance_capability()
    {
        var profile = new ShippopServiceProfile(
            "EMST",
            QuoteEnabled: true,
            BookOutboundEnabled: true,
            ConfirmEnabled: true,
            ReturnEnabled: true,
            InsuranceEnabled: false,
            OperationLookupEnabled: true,
            "DropOff",
            300_000,
            "CERT-TEST");
        var provider = Provider(
            _ => Task.FromResult(
                Json(
                    """
                    {
                      "status": true,
                      "purchase_id": 452002,
                      "data": {
                        "0": {
                          "status": true,
                          "tracking_code": "SP452045855",
                          "courier_code": "EMST",
                          "price": 52
                        }
                      }
                    }
                    """)),
            profile);

        var reservation = await provider.ReserveAsync(
            new ShipmentReservationRequest(
                Guid.NewGuid(),
                Request(),
                Quote() with
                {
                    InsuranceFeeSatang = 0,
                    InsuranceCode = null
                },
                Guid.Empty,
                false,
                "test-operation"),
            default);

        Assert.Equal(0, reservation.InsuranceFeeSatang);
    }

    [Fact]
    public async Task Protected_booking_fails_closed_before_sending_undocumented_payload()
    {
        var providerCallMade = false;
        var profile = new ShippopServiceProfile(
            "EMST",
            QuoteEnabled: true,
            BookOutboundEnabled: true,
            ConfirmEnabled: true,
            ReturnEnabled: true,
            InsuranceEnabled: true,
            OperationLookupEnabled: true,
            "DropOff",
            500_000,
            "CERT-TEST",
            IncludedCoverageSatang: 100_000,
            OptionalProtectionEnabled: true);
        var provider = Provider(
            _ =>
            {
                providerCallMade = true;
                return Task.FromResult(Json("""{"status":true}"""));
            },
            profile);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.ReserveAsync(
                new ShipmentReservationRequest(
                    Guid.NewGuid(),
                    Request(),
                    Quote() with
                    {
                        InsuranceFeeSatang = 4_500,
                        DeclaredValueSatang = 450_000,
                        InsuranceCode = "UNSUPPORTED_PROTECTION"
                    },
                    Guid.Empty,
                    false,
                    "test-operation"),
                default));

        Assert.Equal(
            "SHIPPOP optional parcel protection is not certified for this service profile.",
            exception.Message);
        Assert.False(providerCallMade);
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
    public async Task Complete_without_pod_time_is_unverified_and_has_no_event_time()
    {
        var provider = Provider(_ =>
            Task.FromResult(
                Json(
                    """
                    {
                      "status": true,
                      "order_status": "complete",
                      "courier_code": "EMST",
                      "tracking_code": "SP-NO-POD",
                      "courier_tracking_code": "EF123456789TH",
                      "states": [
                        {
                          "status": "010",
                          "datetime": "2026-07-26 09:00:00"
                        }
                      ]
                    }
                    """)));

        var update = await provider.GetTrackingAsync(
            "SP-NO-POD",
            "THAIPOST",
            default);

        Assert.Equal("unverified", update.EventType);
        Assert.Null(update.OccurredAt);
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

    [Theory]
    [InlineData("problem")]
    [InlineData("invalid")]
    [InlineData("return")]
    [InlineData("unexpected-new-status")]
    public async Task Problem_or_unknown_status_maps_to_carrier_exception(
        string providerStatus)
    {
        var provider = Provider(_ =>
            Task.FromResult(
                Json(
                    $$"""
                    {
                      "status": true,
                      "order_status": "{{providerStatus}}",
                      "courier_code": "EMST",
                      "tracking_code": "SP-PROBLEM",
                      "courier_tracking_code": "EF123456789TH",
                      "states": []
                    }
                    """)));

        var update = await provider.GetTrackingAsync(
            "SP-PROBLEM",
            "THAIPOST",
            default);

        Assert.Equal(
            "carrier_exception",
            update.EventType);
        Assert.Null(update.OccurredAt);
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

    [Fact]
    public async Task Booking_timeout_after_send_is_an_unknown_outcome()
    {
        var provider = Provider(_ =>
            throw new TaskCanceledException("provider timeout"));

        var exception = await Assert.ThrowsAsync<
            ShipmentMutationException>(() =>
            provider.ReserveAsync(
                new ShipmentReservationRequest(
                    Guid.NewGuid(),
                    Request(),
                    Quote(),
                    Guid.Empty,
                    false,
                    "test-operation"),
                default));

        Assert.Equal(
            ShipmentMutationOutcome.OutcomeUnknown,
            exception.Outcome);
        Assert.Equal(
            "shippop-booking-outcome-unknown",
            exception.SanitizedCode);
    }

    [Fact]
    public async Task Booking_malformed_response_is_an_unknown_outcome()
    {
        var provider = Provider(_ =>
            Task.FromResult(Json("""{"status":true}""")));

        var exception = await Assert.ThrowsAsync<
            ShipmentMutationException>(() =>
            provider.ReserveAsync(
                new ShipmentReservationRequest(
                    Guid.NewGuid(),
                    Request(),
                    Quote(),
                    Guid.Empty,
                    false,
                    "test-operation"),
                default));

        Assert.Equal(
            ShipmentMutationOutcome.OutcomeUnknown,
            exception.Outcome);
    }

    [Fact]
    public async Task Confirm_provider_error_is_an_unknown_outcome()
    {
        var provider = Provider(_ =>
            Task.FromResult(new HttpResponseMessage(
                HttpStatusCode.BadGateway)));

        var exception = await Assert.ThrowsAsync<
            ShipmentMutationException>(() =>
            provider.ConfirmAsync(
                "452002",
                "SP452045855",
                "THAIPOST",
                default));

        Assert.Equal(
            ShipmentMutationOutcome.OutcomeUnknown,
            exception.Outcome);
        Assert.Equal(
            "shippop-confirm-outcome-unknown",
            exception.SanitizedCode);
    }

    [Fact]
    public async Task Cancel_provider_error_is_an_unknown_outcome()
    {
        var provider = Provider(_ =>
            Task.FromResult(new HttpResponseMessage(
                HttpStatusCode.BadGateway)));

        var exception = await Assert.ThrowsAsync<
            ShipmentMutationException>(() =>
            provider.CancelAsync(
                "EF123456789TH",
                default));

        Assert.Equal(
            ShipmentMutationOutcome.OutcomeUnknown,
            exception.Outcome);
        Assert.Equal(
            "shippop-cancel-outcome-unknown",
            exception.SanitizedCode);
    }

    private static ShippopShippingProvider Provider(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> response,
        ShippopServiceProfile? profile = null,
        string baseUrl = "https://mkpservice.shippop.com/",
        bool allowInsecureHttp = false)
    {
        var httpClient = new HttpClient(
            new StubHandler(response))
        {
            BaseAddress = new Uri(baseUrl)
        };
        return new ShippopShippingProvider(
            httpClient,
            new ShippopShippingOptions
            {
                BaseUrl = baseUrl,
                AllowInsecureHttp = allowInsecureHttp,
                ApiKey = "api-key-for-tests",
                AccountEmail = "shipping@toklong.test",
                QuoteSigningSecret =
                    "quote-signing-secret-longer-than-thirty-two-characters",
                QuoteLifetimeMinutes = 120,
                ServiceCodes = ["EMST"],
                ServiceProfiles = profile is null
                    ? new Dictionary<
                        string,
                        ShippopServiceProfile>(
                            StringComparer.Ordinal)
                    : new Dictionary<
                        string,
                        ShippopServiceProfile>(
                            StringComparer.Ordinal)
                    {
                        [profile.ServiceCode] = profile
                    }
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

    private static async Task<ParcelProtectionQuoteRequest> ProtectionRequest(
        DevelopmentShippingQuoteProvider provider,
        long itemPriceSatang)
    {
        var shipment = Request() with
        {
            DeclaredValueSatang = itemPriceSatang
        };
        var quote = (await provider.GetQuotesAsync(
            shipment,
            default)).First(item =>
            item.CarrierCode == "THAIPOST");
        return new(
            shipment,
            quote.CarrierCode,
            quote.ServiceCode,
            quote.QuoteReference,
            itemPriceSatang);
    }

    private static ShippingQuoteOption Quote() =>
        new(
            "shippop",
            "signed-quote",
            "THAIPOST",
            "EMST",
            "ไปรษณีย์ไทย EMS",
            5_200,
            0,
            0,
            null,
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

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
