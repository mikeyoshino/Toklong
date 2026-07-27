using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class QuickDealSnapshotComposerTests
{
    [Fact]
    public void Optional_description_falls_back_to_product_name()
    {
        var result = QuickDealSnapshotComposer.Compose(
            "  กล้อง Fujifilm X-T30 II  ",
            " ",
            AppCondition.UsedGood,
            null);

        Assert.Equal(
            "กล้อง Fujifilm X-T30 II",
            result.Description);
        Assert.Equal(
            QuickDealSnapshotComposer.NoBuyerReportedDefects,
            result.KnownDefects);
    }

    [Fact]
    public void Defect_condition_preserves_explicit_report()
    {
        var result = QuickDealSnapshotComposer.Compose(
            "กล้อง",
            " พร้อมเลนส์และแบตเตอรี่ ",
            AppCondition.UsedDefects,
            " มีรอยมุมขวาตามรูป ");

        Assert.Equal(
            "พร้อมเลนส์และแบตเตอรี่",
            result.Description);
        Assert.Equal(
            "มีรอยมุมขวาตามรูป",
            result.KnownDefects);
    }
}
