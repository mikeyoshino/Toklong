using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class InMemoryMobileSessionStoreTests
{
    [Fact]
    public async Task Session_is_available_until_cleared()
    {
        var store = new InMemoryMobileSessionStore();
        var session = new StoredMobileSession(
            "access",
            "refresh",
            DateTimeOffset.UtcNow.AddMinutes(10));

        await store.SaveAsync(session);

        Assert.Equal(session, await store.GetAsync());
        store.Clear();
        Assert.Null(await store.GetAsync());
    }
}
