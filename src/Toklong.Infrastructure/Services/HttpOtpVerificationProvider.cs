using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;

namespace Toklong.Infrastructure.Services;

public sealed class OtpProviderOptions
{
    public const string SectionName = "Otp";

    public string Provider { get; init; } = "Development";
    public string BaseUrl { get; init; } = "";
    public string ApiKey { get; init; } = "";
    public string ApiSecret { get; init; } = "";

    public static OtpProviderOptions From(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        return new OtpProviderOptions
        {
            Provider = section["Provider"] ?? "Development",
            BaseUrl = section["BaseUrl"] ?? "",
            ApiKey = section["ApiKey"] ?? "",
            ApiSecret = section["ApiSecret"] ?? ""
        };
    }

    public Uri GetValidatedBaseUri()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException(
                "Otp:BaseUrl ต้องเป็น HTTPS URL ที่ไม่มีข้อมูลล็อกอินใน URL");
        return uri;
    }
}

public sealed class ThaiBulkSmsOtpVerificationProvider(
    HttpClient httpClient,
    OtpProviderOptions options) : IOtpVerificationProvider
{
    public async Task<OtpChallenge> RequestAsync(
        string phoneNumber,
        OtpPurpose purpose,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var normalized = ThaiMobilePhone.Normalize(phoneNumber);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(options.GetValidatedBaseUri(), "v2/otp/request"))
        {
            Content = new FormUrlEncodedContent(
            [
                new("key", options.ApiKey),
                new("secret", options.ApiSecret),
                new("msisdn", normalized)
            ])
        };
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if ((int)response.StatusCode == 429)
            throw new RequestCooldownException(
                "ขอรหัสถี่เกินไป กรุณารอสักครู่แล้วลองใหม่",
                ReadRetryAfter(response));
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                "ผู้ให้บริการรหัสยืนยันไม่พร้อมใช้งาน");
        var result = await response.Content
            .ReadFromJsonAsync<ThaiBulkSmsRequestResult>(
                cancellationToken: cancellationToken);
        if (result is null ||
            !string.Equals(
                result.Status,
                "success",
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(result.Token) ||
            result.Token.Length > 300 ||
            string.IsNullOrWhiteSpace(result.ReferenceNumber))
            throw new InvalidOperationException(
                "ผู้ให้บริการรหัสยืนยันส่งข้อมูลไม่ครบ");

        return new OtpChallenge(
            ProtectChallenge(
                normalized,
                result.Token.Trim(),
                purpose),
            Mask(normalized),
            null);
    }

    public async Task<string?> VerifyAsync(
        string challengeId,
        string code,
        OtpPurpose purpose,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (code.Length != 6 ||
            code.Any(character => !char.IsAsciiDigit(character)) ||
            !TryUnprotectChallenge(
                challengeId,
                purpose,
                out var phoneNumber,
                out var providerToken))
            return null;
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(options.GetValidatedBaseUri(), "v2/otp/verify"))
        {
            Content = new FormUrlEncodedContent(
            [
                new("key", options.ApiKey),
                new("secret", options.ApiSecret),
                new("token", providerToken),
                new("pin", code)
            ])
        };
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode is
            System.Net.HttpStatusCode.BadRequest or
            System.Net.HttpStatusCode.NotFound or
            System.Net.HttpStatusCode.Gone or
            System.Net.HttpStatusCode.UnprocessableEntity)
            return null;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                "ผู้ให้บริการรหัสยืนยันไม่พร้อมใช้งาน");
        var result = await response.Content
            .ReadFromJsonAsync<ThaiBulkSmsVerifyResult>(
                cancellationToken: cancellationToken);
        return string.Equals(
                result?.Status,
                "success",
                StringComparison.OrdinalIgnoreCase)
            ? phoneNumber
            : null;
    }

    private void EnsureConfigured()
    {
        if (options.ApiKey.Length < 8 ||
            options.ApiSecret.Length < 16)
            throw new InvalidOperationException(
                "ยังไม่ได้ตั้งค่า Key และ Secret ของ ThaibulkSMS");
    }

    private string ProtectChallenge(
        string normalizedPhone,
        string providerToken,
        OtpPurpose purpose)
    {
        var payload = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(
                    $"{purpose}\n{normalizedPhone}\n{providerToken}"))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var signature = Convert.ToHexString(
                HMACSHA256.HashData(
                    Encoding.UTF8.GetBytes(options.ApiSecret),
                    Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
        return $"{payload}.{signature}";
    }

    private bool TryUnprotectChallenge(
        string challengeId,
        OtpPurpose expectedPurpose,
        out string phoneNumber,
        out string providerToken)
    {
        phoneNumber = "";
        providerToken = "";
        if (string.IsNullOrWhiteSpace(challengeId) ||
            challengeId.Length > 800)
            return false;
        var separator = challengeId.LastIndexOf('.');
        if (separator <= 0 ||
            separator == challengeId.Length - 1)
            return false;
        var payload = challengeId[..separator];
        var suppliedSignature = challengeId[(separator + 1)..];
        var expectedSignature = Convert.ToHexString(
                HMACSHA256.HashData(
                    Encoding.UTF8.GetBytes(options.ApiSecret),
                    Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
        if (suppliedSignature.Length != expectedSignature.Length ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(suppliedSignature),
                Encoding.ASCII.GetBytes(expectedSignature)))
            return false;
        try
        {
            var base64 = payload
                .Replace('-', '+')
                .Replace('_', '/');
            base64 = base64.PadRight(
                base64.Length + ((4 - base64.Length % 4) % 4),
                '=');
            var decoded = Encoding.UTF8.GetString(
                Convert.FromBase64String(base64));
            var purposeSeparator = decoded.IndexOf('\n');
            var phoneSeparator = purposeSeparator < 0
                ? -1
                : decoded.IndexOf('\n', purposeSeparator + 1);
            if (purposeSeparator <= 0 ||
                phoneSeparator <= purposeSeparator + 1 ||
                phoneSeparator == decoded.Length - 1 ||
                !Enum.TryParse<OtpPurpose>(
                    decoded[..purposeSeparator],
                    ignoreCase: false,
                    out var protectedPurpose) ||
                protectedPurpose != expectedPurpose)
                return false;
            phoneNumber = ThaiMobilePhone.Normalize(
                decoded[(purposeSeparator + 1)..phoneSeparator]);
            providerToken = decoded[(phoneSeparator + 1)..];
            return providerToken.Length <= 300;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string Mask(string normalizedPhone) =>
        $"0{normalizedPhone[3..5]}-***-{normalizedPhone[^4..]}";

    private static TimeSpan ReadRetryAfter(
        HttpResponseMessage response)
    {
        var seconds = response.Headers.RetryAfter?.Delta ??
                      (response.Headers.RetryAfter?.Date -
                       DateTimeOffset.UtcNow);
        return seconds is { } value && value > TimeSpan.Zero
            ? value
            : TimeSpan.FromSeconds(60);
    }

    private sealed record ThaiBulkSmsRequestResult(
        string Status,
        string Token,
        [property: JsonPropertyName("refno")]
        string ReferenceNumber);

    private sealed record ThaiBulkSmsVerifyResult(
        string Status,
        string? Message);
}

public sealed class HttpOtpVerificationProvider(
    HttpClient httpClient,
    OtpProviderOptions options) : IOtpVerificationProvider
{
    public async Task<OtpChallenge> RequestAsync(
        string phoneNumber,
        OtpPurpose purpose,
        CancellationToken cancellationToken)
    {
        var normalized = ThaiMobilePhone.Normalize(phoneNumber);
        using var request = CreateRequest(
            HttpMethod.Post,
            "v1/otp/challenges",
            new OtpRequest(normalized, purpose.ToString()));
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if ((int)response.StatusCode == 429)
            throw new RequestCooldownException(
                "ขอรหัสถี่เกินไป กรุณารอสักครู่แล้วลองใหม่",
                ReadRetryAfter(response));
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                "ผู้ให้บริการรหัสยืนยันไม่พร้อมใช้งาน");
        var result = await response.Content
            .ReadFromJsonAsync<OtpRequestResult>(
                cancellationToken: cancellationToken);
        if (result is null ||
            !ValidOpaqueId(result.ChallengeId) ||
            string.IsNullOrWhiteSpace(result.MaskedPhoneNumber))
            throw new InvalidOperationException(
                "ผู้ให้บริการรหัสยืนยันส่งข้อมูลไม่ครบ");
        return new OtpChallenge(
            result.ChallengeId,
            result.MaskedPhoneNumber.Trim(),
            null);
    }

    public async Task<string?> VerifyAsync(
        string challengeId,
        string code,
        OtpPurpose purpose,
        CancellationToken cancellationToken)
    {
        if (!ValidOpaqueId(challengeId) ||
            code.Length != 6 ||
            code.Any(character => !char.IsAsciiDigit(character)))
            return null;
        using var request = CreateRequest(
            HttpMethod.Post,
            "v1/otp/verifications",
            new OtpVerification(
                challengeId,
                code,
                purpose.ToString()));
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode is
            System.Net.HttpStatusCode.BadRequest or
            System.Net.HttpStatusCode.NotFound or
            System.Net.HttpStatusCode.Gone or
            System.Net.HttpStatusCode.UnprocessableEntity)
            return null;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                "ผู้ให้บริการรหัสยืนยันไม่พร้อมใช้งาน");
        var result = await response.Content
            .ReadFromJsonAsync<OtpVerificationResult>(
                cancellationToken: cancellationToken);
        if (result?.Verified != true ||
            string.IsNullOrWhiteSpace(result.PhoneNumber))
            return null;
        return ThaiMobilePhone.Normalize(result.PhoneNumber);
    }

    private HttpRequestMessage CreateRequest<T>(
        HttpMethod method,
        string relativePath,
        T body)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new InvalidOperationException(
                "ยังไม่ได้ตั้งค่าคีย์ผู้ให้บริการรหัสยืนยัน");
        var request = new HttpRequestMessage(
            method,
            new Uri(options.GetValidatedBaseUri(), relativePath))
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Api-Key", options.ApiKey);
        request.Headers.Add(
            "Idempotency-Key",
            Convert.ToHexString(
                    RandomNumberGenerator.GetBytes(16))
                .ToLowerInvariant());
        return request;
    }

    private static bool ValidOpaqueId(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length is >= 16 and <= 200 &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) ||
            character is '-' or '_');

    private static TimeSpan ReadRetryAfter(HttpResponseMessage response)
    {
        var seconds = response.Headers.RetryAfter?.Delta ??
                      (response.Headers.RetryAfter?.Date -
                       DateTimeOffset.UtcNow);
        return seconds is { } value && value > TimeSpan.Zero
            ? value
            : TimeSpan.FromSeconds(60);
    }

    private sealed record OtpRequest(
        string PhoneNumber,
        string Purpose);
    private sealed record OtpRequestResult(
        string ChallengeId,
        string MaskedPhoneNumber);
    private sealed record OtpVerification(
        string ChallengeId,
        string Code,
        string Purpose);
    private sealed record OtpVerificationResult(
        bool Verified,
        string? PhoneNumber);
}
