using Toklong.Domain.Transactions;

namespace Toklong.Crm.Disputes;

public sealed record CrmEvidenceChecklist(
    IReadOnlyList<string> SystemEvidence,
    IReadOnlyList<string> BuyerEvidence,
    IReadOnlyList<string> SellerEvidence,
    IReadOnlyList<string> CategoryEvidence);

public static class CrmEvidencePolicy
{
    private static readonly string[] SystemBaseline =
    [
        "Snapshot ข้อตกลง สินค้า รูปที่แนบ ยอดเงิน และ terms version",
        "เวลายอมรับของผู้ซื้อและผู้ขาย",
        "เหตุการณ์ payment/refund/payout ที่ provider ยืนยัน",
        "ข้อมูลขนส่ง Tracking scans และเวลาส่งมอบที่ตรวจสอบได้",
        "กำหนดแจ้งปัญหาและ audit events ก่อนหน้า"
    ];

    public static CrmEvidenceChecklist For(
        DisputeReason? reason,
        FulfillmentType fulfillmentType,
        string category)
    {
        (string[] Buyer, string[] Seller) evidence =
            reason switch
        {
            DisputeReason.NotReceived => (
                ["คำยืนยันปัญหาการรับสินค้า"],
                ["หลักฐานส่งมอบให้ขนส่ง/ผู้รับ"]),
            DisputeReason.WrongItem => (
                ["ภาพสินค้าทั้งชิ้น กล่อง และฉลากขนส่ง"],
                ["ภาพก่อนแพ็กและตำหนิ/จุดระบุชิ้นสินค้า"]),
            DisputeReason.NotAsDescribed => (
                ["ระบุจุดที่ไม่ตรงและภาพเฉพาะจุด"],
                ["ภาพสภาพสินค้าก่อนส่งและรายละเอียดที่ประกาศ"]),
            DisputeReason.UndisclosedDamage => (
                ["ภาพสินค้าและบรรจุภัณฑ์"],
                ["ภาพการแพ็กและสภาพสินค้าก่อนส่ง"]),
            DisputeReason.SuspectedCounterfeit => (
                ["ภาพ label/serial และเหตุผลที่สงสัย"],
                ["ที่มา ใบเสร็จ หรือหลักฐานความแท้"]),
            DisputeReason.EmptyOrTamperedParcel => (
                ["ภาพกล่อง seal ฉลาก และวิดีโอเปิดกล่องถ้ามี"],
                ["ภาพการแพ็ก ใบรับพัสดุ และน้ำหนักที่บันทึก"]),
            _ when fulfillmentType ==
                   FulfillmentType.DigitalHandoff => (
                ["ปัญหาแบบไม่เปิดเผยข้อมูลลับและช่องทางที่ตรวจแล้ว"],
                ["สิทธิ์โอนและหลักฐานส่งมอบที่ไม่มี credential"]),
            _ => (
                ["คำอธิบายและหลักฐานที่เกี่ยวข้อง"],
                ["คำชี้แจงและหลักฐานที่เกี่ยวข้อง"])
        };

        var categoryEvidence =
            CategoryEvidence(category, fulfillmentType);
        return new CrmEvidenceChecklist(
            SystemBaseline,
            evidence.Buyer,
            evidence.Seller,
            categoryEvidence);
    }

    private static IReadOnlyList<string> CategoryEvidence(
        string category,
        FulfillmentType fulfillmentType)
    {
        if (fulfillmentType == FulfillmentType.DigitalHandoff)
            return
            [
                "สิทธิ์ในการโอนและหลักฐานการส่งมอบที่ไม่ใช่รหัสผ่านหรือ credential"
            ];
        if (category.Contains("กล้อง", StringComparison.Ordinal) ||
            category.Contains("อิเล็กทรอนิกส์", StringComparison.Ordinal))
            return
            [
                "serial/จุดระบุชิ้นสินค้า สภาพการทำงาน และอุปกรณ์ที่รวม"
            ];
        if (category.Contains("รองเท้า", StringComparison.Ordinal) ||
            category.Contains("เสื้อ", StringComparison.Ordinal))
            return
            [
                "size/SKU สภาพโดยรวม และจุดตะเข็บ/พื้นรองเท้าที่เกี่ยวข้อง"
            ];
        if (category.Contains("กระเป๋า", StringComparison.Ordinal) ||
            category.Contains("แฟชั่น", StringComparison.Ordinal))
            return
            [
                "serial/date code เมื่อมี และที่มาหากมีการรับรองความแท้"
            ];
        if (category.Contains("สะสม", StringComparison.Ordinal))
            return
            [
                "edition/serial ใบรับรองเมื่อมี และสภาพ seal/บรรจุภัณฑ์"
            ];
        return
        [
            "model/จุดระบุชิ้นส่วน อุปกรณ์ที่รวม และสภาพการทำงาน"
        ];
    }
}
