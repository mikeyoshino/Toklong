using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Api.Tests.Api;

public sealed class StripeWebhookApiTests
    : IClassFixture<MobileApiFactory>
{
    private const string WebhookSecret =
        "whsec_integration_test";
    private readonly MobileApiFactory factory;

    public StripeWebhookApiTests(MobileApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Signed_success_is_processed_once_and_replay_is_safe()
    {
        var transaction = await SeedPendingStripeTransactionAsync();
        var eventCreated = new DateTimeOffset(
            2026,
            7,
            25,
            10,
            0,
            0,
            TimeSpan.Zero);
        var payload = Payload(
            transaction.Id,
            "evt_http_001",
            "pi_http_001",
            455_000,
            eventCreated);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
        using var first = await PostAsync(
            client,
            payload,
            Signature(payload, timestamp));
        first.EnsureSuccessStatusCode();
        using var firstBody = JsonDocument.Parse(
            await first.Content.ReadAsStringAsync());
        Assert.False(
            firstBody.RootElement
                .GetProperty("alreadyProcessed")
                .GetBoolean());

        using var replay = await PostAsync(
            client,
            payload,
            Signature(payload, timestamp));
        replay.EnsureSuccessStatusCode();
        using var replayBody = JsonDocument.Parse(
            await replay.Content.ReadAsStringAsync());
        Assert.True(
            replayBody.RootElement
                .GetProperty("alreadyProcessed")
                .GetBoolean());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        var stored = await db.Transactions
            .Include(item => item.ExternalEvents)
            .SingleAsync(item => item.Id == transaction.Id);
        Assert.NotNull(stored);
        Assert.Equal(
            TransactionState.PaidAwaitingShipment,
            stored.State);
        Assert.Equal(
            eventCreated.AddHours(72),
            stored.ShipByAt);
        Assert.Single(
            stored.ExternalEvents,
            item => item.EventId == "evt_http_001");
    }

    [Fact]
    public async Task Invalid_signature_cannot_change_payment_state()
    {
        var transaction = await SeedPendingStripeTransactionAsync(
            "pi_http_invalid");
        var payload = Payload(
            transaction.Id,
            "evt_http_invalid",
            "pi_http_invalid",
            450_000,
            DateTimeOffset.UtcNow);
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });

        using var response = await PostAsync(
            client,
            payload,
            "t=1,v1=invalid");

        Assert.Equal(
            System.Net.HttpStatusCode.BadRequest,
            response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        var stored = await db.Transactions
            .SingleAsync(item => item.Id == transaction.Id);
        Assert.NotNull(stored);
        Assert.Equal(
            TransactionState.PaymentPending,
            stored.State);
    }

    [Fact]
    public async Task Missing_signature_cannot_change_payment_state()
    {
        var transaction = await SeedPendingStripeTransactionAsync(
            "pi_http_missing_signature");
        var payload = Payload(
            transaction.Id,
            "evt_http_missing_signature",
            "pi_http_missing_signature",
            455_000,
            DateTimeOffset.UtcNow);
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });

        using var response = await client.PostAsync(
            "/api/webhooks/stripe",
            new StringContent(
                payload,
                Encoding.UTF8,
                "application/json"));

        Assert.Equal(
            System.Net.HttpStatusCode.Unauthorized,
            response.StatusCode);
        await AssertPaymentPendingAsync(transaction.Id);
    }

    [Fact]
    public async Task Signed_wrong_amount_cannot_change_payment_state()
    {
        var transaction = await SeedPendingStripeTransactionAsync(
            "pi_http_wrong_amount");
        var payload = Payload(
            transaction.Id,
            "evt_http_wrong_amount",
            "pi_http_wrong_amount",
            1,
            DateTimeOffset.UtcNow);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });

        using var response = await PostAsync(
            client,
            payload,
            Signature(payload, timestamp));

        Assert.Equal(
            System.Net.HttpStatusCode.BadRequest,
            response.StatusCode);
        await AssertPaymentPendingAsync(transaction.Id);
    }

    [Fact]
    public async Task Signed_wrong_payment_intent_cannot_change_payment_state()
    {
        var transaction = await SeedPendingStripeTransactionAsync(
            "pi_http_expected");
        var payload = Payload(
            transaction.Id,
            "evt_http_wrong_intent",
            "pi_http_other",
            455_000,
            DateTimeOffset.UtcNow);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });

        using var response = await PostAsync(
            client,
            payload,
            Signature(payload, timestamp));

        Assert.Equal(
            System.Net.HttpStatusCode.BadRequest,
            response.StatusCode);
        await AssertPaymentPendingAsync(transaction.Id);
    }

    [Fact]
    public async Task Signed_success_event_with_non_succeeded_object_is_rejected()
    {
        var transaction = await SeedPendingStripeTransactionAsync(
            "pi_http_processing");
        var payload = Payload(
                transaction.Id,
                "evt_http_processing",
                "pi_http_processing",
                455_000,
                DateTimeOffset.UtcNow)
            .Replace(
                "\"status\": \"succeeded\"",
                "\"status\": \"processing\"",
                StringComparison.Ordinal);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });

        using var response = await PostAsync(
            client,
            payload,
            Signature(payload, timestamp));

        Assert.Equal(
            System.Net.HttpStatusCode.BadRequest,
            response.StatusCode);
        await AssertPaymentPendingAsync(transaction.Id);
    }

    [Fact]
    public async Task Signed_event_from_wrong_Stripe_environment_is_rejected()
    {
        var transaction = await SeedPendingStripeTransactionAsync(
            "pi_http_live");
        var payload = Payload(
                transaction.Id,
                "evt_http_live",
                "pi_http_live",
                455_000,
                DateTimeOffset.UtcNow)
            .Replace(
                "\"livemode\": false",
                "\"livemode\": true",
                StringComparison.Ordinal);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });

        using var response = await PostAsync(
            client,
            payload,
            Signature(payload, timestamp));

        Assert.Equal(
            System.Net.HttpStatusCode.BadRequest,
            response.StatusCode);
        await AssertPaymentPendingAsync(transaction.Id);
    }

    [Fact]
    public async Task Signed_payment_after_deadline_never_exposes_fulfillment()
    {
        var transaction = await SeedPendingStripeTransactionAsync(
            "pi_http_late");
        var eventCreated =
            transaction.BuyerPaymentDeadlineAt!.Value.AddSeconds(1);
        var payload = Payload(
            transaction.Id,
            "evt_http_late",
            "pi_http_late",
            455_000,
            eventCreated);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });

        using var response = await PostAsync(
            client,
            payload,
            Signature(payload, timestamp));

        response.EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        var stored = await db.Transactions
            .Include(item => item.ExternalEvents)
            .SingleAsync(item => item.Id == transaction.Id);
        Assert.Equal(TransactionState.RefundPending, stored.State);
        Assert.Null(stored.ShipByAt);
        Assert.Single(
            stored.ExternalEvents,
            item => item.EventId == "evt_http_late");
    }

    [Fact]
    public async Task Signed_succeeded_refund_is_processed_once()
    {
        var transaction = await SeedPendingStripeRefundAsync(
            "pi_http_refund",
            "re_http_001");

        var eventCreated = DateTimeOffset.UtcNow;
        var payload = RefundPayload(
            transaction.Id,
            "evt_refund_http_001",
            "re_http_001",
            "pi_http_refund",
            455_000,
            eventCreated);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });

        using var first = await PostAsync(
            client,
            payload,
            Signature(payload, timestamp));
        first.EnsureSuccessStatusCode();
        using var replay = await PostAsync(
            client,
            payload,
            Signature(payload, timestamp));
        replay.EnsureSuccessStatusCode();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        var refunded = await db.Transactions
            .Include(item => item.ExternalEvents)
            .SingleAsync(item => item.Id == transaction.Id);
        Assert.Equal(TransactionState.Refunded, refunded.State);
        Assert.Equal("re_http_001", refunded.RefundReference);
        Assert.NotNull(refunded.RefundConfirmedAt);
        Assert.Single(
            refunded.ExternalEvents,
            item => item.EventId == "evt_refund_http_001");
    }

    [Fact]
    public async Task Signed_refund_with_wrong_amount_cannot_complete_refund()
    {
        var transaction = await SeedPendingStripeRefundAsync(
            "pi_http_refund_wrong_amount",
            "re_http_wrong_amount");
        var payload = RefundPayload(
            transaction.Id,
            "evt_refund_http_wrong_amount",
            "re_http_wrong_amount",
            "pi_http_refund_wrong_amount",
            454_999,
            DateTimeOffset.UtcNow);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });

        using var response = await PostAsync(
            client,
            payload,
            Signature(payload, timestamp));

        Assert.Equal(
            System.Net.HttpStatusCode.BadRequest,
            response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        var stored = await database.Transactions
            .Include(item => item.ExternalEvents)
            .SingleAsync(item => item.Id == transaction.Id);
        Assert.Equal(TransactionState.RefundPending, stored.State);
        Assert.Null(stored.RefundConfirmedAt);
        Assert.DoesNotContain(
            stored.ExternalEvents,
            item => item.EventId ==
                    "evt_refund_http_wrong_amount");
    }

    [Fact]
    public async Task Signed_refund_progress_is_replay_safe_and_succeeds_only_after_confirmation()
    {
        var transaction = await SeedPendingStripeRefundAsync(
            "pi_http_promptpay_refund",
            "re_http_promptpay");
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddDays(45);
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });

        async Task PostProgressAsync(
            string eventId,
            string status,
            DateTimeOffset eventCreated)
        {
            var payload = RefundPayload(
                transaction.Id,
                eventId,
                "re_http_promptpay",
                "pi_http_promptpay_refund",
                455_000,
                eventCreated,
                status,
                status == "requires_action"
                    ? expiresAt
                    : null);
            using var response = await PostAsync(
                client,
                payload,
                Signature(
                    payload,
                    DateTimeOffset.UtcNow
                        .ToUnixTimeSeconds()));
            response.EnsureSuccessStatusCode();
        }

        await PostProgressAsync(
            "evt_refund_action_001",
            "requires_action",
            now);
        await PostProgressAsync(
            "evt_refund_action_001",
            "requires_action",
            now);
        await PostProgressAsync(
            "evt_refund_pending_001",
            "pending",
            now.AddMinutes(1));
        await PostProgressAsync(
            "evt_refund_action_002",
            "requires_action",
            now.AddMinutes(2));

        await using (var progressScope =
                     factory.Services.CreateAsyncScope())
        {
            var progressDatabase = progressScope.ServiceProvider
                .GetRequiredService<ToklongDbContext>();
            var pending = await progressDatabase.Transactions
                .Include(item => item.ExternalEvents)
                .Include(item => item.Notifications)
                .SingleAsync(item =>
                    item.Id == transaction.Id);
            Assert.Equal(
                TransactionState.RefundPending,
                pending.State);
            Assert.Equal(
                "requires_action",
                pending.RefundProviderStatus);
            Assert.Equal(
                expiresAt.ToUnixTimeSeconds(),
                pending.RefundActionExpiresAt!
                    .Value.ToUnixTimeSeconds());
            Assert.Equal(
                2,
                pending.Notifications.Count(item =>
                    item.Template ==
                    "refund_action_required"));
            Assert.Equal(
                3,
                pending.ExternalEvents.Count(item =>
                    item.EventType.StartsWith(
                        "refund.",
                        StringComparison.Ordinal)));
        }

        await PostProgressAsync(
            "evt_refund_succeeded_001",
            "succeeded",
            now.AddMinutes(3));

        await using var finalScope =
            factory.Services.CreateAsyncScope();
        var finalDatabase = finalScope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        var refunded = await finalDatabase.Transactions
            .Include(item => item.ExternalEvents)
            .SingleAsync(item =>
                item.Id == transaction.Id);
        Assert.Equal(
            TransactionState.Refunded,
            refunded.State);
        Assert.Equal(
            "succeeded",
            refunded.RefundProviderStatus);
    }

    private async Task<SaleTransaction> SeedPendingStripeTransactionAsync(
        string paymentIntentId = "pi_http_001")
    {
        var now = new DateTimeOffset(
            2026,
            7,
            25,
            9,
            0,
            0,
            TimeSpan.Zero);
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
            0,
            10_000,
            440_000,
            "fee-test-v1",
            TestTransactionFactory.ShippingQuote(
                now.AddMinutes(1)));
        transaction.BeginCheckout(
            "ผู้ซื้อ ทดสอบ",
            "+66811111111",
            "123 กรุงเทพฯ",
            now.AddMinutes(2),
            transitions,
            "stripe",
            paymentIntentId,
            10_000,
            440_000,
            "fee-test-v1");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        return transaction;
    }

    private async Task<SaleTransaction> SeedPendingStripeRefundAsync(
        string paymentIntentId,
        string refundReference)
    {
        var transaction = await SeedPendingStripeTransactionAsync(
            paymentIntentId);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        var stored = await database.Transactions
            .Include(item => item.AuditEvents)
            .Include(item => item.AgreementAcceptances)
            .Include(item => item.ExternalEvents)
            .SingleAsync(item => item.Id == transaction.Id);
        stored.ConfirmStripePayment(
            $"evt_http_late_for_{refundReference}",
            paymentIntentId,
            stored.BuyerTotalSatang,
            stored.Currency,
            stored.BuyerPaymentDeadlineAt!.Value.AddSeconds(1),
            stored.BuyerPaymentDeadlineAt.Value.AddSeconds(2),
            new TransactionTransitionService());
        stored.RecordRefundInstruction(
            "stripe",
            refundReference,
            DateTimeOffset.UtcNow);
        await database.SaveChangesAsync();
        return stored;
    }

    private async Task AssertPaymentPendingAsync(Guid transactionId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        var transaction = await database.Transactions
            .Include(item => item.ExternalEvents)
            .SingleAsync(item => item.Id == transactionId);
        Assert.Equal(TransactionState.PaymentPending, transaction.State);
        Assert.Empty(transaction.ExternalEvents);
    }

    private static Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string payload,
        string signature)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/webhooks/stripe")
        {
            Content = new StringContent(
                payload,
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.TryAddWithoutValidation(
            "Stripe-Signature",
            signature);
        return client.SendAsync(request);
    }

    private static string Payload(
        Guid transactionId,
        string eventId,
        string paymentIntentId,
        long amountSatang,
        DateTimeOffset eventCreated) =>
        $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "api_version": "2026-06-24.dahlia",
          "created": {{eventCreated.ToUnixTimeSeconds()}},
          "data": {
            "object": {
              "id": "{{paymentIntentId}}",
              "object": "payment_intent",
              "amount": {{amountSatang}},
              "currency": "thb",
              "created": {{eventCreated.AddMinutes(-5).ToUnixTimeSeconds()}},
              "livemode": false,
              "metadata": {
                "toklong_transaction_id": "{{transactionId:N}}"
              },
              "status": "succeeded"
            }
          },
          "livemode": false,
          "pending_webhooks": 1,
          "request": null,
          "type": "payment_intent.succeeded"
        }
        """;

    private static string RefundPayload(
        Guid transactionId,
        string eventId,
        string refundId,
        string paymentIntentId,
        long amountSatang,
        DateTimeOffset eventCreated,
        string status = "succeeded",
        DateTimeOffset? actionExpiresAt = null)
    {
        var nextAction =
            status == "requires_action"
                ? $$"""
                  ,
                  "next_action": {
                    "type": "display_details",
                    "display_details": {
                      "expires_at": {{actionExpiresAt!.Value.ToUnixTimeSeconds()}},
                      "email_sent": {
                        "email_sent_at": {{eventCreated.ToUnixTimeSeconds()}},
                        "email_sent_to": "buyer@example.com"
                      }
                    }
                  }
                  """
                : "";
        return $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "api_version": "2026-06-24.dahlia",
          "created": {{eventCreated.ToUnixTimeSeconds()}},
          "data": {
            "object": {
              "id": "{{refundId}}",
              "object": "refund",
              "amount": {{amountSatang}},
              "currency": "thb",
              "created": {{eventCreated.AddMinutes(-1).ToUnixTimeSeconds()}},
              "metadata": {
                "toklong_transaction_id": "{{transactionId:N}}"
              },
              "payment_intent": "{{paymentIntentId}}",
              "status": "{{status}}"{{nextAction}}
            }
          },
          "livemode": false,
          "pending_webhooks": 1,
          "request": null,
          "type": "refund.updated"
        }
        """;
    }

    private static string Signature(string payload, long timestamp)
    {
        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(WebhookSecret),
            Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        return $"t={timestamp},v1={Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
