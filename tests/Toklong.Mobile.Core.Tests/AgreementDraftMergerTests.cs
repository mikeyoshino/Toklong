using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class AgreementDraftMergerTests
{
    [Fact]
    public void Merge_fills_only_blank_fields()
    {
        var current = new AgreementFormValues(
            "081-111-1111",
            "",
            "รายละเอียดที่ผู้ใช้กรอก",
            "",
            "",
            -1);
        var draft = new AgreementDraft(
            "0892222222",
            "กล้อง Fujifilm",
            "รายละเอียดจาก AI",
            "มีรอยด้านข้าง",
            4500m,
            AppCondition.UsedDefects,
            "high",
            ["ชื่อสินค้า", "ราคา", "ตำหนิ"]);

        var result = AgreementDraftMerger.MergeBlankFields(
            current,
            draft);

        Assert.Equal("081-111-1111", result.Values.SellerPhoneNumber);
        Assert.Equal("กล้อง Fujifilm", result.Values.ProductName);
        Assert.Equal(
            "รายละเอียดที่ผู้ใช้กรอก",
            result.Values.AgreementDetails);
        Assert.Equal("มีรอยด้านข้าง", result.Values.KnownDefects);
        Assert.Equal("4500", result.Values.AmountBaht);
        Assert.Equal(2, result.Values.SelectedConditionIndex);
        Assert.Equal(4, result.AppliedFieldCount);
    }

    [Fact]
    public void Merge_does_not_invent_missing_defect_or_condition()
    {
        var result = AgreementDraftMerger.MergeBlankFields(
            new AgreementFormValues("", "", "", "", "", -1),
            new AgreementDraft(
                "",
                "สินค้า",
                "",
                "",
                null,
                null,
                "low",
                ["ชื่อสินค้า"]));

        Assert.Equal("", result.Values.KnownDefects);
        Assert.Equal(-1, result.Values.SelectedConditionIndex);
        Assert.Equal(1, result.AppliedFieldCount);
    }
}
