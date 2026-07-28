using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class SpotlightGradientPresentationTests
{
    [Fact]
    public void Colors_remain_valid_when_spotlight_is_removed()
    {
        var spotlight = new AppTransaction(
            Guid.Parse("00000000-0000-0000-0000-000000000091"),
            "สินค้าทดสอบ",
            1_000_00,
            "THB",
            AppTransactionRole.Seller,
            AppFulfillmentType.Digital,
            "PaidAwaitingDigitalDelivery",
            DateTimeOffset.Parse("2026-07-28T18:00:00+07:00"),
            DateTimeOffset.Parse("2026-07-29T18:00:00+07:00"),
            "ผู้ซื้อ");
        var presentation =
            new SpotlightGradientPresentation(spotlight);

        presentation.SetSpotlight(null);

        Assert.Equal(spotlight.RoleHeaderStart, presentation.Start);
        Assert.Equal(spotlight.RoleHeaderMiddle, presentation.Middle);
        Assert.Equal(spotlight.RoleHeaderEnd, presentation.End);
        Assert.All(
            [presentation.Start, presentation.Middle, presentation.End],
            color => Assert.Matches("^#[0-9A-F]{6}$", color));
    }
}
