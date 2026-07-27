using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Api.Tests.Api;

public sealed class InternalDisputeOperationsApiTests
    : IClassFixture<MobileApiFactory>
{
    private readonly MobileApiFactory factory;

    public InternalDisputeOperationsApiTests(
        MobileApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Direct_dispute_resolution_endpoint_is_not_exposed()
    {
        var transactionId = await SeedDisputeAsync();
        var requestedAt = DateTimeOffset.UtcNow;
        var reference = "CASE-REFUND-001";
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/internal/disputes/{transactionId}/resolution")
        {
            Content = JsonContent.Create(new
            {
                ReviewReference = reference,
                Resolution = "FullRefund",
                RequestedAt = requestedAt
            })
        };
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        var stored = await database.Transactions
            .Include(item => item.AuditEvents)
            .SingleAsync(item => item.Id == transactionId);
        Assert.Equal(TransactionState.Disputed, stored.State);
        Assert.Null(stored.DisputeResolutionReference);
        Assert.DoesNotContain(
            stored.AuditEvents,
            audit => audit.Name.StartsWith(
                "dispute.resolved_",
                StringComparison.Ordinal));
    }

    private async Task<Guid> SeedDisputeAsync()
    {
        var now = DateTimeOffset.UtcNow.AddHours(-5);
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
            "payment-dispute-001",
            now.AddMinutes(3),
            transitions);
        transaction.SubmitTracking(
            transaction.SellerAccessToken,
            "FLASH",
            "TH12345678",
            now.AddMinutes(4),
            transitions);
        transaction.RecordCarrierEvent(
            "delivery-dispute-001",
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

        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        return transaction.Id;
    }
}
