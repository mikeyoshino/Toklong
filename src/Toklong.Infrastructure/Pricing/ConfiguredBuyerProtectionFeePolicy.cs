using Toklong.Application.Pricing;
using Toklong.Domain.Common;

namespace Toklong.Infrastructure.Pricing;

public sealed class ConfiguredBuyerProtectionFeePolicy(
    BuyerProtectionFeeOptions options)
    : IPaymentFeePolicy
{
    public void EnsureItemPriceAllowed(long itemPriceSatang)
    {
        options.ValidateStructure();
        if (itemPriceSatang <
                options.MinimumItemPriceSatang ||
            itemPriceSatang >
                options.MaximumItemPriceSatang)
            throw new DomainException(
                $"ราคาสินค้าต้องอยู่ระหว่าง " +
                $"{FormatBaht(options.MinimumItemPriceSatang)}–" +
                $"{FormatBaht(options.MaximumItemPriceSatang)} บาท " +
                "ตามวงเงินที่เปิดใช้งาน");
    }

    public PaymentFeeBreakdown GetDisclosure(long itemPriceSatang)
    {
        EnsureItemPriceAllowed(itemPriceSatang);
        if (!options.Enabled)
            return new PaymentFeeBreakdown(
                0,
                0,
                itemPriceSatang,
                "payments-disabled");
        return CalculateConfigured(itemPriceSatang);
    }

    public PaymentFeeBreakdown Calculate(long itemPriceSatang)
    {
        if (!options.Enabled)
            throw new InvalidOperationException(
                "ยังไม่เปิดรับชำระเงินจริง เพราะยังไม่ได้อนุมัตินโยบายค่าบริการ");
        return CalculateConfigured(itemPriceSatang);
    }

    private PaymentFeeBreakdown CalculateConfigured(
        long itemPriceSatang)
    {
        options.Validate();
        EnsureItemPriceAllowed(itemPriceSatang);
        long weightedFeeNumerator = 0;
        long previousUpperBound = 0;
        foreach (var tier in options.Tiers)
        {
            var upperBound = Math.Min(
                itemPriceSatang,
                tier.UpToItemPriceSatang);
            if (upperBound > previousUpperBound)
            {
                weightedFeeNumerator = checked(
                    weightedFeeNumerator +
                    checked(
                        (upperBound - previousUpperBound) *
                        tier.RateBasisPoints));
            }

            if (itemPriceSatang <= tier.UpToItemPriceSatang)
                break;
            previousUpperBound = tier.UpToItemPriceSatang;
        }

        var percentageFeeSatang = checked(
            (weightedFeeNumerator + 9_999) / 10_000);
        var buyerFee = Math.Max(
            options.MinimumFeeSatang,
            percentageFeeSatang);
        return new PaymentFeeBreakdown(
            buyerFee,
            0,
            itemPriceSatang,
            options.PolicyVersion.Trim());
    }

    private static string FormatBaht(long satang) =>
        (satang / 100m).ToString(
            "#,##0.##",
            System.Globalization.CultureInfo.InvariantCulture);
}
