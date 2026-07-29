namespace Toklong.Domain.Transactions;

public static class TestTransactionFactory
{
    public const string DeliveryProvinceName = "กรุงเทพมหานคร";
    public const string DeliveryDistrictName = "วัฒนา";
    public const string DeliverySubdistrictName = "คลองเตยเหนือ";
    public const string DeliveryPostalCode = "10110";
    public const string DeliveryAddress =
        "กรุงเทพฯ ประเทศไทย";
    public const string DeliveryAddressLine =
        "123 ถนนสุขุมวิท";
    public const string ShippingOriginAddress =
        "123 ถนนสุขุมวิท เขตวัฒนา กรุงเทพมหานคร 10110";

    public static AcceptedShippingQuote ShippingQuote(
        DateTimeOffset acceptedAt,
        long feeSatang = 5_000) =>
        new(
            ShippingOriginAddress,
            DeliveryProvinceName,
            DeliveryPostalCode,
            1_200,
            20,
            30,
            15,
            "test-shipping",
            $"quote-{acceptedAt.ToUnixTimeMilliseconds()}",
            "FLASH",
            "STANDARD",
            "Flash Express Standard",
            feeSatang,
            0,
            0,
            null,
            acceptedAt.AddHours(2));

    public static SaleTransaction CreateBuyerOffer(
        Guid buyerId,
        string buyerDisplayName,
        string buyerContact,
        FulfillmentType fulfillmentType,
        string productName,
        string proposedDescription,
        ConditionCode condition,
        string knownDefects,
        string? photoUrl,
        long priceSatang,
        string termsVersion,
        DateTimeOffset now,
        TransactionTransitionService transitions) =>
        SaleTransaction.CreateBuyerOffer(
            buyerId,
            buyerDisplayName,
            buyerContact,
            fulfillmentType,
            productName,
            proposedDescription,
            condition,
            knownDefects,
            photoUrl,
            priceSatang,
            RegionValue(
                fulfillmentType,
                DeliveryAddress),
            RegionValue(fulfillmentType, DeliveryProvinceName),
            RegionValue(fulfillmentType, DeliveryPostalCode),
            termsVersion,
            now,
            transitions,
            RegionValue(
                fulfillmentType,
                DeliveryDistrictName),
            RegionValue(
                fulfillmentType,
                DeliverySubdistrictName),
            RegionValue(
                fulfillmentType,
                DeliveryAddressLine));

    public static SaleTransaction CreateBuyerOffer(
        Guid buyerId,
        string buyerDisplayName,
        string buyerContact,
        string intendedSellerContact,
        FulfillmentType fulfillmentType,
        string productName,
        string proposedDescription,
        ConditionCode condition,
        string knownDefects,
        string? photoUrl,
        long priceSatang,
        string termsVersion,
        DateTimeOffset now,
        TransactionTransitionService transitions) =>
        SaleTransaction.CreateBuyerOffer(
            buyerId,
            buyerDisplayName,
            buyerContact,
            intendedSellerContact,
            fulfillmentType,
            productName,
            proposedDescription,
            condition,
            knownDefects,
            photoUrl,
            priceSatang,
            RegionValue(
                fulfillmentType,
                DeliveryAddress),
            RegionValue(fulfillmentType, DeliveryProvinceName),
            RegionValue(fulfillmentType, DeliveryPostalCode),
            termsVersion,
            now,
            transitions,
            RegionValue(
                fulfillmentType,
                DeliveryDistrictName),
            RegionValue(
                fulfillmentType,
                DeliverySubdistrictName),
            RegionValue(
                fulfillmentType,
                DeliveryAddressLine));

    public static void BeginCheckout(
        this SaleTransaction transaction,
        string buyerDisplayName,
        string buyerContact,
        string deliveryAddress,
        DateTimeOffset now,
        TransactionTransitionService transitions,
        string paymentProvider = "manual-bank",
        string? paymentReference = null,
        long platformFeeSatang = 0,
        long? sellerExpectedNetSatang = null,
        string feePolicyVersion = "manual-unconfigured",
        long buyerProtectionFeeSatang = 0) =>
        transaction.BeginCheckout(
            buyerDisplayName,
            buyerContact,
            now,
            transitions,
            paymentProvider,
            paymentReference,
            buyerProtectionFeeSatang,
            platformFeeSatang,
            sellerExpectedNetSatang,
            feePolicyVersion);

    private static string? RegionValue(
        FulfillmentType fulfillmentType,
        string? value) =>
        fulfillmentType == FulfillmentType.PhysicalShipment
            ? value
            : null;
}
