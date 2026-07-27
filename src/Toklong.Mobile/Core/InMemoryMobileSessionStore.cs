namespace Toklong.Mobile.Core;

/// <summary>
/// Keeps development-simulator credentials out of persistent storage when the
/// simulator cannot use Keychain without a valid Apple signing identity.
/// Closing the app process signs the developer out.
/// </summary>
public sealed class InMemoryMobileSessionStore : IMobileSessionStore
{
    private StoredMobileSession? session;

    public Task<StoredMobileSession?> GetAsync() =>
        Task.FromResult(session);

    public Task SaveAsync(StoredMobileSession value)
    {
        session = value;
        return Task.CompletedTask;
    }

    public void Clear() => session = null;
}
