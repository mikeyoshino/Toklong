using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Api.Tests.Api;

public sealed class InternalRetentionOperationsApiTests
    : IClassFixture<MobileApiFactory>
{
    private const string Secret =
        "integration-reconciliation-secret";
    private readonly MobileApiFactory factory;

    public InternalRetentionOperationsApiTests(
        MobileApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Signed_hold_blocks_retention_until_signed_release()
    {
        var transactionId =
            await SeedDueTransactionAsync();
        var reference = "LEGAL-001";
        var reason = "court preservation request";
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress =
                    new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
        using var remoteExecute = await client.PostAsJsonAsync(
            "/api/internal/retention/execute",
            new
            {
                BatchSize = 100,
                RequestedAt = DateTimeOffset.UtcNow
            });
        Assert.Equal(
            HttpStatusCode.NotFound,
            remoteExecute.StatusCode);
        using var malformed = await client.PostAsJsonAsync(
            $"/api/internal/transactions/{transactionId}/legal-hold",
            new
            {
                Reference = (string?)null,
                Reason = "invalid",
                RequestedAt = DateTimeOffset.UtcNow
            });
        Assert.Equal(
            HttpStatusCode.BadRequest,
            malformed.StatusCode);

        var requestedAt = DateTimeOffset.UtcNow;
        using var unsigned = await client.PostAsJsonAsync(
            $"/api/internal/transactions/{transactionId}/legal-hold",
            new
            {
                Reference = reference,
                Reason = reason,
                RequestedAt = requestedAt
            });
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            unsigned.StatusCode);

        var placePayload =
            $"legal-hold|place|{transactionId:N}|" +
            $"{reference}|{reason}|" +
            $"{requestedAt.ToUnixTimeSeconds()}";
        using var placed = await SendSignedAsync(
            client,
            $"/api/internal/transactions/{transactionId}/legal-hold",
            new
            {
                Reference = reference,
                Reason = reason,
                RequestedAt = requestedAt
            },
            placePayload);
        Assert.Equal(
            HttpStatusCode.NoContent,
            placed.StatusCode);

        requestedAt = DateTimeOffset.UtcNow;
        using var heldPreview =
            await RetentionOperationAsync(
                client,
                "preview",
                requestedAt);
        using var heldJson = JsonDocument.Parse(
            await heldPreview.Content
                .ReadAsStringAsync());
        Assert.Empty(
            heldJson.RootElement
                .GetProperty("transactionEvidence")
                .EnumerateArray());

        requestedAt = DateTimeOffset.UtcNow;
        var releasePayload =
            $"legal-hold|release|{transactionId:N}|" +
            $"{reference}|" +
            $"{requestedAt.ToUnixTimeSeconds()}";
        using var released = await SendSignedAsync(
            client,
            $"/api/internal/transactions/{transactionId}/legal-hold/release",
            new
            {
                Reference = reference,
                RequestedAt = requestedAt
            },
            releasePayload);
        Assert.Equal(
            HttpStatusCode.NoContent,
            released.StatusCode);

        requestedAt = DateTimeOffset.UtcNow;
        using var releasedPreview =
            await RetentionOperationAsync(
                client,
                "preview",
                requestedAt);
        Assert.Equal(
            HttpStatusCode.OK,
            releasedPreview.StatusCode);
        using var releasedJson = JsonDocument.Parse(
            await releasedPreview.Content
                .ReadAsStringAsync());
        Assert.Contains(
            releasedJson.RootElement
                .GetProperty("transactionEvidence")
                .EnumerateArray(),
            item =>
                item.GetProperty("transactionId")
                    .GetGuid() == transactionId);

        await using var scope =
            factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        var stored = await database.Transactions
            .Include(item => item.AuditEvents)
            .SingleAsync(
                item => item.Id == transactionId);
        Assert.False(stored.HasActiveLegalHold);
        Assert.Contains(
            stored.AuditEvents,
            item =>
                item.Name ==
                "retention.legal_hold_placed");
        Assert.Contains(
            stored.AuditEvents,
            item =>
                item.Name ==
                "retention.legal_hold_released");
    }

    private async Task<Guid> SeedDueTransactionAsync()
    {
        var start = DateTimeOffset.UtcNow
            .AddYears(
                -SaleTransaction
                    .EvidenceRetentionYears)
            .AddDays(-2);
        var transitions =
            new TransactionTransitionService();
        var transaction =
            TestTransactionFactory.CreateBuyerOffer(
                Guid.NewGuid(),
                "ผู้ซื้อ ทดสอบ",
                "+66811111111",
                FulfillmentType.PhysicalShipment,
                "กล้องพร้อมเลนส์",
                "กล้องใช้งานได้ปกติ",
                ConditionCode.UsedGood,
                "",
                "https://example.com/photo.jpg",
                450_000,
                "mvp-th-2026-07",
                start,
                transitions);
        Assert.True(transaction.ExpireIfDue(
            transaction.SellerAcceptanceDeadlineAt,
            transitions));
        Assert.True(
            transaction.RetentionExpiresAt <
            DateTimeOffset.UtcNow);

        await using var scope =
            factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        return transaction.Id;
    }

    private static Task<HttpResponseMessage>
        RetentionOperationAsync(
            HttpClient client,
            string operation,
            DateTimeOffset requestedAt)
    {
        const int batchSize = 100;
        var payload =
            $"retention|{operation}|{batchSize}|" +
            $"{requestedAt.ToUnixTimeSeconds()}";
        return SendSignedAsync(
            client,
            $"/api/internal/retention/{operation}",
            new
            {
                BatchSize = batchSize,
                RequestedAt = requestedAt
            },
            payload);
    }

    private static async Task<HttpResponseMessage>
        SendSignedAsync(
            HttpClient client,
            string path,
            object body,
            string payload)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add(
            "X-Toklong-Signature",
            Sign(payload));
        return await client.SendAsync(request);
    }

    private static string Sign(string payload)
    {
        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(Secret),
            Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash)
            .ToLowerInvariant();
    }
}
