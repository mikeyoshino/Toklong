using Microsoft.Maui.Storage;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Services;

public sealed class SecureMobileSessionStore : IMobileSessionStore
{
    private const string AccessTokenKey = "toklong.access_token";
    private const string RefreshTokenKey = "toklong.refresh_token";
    private const string AccessExpiryKey = "toklong.access_expiry";

    public async Task<StoredMobileSession?> GetAsync()
    {
        var access = await SecureStorage.Default.GetAsync(AccessTokenKey);
        var refresh = await SecureStorage.Default.GetAsync(RefreshTokenKey);
        var rawExpiry = await SecureStorage.Default.GetAsync(AccessExpiryKey);
        if (string.IsNullOrWhiteSpace(access) ||
            string.IsNullOrWhiteSpace(refresh) ||
            !DateTimeOffset.TryParse(rawExpiry, out var expiry))
            return null;
        return new StoredMobileSession(access, refresh, expiry);
    }

    public async Task SaveAsync(StoredMobileSession session)
    {
        await SecureStorage.Default.SetAsync(
            AccessTokenKey,
            session.AccessToken);
        await SecureStorage.Default.SetAsync(
            RefreshTokenKey,
            session.RefreshToken);
        await SecureStorage.Default.SetAsync(
            AccessExpiryKey,
            session.AccessTokenExpiresAt.ToString("O"));
    }

    public void Clear()
    {
        SecureStorage.Default.Remove(AccessTokenKey);
        SecureStorage.Default.Remove(RefreshTokenKey);
        SecureStorage.Default.Remove(AccessExpiryKey);
    }
}
