using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Toklong.Application.Abstractions;

namespace Toklong.Infrastructure.Services;

public sealed class NotificationProviderOptions
{
    public const string SectionName = "Notifications";

    public bool Enabled { get; init; }
    public string BaseUrl { get; init; } = "";
    public string ApiKey { get; init; } = "";

    public static NotificationProviderOptions From(
        IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        return new NotificationProviderOptions
        {
            Enabled = section.GetValue<bool>("Enabled"),
            BaseUrl = section["BaseUrl"] ?? "",
            ApiKey = section["ApiKey"] ?? ""
        };
    }

    public Uri GetValidatedBaseUri()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException(
                "Notifications:BaseUrl ต้องเป็น HTTPS URL");
        return uri;
    }
}

public sealed class DisabledNotificationProvider
    : INotificationProvider,
      IDeviceNotificationRegistrationProvider
{
    public Task<NotificationDeliveryResult> SendAsync(
        Guid notificationId,
        string recipient,
        string template,
        Guid transactionId,
        string title,
        string body,
        string deepLink,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            "ยังไม่ได้เปิดผู้ให้บริการแจ้งเตือน");

    public Task RegisterAsync(
        string recipientPhoneNumber,
        string installationId,
        string platform,
        string pushToken,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task UnregisterAsync(
        string recipientPhoneNumber,
        string installationId,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

public sealed class HttpNotificationProvider(
    HttpClient client,
    NotificationProviderOptions options) :
    INotificationProvider,
    IDeviceNotificationRegistrationProvider
{
    public async Task<NotificationDeliveryResult> SendAsync(
        Guid notificationId,
        string recipient,
        string template,
        Guid transactionId,
        string title,
        string body,
        string deepLink,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled ||
            string.IsNullOrWhiteSpace(options.ApiKey))
            throw new InvalidOperationException(
                "ยังไม่ได้เปิดผู้ให้บริการแจ้งเตือน");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(
                options.GetValidatedBaseUri(),
                "v1/messages"))
        {
            Content = JsonContent.Create(new
            {
                Recipient = recipient,
                Template = template,
                TransactionId = transactionId.ToString("N"),
                Title = title,
                Body = body,
                DeepLink = deepLink
            })
        };
        request.Headers.Add("X-Api-Key", options.ApiKey);
        request.Headers.Add(
            "Idempotency-Key",
            $"toklong-notification-{notificationId:N}");
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                "ผู้ให้บริการยังไม่รับข้อความ");
        var result = await response.Content
            .ReadFromJsonAsync<NotificationResponse>(
                cancellationToken: cancellationToken);
        if (result is null ||
            string.IsNullOrWhiteSpace(result.Reference) ||
            result.Reference.Length > 160)
            throw new InvalidOperationException(
                "ผู้ให้บริการส่งเลขอ้างอิงข้อความไม่ถูกต้อง");
        return new NotificationDeliveryResult(
            result.Reference.Trim());
    }

    public async Task RegisterAsync(
        string recipientPhoneNumber,
        string installationId,
        string platform,
        string pushToken,
        CancellationToken cancellationToken)
    {
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Put,
            $"v1/devices/{Uri.EscapeDataString(installationId)}",
            new
            {
                Recipient = recipientPhoneNumber,
                Platform = platform,
                PushToken = pushToken
            });
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                "ลงทะเบียนการแจ้งเตือนไม่สำเร็จ");
    }

    public async Task UnregisterAsync(
        string recipientPhoneNumber,
        string installationId,
        CancellationToken cancellationToken)
    {
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Delete,
            $"v1/devices/{Uri.EscapeDataString(installationId)}",
            new { Recipient = recipientPhoneNumber });
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode &&
            response.StatusCode !=
                System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException(
                "ยกเลิกการแจ้งเตือนไม่สำเร็จ");
    }

    private HttpRequestMessage CreateAuthenticatedRequest(
        HttpMethod method,
        string relativePath,
        object body)
    {
        if (!options.Enabled ||
            string.IsNullOrWhiteSpace(options.ApiKey))
            throw new InvalidOperationException(
                "ยังไม่ได้เปิดผู้ให้บริการแจ้งเตือน");
        var request = new HttpRequestMessage(
            method,
            new Uri(
                options.GetValidatedBaseUri(),
                relativePath))
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Api-Key", options.ApiKey);
        return request;
    }

    private sealed record NotificationResponse(string Reference);
}
