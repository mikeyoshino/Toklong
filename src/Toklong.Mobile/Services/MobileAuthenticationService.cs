using System.Net.Http.Json;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Services;

public sealed class MobileAuthenticationService(
    MobileApiClient api,
    IMobileSessionStore sessionStore,
    IPendingRegistrationStore pendingRegistrations,
    IInstallationIdProvider installationIds,
    IPushRegistrationService pushRegistration)
    : IAuthenticationService
{
    public async Task<bool> HasSessionAsync() =>
        await sessionStore.GetAsync() is not null;

    public async Task<OtpChallengeResult> RequestCodeAsync(
        string phoneNumber,
        AuthenticationMode mode,
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
                Mode = mode.ToString()
            },
            cancellationToken);
        await MobileApiClient.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<OtpChallengeResult>(
                   cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException(
                   "ไม่พบข้อมูลรหัสยืนยัน");
    }

    public async Task<AuthenticationVerificationResult> VerifyCodeAsync(
        string challengeId,
        string code,
        AuthenticationMode mode,
        CancellationToken cancellationToken = default)
    {
        using var response = await api.CreateClient().PostAsJsonAsync(
            "api/mobile/auth/otp/verify",
            new
            {
                ChallengeId = challengeId,
                Code = code.Trim(),
                Mode = mode.ToString(),
                InstallationId =
                    installationIds.GetInstallationId()
            },
            cancellationToken);
        await MobileApiClient.EnsureSuccessAsync(response, cancellationToken);
        var verification = await response.Content
            .ReadFromJsonAsync<VerificationResponse>(
                cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException(
                "เข้าสู่ระบบไม่สำเร็จ");
        if (string.Equals(
                verification.Outcome,
                "session",
                StringComparison.Ordinal) &&
            verification.Session is not null)
        {
            await SaveSessionAsync(verification.Session);
            pendingRegistrations.Clear();
            return new SessionVerificationResult();
        }

        if (string.Equals(
                verification.Outcome,
                "registration_required",
                StringComparison.Ordinal) &&
            verification.Registration is not null)
        {
            var pending = new PendingMobileRegistration(
                verification.Registration.RegistrationTicket,
                verification.Registration.ExpiresAt,
                verification.Registration.MaskedPhoneNumber,
                installationIds.GetInstallationId(),
                Guid.NewGuid().ToString("N"));
            await pendingRegistrations.SaveAsync(pending);
            return new RegistrationRequiredVerificationResult(
                pending);
        }

        throw new InvalidOperationException(
            "ผลการยืนยันเบอร์ไม่ถูกต้อง");
    }

    public async Task CompleteRegistrationAsync(
        string fullName,
        string email,
        string termsVersion,
        CancellationToken cancellationToken = default)
    {
        var pending = await pendingRegistrations.GetValidAsync(
                DateTimeOffset.UtcNow)
            ?? throw new InvalidOperationException(
                "การยืนยันเบอร์หมดอายุ กรุณายืนยันเบอร์ใหม่");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "api/mobile/auth/registration/complete")
        {
            Content = JsonContent.Create(new
            {
                pending.RegistrationTicket,
                FullName = fullName.Trim(),
                Email = email.Trim(),
                TermsVersion = termsVersion,
                pending.InstallationId
            })
        };
        request.Headers.Add(
            "Idempotency-Key",
            pending.CompletionIdempotencyKey);
        using var response = await api.CreateClient().SendAsync(
            request,
            cancellationToken);
        await MobileApiClient.EnsureSuccessAsync(
            response,
            cancellationToken);
        var issued = await response.Content
            .ReadFromJsonAsync<MobileApiClient.SessionResponse>(
                cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException(
                "สร้างบัญชีไม่สำเร็จ");
        await SaveSessionAsync(issued);
        pendingRegistrations.Clear();
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

    public async Task<PendingEmailChange?> GetPendingEmailChangeAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await api.SendAuthenticatedAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                "api/mobile/me/email-change"),
            cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return null;
        await MobileApiClient.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<PendingEmailChange>(
                   cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException(
                   "ไม่พบข้อมูลการเปลี่ยนอีเมล");
    }

    public Task<PendingEmailChange> RequestEmailChangeAsync(
        string email,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        SendEmailChangeAsync(
            HttpMethod.Post,
            "api/mobile/me/email-change",
            new
            {
                Email = email.Trim(),
                IdempotencyKey = idempotencyKey
            },
            cancellationToken);

    public Task<PendingEmailChange> ResendEmailChangeAsync(
        Guid challengeId,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        SendEmailChangeAsync(
            HttpMethod.Post,
            $"api/mobile/me/email-change/{challengeId}/resend",
            new { IdempotencyKey = idempotencyKey },
            cancellationToken);

    public async Task<string> VerifyEmailChangeAsync(
        Guid challengeId,
        string code,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        using var response = await api.SendAuthenticatedAsync(
            () => new HttpRequestMessage(
                HttpMethod.Post,
                $"api/mobile/me/email-change/{challengeId}/verify")
            {
                Content = JsonContent.Create(new
                {
                    Code = code.Trim(),
                    IdempotencyKey = idempotencyKey
                })
            },
            cancellationToken);
        await MobileApiClient.EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content
                .ReadFromJsonAsync<VerifiedEmailChangeResponse>(
                    cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException(
                    "ไม่พบข้อมูลอีเมลที่ยืนยันแล้ว"))
            .Email;
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
            pendingRegistrations.Clear();
        }
    }

    private Task SaveSessionAsync(
        MobileApiClient.SessionResponse issued) =>
        sessionStore.SaveAsync(new StoredMobileSession(
            issued.AccessToken,
            issued.RefreshToken,
            issued.AccessTokenExpiresAt));

    private async Task<PendingEmailChange> SendEmailChangeAsync(
        HttpMethod method,
        string route,
        object request,
        CancellationToken cancellationToken)
    {
        using var response = await api.SendAuthenticatedAsync(
            () => new HttpRequestMessage(method, route)
            {
                Content = JsonContent.Create(request)
            },
            cancellationToken);
        await MobileApiClient.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<PendingEmailChange>(
                   cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException(
                   "ไม่พบข้อมูลการเปลี่ยนอีเมล");
    }

    private sealed record VerificationResponse(
        string Outcome,
        MobileApiClient.SessionResponse? Session,
        RegistrationResponse? Registration);

    private sealed record RegistrationResponse(
        string RegistrationTicket,
        DateTimeOffset ExpiresAt,
        string MaskedPhoneNumber);

    private sealed record VerifiedEmailChangeResponse(
        string Email,
        DateTimeOffset CompletedAt);
}
