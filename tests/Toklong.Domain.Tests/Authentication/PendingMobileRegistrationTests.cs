using Toklong.Domain.Authentication;
using Toklong.Domain.Common;

namespace Toklong.Domain.Tests.Authentication;

public sealed class PendingMobileRegistrationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 28, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid BuyerId = Guid.NewGuid();

    [Fact]
    public void Create_requires_sha256_hash_and_future_expiry()
    {
        Assert.Throws<DomainException>(() =>
            PendingMobileRegistration.Create(
                "raw-ticket",
                "+66812345678",
                Guid.NewGuid().ToString("N"),
                Now,
                Now.AddMinutes(15)));

        Assert.Throws<DomainException>(() =>
            PendingMobileRegistration.Create(
                new string('a', 64),
                "+66812345678",
                Guid.NewGuid().ToString("N"),
                Now,
                Now));
    }

    [Fact]
    public void Complete_is_one_time_but_exact_retry_is_recognized()
    {
        var idempotencyKey = Guid.NewGuid().ToString("N");
        var pending = NewPending();

        pending.Complete(BuyerId, idempotencyKey, Now.AddMinutes(1));

        Assert.Equal(
            RegistrationCompletionStatus.ExactReplay,
            pending.ValidateCompletion(
                pending.InstallationId,
                idempotencyKey,
                Now.AddMinutes(2)));
        Assert.Throws<DomainException>(() =>
            pending.ValidateCompletion(
                pending.InstallationId,
                Guid.NewGuid().ToString("N"),
                Now.AddMinutes(2)));
    }

    [Fact]
    public void ValidateCompletion_rejects_expiry_and_installation_mismatch()
    {
        var pending = NewPending();

        Assert.Throws<DomainException>(() =>
            pending.ValidateCompletion(
                Guid.NewGuid().ToString("N"),
                Guid.NewGuid().ToString("N"),
                Now.AddMinutes(1)));
        Assert.Throws<DomainException>(() =>
            pending.ValidateCompletion(
                pending.InstallationId,
                Guid.NewGuid().ToString("N"),
                Now.AddMinutes(16)));
    }

    private static PendingMobileRegistration NewPending() =>
        PendingMobileRegistration.Create(
            new string('a', 64),
            "+66812345678",
            Guid.NewGuid().ToString("N"),
            Now,
            Now.AddMinutes(15));
}
