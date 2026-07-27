namespace Toklong.Application.Pricing;

public sealed record PaymentFeeBreakdown(
    long BuyerProtectionFeeSatang,
    long PlatformFeeSatang,
    long SellerExpectedNetSatang,
    string PolicyVersion);

public interface IPaymentFeePolicy
{
    void EnsureItemPriceAllowed(long itemPriceSatang);
    PaymentFeeBreakdown GetDisclosure(long itemPriceSatang);
    PaymentFeeBreakdown Calculate(long itemPriceSatang);
}
