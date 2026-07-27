using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Toklong.Application.Abstractions;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Api.Tests.Api;

public sealed class MobileDisputeEvidenceApiTests
    : IClassFixture<MobileApiFactory>
{
    private readonly MobileApiFactory factory;

    public MobileDisputeEvidenceApiTests(
        MobileApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Buyer_upload_is_idempotent_private_and_downloadable()
    {
        var store = new InMemoryEvidenceStore();
        using var customizedFactory = factory.WithWebHostBuilder(
            builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDisputeEvidenceStore>();
                services.AddSingleton<IDisputeEvidenceStore>(store);
            }));
        using var client = customizedFactory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
        var accessToken = await SignUpAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);
        var transactionId = await SeedDisputedTransactionAsync(
            customizedFactory.Services);

        using var first = await UploadAsync(
            client,
            transactionId,
            "mobile-evidence-retry");
        using var replay = await UploadAsync(
            client,
            transactionId,
            "mobile-evidence-retry");

        Assert.True(
            first.IsSuccessStatusCode,
            await first.Content.ReadAsStringAsync());
        Assert.True(
            replay.IsSuccessStatusCode,
            await replay.Content.ReadAsStringAsync());
        using var firstJson = JsonDocument.Parse(
            await first.Content.ReadAsStringAsync());
        using var replayJson = JsonDocument.Parse(
            await replay.Content.ReadAsStringAsync());
        var evidenceId =
            firstJson.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(
            evidenceId,
            replayJson.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(1, store.SaveCount);

        using var list = await client.GetAsync(
            $"/api/mobile/transactions/{transactionId}/dispute-evidence?party=Buyer");
        list.EnsureSuccessStatusCode();
        using var listJson = JsonDocument.Parse(
            await list.Content.ReadAsStringAsync());
        Assert.Single(listJson.RootElement.EnumerateArray());

        using var download = await client.GetAsync(
            $"/api/mobile/transactions/{transactionId}/dispute-evidence/{evidenceId}?party=Buyer");
        download.EnsureSuccessStatusCode();
        Assert.Equal(
            "no-store",
            download.Headers.CacheControl?.ToString());
        Assert.Equal(
            store.Content,
            await download.Content.ReadAsByteArrayAsync());

        using var counterparty = await client.GetAsync(
            $"/api/mobile/transactions/{transactionId}/dispute-evidence?party=Seller");
        Assert.Equal(
            HttpStatusCode.BadRequest,
            counterparty.StatusCode);

        await using var scope =
            customizedFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        Assert.Equal(
            1,
            await db.DisputeEvidence.CountAsync());
        Assert.Equal(
            1,
            await db.AuditEvents.CountAsync(item =>
                item.Name == "dispute.evidence_submitted"));
    }

    [Fact]
    public async Task Upload_requires_authentication()
    {
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });

        using var response = await UploadAsync(
            client,
            Guid.NewGuid(),
            "unauthenticated");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        Guid transactionId,
        string idempotencyKey)
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent("Buyer"), "party" },
            {
                new StringContent("Packaging"),
                "evidenceType"
            },
            {
                new StringContent(
                    "ภาพกล่องและตัวสินค้าหลังรับของ"),
                "description"
            }
        };
        var file = new ByteArrayContent([1, 2, 3, 4]);
        file.Headers.ContentType =
            new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "file", "evidence.jpg");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/mobile/transactions/{transactionId}/dispute-evidence")
        {
            Content = content
        };
        request.Headers.Add(
            "Idempotency-Key",
            idempotencyKey);
        return await client.SendAsync(request);
    }

    private static async Task<Guid> SeedDisputedTransactionAsync(
        IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        var buyer = await db.Buyers.SingleAsync(item =>
            item.PhoneNumber == "+66812345678");
        var transitions = new TransactionTransitionService();
        var now = DateTimeOffset.UtcNow.AddHours(-4);
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            buyer.Id,
            buyer.FullName,
            buyer.PhoneNumber,
            "+66899999999",
            FulfillmentType.PhysicalShipment,
            "กล้องพร้อมเลนส์",
            "ใช้งานได้ปกติ",
            ConditionCode.UsedGood,
            "",
            null,
            450_000,
            "mvp-th-2026-07",
            now,
            transitions);
        transaction.AcceptBuyerOffer(
            Guid.NewGuid(),
            "ผู้ขาย ทดสอบ",
            "+66899999999",
            "KBANK",
            "ผู้ขาย ทดสอบ",
            "1234567890",
            true,
            now.AddMinutes(1),
            transitions,
            shipping: TestTransactionFactory.ShippingQuote(
                now.AddMinutes(1)));
        transaction.BeginCheckout(
            buyer.FullName,
            buyer.PhoneNumber,
            "123 กรุงเทพฯ",
            now.AddMinutes(2),
            transitions);
        transaction.ConfirmPayment(
            $"payment-{Guid.NewGuid():N}",
            now.AddMinutes(3),
            transitions);
        transaction.SubmitTracking(
            transaction.SellerAccessToken,
            "FLASH",
            $"TH{Random.Shared.Next(10000000, 99999999)}",
            now.AddMinutes(4),
            transitions);
        transaction.RecordCarrierEvent(
            $"delivery-{Guid.NewGuid():N}",
            "delivered",
            now.AddHours(1),
            now.AddHours(1),
            transitions);
        transaction.OpenDispute(
            transaction.BuyerAccessToken!,
            DisputeReason.NotAsDescribed,
            "สินค้าไม่ตรงตามรายละเอียด",
            now.AddHours(2),
            transitions);
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        return transaction.Id;
    }

    private static async Task<string> SignUpAsync(
        HttpClient client)
    {
        using var requestOtp = await client.PostAsJsonAsync(
            "/api/mobile/auth/otp/request",
            new
            {
                PhoneNumber = "0812345678",
                Mode = "SignUp",
                FullName = "ผู้ซื้อ หลักฐาน",
                Email = "evidence@example.com"
            });
        requestOtp.EnsureSuccessStatusCode();
        using var otpJson = JsonDocument.Parse(
            await requestOtp.Content.ReadAsStringAsync());
        var challengeId = otpJson.RootElement
            .GetProperty("challengeId")
            .GetString();
        using var verify = await client.PostAsJsonAsync(
            "/api/mobile/auth/otp/verify",
            new
            {
                ChallengeId = challengeId,
                Code = "123456",
                Mode = "SignUp",
                FullName = "ผู้ซื้อ หลักฐาน",
                Email = "evidence@example.com"
            });
        verify.EnsureSuccessStatusCode();
        using var sessionJson = JsonDocument.Parse(
            await verify.Content.ReadAsStringAsync());
        return sessionJson.RootElement
            .GetProperty("accessToken")
            .GetString()!;
    }

    private sealed class InMemoryEvidenceStore
        : IDisputeEvidenceStore
    {
        private readonly Dictionary<string, byte[]> files = [];

        public byte[] Content { get; } = [0xff, 0xd8, 1, 2, 0xff, 0xd9];
        public int SaveCount { get; private set; }

        public Task<StoredDisputeEvidenceFile> SaveImageAsync(
            DisputeEvidenceFileInput input,
            CancellationToken cancellationToken)
        {
            SaveCount++;
            var reference = $"evidence:{Guid.NewGuid():N}.bin";
            files[reference] = Content;
            return Task.FromResult(
                new StoredDisputeEvidenceFile(
                    reference,
                    "image/jpeg",
                    Content.LongLength,
                    Convert.ToHexString(
                            SHA256.HashData(Content))
                        .ToLowerInvariant()));
        }

        public Task<DisputeEvidenceFileContent> ReadAsync(
            string storageReference,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new DisputeEvidenceFileContent(
                    files[storageReference],
                    "image/jpeg"));

        public Task DeleteAsync(
            string storageReference,
            CancellationToken cancellationToken)
        {
            files.Remove(storageReference);
            return Task.CompletedTask;
        }
    }
}
