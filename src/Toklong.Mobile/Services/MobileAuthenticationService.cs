using System.Net.Http.Json;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Services;

public sealed class MobileAuthenticationService(
    MobileApiClient api,
    IMobileSessionStore sessionStore,
    IPushRegistrationService pushRegistration)
    : IAuthenticationService
{
    public async Task<bool> HasSessionAsync() =>
        await sessionStore.GetAsync() is not null;

    public async Task<OtpChallengeResult> RequestCodeAsync(
        string phoneNumber,
        AuthenticationMode mode,
        string? fullName,
        string? email,
        CancellationToken cancellationToken = default)
    {
        var normalizedPhone = ThaiMobilePhoneInput.Sanitize(phoneNumber);
        if (!ThaiMobilePhoneInput.IsValid(normalizedPhone))
            throw new ArgumentException(
                "กรอกเบอร์มือถือไทย 10 หลัก เช่น 081-234-5678");

        using var response = await api.CreateClient().PostAsJsonAsync(
            "api/mobile/auth/otp/request",
            new
            {
                PhoneNumber = normalizedPhone,
                Mode = mode.ToString(),
                FullName = mode == AuthenticationMode.SignUp
                    ? fullName?.Trim()
                    : null,
                Email = mode == AuthenticationMode.SignUp
                    ? email?.Trim()
                    : null
            },
            cancellationToken);
        await MobileApiClient.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<OtpChallengeResult>(
                   cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException(
                   "ไม่พบข้อมูลรหัสยืนยัน");
    }

    public async Task VerifyCodeAsync(
        string challengeId,
        string code,
        AuthenticationMode mode,
        string? fullName,
        string? email,
        CancellationToken cancellationToken = default)
    {
        using var response = await api.CreateClient().PostAsJsonAsync(
            "api/mobile/auth/otp/verify",
            new
            {
                ChallengeId = challengeId,
                Code = code.Trim(),
                Mode = mode.ToString(),
                FullName = mode == AuthenticationMode.SignUp
                    ? fullName?.Trim()
                    : null,
                Email = mode == AuthenticationMode.SignUp
                    ? email?.Trim()
                    : null
            },
            cancellationToken);
        await MobileApiClient.EnsureSuccessAsync(response, cancellationToken);
        var issued = await response.Content
            .ReadFromJsonAsync<MobileApiClient.SessionResponse>(
                cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException(
                "เข้าสู่ระบบไม่สำเร็จ");
        await sessionStore.SaveAsync(new StoredMobileSession(
            issued.AccessToken,
            issued.RefreshToken,
            issued.AccessTokenExpiresAt));
    }

    public async Task<MobileProfile> GetProfileAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await api.SendAuthenticatedAsync(
            () => new HttpRequestMessage(HttpMethod.Get, "api/mobile/me"),
            cancellationToken);
        await MobileApiClient.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<MobileProfile>(
                   cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException("ไม่พบข้อมูลบัญชี");
    }

    public async Task<string> UpdateEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        using var response = await api.SendAuthenticatedAsync(
            () => new HttpRequestMessage(
                HttpMethod.Put,
                "api/mobile/me/email")
            {
                Content = JsonContent.Create(new { Email = email.Trim() })
            },
            cancellationToken);
        await MobileApiClient.EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content
            .ReadFromJsonAsync<EmailUpdateResponse>(
                cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("บันทึกอีเมลไม่สำเร็จ");
        return result.Email;
    }

    public async Task SignOutAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            try
            {
                await pushRegistration.UnregisterAsync(
                    cancellationToken);
            }
            catch
            {
                // Session revocation must still complete if a push gateway is
                // temporarily unavailable.
            }
            using var response = await api.SendAuthenticatedAsync(
                () => new HttpRequestMessage(
                    HttpMethod.Post,
                    "api/mobile/auth/logout"),
                cancellationToken);
        }
        finally
        {
            sessionStore.Clear();
        }
    }

    private sealed record EmailUpdateResponse(string Email);
}
