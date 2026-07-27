using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Toklong.Api.Security;
using Toklong.Application.Features.Authentication;
using Toklong.Domain.Buyers;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Api.Tests.Api;

public sealed class MobilePricingPreviewApiTests
    : IClassFixture<MobileApiFactory>
{
    private readonly MobileApiFactory factory;

    public MobilePricingPreviewApiTests(MobileApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Buyer_receives_server_calculated_tiered_preview_without_creating_transaction()
    {
        using var localFactory = factory.WithWebHostBuilder(_ => { });
        using var client = localFactory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                await CreateBuyerSessionAsync(localFactory.Services));
        var examples = new[]
        {
            (ItemPriceSatang: 100_000L, FeeSatang: 5_900L),
            (ItemPriceSatang: 500_000L, FeeSatang: 20_000L),
            (ItemPriceSatang: 1_500_000L, FeeSatang: 55_000L),
            (ItemPriceSatang: 3_000_000L, FeeSatang: 100_000L)
        };

        foreach (var example in examples)
        {
            using var response = await client.GetAsync(
                "/api/mobile/pricing/buyer-protection" +
                $"?itemPriceSatang={example.ItemPriceSatang}");

            response.EnsureSuccessStatusCode();
            var preview = await response.Content
                .ReadFromJsonAsync<PricingPreviewResponse>();
            Assert.NotNull(preview);
            Assert.Equal(
                example.ItemPriceSatang,
                preview.ItemPriceSatang);
            Assert.Equal(
                example.FeeSatang,
                preview.BuyerProtectionFeeSatang);
            Assert.Equal(0, preview.PlatformFeeSatang);
            Assert.Equal(
                example.ItemPriceSatang,
                preview.SellerExpectedNetSatang);
            Assert.Equal(
                example.ItemPriceSatang + example.FeeSatang,
                preview.TotalBeforeShippingSatang);
            Assert.Equal("THB", preview.Currency);
            Assert.Equal(
                "buyer-protection-v2",
                preview.FeePolicyVersion);
        }

        await using var scope =
            localFactory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        Assert.Equal(
            0,
            await database.Transactions.CountAsync());
    }

    [Theory]
    [InlineData(99_999)]
    [InlineData(3_000_001)]
    public async Task Preview_rejects_item_prices_outside_enabled_range(
        long itemPriceSatang)
    {
        using var localFactory = factory.WithWebHostBuilder(_ => { });
        using var client = localFactory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                await CreateBuyerSessionAsync(localFactory.Services));

        using var response = await client.GetAsync(
            "/api/mobile/pricing/buyer-protection" +
            $"?itemPriceSatang={itemPriceSatang}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Preview_requires_authenticated_buyer()
    {
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });

        using var response = await client.GetAsync(
            "/api/mobile/pricing/buyer-protection" +
            "?itemPriceSatang=500000");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<string> CreateBuyerSessionAsync(
        IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        var buyer = BuyerAccount.Create(
            "+66981111111",
            "ผู้ซื้อ Pricing Preview",
            "pricing-preview@example.com",
            DateTimeOffset.UtcNow);
        database.Buyers.Add(buyer);
        await database.SaveChangesAsync();
        var tokens = scope.ServiceProvider
            .GetRequiredService<MobileSessionTokenService>();
        var session = await tokens.CreateAsync(
            new MobileSessionProfile(
                buyer.Id,
                null,
                buyer.PhoneNumber,
                buyer.FullName),
            CancellationToken.None);
        return session.AccessToken;
    }

    private sealed record PricingPreviewResponse(
        long ItemPriceSatang,
        long BuyerProtectionFeeSatang,
        long PlatformFeeSatang,
        long SellerExpectedNetSatang,
        long TotalBeforeShippingSatang,
        string Currency,
        string FeePolicyVersion);
}
