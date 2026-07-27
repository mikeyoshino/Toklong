using System.Net;
using Stripe;
using Toklong.Application.Abstractions;
using Toklong.Infrastructure.Payments;

namespace Toklong.Application.Tests.Payments;

public sealed class StripeRefundProviderTests
{
    [Fact]
    public async Task Refund_uses_original_PaymentIntent_email_and_returns_action_details()
    {
        var transactionId = Guid.NewGuid();
        var expiresAt = new DateTimeOffset(
            2026, 9, 10, 9, 0, 0,
            TimeSpan.Zero);
        var sentAt = new DateTimeOffset(
            2026, 7, 27, 9, 0, 0,
            TimeSpan.Zero);
        var http = new StripeHttpClient(
            transactionId,
            expiresAt,
            sentAt);
        var stripeClient = new StripeClient(
            "sk_test_not_real",
            httpClient: http);
        var provider = new StripeRefundProvider(
            new StripePaymentOptions
            {
                Enabled = true,
                LiveMode = false,
                SecretKey = "sk_test_not_real"
            },
            new FixedClock(sentAt),
            stripeClient);

        var result = await provider.CreateFullRefundAsync(
            transactionId,
            "pi_promptpay",
            111_400,
            "THB",
            null,
            default);

        Assert.Equal(
            "requires_action",
            result.Status);
        Assert.Equal(
            expiresAt,
            result.ActionExpiresAt);
        Assert.Equal(
            sentAt,
            result.InstructionsSentAt);
        Assert.Contains(
            "instructions_email=buyer%40example.com",
            http.RefundRequestBody);
        Assert.DoesNotContain(
            "bank",
            http.RefundRequestBody,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Card_refund_does_not_send_PromptPay_only_instructions_parameter()
    {
        var transactionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var http = new StripeHttpClient(
            transactionId,
            now.AddDays(45),
            now,
            "card");
        var provider = new StripeRefundProvider(
            new StripePaymentOptions
            {
                Enabled = true,
                LiveMode = false,
                SecretKey = "sk_test_not_real"
            },
            new FixedClock(now),
            new StripeClient(
                "sk_test_not_real",
                httpClient: http));

        await provider.CreateFullRefundAsync(
            transactionId,
            "pi_promptpay",
            111_400,
            "THB",
            null,
            default);

        Assert.DoesNotContain(
            "instructions_email",
            http.RefundRequestBody,
            StringComparison.Ordinal);
    }

    private sealed class StripeHttpClient(
        Guid transactionId,
        DateTimeOffset expiresAt,
        DateTimeOffset sentAt,
        string paymentMethodType = "promptpay")
        : Stripe.IHttpClient
    {
        public string RefundRequestBody { get; private set; } = "";

        public async Task<StripeResponse> MakeRequestAsync(
            StripeRequest request,
            CancellationToken cancellationToken =
                default)
        {
            string response;
            if (request.Method == HttpMethod.Get &&
                request.Uri.AbsolutePath ==
                "/v1/payment_intents/pi_promptpay")
            {
                response = $$"""
                {
                  "id": "pi_promptpay",
                  "object": "payment_intent",
                  "amount": 111400,
                  "currency": "thb",
                  "livemode": false,
                  "latest_charge": {
                    "id": "ch_promptpay",
                    "object": "charge",
                    "payment_method_details": {
                      "type": "{{paymentMethodType}}"
                    }
                  },
                  "metadata": {
                    "toklong_transaction_id": "{{transactionId:N}}"
                  },
                  "receipt_email": "buyer@example.com",
                  "status": "succeeded"
                }
                """;
            }
            else if (request.Method == HttpMethod.Post &&
                     request.Uri.AbsolutePath ==
                     "/v1/refunds")
            {
                RefundRequestBody =
                    request.Content is null
                        ? ""
                        : await request.Content
                            .ReadAsStringAsync(
                                cancellationToken);
                response = $$"""
                {
                  "id": "re_promptpay",
                  "object": "refund",
                  "amount": 111400,
                  "currency": "thb",
                  "instructions_email": "buyer@example.com",
                  "metadata": {
                    "toklong_transaction_id": "{{transactionId:N}}",
                    "toklong_currency": "thb"
                  },
                  "next_action": {
                    "type": "display_details",
                    "display_details": {
                      "expires_at": {{expiresAt.ToUnixTimeSeconds()}},
                      "email_sent": {
                        "email_sent_at": {{sentAt.ToUnixTimeSeconds()}},
                        "email_sent_to": "buyer@example.com"
                      }
                    }
                  },
                  "payment_intent": "pi_promptpay",
                  "status": "requires_action"
                }
                """;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unexpected Stripe request: {request.Method} {request.Uri}");
            }

            using var message = new HttpResponseMessage(
                HttpStatusCode.OK);
            return new StripeResponse(
                HttpStatusCode.OK,
                message.Headers,
                response);
        }

        public Task<StripeStreamedResponse>
            MakeStreamingRequestAsync(
            StripeRequest request,
            CancellationToken cancellationToken =
                default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedClock(DateTimeOffset now)
        : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
