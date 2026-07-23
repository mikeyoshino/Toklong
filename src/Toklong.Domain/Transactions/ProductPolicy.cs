namespace Toklong.Domain.Transactions;

public static class ProductPolicy
{
    private static readonly HashSet<string> AllowedCategories =
    [
        "กล้องและอุปกรณ์",
        "รองเท้าและเสื้อผ้า",
        "กระเป๋าและแฟชั่น",
        "ของสะสม",
        "อิเล็กทรอนิกส์",
        "งานอดิเรกและของใช้",
        "สินค้าดิจิทัลที่โอนได้"
    ];

    private static readonly string[] ProhibitedTerms =
    [
        "อาวุธ", "ปืน", "กระสุน", "ยาเสพติด", "กัญชา", "บุหรี่", "บุหรี่ไฟฟ้า",
        "ของปลอม", "ละเมิดลิขสิทธิ์", "บริการ", "พรีออเดอร์", "preorder",
        "คริปโต", "crypto", "wallet", "วอลเล็ต", "private key", "seed phrase",
        "บัตรของขวัญ", "gift card", "บัญชีธนาคาร", "เอกสารประจำตัว"
    ];

    public static ProductPolicyResult Evaluate(
        FulfillmentType fulfillmentType,
        string category,
        string productName,
        string description)
    {
        if (!AllowedCategories.Contains(category.Trim()))
            return new(false, "unsupported_category", "หมวดสินค้านี้ยังไม่รองรับใน MVP");
        if (fulfillmentType == FulfillmentType.DigitalHandoff &&
            !string.Equals(category.Trim(), "สินค้าดิจิทัลที่โอนได้", StringComparison.Ordinal))
            return new(false, "unsupported_digital_category", "กรุณาใช้ประเภทสินค้าดิจิทัลที่โอนได้");
        if (fulfillmentType == FulfillmentType.PhysicalShipment &&
            string.Equals(category.Trim(), "สินค้าดิจิทัลที่โอนได้", StringComparison.Ordinal))
            return new(false, "fulfillment_category_mismatch", "ประเภทการส่งมอบไม่ตรงกับรายการ");

        var content = $"{productName} {description}";
        if (ProhibitedTerms.Any(term => content.Contains(term, StringComparison.OrdinalIgnoreCase)))
            return new(false, "prohibited_content", "พบคำที่อาจเกี่ยวข้องกับสินค้าหรือรูปแบบการขายที่ไม่รองรับ");

        return new(true, "", "");
    }
}

public sealed record ProductPolicyResult(bool Allowed, string ReasonCode, string UserMessage);
