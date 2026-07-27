using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Api.Tests.Api;

public sealed class InternalCarrierOperationsApiTests
    : IClassFixture<MobileApiFactory>
{
    private const string Secret =
        "integration-reconciliation-secret";
    private readonly MobileApiFactory factory;

    public InternalCarrierOperationsApiTests(
        MobileApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Signed_carrier_event_is_replay_safe()
    {
        var transactionId = await SeedTrackingAsync();
        var requestedAt = DateTimeOffset.UtcNow;
        var eventId = "carrier-local-001";
        var eventType = "in_transit";
        var carrierCode = "FLASH";
        var trackingNumber = "TH1234567890";
        var payload =
            $"carrier|{transactionId:N}|{eventId}|{eventType}|" +
            $"{carrierCode}|{trackingNumber}|" +
            $"{requestedAt.ToUnixTimeSeconds()}";
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
        var path =
            $"/api/internal/transactions/{transactionId}/carrier-events";
        var body = new
        {
            EventId = eventId,
            EventType = eventType,
            OccurredAt = requestedAt,
            RequestedAt = requestedAt,
            CarrierCode = carrierCode,
            TrackingNumber = trackingNumber
        };

        using var unsigned = await client.PostAsJsonAsync(path, body);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            unsigned.StatusCode);

        using var first = await SendSignedAsync(
            client,
            path,
            body,
            payload);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var replay = await SendSignedAsync(
            client,
            path,
            body,
            payload);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        var stored = await database.Transactions
            .Include(item => item.ExternalEvents)
            .SingleAsync(item => item.Id == transactionId);
        Assert.Equal(TransactionState.InTransit, stored.State);
        Assert.Single(
            stored.ExternalEvents,
            item => item.EventId == eventId);
    }

    private async Task<Guid> SeedTrackingAsync()
    {
        var now = DateTimeOffset.UtcNow.AddHours(-1);
        var transitions = new TransactionTransitionService();
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ ทดสอบ",
            "+66811111111",
            FulfillmentType.PhysicalShipment,
            "กล้องพร้อมเลนส์",
            "ใช้งานปกติ มีรอยตามรูป พร้อมสายคล้อง",
            ConditionCode.UsedDefects,
            "มีรอยด้านข้าง",
            "https://example.com/photo.jpg",
            450_000,
            "mvp-th-2026-07",
            now,
            transitions);
        transaction.AcceptBuyerOffer(
            Guid.NewGuid(),
            "ผู้ขาย ทดสอบ",
            "+66822222222",
            "KBANK",
            "ผู้ขาย ทดสอบ",
            "1234567890",
            true,
            now.AddMinutes(1),
            transitions,
            shipping: TestTransactionFactory.ShippingQuote(
                now.AddMinutes(1)));
        transaction.BeginCheckout(
            "ผู้ซื้อ ทดสอบ",
            "+66811111111",
            "123 กรุงเทพฯ",
            now.AddMinutes(2),
            transitions);
        transaction.ConfirmPayment(
            "payment-carrier-001",
            now.AddMinutes(3),
            transitions);
        transaction.SubmitTracking(
            transaction.SellerAccessToken,
            "FLASH",
            "TH1234567890",
            now.AddMinutes(4),
            transitions);

        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        return transaction.Id;
    }

    private static Task<HttpResponseMessage> SendSignedAsync(
        HttpClient client,
        string path,
        object body,
        string payload)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add(
            "X-Toklong-Signature",
            Sign(payload));
        return client.SendAsync(request);
    }

    private static string Sign(string payload)
    {
        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(Secret),
            Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
