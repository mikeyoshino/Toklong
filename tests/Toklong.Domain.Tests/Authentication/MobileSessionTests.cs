using Toklong.Domain.Authentication;
using Toklong.Domain.Common;

namespace Toklong.Domain.Tests.Authentication;

public sealed class MobileSessionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SellerCanBeAttachedOnlyWithTheVerifiedSessionPhone()
    {
        var session = MobileSession.Create(
            Guid.NewGuid(),
            null,
            "ผู้ซื้อ ทดสอบ",
            "+66812345678",
            new string('a', 64),
            Now,
            Now.AddDays(30));
        var sellerId = Guid.NewGuid();

        session.AttachSeller(
            sellerId,
            "+66812345678",
            "ผู้ขาย 5678",
            Now.AddMinutes(1));

        Assert.Equal(sellerId, session.SellerId);
        Assert.Throws<DomainException>(() =>
            session.AttachSeller(
                Guid.NewGuid(),
                "+66899999999",
                "ผู้ขายอื่น",
                Now.AddMinutes(2)));
    }
}
