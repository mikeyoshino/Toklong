using Toklong.Domain.Common;

namespace Toklong.Application.Pricing;

public interface IParcelProtectionPricingPolicy
{
    const long ServiceFeeSatang = 1_500;

    ParcelProtectionPrice Price(long providerCostSatang);
}

public sealed record ParcelProtectionPrice(
    long ProviderCostSatang,
    long ToklongServiceFeeSatang,
    long CustomerPriceSatang);

public sealed class ParcelProtectionPricingPolicy
    : IParcelProtectionPricingPolicy
{
    public ParcelProtectionPrice Price(long providerCostSatang)
    {
        if (providerCostSatang <= 0)
            throw new DomainException(
                "ราคาความคุ้มครองจากผู้ให้บริการไม่ถูกต้อง");

        return new ParcelProtectionPrice(
            providerCostSatang,
            IParcelProtectionPricingPolicy.ServiceFeeSatang,
            checked(providerCostSatang +
                IParcelProtectionPricingPolicy.ServiceFeeSatang));
    }
}
