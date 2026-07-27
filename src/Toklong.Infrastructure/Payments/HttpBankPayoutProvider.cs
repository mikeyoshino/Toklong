using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Toklong.Application.Abstractions;

namespace Toklong.Infrastructure.Payments;

public sealed class BankPayoutOptions
{
    public const string SectionName = "BankPayout";

    public string Provider { get; init; } = "Manual";
    public string BaseUrl { get; init; } = "";
    public string ApiKey { get; init; } = "";

    public static BankPayoutOptions From(
        IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        return new BankPayoutOptions
        {
            Provider = section["Provider"] ?? "Manual",
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
                "BankPayout:BaseUrl ต้องเป็น HTTPS URL ที่ไม่มีข้อมูลล็อกอินใน URL");
        return uri;
    }
}

public sealed class HttpBankPayoutProvider(
    HttpClient client,
    BankPayoutOptions options) : IPayoutProvider
{
    public async Task<PayoutInstructionPreparation>
        CreateInstructionAsync(
            Guid transactionId,
            long amountSatang,
            string currency,
            string bankCode,
            string accountName,
            string accountNumber,
            CancellationToken cancellationToken)
    {
        if (amountSatang <= 0 ||
            string.IsNullOrWhiteSpace(options.ApiKey))
            throw new InvalidOperationException(
                "ยังไม่ได้ตั้งค่าการโอนเงินให้ผู้ขาย");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(
                options.GetValidatedBaseUri(),
                "v1/payouts"))
        {
            Content = JsonContent.Create(new
            {
                ExternalId = transactionId.ToString("N"),
                AmountSatang = amountSatang,
                Currency = currency,
                Beneficiary = new
                {
                    BankCode = bankCode,
                    AccountName = accountName,
                    AccountNumber = accountNumber
                }
            })
        };
        request.Headers.Add("X-Api-Key", options.ApiKey);
        request.Headers.Add(
            "Idempotency-Key",
            $"toklong-payout-{transactionId:N}");
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                "ธนาคารยังไม่รับคำขอโอนเงิน กรุณาตรวจสอบและลองใหม่");
        var result = await response.Content
            .ReadFromJsonAsync<BankPayoutResponse>(
                cancellationToken: cancellationToken);
        if (result is null ||
            string.IsNullOrWhiteSpace(result.Reference) ||
            result.Reference.Length > 160 ||
            !string.Equals(
                result.Status,
                "accepted",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "ธนาคารส่งผลการสร้างคำขอโอนไม่ถูกต้อง");
        return new PayoutInstructionPreparation(
            options.Provider.Trim(),
            result.Reference.Trim(),
            result.Status);
    }

    private sealed record BankPayoutResponse(
        string Reference,
        string Status);
}
