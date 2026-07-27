using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class PendingRegistrationStoreTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 28, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetValidAsync_clears_expired_registration()
    {
        var store = new InMemoryPendingRegistrationStore();
        await store.SaveAsync(Pending(
            expiresAt: Now.AddMinutes(-1)));

        Assert.Null(await store.GetValidAsync(Now));
        Assert.Null(await store.GetValidAsync(Now));
    }

    [Fact]
    public async Task Completion_idempotency_key_survives_resume()
    {
        var store = new InMemoryPendingRegistrationStore();
        var pending = Pending(
            completionIdempotencyKey:
                Guid.NewGuid().ToString("N"));
        await store.SaveAsync(pending);

        Assert.Equal(
            pending.CompletionIdempotencyKey,
            (await store.GetValidAsync(Now))!
                .CompletionIdempotencyKey);
    }

    [Fact]
    public async Task Invalid_pending_registration_is_not_saved()
    {
        var store = new InMemoryPendingRegistrationStore();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.SaveAsync(Pending(
                installationId: "not-a-guid")));

        Assert.Null(await store.GetValidAsync(Now));
    }

    private static PendingMobileRegistration Pending(
        DateTimeOffset? expiresAt = null,
        string? installationId = null,
        string? completionIdempotencyKey = null) =>
        new(
            "opaque-registration-ticket",
            expiresAt ?? Now.AddMinutes(15),
            "081-***-5678",
            installationId ?? Guid.NewGuid().ToString("N"),
            completionIdempotencyKey ??
            Guid.NewGuid().ToString("N"));
}
