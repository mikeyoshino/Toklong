namespace Toklong.Mobile.Core;

public sealed record BuyerCostPreview
{
    public BuyerCostPreview(
        long itemPriceSatang,
        long buyerProtectionFeeSatang,
        long platformFeeSatang,
        long sellerExpectedNetSatang,
        long totalBeforeShippingSatang,
        string currency,
        string feePolicyVersion)
    {
        if (itemPriceSatang < 0)
            throw new ArgumentOutOfRangeException(
                nameof(itemPriceSatang));
        if (buyerProtectionFeeSatang < 0)
            throw new ArgumentOutOfRangeException(
                nameof(buyerProtectionFeeSatang));
        if (platformFeeSatang < 0)
            throw new ArgumentOutOfRangeException(
                nameof(platformFeeSatang));
        if (sellerExpectedNetSatang < 0)
            throw new ArgumentOutOfRangeException(
                nameof(sellerExpectedNetSatang));
        if (totalBeforeShippingSatang != checked(
                itemPriceSatang + buyerProtectionFeeSatang))
            throw new ArgumentException(
                "ยอดรวมก่อนค่าจัดส่งไม่ตรงกับราคาสินค้าและค่าคุ้มครอง");
        if (string.IsNullOrWhiteSpace(currency) ||
            currency.Trim().Length != 3)
            throw new ArgumentException(
                "กรุณาระบุสกุลเงิน ISO ให้ถูกต้อง",
                nameof(currency));
        if (string.IsNullOrWhiteSpace(feePolicyVersion))
            throw new ArgumentException(
                "กรุณาระบุเวอร์ชันนโยบายค่าบริการ",
                nameof(feePolicyVersion));

        ItemPriceSatang = itemPriceSatang;
        BuyerProtectionFeeSatang = buyerProtectionFeeSatang;
        PlatformFeeSatang = platformFeeSatang;
        SellerExpectedNetSatang = sellerExpectedNetSatang;
        TotalBeforeShippingSatang = totalBeforeShippingSatang;
        Currency = currency.Trim().ToUpperInvariant();
        FeePolicyVersion = feePolicyVersion.Trim();
    }

    public long ItemPriceSatang { get; }
    public long BuyerProtectionFeeSatang { get; }
    public long PlatformFeeSatang { get; }
    public long SellerExpectedNetSatang { get; }
    public long TotalBeforeShippingSatang { get; }
    public string Currency { get; }
    public string FeePolicyVersion { get; }

    public string FormattedItemPrice =>
        MoneyFormatter.Format(ItemPriceSatang, Currency);

    public string FormattedProtectionFee =>
        MoneyFormatter.Format(BuyerProtectionFeeSatang, Currency);

    public string FormattedTotalBeforeShipping =>
        MoneyFormatter.Format(TotalBeforeShippingSatang, Currency);

    public string SummaryLabel(AppFulfillmentType fulfillmentType) =>
        fulfillmentType == AppFulfillmentType.Physical
            ? "ยอดก่อนค่าจัดส่ง"
            : "ยอดเมื่อผู้ขายตอบรับ";

    public string ShippingText(AppFulfillmentType fulfillmentType) =>
        fulfillmentType == AppFulfillmentType.Physical
            ? "รอผู้ขายเลือก"
            : "ไม่มีค่าจัดส่ง";
}

public sealed class BuyerCostPreviewRequestTracker
{
    private long currentVersion;

    public long Begin() =>
        Interlocked.Increment(ref currentVersion);

    public void Invalidate() =>
        Interlocked.Increment(ref currentVersion);

    public bool IsCurrent(long version) =>
        Interlocked.Read(ref currentVersion) == version;
}
