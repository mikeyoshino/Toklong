using System.Security.Cryptography;
using System.Text;
using Stripe;
using Toklong.Api.Security;

namespace Toklong.Api.Tests.Security;

public sealed class StripeWebhookEventParserTests
{
    private const string Secret = "whsec_test_secret";
    private const string Payload =
        """
        {
          "id": "evt_test_001",
          "object": "event",
          "api_version": "2026-06-24.dahlia",
          "created": 1784937600,
          "data": {
            "object": {
              "id": "pi_test_001",
              "object": "payment_intent",
              "amount": 450000,
              "currency": "thb",
              "created": 1784937600,
              "livemode": false,
              "metadata": {
                "toklong_transaction_id": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
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

    [Fact]
    public void Valid_signature_is_accepted()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var parser = new StripeWebhookEventParser();

        var stripeEvent = parser.Parse(
            Payload,
            Signature(Payload, timestamp),
            Secret);

        Assert.Equal("evt_test_001", stripeEvent.Id);
        Assert.Equal(EventTypes.PaymentIntentSucceeded, stripeEvent.Type);
    }

    [Fact]
    public void Tampered_payload_is_rejected()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var parser = new StripeWebhookEventParser();
        var signature = Signature(Payload, timestamp);

        Assert.Throws<StripeException>(() =>
            parser.Parse(
                Payload.Replace("450000", "1", StringComparison.Ordinal),
                signature,
                Secret));
    }

    [Fact]
    public void Stale_signature_is_rejected()
    {
        var timestamp = DateTimeOffset.UtcNow
            .AddMinutes(-6)
            .ToUnixTimeSeconds();
        var parser = new StripeWebhookEventParser();

        Assert.Throws<StripeException>(() =>
            parser.Parse(
                Payload,
                Signature(Payload, timestamp),
                Secret));
    }

    private static string Signature(string payload, long timestamp)
    {
        var signedPayload = $"{timestamp}.{payload}";
        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(Secret),
            Encoding.UTF8.GetBytes(signedPayload));
        return $"t={timestamp},v1={Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
