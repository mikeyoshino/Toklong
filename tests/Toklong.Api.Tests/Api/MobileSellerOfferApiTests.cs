using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Toklong.Api.Security;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Authentication;
using Toklong.Domain.Buyers;
using Toklong.Domain.Sellers;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Api.Tests.Api;

public sealed class MobileSellerOfferApiTests
    : IClassFixture<MobileApiFactory>
{
    private readonly MobileApiFactory factory;

    public MobileSellerOfferApiTests(MobileApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Buyer_can_create_offer_without_product_photo()
    {
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
        var accessToken = await CreateBuyerSessionAsync();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);
        using var content = new MultipartFormDataContent
        {
            { new StringContent("0899999999"), "sellerPhoneNumber" },
            { new StringContent("PhysicalShipment"), "fulfillmentType" },
            { new StringContent("UsedGood"), "condition" },
            { new StringContent("กล้องฟิล์ม"), "productName" },
            {
                new StringContent(
                    "กล้องพร้อมเลนส์ ใช้งานปกติตามที่ตกลงกัน"),
                "agreementDetails"
            },
            { new StringContent(""), "knownDefects" },
            { new StringContent("450000"), "amountSatang" },
            { new StringContent("false"), "useSavedAddress" },
            { new StringContent("false"), "rememberAddress" },
            { new StringContent("123 ถนนตัวอย่าง"), "addressLine" },
            { new StringContent("1"), "provinceId" },
            { new StringContent("1001"), "districtId" },
            { new StringContent("100101"), "subdistrictId" }
        };

        using var response = await client.PostAsync(
            "/api/mobile/offers",
            content);

        Assert.Equal(
            System.Net.HttpStatusCode.Created,
            response.StatusCode);
        using var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        Assert.Equal(
            JsonValueKind.Null,
            json.RootElement.GetProperty("photoUrl").ValueKind);
    }

    [Fact]
    public async Task Pricing_v2_is_consistent_across_local_http_lifecycle()
    {
        using var localFactory = factory.WithWebHostBuilder(_ => { });
        using var client = localFactory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
        var sellerSession = await SignUpAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                sellerSession.AccessToken);
        using var payoutResponse = await client.PutAsJsonAsync(
            "/api/mobile/seller/payout-account",
            new
            {
                AccountId = (Guid?)null,
                BankCode = "KBANK",
                AccountName = "ผู้ขาย Pricing",
                AccountNumber = "1234567890"
            });
        payoutResponse.EnsureSuccessStatusCode();
        var payoutUpdate = await payoutResponse.Content
            .ReadFromJsonAsync<SellerProfileUpdateResponse>();
        Assert.NotNull(payoutUpdate);
        var payout = Assert.Single(payoutUpdate.PayoutAccounts);
        var sellerAccessToken =
            payoutUpdate.Session.AccessToken;
        var examples = new[]
        {
            (ItemPriceSatang: 100_000L, FeeSatang: 5_900L),
            (ItemPriceSatang: 500_000L, FeeSatang: 20_000L),
            (ItemPriceSatang: 1_000_000L, FeeSatang: 37_500L),
            (ItemPriceSatang: 1_500_000L, FeeSatang: 55_000L),
            (ItemPriceSatang: 3_000_000L, FeeSatang: 100_000L)
        };

        for (var index = 0; index < examples.Length; index++)
        {
            var example = examples[index];
            var buyerAccessToken = await CreateBuyerSessionAsync(
                localFactory.Services,
                $"+6691000000{index + 1}",
                $"ผู้ซื้อ Pricing {index + 1}",
                $"pricing-{index + 1}@example.com");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    buyerAccessToken);
            using var offerContent = CreateOfferContent(
                example.ItemPriceSatang,
                $"กล้อง Pricing {index + 1}");
            using var createResponse = await client.PostAsync(
                "/api/mobile/offers",
                offerContent);
            Assert.True(
                createResponse.IsSuccessStatusCode,
                await createResponse.Content.ReadAsStringAsync());
            var created = await createResponse.Content
                .ReadFromJsonAsync<TransactionResponse>();
            Assert.NotNull(created);
            Assert.Equal(
                example.ItemPriceSatang,
                created.ItemPriceSatang);
            Assert.NotNull(created.SellerInvitationUrl);
            var publicToken = new Uri(
                    created.SellerInvitationUrl)
                .Segments[^1]
                .Trim('/');

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    sellerAccessToken);
            using var invitationResponse = await client.GetAsync(
                $"/api/mobile/seller-offers/{publicToken}");
            invitationResponse.EnsureSuccessStatusCode();
            var invitationJson =
                await invitationResponse.Content.ReadAsStringAsync();
            Assert.DoesNotContain(
                "\"BuyerProtectionFeeSatang\"",
                invitationJson,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "feePolicyVersion",
                invitationJson,
                StringComparison.OrdinalIgnoreCase);
            var invitation = JsonSerializer.Deserialize<SellerOfferResponse>(
                invitationJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.NotNull(invitation);
            Assert.Equal(
                example.ItemPriceSatang,
                invitation.SellerExpectedNetSatang);

            using var quoteResponse = await client.PostAsJsonAsync(
                $"/api/mobile/seller-offers/{publicToken}/shipping-quotes",
                new
                {
                    UseSavedOrigin = false,
                    AddressLine = "123 ถนนต้นทาง",
                    ProvinceId = (int?)1,
                    DistrictId = (int?)1001,
                    SubdistrictId = (int?)100101,
                    WeightGrams = 1_000,
                    WidthCentimeters = 20,
                    LengthCentimeters = 20,
                    HeightCentimeters = 10
                });
            quoteResponse.EnsureSuccessStatusCode();
            var quoteJson = await quoteResponse.Content.ReadAsStringAsync();
            Assert.DoesNotContain("insurance", quoteJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("declaredValue", quoteJson, StringComparison.OrdinalIgnoreCase);
            var quotes = JsonSerializer.Deserialize<IReadOnlyList<ShippingQuoteResponse>>(
                quoteJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.NotNull(quotes);
            var quote = Assert.Single(
                quotes,
                item => item.CarrierCode == "THAIPOST");

            using var acceptResponse = await client.PostAsJsonAsync(
                $"/api/mobile/seller-offers/{publicToken}/accept",
                new
                {
                    PayoutAccountId = payout.Id,
                    TransferRightsAttested = true,
                    SellerAcceptedTerms = true,
                    Shipping = new
                    {
                        UseSavedOrigin = false,
                        AddressLine = "123 ถนนต้นทาง",
                        ProvinceId = (int?)1,
                        DistrictId = (int?)1001,
                        SubdistrictId = (int?)100101,
                        RememberOrigin = false,
                        WeightGrams = 1_000,
                        WidthCentimeters = 20,
                        LengthCentimeters = 20,
                        HeightCentimeters = 10,
                        quote.QuoteReference,
                        DisclosedShippingFeeSatang =
                            quote.FeeSatang
                    }
                });
            Assert.True(
                acceptResponse.IsSuccessStatusCode,
                await acceptResponse.Content.ReadAsStringAsync());
            var accepted = await acceptResponse.Content
                .ReadFromJsonAsync<SellerOfferActionResponse>();
            Assert.NotNull(accepted);
            Assert.Equal(0, accepted.Transaction.BuyerProtectionFeeSatang);
            Assert.Equal(
                example.ItemPriceSatang,
                accepted.Transaction.AmountSatang);

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    buyerAccessToken);
            using var checkoutResponse = await client.PostAsJsonAsync(
                $"/api/mobile/transactions/{created.Id}/payment-sheet",
                new { AcceptedTerms = true });
            Assert.True(
                checkoutResponse.IsSuccessStatusCode,
                await checkoutResponse.Content.ReadAsStringAsync());
            using var transactionResponse = await client.GetAsync(
                $"/api/mobile/transactions/{created.Id}");
            Assert.True(
                transactionResponse.IsSuccessStatusCode,
                await transactionResponse.Content.ReadAsStringAsync());
            var checkedOut = await transactionResponse.Content
                .ReadFromJsonAsync<TransactionResponse>();
            Assert.NotNull(checkedOut);
            Assert.Equal(
                nameof(TransactionState.PaymentPending),
                checkedOut.State);
            Assert.Equal(
                example.FeeSatang,
                checkedOut.BuyerProtectionFeeSatang);
            Assert.Equal(
                "buyer-protection-v2",
                checkedOut.FeePolicyVersion);
            Assert.NotNull(checkedOut.BuyerAcceptedAt);

            await using var scope =
                localFactory.Services.CreateAsyncScope();
            var stored = await scope.ServiceProvider
                .GetRequiredService<ITransactionRepository>()
                .GetByIdAsync(
                    created.Id,
                    CancellationToken.None);
            Assert.NotNull(stored);
            Assert.Equal(
                example.FeeSatang,
                stored.BuyerProtectionFeeSatang);
            Assert.Equal(
                example.ItemPriceSatang +
                quote.FeeSatang +
                example.FeeSatang,
                stored.BuyerTotalSatang);
            Assert.Equal(
                "buyer-protection-v2",
                stored.FeePolicyVersion);
            Assert.NotNull(stored.ProductSnapshotHash);
            Assert.Contains(
                $"\"BuyerProtectionFeeSatang\":{example.FeeSatang}",
                stored.ProductSnapshotJson);
            Assert.Equal(2, stored.AgreementAcceptances.Count);
        }

        var rejectedBuyerToken = await CreateBuyerSessionAsync(
            localFactory.Services,
            "+66910000009",
            "ผู้ซื้อ เกินวงเงิน",
            "pricing-over-limit@example.com");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                rejectedBuyerToken);
        using var rejectedContent = CreateOfferContent(
            3_000_001,
            "กล้องเกินวงเงิน");
        using var rejectedResponse = await client.PostAsync(
            "/api/mobile/offers",
            rejectedContent);
        Assert.Equal(
            System.Net.HttpStatusCode.BadRequest,
            rejectedResponse.StatusCode);
    }

    [Fact]
    public async Task Historical_v1_snapshot_remains_readable_without_repricing()
    {
        using var localFactory = factory.WithWebHostBuilder(_ => { });
        Guid transactionId;
        string buyerAccessToken;
        const long historicalFeeSatang = 20_650;
        await using (var scope =
                     localFactory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider
                .GetRequiredService<ToklongDbContext>();
            var transitions = scope.ServiceProvider
                .GetRequiredService<TransactionTransitionService>();
            var now = DateTimeOffset.UtcNow;
            var buyer = BuyerAccount.Create(
                "+66920000001",
                "ผู้ซื้อ Historical",
                "historical-v1@example.com",
                now);
            var verifiedSellerAccount = BuyerAccount.Create(
                "+66920000002",
                "สมชาย ใจดี",
                "historical-seller@example.com",
                now);
            var transaction =
                TestTransactionFactory.CreateBuyerOffer(
                    buyer.Id,
                    buyer.FullName,
                    buyer.PhoneNumber,
                    verifiedSellerAccount.PhoneNumber,
                    FulfillmentType.PhysicalShipment,
                    "กล้อง Historical v1",
                    "กล้องพร้อมเลนส์ ใช้งานได้ปกติ",
                    ConditionCode.UsedGood,
                    "",
                    null,
                    450_000,
                    "terms-v1",
                    now,
                    transitions);
            transaction.AcceptBuyerOffer(
                Guid.NewGuid(),
                "ผู้ขาย 0002",
                verifiedSellerAccount.PhoneNumber,
                "KBANK",
                "ผู้ขาย Historical",
                "1234567890",
                true,
                now.AddMinutes(1),
                transitions,
                historicalFeeSatang,
                0,
                450_000,
                "buyer-protection-v1",
                TestTransactionFactory.ShippingQuote(
                    now.AddMinutes(1)));
            transaction.BeginCheckout(
                buyer.FullName,
                buyer.PhoneNumber,
                now.AddMinutes(2),
                transitions,
                "stripe",
                "pi_historical_v1",
                historicalFeeSatang,
                0,
                450_000,
                "buyer-protection-v1");
            database.Buyers.AddRange(
                buyer,
                verifiedSellerAccount);
            database.Transactions.Add(transaction);
            await database.SaveChangesAsync();
            transactionId = transaction.Id;
            buyerAccessToken = (await scope.ServiceProvider
                .GetRequiredService<MobileSessionTokenService>()
                .CreateAsync(
                    new MobileSessionProfile(
                        BuyerId: buyer.Id,
                        SellerId: null,
                        PhoneNumber: buyer.PhoneNumber,
                        DisplayName: buyer.FullName),
                    CancellationToken.None)).AccessToken;
        }

        using var client = localFactory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                buyerAccessToken);
        using var response = await client.GetAsync(
            $"/api/mobile/transactions/{transactionId}");
        Assert.True(
            response.IsSuccessStatusCode,
            await response.Content.ReadAsStringAsync());
        var historical = await response.Content
            .ReadFromJsonAsync<TransactionResponse>();

        Assert.NotNull(historical);
        Assert.Equal(
            "สมชาย ใจดี",
            historical.CounterpartyName);
        Assert.Equal(
            "buyer-protection-v1",
            historical.FeePolicyVersion);
        Assert.Equal(
            historicalFeeSatang,
            historical.BuyerProtectionFeeSatang);
        Assert.Equal(
            historical.ItemPriceSatang +
            historical.ShippingFeeSatang +
            historicalFeeSatang,
            historical.AmountSatang);
    }

    [Fact]
    public async Task Seller_transaction_json_never_contains_parcel_protection_fields()
    {
        using var localFactory = factory.WithWebHostBuilder(_ => { });
        Guid transactionId;
        string buyerAccessToken;
        string sellerAccessToken;
        await using (var scope =
                     localFactory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider
                .GetRequiredService<ToklongDbContext>();
            var transitions = scope.ServiceProvider
                .GetRequiredService<TransactionTransitionService>();
            var now = DateTimeOffset.UtcNow;
            var buyer = BuyerAccount.Create(
                "+66855555551",
                "ผู้ซื้อ Privacy",
                "buyer-parcel-privacy@example.com",
                now);
            var seller = SellerAccount.Create(
                "+66855555552",
                now,
                "ผู้ขาย Privacy");
            var transaction =
                TestTransactionFactory.CreateBuyerOffer(
                    buyer.Id,
                    buyer.FullName,
                    buyer.PhoneNumber,
                    seller.PhoneNumber,
                    FulfillmentType.PhysicalShipment,
                    "กล้อง Privacy",
                    "กล้องพร้อมเลนส์ ใช้งานได้ปกติ",
                    ConditionCode.UsedGood,
                    "",
                    null,
                    450_000,
                    "terms-v1",
                    now,
                    transitions);
            transaction.AcceptBuyerOffer(
                seller.Id,
                seller.DisplayName,
                seller.PhoneNumber,
                "KBANK",
                seller.DisplayName,
                "1234567890",
                true,
                now.AddMinutes(1),
                transitions,
                shipping:
                    TestTransactionFactory.ShippingQuote(
                        now.AddMinutes(1)));
            transaction.RecordParcelProtectionElection(
                buyer.Id,
                new ParcelProtectionSelection(
                    ParcelProtectionElectionStatus.Accepted,
                    6_000,
                    4_500,
                    1_500,
                    100_000,
                    450_000,
                    "parcel-protection-2026-07-30",
                    "protected-option",
                    now,
                    now.AddMinutes(30)),
                now.AddMinutes(2));
            database.Buyers.Add(buyer);
            database.Sellers.Add(seller);
            database.Transactions.Add(transaction);
            await database.SaveChangesAsync();
            transactionId = transaction.Id;
            var tokens = scope.ServiceProvider
                .GetRequiredService<MobileSessionTokenService>();
            buyerAccessToken = (await tokens
                .CreateAsync(
                    new MobileSessionProfile(
                        BuyerId: buyer.Id,
                        SellerId: null,
                        PhoneNumber: buyer.PhoneNumber,
                        DisplayName: buyer.FullName),
                    CancellationToken.None)).AccessToken;
            sellerAccessToken = (await tokens
                .CreateAsync(
                    new MobileSessionProfile(
                        BuyerId: null,
                        SellerId: seller.Id,
                        PhoneNumber: seller.PhoneNumber,
                        DisplayName: seller.DisplayName),
                    CancellationToken.None)).AccessToken;
        }

        using var client = localFactory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                sellerAccessToken);

        using var response = await client.GetAsync(
            $"/api/mobile/transactions/{transactionId}");

        Assert.True(
            response.IsSuccessStatusCode,
            await response.Content.ReadAsStringAsync());
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(
            "\"parcelInsuranceFeeSatang\"",
            json,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "\"shippingDeclaredValueSatang\"",
            json,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "parcelProtection",
            json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "feePolicyVersion",
            json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "termsVersion",
            json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "agreementCoreSnapshotHash",
            json,
            StringComparison.OrdinalIgnoreCase);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                buyerAccessToken);
        using var buyerResponse = await client.GetAsync(
            $"/api/mobile/transactions/{transactionId}");
        Assert.True(
            buyerResponse.IsSuccessStatusCode,
            await buyerResponse.Content.ReadAsStringAsync());
        using var buyerJson = JsonDocument.Parse(
            await buyerResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            6_000,
            buyerJson.RootElement
                .GetProperty("parcelInsuranceFeeSatang")
                .GetInt64());
    }

    [Fact]
    public async Task Verified_phone_can_save_payout_accept_offer_and_receive_seller_session()
    {
        var publicToken = await SeedBuyerOfferAsync();
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
        var session = await SignUpAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);

        using var pendingListResponse = await client.GetAsync(
            "/api/mobile/transactions");
        pendingListResponse.EnsureSuccessStatusCode();
        using var pendingList = JsonDocument.Parse(
            await pendingListResponse.Content.ReadAsStringAsync());
        var pendingSellerOffer = Assert.Single(
            pendingList.RootElement.EnumerateArray(),
            item =>
                item.GetProperty("state").GetString() ==
                    nameof(
                        TransactionState
                            .AwaitingSellerAcceptance) &&
                item.GetProperty("role").GetString() ==
                    "Seller");
        Assert.Equal(
            $"toklong://offer/{publicToken}",
            pendingSellerOffer
                .GetProperty("sellerInvitationUrl")
                .GetString());
        Assert.Equal(
            nameof(ConditionCode.UsedGood),
            pendingSellerOffer
                .GetProperty("condition")
                .GetString());
        Assert.Equal(
            "รอยเล็กน้อยด้านข้าง",
            pendingSellerOffer
                .GetProperty("knownDefects")
                .GetString());

        using var invitationResponse = await client.GetAsync(
            $"/api/mobile/seller-offers/{publicToken}");
        invitationResponse.EnsureSuccessStatusCode();
        var invitation = await invitationResponse.Content
            .ReadFromJsonAsync<SellerOfferResponse>();
        Assert.NotNull(invitation);
        Assert.Empty(invitation.PayoutAccounts);
        Assert.Null(invitation.SavedShippingOrigin);

        using var payoutResponse = await client.PutAsJsonAsync(
            "/api/mobile/seller/payout-account",
            new
            {
                AccountId = (Guid?)null,
                BankCode = "KBANK",
                AccountName = "ผู้ขาย ทดสอบ",
                AccountNumber = "1234567890"
            });
        Assert.True(
            payoutResponse.IsSuccessStatusCode,
            await payoutResponse.Content.ReadAsStringAsync());
        var payoutUpdate = await payoutResponse.Content
            .ReadFromJsonAsync<SellerProfileUpdateResponse>();
        Assert.NotNull(payoutUpdate);
        Assert.True(payoutUpdate.Session.CanSell);
        var payout = Assert.Single(payoutUpdate.PayoutAccounts);
        Assert.DoesNotContain("1234567890", payout.MaskedNumber);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                payoutUpdate.Session.AccessToken);

        using var quoteResponse = await client.PostAsJsonAsync(
            $"/api/mobile/seller-offers/{publicToken}/shipping-quotes",
            new
            {
                UseSavedOrigin = false,
                AddressLine = "123 ถนนต้นทาง",
                ProvinceId = (int?)1,
                DistrictId = (int?)1001,
                SubdistrictId = (int?)100101,
                WeightGrams = 1_200,
                WidthCentimeters = 20,
                LengthCentimeters = 30,
                HeightCentimeters = 15
            });
        Assert.True(
            quoteResponse.IsSuccessStatusCode,
            await quoteResponse.Content.ReadAsStringAsync());
        var quotes = await quoteResponse.Content
            .ReadFromJsonAsync<IReadOnlyList<ShippingQuoteResponse>>();
        Assert.NotNull(quotes);
        var quote = Assert.Single(
            quotes,
            item => item.CarrierCode == "THAIPOST");

        using var acceptResponse = await client.PostAsJsonAsync(
            $"/api/mobile/seller-offers/{publicToken}/accept",
            new
            {
                PayoutAccountId = payout.Id,
                TransferRightsAttested = true,
                SellerAcceptedTerms = true,
                Shipping = new
                {
                    UseSavedOrigin = false,
                    AddressLine = "123 ถนนต้นทาง",
                    ProvinceId = (int?)1,
                    DistrictId = (int?)1001,
                    SubdistrictId = (int?)100101,
                    RememberOrigin = true,
                    WeightGrams = 1_200,
                    WidthCentimeters = 20,
                    LengthCentimeters = 30,
                    HeightCentimeters = 15,
                    quote.QuoteReference,
                    DisclosedShippingFeeSatang =
                        quote.FeeSatang
                }
            });
        Assert.True(
            acceptResponse.IsSuccessStatusCode,
            await acceptResponse.Content.ReadAsStringAsync());
        var accepted = await acceptResponse.Content
            .ReadFromJsonAsync<SellerOfferActionResponse>();
        Assert.NotNull(accepted);
        Assert.Equal(
            nameof(TransactionState.SellerAcceptedAwaitingPayment),
            accepted.Transaction.State);
        Assert.True(accepted.Session.CanSell);
        Assert.Equal(
            450_000,
            accepted.Transaction.AmountSatang);
        Assert.Equal(
            quote.FeeSatang,
            accepted.Transaction.ShippingFeeSatang);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accepted.Session.AccessToken);
        using var listResponse = await client.GetAsync(
            "/api/mobile/transactions");
        listResponse.EnsureSuccessStatusCode();
        using var list = JsonDocument.Parse(
            await listResponse.Content.ReadAsStringAsync());
        var sellerTransaction = Assert.Single(
            list.RootElement.EnumerateArray(),
            item => item.GetProperty("id").GetGuid() ==
                    accepted.Transaction.Id &&
                    item.GetProperty("role").GetString() == "Seller");
        Assert.Equal(
            "https://localhost/images/test.jpg",
            sellerTransaction.GetProperty("photoUrl").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            sellerTransaction.GetProperty("deliveryAddress").ValueKind);

        await using (var scope =
                     factory.Services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider
                .GetRequiredService<ITransactionRepository>();
            var unitOfWork = scope.ServiceProvider
                .GetRequiredService<IUnitOfWork>();
            var transitions = scope.ServiceProvider
                .GetRequiredService<TransactionTransitionService>();
            var transaction = await repository.GetByIdAsync(
                accepted.Transaction.Id,
                CancellationToken.None);
            Assert.NotNull(transaction);
            Assert.Equal(
                "123 ถนนต้นทาง ตำบล/แขวง พระบรมมหาราชวัง อำเภอ/เขต เขตพระนคร จังหวัด กรุงเทพมหานคร 10200",
                transaction.ShippingOriginAddress);
            Assert.Equal(1_200, transaction.PackageWeightGrams);
            Assert.Equal(
                quote.QuoteReference,
                transaction.ShippingQuoteReference);
            var seller = await scope.ServiceProvider
                .GetRequiredService<ISellerRepository>()
                .GetByIdAsync(
                    transaction.SellerId!.Value,
                    CancellationToken.None);
            Assert.NotNull(seller);
            Assert.Equal(
                "123 ถนนต้นทาง ตำบล/แขวง พระบรมมหาราชวัง อำเภอ/เขต เขตพระนคร จังหวัด กรุงเทพมหานคร 10200",
                seller.GetSavedShippingOrigin()?.ToDisplayText());
            var checkoutAt =
                transaction.SellerAcceptedAt!.Value.AddMinutes(1);
            transaction.BeginCheckout(
                transaction.BuyerDisplayName!,
                transaction.BuyerContact!,
                checkoutAt,
                transitions,
                buyerProtectionFeeSatang:
                    transaction.BuyerProtectionFeeSatang,
                platformFeeSatang:
                    transaction.PlatformFeeSatang,
                sellerExpectedNetSatang:
                    transaction.SellerExpectedNetSatang,
                feePolicyVersion:
                    transaction.FeePolicyVersion);
            transaction.ConfirmPayment(
                $"payment-{transaction.Id:N}",
                checkoutAt.AddMinutes(1),
                transitions);
            await unitOfWork.SaveChangesAsync(
                CancellationToken.None);
        }

        using var paidListResponse = await client.GetAsync(
            "/api/mobile/transactions");
        paidListResponse.EnsureSuccessStatusCode();
        using var paidList = JsonDocument.Parse(
            await paidListResponse.Content.ReadAsStringAsync());
        var paidSellerTransaction = Assert.Single(
            paidList.RootElement.EnumerateArray(),
            item => item.GetProperty("id").GetGuid() ==
                    accepted.Transaction.Id &&
                    item.GetProperty("role").GetString() ==
                    "Seller");
        Assert.Equal(
            TestTransactionFactory.DeliveryAddress,
            paidSellerTransaction
                .GetProperty("deliveryAddress")
                .GetString());
    }

    [Fact]
    public async Task Different_verified_phone_cannot_open_targeted_offer()
    {
        var publicToken = await SeedBuyerOfferAsync(
            "+66823456789");
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
        var session = await SignUpAsync(
            client,
            "0899999999",
            "ผู้ขาย คนอื่น");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                session.AccessToken);

        using var response = await client.GetAsync(
            $"/api/mobile/seller-offers/{publicToken}");

        Assert.Equal(
            System.Net.HttpStatusCode.Forbidden,
            response.StatusCode);

        using var listResponse = await client.GetAsync(
            "/api/mobile/transactions");
        listResponse.EnsureSuccessStatusCode();
        using var list = JsonDocument.Parse(
            await listResponse.Content.ReadAsStringAsync());
        Assert.DoesNotContain(
            list.RootElement.EnumerateArray(),
            item =>
                item.TryGetProperty(
                    "sellerInvitationUrl",
                    out var invitationUrl) &&
                invitationUrl.GetString() ==
                    $"toklong://offer/{publicToken}");
    }

    [Fact]
    public async Task Intended_seller_receives_offer_in_notification_inbox()
    {
        var publicToken = await SeedBuyerOfferAsync(
            "+66812345678");
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
        var session = await SignUpAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                session.AccessToken);

        using var response = await client.GetAsync(
            "/api/mobile/notifications");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        var notification = Assert.Single(
            json.RootElement.EnumerateArray(),
            item =>
                item.GetProperty("eventType")
                    .GetString() ==
                    "buyer_offer_received" &&
                item.GetProperty("deepLink")
                    .GetString() ==
                    $"toklong://offer/{publicToken}");

        Assert.Equal(
            "ได้รับข้อเสนอซื้อ",
            notification.GetProperty("title").GetString());
        Assert.Contains(
            "กล้องมือสอง สภาพดี พร้อมแบตเตอรี่",
            notification.GetProperty("body").GetString());
        Assert.Equal(
            $"toklong://offer/{publicToken}",
            notification.GetProperty("deepLink").GetString());
    }

    private async Task<string> SeedBuyerOfferAsync(
        string intendedSellerPhone = "+66812345678")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        var transitions = scope.ServiceProvider
            .GetRequiredService<TransactionTransitionService>();
        var now = DateTimeOffset.UtcNow;
        var buyer = BuyerAccount.Create(
            "+66899999999",
            "ผู้ซื้อ รายอื่น",
            "other-buyer@example.com",
            now);
        var offer = TestTransactionFactory.CreateBuyerOffer(
            buyer.Id,
            buyer.FullName,
            buyer.PhoneNumber,
            intendedSellerPhone,
            FulfillmentType.PhysicalShipment,
            "กล้องมือสอง สภาพดี พร้อมแบตเตอรี่",
            "กล้องใช้งานปกติ มีรอยเล็กน้อย พร้อมแบตเตอรี่",
            ConditionCode.UsedGood,
            "รอยเล็กน้อยด้านข้าง",
            "/images/test.jpg",
            450_000,
            "terms-v1",
            now,
            transitions);
        database.Buyers.Add(buyer);
        database.Transactions.Add(offer);
        await database.SaveChangesAsync();
        return offer.PublicToken;
    }

    private Task<string> CreateBuyerSessionAsync() =>
        CreateBuyerSessionAsync(
            factory.Services,
            "+66877777777",
            "ผู้ซื้อ ไม่มีรูป",
            "buyer-no-photo@example.com");

    private static async Task<string> CreateBuyerSessionAsync(
        IServiceProvider services,
        string phoneNumber,
        string fullName,
        string email)
    {
        await using var scope =
            services.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        var now = DateTimeOffset.UtcNow;
        var buyer = BuyerAccount.Create(
            phoneNumber,
            fullName,
            email,
            now);
        database.Buyers.Add(buyer);
        await database.SaveChangesAsync();
        var tokens = scope.ServiceProvider
            .GetRequiredService<MobileSessionTokenService>();
        var session = await tokens.CreateAsync(
            new MobileSessionProfile(
                BuyerId: buyer.Id,
                SellerId: null,
                PhoneNumber: buyer.PhoneNumber,
                DisplayName: buyer.FullName),
            CancellationToken.None);
        return session.AccessToken;
    }

    private static MultipartFormDataContent CreateOfferContent(
        long itemPriceSatang,
        string productName) =>
        new()
        {
            { new StringContent("0812345678"), "sellerPhoneNumber" },
            { new StringContent("PhysicalShipment"), "fulfillmentType" },
            { new StringContent("UsedGood"), "condition" },
            { new StringContent(productName), "productName" },
            {
                new StringContent(
                    "กล้องพร้อมเลนส์ ใช้งานปกติตามที่ตกลงกัน"),
                "agreementDetails"
            },
            { new StringContent(""), "knownDefects" },
            {
                new StringContent(
                    itemPriceSatang.ToString(
                        System.Globalization.CultureInfo
                            .InvariantCulture)),
                "amountSatang"
            },
            { new StringContent("false"), "useSavedAddress" },
            { new StringContent("false"), "rememberAddress" },
            { new StringContent("123 ถนนตัวอย่าง"), "addressLine" },
            { new StringContent("1"), "provinceId" },
            { new StringContent("1001"), "districtId" },
            { new StringContent("100101"), "subdistrictId" }
        };

    private static async Task<SessionResponse> SignUpAsync(
        HttpClient client,
        string phoneNumber = "0812345678",
        string fullName = "ผู้ขาย ทดสอบ")
    {
        var installationId = Guid.NewGuid().ToString("N");
        using var request = await client.PostAsJsonAsync(
            "/api/mobile/auth/otp/request",
            new
            {
                PhoneNumber = phoneNumber,
                Mode = "SignUp"
            });
        request.EnsureSuccessStatusCode();
        var challenge = await request.Content
            .ReadFromJsonAsync<OtpResponse>();
        Assert.NotNull(challenge);
        using var verify = await client.PostAsJsonAsync(
            "/api/mobile/auth/otp/verify",
            new
            {
                challenge.ChallengeId,
                Code = "123456",
                Mode = "SignUp",
                InstallationId = installationId
            });
        verify.EnsureSuccessStatusCode();
        var verification = await verify.Content
            .ReadFromJsonAsync<VerificationResponse>();
        Assert.NotNull(verification);
        if (verification.Session is not null)
            return verification.Session;

        Assert.NotNull(verification.Registration);
        using var completion = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/mobile/auth/registration/complete")
        {
            Content = JsonContent.Create(new
            {
                verification.Registration.RegistrationTicket,
                FullName = fullName,
                Email = $"{phoneNumber}@example.com",
                TermsVersion = "terms-mvp-v1",
                InstallationId = installationId
            })
        };
        completion.Headers.Add(
            "Idempotency-Key",
            Guid.NewGuid().ToString("N"));
        using var completed = await client.SendAsync(completion);
        completed.EnsureSuccessStatusCode();
        return (await completed.Content
            .ReadFromJsonAsync<SessionResponse>())!;
    }

    private sealed record OtpResponse(string ChallengeId);
    private sealed record SessionResponse(
        string AccessToken,
        string RefreshToken,
        DateTimeOffset AccessTokenExpiresAt,
        bool CanSell);
    private sealed record VerificationResponse(
        SessionResponse? Session,
        RegistrationResponse? Registration);
    private sealed record RegistrationResponse(
        string RegistrationTicket);
    private sealed record PayoutAccount(
        Guid Id,
        string MaskedNumber);
    private sealed record SellerProfileUpdateResponse(
        SessionResponse Session,
        IReadOnlyList<PayoutAccount> PayoutAccounts);
    private sealed record SellerOfferResponse(
        long SellerExpectedNetSatang,
        IReadOnlyList<PayoutAccount> PayoutAccounts,
        SavedShippingOriginResponse? SavedShippingOrigin);
    private sealed record SavedShippingOriginResponse(
        string DisplayText);
    private sealed record ShippingQuoteResponse(
        string Provider,
        string QuoteReference,
        string CarrierCode,
        string ServiceCode,
        string ServiceName,
        long FeeSatang,
        DateTimeOffset ExpiresAt);
    private sealed record TransactionResponse(
        Guid Id,
        string State,
        string CounterpartyName,
        long AmountSatang,
        long ItemPriceSatang,
        long ShippingFeeSatang,
        long BuyerProtectionFeeSatang,
        long PlatformFeeSatang,
        long SellerExpectedNetSatang,
        string FeePolicyVersion,
        DateTimeOffset? BuyerAcceptedAt,
        string? SellerInvitationUrl);
    private sealed record SellerOfferActionResponse(
        TransactionResponse Transaction,
        SessionResponse Session);
}
