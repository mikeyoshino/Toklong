using System.Net;
using System.Text;
using System.Text.Json;
using Toklong.Infrastructure.Payments;
using Toklong.Infrastructure.Services;

namespace Toklong.Application.Tests.Payments;

public sealed class ExternalProviderBoundaryTests
{
    [Fact]
    public async Task Bank_payout_uses_integer_satang_and_stable_idempotency()
    {
        var handler = new StubHandler(
            """
            {"reference":"bank-transfer-001","status":"accepted"}
            """);
        var provider = new HttpBankPayoutProvider(
            new HttpClient(handler),
            new BankPayoutOptions
            {
                Provider = "approved-bank",
                BaseUrl = "https://bank.example.test/",
                ApiKey = "bank-secret-test"
            });
        var transactionId = Guid.NewGuid();

        var result = await provider.CreateInstructionAsync(
            transactionId,
            440_000,
            "THB",
            "KBANK",
            "ผู้ขาย ทดสอบ",
            "1234567890",
            default);

        Assert.Equal("approved-bank", result.Provider);
        Assert.Equal(
            $"toklong-payout-{transactionId:N}",
            handler.LastRequest!.Headers
                .GetValues("Idempotency-Key")
                .Single());
        using var body = JsonDocument.Parse(handler.LastBody);
        Assert.Equal(
            440_000,
            body.RootElement
                .GetProperty("amountSatang")
                .GetInt64());
        Assert.Equal(
            "1234567890",
            body.RootElement
                .GetProperty("beneficiary")
                .GetProperty("accountNumber")
                .GetString());
    }

    [Fact]
    public async Task Notification_idempotency_is_unique_per_outbox_message()
    {
        var handler = new StubHandler(
            """{"reference":"message-001"}""");
        var provider = new HttpNotificationProvider(
            new HttpClient(handler),
            new NotificationProviderOptions
            {
                Enabled = true,
                BaseUrl = "https://messages.example.test/",
                ApiKey = "message-secret-test"
            });
        var notificationId = Guid.NewGuid();

        await provider.SendAsync(
            notificationId,
            "+66812345678",
            "payment_confirmed",
            Guid.NewGuid(),
            "ผู้ซื้อจ่ายเงินแล้ว",
            "กล้อง Fujifilm · ส่งสินค้าได้แล้ว",
            "toklong://transaction/0123456789abcdef0123456789abcdef",
            default);

        Assert.Equal(
            $"toklong-notification-{notificationId:N}",
            handler.LastRequest!.Headers
                .GetValues("Idempotency-Key")
                .Single());
    }

    [Fact]
    public async Task Notification_device_registration_uses_authenticated_gateway_contract()
    {
        var handler = new StubHandler("{}");
        var provider = new HttpNotificationProvider(
            new HttpClient(handler),
            new NotificationProviderOptions
            {
                Enabled = true,
                BaseUrl = "https://messages.example.test/",
                ApiKey = "message-secret-test"
            });
        var installationId = Guid.NewGuid().ToString("N");

        await provider.RegisterAsync(
            "+66812345678",
            installationId,
            "ios",
            "opaque-apns-token-value",
            default);

        Assert.Equal(
            HttpMethod.Put,
            handler.LastRequest!.Method);
        Assert.Equal(
            $"/v1/devices/{installationId}",
            handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal(
            "message-secret-test",
            handler.LastRequest.Headers
                .GetValues("X-Api-Key")
                .Single());
        using var body = JsonDocument.Parse(handler.LastBody);
        Assert.Equal(
            "+66812345678",
            body.RootElement
                .GetProperty("recipient")
                .GetString());
        Assert.Equal(
            "ios",
            body.RootElement
                .GetProperty("platform")
                .GetString());
        Assert.Equal(
            "opaque-apns-token-value",
            body.RootElement
                .GetProperty("pushToken")
                .GetString());
    }

    private sealed class StubHandler(string response)
        : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string LastBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(
                    cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    response,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
