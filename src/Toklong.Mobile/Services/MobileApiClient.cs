using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Services;

public sealed record MobileApiOptions(Uri BaseUri);

public sealed class MobileApiClient(
    IHttpClientFactory httpClientFactory,
    IMobileSessionStore sessionStore)
{
    private readonly SemaphoreSlim refreshLock = new(1, 1);

    public HttpClient CreateClient() =>
        httpClientFactory.CreateClient("ToklongApi");

    public async Task<HttpResponseMessage> SendAuthenticatedAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken = default)
    {
        var session = await EnsureFreshSessionAsync(cancellationToken)
            ?? throw new UnauthorizedAccessException(
                "เซสชันหมดอายุ กรุณาเข้าสู่ระบบอีกครั้ง");
        using var firstRequest = requestFactory();
        firstRequest.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);
        var response = await CreateClient().SendAsync(
            firstRequest,
            cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        response.Dispose();
        session = await RefreshAsync(cancellationToken, force: true)
            ?? throw new UnauthorizedAccessException(
                "เซสชันหมดอายุ กรุณาเข้าสู่ระบบอีกครั้ง");
        using var retryRequest = requestFactory();
        retryRequest.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return await CreateClient().SendAsync(
            retryRequest,
            cancellationToken);
    }

    public static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;
        string? message = null;
        ApiProblem? problemMetadata = null;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ApiProblem>(
                cancellationToken: cancellationToken);
            message = problem?.Detail ?? problem?.Title;
            problemMetadata = problem;
        }
        catch (JsonException)
        {
        }
        var retryAfter =
            response.Headers.RetryAfter?.Delta ??
            (response.Headers.RetryAfter?.Date is { } retryAt
                ? TimeSpan.FromSeconds(Math.Max(
                    0,
                    (retryAt - DateTimeOffset.UtcNow)
                    .TotalSeconds))
                : problemMetadata?.RetryAfterSeconds is { } seconds
                    ? TimeSpan.FromSeconds(Math.Max(1, seconds))
                    : null);
        throw new MobileApiRequestException(
            response.StatusCode,
            string.IsNullOrWhiteSpace(message)
                ? "เชื่อมต่อ TOKLONG ไม่สำเร็จ กรุณาลองอีกครั้ง"
                : message,
            retryAfter,
            problemMetadata?.Code,
            problemMetadata?.Field,
            problemMetadata?.RemainingAttempts,
            problemMetadata?.NextAllowedAt);
    }

    private async Task<StoredMobileSession?> EnsureFreshSessionAsync(
        CancellationToken cancellationToken)
    {
        var session = await sessionStore.GetAsync();
        if (session is null)
            return null;
        return session.AccessTokenExpiresAt >
               DateTimeOffset.UtcNow.AddMinutes(1)
            ? session
            : await RefreshAsync(cancellationToken);
    }

    private async Task<StoredMobileSession?> RefreshAsync(
        CancellationToken cancellationToken,
        bool force = false)
    {
        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            var current = await sessionStore.GetAsync();
            if (current is null)
                return null;
            if (!force &&
                current.AccessTokenExpiresAt >
                DateTimeOffset.UtcNow.AddMinutes(1))
                return current;

            using var response = await CreateClient().PostAsJsonAsync(
                "api/mobile/auth/refresh",
                new { current.RefreshToken },
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                sessionStore.Clear();
                return null;
            }
            var issued = await response.Content
                .ReadFromJsonAsync<SessionResponse>(
                    cancellationToken: cancellationToken);
            if (issued is null)
                return null;
            var replacement = new StoredMobileSession(
                issued.AccessToken,
                issued.RefreshToken,
                issued.AccessTokenExpiresAt);
            await sessionStore.SaveAsync(replacement);
            return replacement;
        }
        finally
        {
            refreshLock.Release();
        }
    }

    internal sealed record SessionResponse(
        string AccessToken,
        string RefreshToken,
        DateTimeOffset AccessTokenExpiresAt,
        string DisplayName,
        string PhoneNumber,
        bool CanBuy,
        bool CanSell);

    private sealed record ApiProblem(
        string? Title,
        string? Detail,
        string? Code,
        string? Field,
        int? RemainingAttempts,
        int? RetryAfterSeconds,
        DateTimeOffset? NextAllowedAt);
}
