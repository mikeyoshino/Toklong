using Toklong.Domain.Authentication;

namespace Toklong.Domain.Tests.Authentication;

public sealed class MobileAccountTermsAcceptanceTests
{
    private static readonly Guid BuyerId = Guid.NewGuid();
    private static readonly string InstallationId = Guid.NewGuid().ToString("N");
    private static readonly string IdempotencyKey = Guid.NewGuid().ToString("N");
    private static readonly DateTimeOffset Now =
        new(2026, 7, 28, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_records_exact_account_terms_evidence()
    {
        var acceptance = MobileAccountTermsAcceptance.Create(
            BuyerId,
            "terms-mvp-v1",
            InstallationId,
            IdempotencyKey,
            Now);

        Assert.Equal(BuyerId, acceptance.BuyerId);
        Assert.Equal("terms-mvp-v1", acceptance.TermsVersion);
        Assert.Equal(InstallationId, acceptance.InstallationId);
        Assert.Equal(IdempotencyKey, acceptance.IdempotencyKey);
        Assert.Equal(Now, acceptance.AcceptedAt);
    }
}
