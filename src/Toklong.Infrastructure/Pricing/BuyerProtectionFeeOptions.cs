using Microsoft.Extensions.Configuration;
using Toklong.Domain.Transactions;

namespace Toklong.Infrastructure.Pricing;

public sealed class BuyerProtectionFeeOptions
{
    public const string SectionName = "BuyerProtectionFee";

    public bool Enabled { get; init; }
    public long MinimumFeeSatang { get; init; } = 5_900;
    public long MinimumItemPriceSatang { get; init; } = 100_000;
    public long MaximumItemPriceSatang { get; init; } = 3_000_000;
    public string PolicyVersion { get; init; } = "";
    public IReadOnlyList<BuyerProtectionFeeTierOptions> Tiers { get; init; } =
        DefaultTiers();

    public static BuyerProtectionFeeOptions From(
        IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var configuredTiers = section
            .GetSection("Tiers")
            .GetChildren()
            .Select(child => new BuyerProtectionFeeTierOptions
            {
                UpToItemPriceSatang = child.GetValue<long>(
                    "UpToItemPriceSatang"),
                RateBasisPoints = child.GetValue<int>(
                    "RateBasisPoints")
            })
            .ToArray();
        return new BuyerProtectionFeeOptions
        {
            Enabled = section.GetValue<bool>("Enabled"),
            MinimumFeeSatang =
                section.GetValue("MinimumFeeSatang", 5_900L),
            MinimumItemPriceSatang =
                section.GetValue(
                    "MinimumItemPriceSatang",
                    100_000L),
            MaximumItemPriceSatang =
                section.GetValue(
                    "MaximumItemPriceSatang",
                    3_000_000L),
            PolicyVersion = section["PolicyVersion"] ?? "",
            Tiers = configuredTiers.Length > 0
                ? configuredTiers
                : DefaultTiers()
        };
    }

    public void Validate()
    {
        ValidateStructure();
        if (!Enabled ||
            string.IsNullOrWhiteSpace(PolicyVersion))
            throw new InvalidOperationException(
                "การตั้งค่าค่าคุ้มครองผู้ซื้อไม่ถูกต้อง");
    }

    public void ValidateStructure()
    {
        if (MinimumFeeSatang < 0 ||
            MinimumItemPriceSatang <
                SaleTransaction.MinimumProtectedItemPriceSatang ||
            MaximumItemPriceSatang <
                MinimumItemPriceSatang ||
            MaximumItemPriceSatang >
                SaleTransaction.MaximumProtectedItemPriceSatang ||
            Tiers.Count == 0)
            throw new InvalidOperationException(
                "การตั้งค่าค่าคุ้มครองผู้ซื้อไม่ถูกต้อง");

        long previousUpperBound = 0;
        var previousRate = int.MaxValue;
        foreach (var tier in Tiers)
        {
            if (tier.UpToItemPriceSatang <= previousUpperBound ||
                tier.UpToItemPriceSatang >
                    SaleTransaction.MaximumProtectedItemPriceSatang ||
                tier.RateBasisPoints is < 0 or > 10_000 ||
                tier.RateBasisPoints > previousRate)
                throw new InvalidOperationException(
                    "การตั้งค่าค่าคุ้มครองผู้ซื้อไม่ถูกต้อง");
            previousUpperBound = tier.UpToItemPriceSatang;
            previousRate = tier.RateBasisPoints;
        }

        if (previousUpperBound < MaximumItemPriceSatang)
            throw new InvalidOperationException(
                "ช่วงค่าคุ้มครองผู้ซื้อไม่ครอบคลุมวงเงินที่เปิดใช้งาน");
    }

    private static IReadOnlyList<BuyerProtectionFeeTierOptions>
        DefaultTiers() =>
        [
            new()
            {
                UpToItemPriceSatang = 500_000,
                RateBasisPoints = 400
            },
            new()
            {
                UpToItemPriceSatang = 1_500_000,
                RateBasisPoints = 350
            },
            new()
            {
                UpToItemPriceSatang = 3_000_000,
                RateBasisPoints = 300
            }
        ];
}

public sealed class BuyerProtectionFeeTierOptions
{
    public long UpToItemPriceSatang { get; init; }
    public int RateBasisPoints { get; init; }
}
