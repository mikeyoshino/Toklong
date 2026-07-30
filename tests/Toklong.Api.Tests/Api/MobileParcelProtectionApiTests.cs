using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Toklong.Api.Security;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Authentication;
using Toklong.Application.Transactions;
using Toklong.Domain.Buyers;
using Toklong.Domain.Common;
using Toklong.Domain.Sellers;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Api.Tests.Api;

public sealed class MobileParcelProtectionApiTests
    : IClassFixture<MobileApiFactory>
{
    private readonly MobileApiFactory factory;

    public MobileParcelProtectionApiTests(MobileApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Buyer_can_read_one_combined_price_and_one_maximum()
    {
        await using var fixture = await CheckoutFixture.CreateAsync(factory);
        using var request = fixture.BuyerRequest(
            HttpMethod.Post,
            "/parcel-protection/prepare",
            "prepare-protection-choice");

        using var response = await fixture.Client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, json);
        Assert.Contains("\"maximumCoverageLimitSatang\":450000", json);
        Assert.Contains("\"customerPriceSatang\":6000", json);
        Assert.DoesNotContain("providerCost", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("toklongServiceFee", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("test-shipping", json,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Seller_and_other_buyer_cannot_read_or_write_buyer_annex()
    {
        await using var fixture = await CheckoutFixture.CreateAsync(factory);

        fixture.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", fixture.SellerAccessToken);
        using var sellerRead = await fixture.Client.GetAsync(fixture.Path);
        using var sellerWrite = ElectionRequest(
            fixture.ElectionPath, "seller-protection-0001", AcceptedRequest());
        using var sellerWriteResponse = await fixture.Client.SendAsync(sellerWrite);
        Assert.Equal(HttpStatusCode.Forbidden, sellerRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, sellerWriteResponse.StatusCode);

        fixture.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", fixture.OtherBuyerAccessToken);
        using var otherRead = await fixture.Client.GetAsync(fixture.Path);
        using var otherWrite = ElectionRequest(
            fixture.ElectionPath, "other-protection-0001", AcceptedRequest());
        using var otherWriteResponse = await fixture.Client.SendAsync(otherWrite);
        Assert.Equal(HttpStatusCode.Forbidden, otherRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, otherWriteResponse.StatusCode);
    }

    [Fact]
    public async Task Election_requires_header_idempotency_key_and_replays_only_identical_request()
    {
        await using var fixture = await CheckoutFixture.CreateAsync(factory);

        using var missing = await fixture.Client.PostAsJsonAsync(
            fixture.ElectionPath, AcceptedRequest());
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        using var first = fixture.BuyerRequest(
            HttpMethod.Post,
            "/parcel-protection-election",
            "choose-protection-0001",
            AcceptedRequest());
        using var firstResponse = await fixture.Client.SendAsync(first);
        var firstJson = await firstResponse.Content.ReadAsStringAsync();
        Assert.True(firstResponse.IsSuccessStatusCode, firstJson);
        Assert.Contains("\"bookingStatus\":\"preparing_shipping\"", firstJson);
        Assert.DoesNotContain("providerCost", firstJson,
            StringComparison.OrdinalIgnoreCase);

        using var replay = fixture.BuyerRequest(
            HttpMethod.Post,
            "/parcel-protection-election",
            "choose-protection-0001",
            AcceptedRequest());
        using var replayResponse = await fixture.Client.SendAsync(replay);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);

        using var conflict = fixture.BuyerRequest(
            HttpMethod.Post,
            "/parcel-protection-election",
            "choose-protection-0001",
            new
            {
                AddProtection = false,
                OptionReference = (string?)null,
                DisclosedCustomerPriceSatang = (long?)null
            });
        using var conflictResponse = await fixture.Client.SendAsync(conflict);
        Assert.Equal(HttpStatusCode.BadRequest, conflictResponse.StatusCode);
    }

    [Fact]
    public async Task Stale_add_on_election_returns_conflict_for_reconfirmation()
    {
        await using var fixture = await CheckoutFixture.CreateAsync(
            factory, throwWhenValidating: true);
        using var request = fixture.BuyerRequest(
            HttpMethod.Post,
            "/parcel-protection-election",
            "stale-protection-0001",
            AcceptedRequest());

        using var response = await fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("\"bookingStatus\":\"reconfirmation_required\"",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Changed_add_on_terms_returns_conflict_for_reconfirmation()
    {
        await using var fixture = await CheckoutFixture.CreateAsync(
            factory, returnMismatchedTerms: true);
        using var request = fixture.BuyerRequest(
            HttpMethod.Post,
            "/parcel-protection-election",
            "changed-terms-protection-0001",
            AcceptedRequest());

        using var response = await fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("\"bookingStatus\":\"reconfirmation_required\"",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Expired_mobile_session_cannot_access_buyer_protection_annex()
    {
        await using var fixture = await CheckoutFixture.CreateAsync(factory);
        fixture.Client.DefaultRequestHeaders.Authorization = null;
        using var response = await fixture.Client.GetAsync(fixture.Path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Agreement_evidence_exposes_the_validated_buyer_annex_only_to_the_buyer()
    {
        await using var fixture = await CheckoutFixture.CreateAsync(factory);
        using var choice = fixture.BuyerRequest(
            HttpMethod.Post,
            "/parcel-protection-election",
            "evidence-protection-choice",
            AcceptedRequest());
        using var choiceResponse = await fixture.Client.SendAsync(choice);
        Assert.True(choiceResponse.IsSuccessStatusCode,
            await choiceResponse.Content.ReadAsStringAsync());
        await fixture.CompleteCheckoutAsync();

        using var buyerResponse = await fixture.Client.GetAsync(
            fixture.EvidencePath);
        var buyerJsonText = await buyerResponse.Content.ReadAsStringAsync();
        Assert.True(buyerResponse.IsSuccessStatusCode, buyerJsonText);
        using var buyerJson = JsonDocument.Parse(buyerJsonText);
        var buyerEvidence = buyerJson.RootElement.GetProperty("evidence");
        var buyerAnnex = buyerEvidence.GetProperty("buyerCheckoutAnnex");
        Assert.Equal("Accepted",
            buyerAnnex.GetProperty("parcelProtectionElection").GetString());
        Assert.Equal(6_000,
            buyerAnnex.GetProperty("customerPriceSatang").GetInt64());
        Assert.Equal(450_000,
            buyerAnnex.GetProperty("selectedCoverageLimitSatang").GetInt64());

        fixture.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer", fixture.SellerAccessToken);
        using var sellerResponse = await fixture.Client.GetAsync(
            fixture.EvidencePath);
        var sellerJsonText = await sellerResponse.Content.ReadAsStringAsync();
        Assert.True(sellerResponse.IsSuccessStatusCode, sellerJsonText);
        using var sellerJson = JsonDocument.Parse(sellerJsonText);
        var sellerEvidence = sellerJson.RootElement.GetProperty("evidence");
        Assert.False(sellerEvidence.TryGetProperty(
            "buyerCheckoutAnnex", out _));
        Assert.DoesNotContain("customerPriceSatang", sellerJsonText);
        Assert.Equal(
            buyerEvidence.GetProperty("hashes")
                .GetProperty("agreementCoreSnapshotHash").GetString(),
            sellerEvidence.GetProperty("hashes")
                .GetProperty("agreementCoreSnapshotHash").GetString());
    }

    private static object AcceptedRequest() => new
    {
        AddProtection = true,
        OptionReference = "parcel-protection-option",
        DisclosedCustomerPriceSatang = 6_000L
    };

    private static HttpRequestMessage ElectionRequest(
        string path,
        string idempotencyKey,
        object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private sealed class CheckoutFixture : IAsyncDisposable
    {
        private readonly WebApplicationFactory<Program> factory;
        private readonly Guid transactionId;
        private readonly string buyerAccessToken;

        public CheckoutFixture(
            WebApplicationFactory<Program> factory,
            HttpClient client,
            Guid transactionId,
            string buyerAccessToken,
            string otherBuyerAccessToken,
            string sellerAccessToken)
        {
            this.factory = factory;
            Client = client;
            this.transactionId = transactionId;
            this.buyerAccessToken = buyerAccessToken;
            OtherBuyerAccessToken = otherBuyerAccessToken;
            SellerAccessToken = sellerAccessToken;
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", buyerAccessToken);
        }

        public HttpClient Client { get; }
        public string OtherBuyerAccessToken { get; }
        public string SellerAccessToken { get; }
        public string Path =>
            $"/api/mobile/transactions/{transactionId}/parcel-protection";
        public string ElectionPath =>
            $"/api/mobile/transactions/{transactionId}/parcel-protection-election";
        public string EvidencePath =>
            $"/api/mobile/transactions/{transactionId}/agreement-evidence";

        public static async Task<CheckoutFixture> CreateAsync(
            MobileApiFactory rootFactory,
            bool throwWhenValidating = false,
            bool returnMismatchedTerms = false)
        {
            var protectionProvider = new TestParcelProtectionProvider(
                throwWhenValidating, returnMismatchedTerms);
            var localFactory = rootFactory.WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IParcelProtectionQuoteProvider>();
                    services.AddSingleton<IParcelProtectionQuoteProvider>(
                        protectionProvider);
                }));
            var client = localFactory.CreateClient(
                new WebApplicationFactoryClientOptions
                {
                    BaseAddress = new Uri("https://localhost"),
                    AllowAutoRedirect = false
                });
            await using var scope = localFactory.Services.CreateAsyncScope();
            var database = scope.ServiceProvider
                .GetRequiredService<ToklongDbContext>();
            var transitions = scope.ServiceProvider
                .GetRequiredService<TransactionTransitionService>();
            var now = DateTimeOffset.UtcNow;
            var buyer = BuyerAccount.Create(
                "+66855555001", "ผู้ซื้อ คุ้มครอง", "buyer@example.com", now);
            var otherBuyer = BuyerAccount.Create(
                "+66855555002", "ผู้ซื้อ อื่น", "other@example.com", now);
            var seller = SellerAccount.Create(
                "+66855555003", now, "ผู้ขายคุ้มครอง");
            var transaction = TestTransactionFactory.CreateBuyerOffer(
                buyer.Id, buyer.FullName, buyer.PhoneNumber, seller.PhoneNumber,
                FulfillmentType.PhysicalShipment, "กล้องทดสอบ", "กล้องพร้อมเลนส์",
                ConditionCode.UsedGood, "", null, 450_000, "terms-v1", now,
                transitions);
            transaction.AcceptBuyerOffer(
                seller.Id, seller.DisplayName, seller.PhoneNumber, "KBANK",
                seller.DisplayName, "1234567890", true, now.AddMinutes(1),
                transitions, shipping: new AcceptedShippingQuote(
                    TestTransactionFactory.ShippingOriginAddress,
                    TestTransactionFactory.DeliveryProvinceName,
                    TestTransactionFactory.DeliveryPostalCode,
                    1_200, 20, 30, 15, "test-shipping", "quote-protection",
                    "THAIPOST", "EMST", "EMS", 5_000, 0, 0, null,
                    now.AddHours(2), TestTransactionFactory.DeliveryDistrictName,
                    TestTransactionFactory.DeliverySubdistrictName,
                    OriginAddressLine: TestTransactionFactory.ShippingOriginAddress));
            database.Buyers.AddRange(buyer, otherBuyer);
            database.Sellers.Add(seller);
            database.Transactions.Add(transaction);
            await database.SaveChangesAsync();
            var tokens = scope.ServiceProvider
                .GetRequiredService<MobileSessionTokenService>();
            var buyerToken = (await tokens.CreateAsync(new MobileSessionProfile(
                buyer.Id, null, buyer.PhoneNumber, buyer.FullName), default)).AccessToken;
            var otherBuyerToken = (await tokens.CreateAsync(new MobileSessionProfile(
                otherBuyer.Id, null, otherBuyer.PhoneNumber, otherBuyer.FullName), default)).AccessToken;
            var sellerToken = (await tokens.CreateAsync(new MobileSessionProfile(
                null, seller.Id, seller.PhoneNumber, seller.DisplayName), default)).AccessToken;
            return new CheckoutFixture(localFactory, client, transaction.Id,
                buyerToken, otherBuyerToken, sellerToken);
        }

        public HttpRequestMessage BuyerRequest(
            HttpMethod method,
            string suffix,
            string idempotencyKey = "",
            object? body = null)
        {
            var request = new HttpRequestMessage(
                method, $"/api/mobile/transactions/{transactionId}{suffix}");
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", buyerAccessToken);
            if (!string.IsNullOrEmpty(idempotencyKey))
                request.Headers.Add("Idempotency-Key", idempotencyKey);
            if (body is not null)
                request.Content = JsonContent.Create(body);
            return request;
        }

        public async Task CompleteCheckoutAsync()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var database = scope.ServiceProvider
                .GetRequiredService<ToklongDbContext>();
            var transaction = await new TransactionRepository(database)
                .GetByIdAsync(transactionId, default)
                ?? throw new InvalidOperationException(
                    "test transaction missing");
            var shipment = transaction.CurrentOutboundShipment
                ?? throw new InvalidOperationException(
                    "test outbound shipment missing");
            var operation = transaction.ShippingOperations.Single(item =>
                item.ManagedShipmentId == shipment.Id &&
                item.OperationType == ShippingOperationType.BookOutbound);
            var now = DateTimeOffset.UtcNow;
            operation.Claim("evidence-test", now, TimeSpan.FromMinutes(5));
            transaction.CompleteBuyerCheckoutShipmentBooking(
                shipment.Id,
                shipment.Provider,
                "evidence-purchase",
                "evidence-provider-tracking",
                "evidence-courier-tracking",
                shipment.CarrierCode,
                shipment.ServiceCode,
                shipment.BaseShippingFeeSatang,
                shipment.InsuranceFeeSatang,
                shipment.DeclaredValueSatang,
                shipment.InsuranceCode,
                now,
                now);
            operation.Succeed(
                "evidence-test",
                "evidence-purchase",
                "evidence-provider-tracking",
                now);
            transaction.BeginCheckout(
                transaction.BuyerDisplayName!,
                transaction.BuyerContact!,
                now,
                new TransactionTransitionService());
            await database.SaveChangesAsync();
        }

        public ValueTask DisposeAsync()
        {
            Client.Dispose();
            return factory.DisposeAsync();
        }
    }

    private sealed class TestParcelProtectionProvider(
        bool throwWhenValidating,
        bool returnMismatchedTerms)
        : IParcelProtectionQuoteProvider
    {
        private static readonly DateTimeOffset ExpiresAt =
            DateTimeOffset.UtcNow.AddMinutes(30);

        public Task<ParcelProtectionAvailability> GetAvailabilityAsync(
            ParcelProtectionQuoteRequest request,
            CancellationToken cancellationToken) => Task.FromResult(
                new ParcelProtectionAvailability(100_000,
                    new ProviderParcelProtectionOption(
                        "test-shipping", "parcel-protection-option", 100_000,
                        450_000, 4_500, "parcel-protection-v1", "PROTECT",
                        DateTimeOffset.UtcNow, ExpiresAt), true));

        public Task<ProviderParcelProtectionOption> ValidateOptionAsync(
            ParcelProtectionQuoteRequest request,
            string optionReference,
            CancellationToken cancellationToken)
        {
            if (throwWhenValidating)
                throw new ParcelProtectionOptionChangedException(
                    "parcel-protection-option-changed");
            return Task.FromResult(new ProviderParcelProtectionOption(
                "test-shipping", "parcel-protection-option", 100_000,
                450_000, 4_500,
                returnMismatchedTerms
                    ? "parcel-protection-v2"
                    : "parcel-protection-v1",
                "PROTECT",
                DateTimeOffset.UtcNow, ExpiresAt));
        }
    }
}
