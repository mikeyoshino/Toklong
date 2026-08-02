using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Toklong.Api.Security;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Authentication;
using Toklong.Domain.Transactions;
using Toklong.Domain.Sellers;
using Toklong.Domain.Buyers;
using Toklong.Infrastructure.Persistence;
using Toklong.TestSupport;

namespace Toklong.Api.Tests.Api;

public sealed class MobileCounterQrApiTests :
    IClassFixture<MobileApiFactory>
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    private readonly MobileApiFactory factory;

    public MobileCounterQrApiTests(MobileApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Seller_receives_no_store_png_without_resource_metadata()
    {
        using var host = factory.WithWebHostBuilder(_ => { });
        var (transactionId, token, _, expected) =
            await SeedReadyAsync(host.Services);
        using var client = host.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync(
            $"/api/mobile/transactions/{transactionId}/counter-qr");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Contains("no-cache", response.Headers.Pragma.ToString());
        Assert.Equal(
            "nosniff",
            Assert.Single(
                response.Headers.GetValues(
                    "X-Content-Type-Options")));
        Assert.Contains(
            "default-src 'none'",
            Assert.Single(
                response.Headers.GetValues(
                    "Content-Security-Policy")));
        Assert.Equal(
            expected,
            await response.Content.ReadAsByteArrayAsync());

        using var detail = await client.GetAsync(
            $"/api/mobile/transactions/{transactionId}");
        var json = await detail.Content.ReadAsStringAsync();
        Assert.Contains("\"counterQrStatus\":\"Ready\"", json);
        Assert.DoesNotContain("protectedArtifact", json);
        Assert.DoesNotContain("providerResourceDigest", json);
        Assert.DoesNotContain("artifactSha256", json);
    }

    [Fact]
    public async Task Buyer_cannot_read_seller_counter_qr()
    {
        using var host = factory.WithWebHostBuilder(_ => { });
        var (transactionId, _, buyerToken, _) =
            await SeedReadyAsync(host.Services);
        using var client = host.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", buyerToken);

        using var response = await client.GetAsync(
            $"/api/mobile/transactions/{transactionId}/counter-qr");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Counter_qr_requires_authentication()
    {
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });

        using var response = await client.GetAsync(
            $"/api/mobile/transactions/{Guid.NewGuid()}/counter-qr");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refund_pending_transaction_revokes_ready_counter_qr_access()
    {
        using var host = factory.WithWebHostBuilder(_ => { });
        var (transactionId, token, _, _) =
            await SeedReadyAsync(
                host.Services,
                transaction => Assert.True(
                    transaction.MarkShipmentOverdue(
                        Now.AddHours(80),
                        new TransactionTransitionService())));
        using var client = host.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync(
            $"/api/mobile/transactions/{transactionId}/counter-qr");
        using var detail = await client.GetAsync(
            $"/api/mobile/transactions/{transactionId}");
        var json = await detail.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("\"counterQrStatus\":\"Ready\"", json);
    }

    private static async Task<(
        Guid TransactionId,
        string SellerToken,
        string BuyerToken,
        byte[] Png)>
        SeedReadyAsync(
            IServiceProvider services,
            Action<SaleTransaction>? mutate = null)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        var seller = SellerAccount.Create(
            "+66811111111",
            Now,
            "ผู้ขาย ทดสอบ");
        var buyer = BuyerAccount.Create(
            "+66822222222",
            "ผู้ซื้อ อื่น",
            "other-buyer@example.com",
            Now);
        var transaction =
            CounterQrTestTransactionFactory
                .ConfirmedManagedTransaction(
                    Now,
                    out var sellerId,
                    seller.Id);
        var resource = transaction.CurrentOutboundShipment!
            .CounterQrResource!;
        var png = CounterQrTestPng.Create();
        var protector = scope.ServiceProvider
            .GetRequiredService<ICounterQrArtifactProtector>();
        var protectedArtifact = protector.Protect(
            new CounterQrArtifact(png, "image/png"));
        var readyAt = Now.AddMinutes(6);
        resource.Claim(
            "test-setup",
            readyAt,
            TimeSpan.FromMinutes(1));
        resource.RecordReady(
            CounterQrRepresentation.ProviderPng,
            protectedArtifact.Ciphertext,
            protectedArtifact.ProtectionVersion,
            protectedArtifact.Sha256,
            new string('a', 64),
            null,
            readyAt,
            "test-setup");
        mutate?.Invoke(transaction);
        database.Sellers.Add(seller);
        database.Buyers.Add(buyer);
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();

        var tokens = scope.ServiceProvider
            .GetRequiredService<MobileSessionTokenService>();
        var session = await tokens.CreateAsync(
            new MobileSessionProfile(
                null,
                sellerId,
                "+66811111111",
                "ผู้ขาย ทดสอบ"),
            default);
        var buyerSession = await tokens.CreateAsync(
            new MobileSessionProfile(
                buyer.Id,
                null,
                buyer.PhoneNumber,
                buyer.FullName),
            default);
        return (
            transaction.Id,
            session.AccessToken,
            buyerSession.AccessToken,
            png);
    }
}
