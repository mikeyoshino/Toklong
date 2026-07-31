using Toklong.Domain.Common;

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
            acceptedAt.AddHours(2),
            ReservedAt: acceptedAt);

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
        long buyerProtectionFeeSatang = 0)
    {
        PreparePhysicalCheckoutBooking(transaction, now);
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
    }

    public static void PreparePhysicalCheckoutBooking(
        SaleTransaction transaction,
        DateTimeOffset now)
    {
        if (transaction.FulfillmentType !=
            FulfillmentType.PhysicalShipment ||
            transaction.State !=
                TransactionState.SellerAcceptedAwaitingPayment ||
            transaction.ParcelProtectionBookingReady)
            return;
        if (transaction.ParcelProtectionElection is
            ParcelProtectionElectionStatus.Pending or
            ParcelProtectionElectionStatus.ReconfirmationRequired)
            transaction.RecordParcelProtectionElection(
                transaction.BuyerId!.Value,
                new ParcelProtectionSelection(
                    ParcelProtectionElectionStatus.Declined,
                    0,
                    0,
                    0,
                    0,
                    0,
                    "parcel-protection-included-v1",
                    null,
                    now,
                    transaction.BuyerPaymentDeadlineAt!.Value),
                now);
        if (transaction.ParcelProtectionBookingReady)
            return;

        var election = transaction.ParcelProtectionElection;
        var accepted = election ==
            ParcelProtectionElectionStatus.Accepted;
        var shipment = transaction.CurrentOutboundShipment ??
            ManagedShipment.CreateOutbound(
                transaction.Id,
                new ManagedShipmentDraft(
                    transaction.ShippingQuoteProvider!,
                    $"test-origin:{transaction.Id:N}",
                    $"test-destination:{transaction.Id:N}",
                    transaction.ProductName,
                    transaction.PackageWeightGrams!.Value,
                    transaction.PackageWidthCentimeters!.Value,
                    transaction.PackageLengthCentimeters!.Value,
                    transaction.PackageHeightCentimeters!.Value,
                    transaction.CarrierCode!,
                    transaction.ShippingServiceCode!,
                    transaction.ShippingServiceName!,
                    transaction.ShippingFeeSatang,
                    accepted
                        ? transaction.ParcelProtectionProviderCostSatang
                        : 0,
                    accepted
                        ? transaction.ParcelProtectionSelectedCoverageSatang
                        : 0,
                    accepted ? "TEST_PROTECTION" : null,
                    transaction.ShippingQuoteReference!,
                    transaction.ShippingQuoteExpiresAt!.Value,
                    transaction.ParcelProtectionTermsVersion!,
                    transaction.ParcelProtectionOptionReference,
                    election,
                    accepted
                        ? transaction.ParcelProtectionProviderCostSatang
                        : 0,
                    transaction.ParcelProtectionIncludedCoverageSatang,
                    transaction.ParcelProtectionSelectedCoverageSatang),
                now);
        if (transaction.CurrentOutboundShipment is null)
        {
            transaction.RecordBuyerCheckoutShipmentIntent(
                shipment,
                transaction.BuyerId!.Value,
                $"test-book:{shipment.Id:N}",
                now);
        }
        var purchaseReference = $"test-purchase:{shipment.Id:N}";
        var providerTracking = $"test-provider:{shipment.Id:N}";
        transaction.CompleteBuyerCheckoutShipmentBooking(
            shipment.Id,
            shipment.Provider,
            purchaseReference,
            providerTracking,
            $"test-courier:{shipment.Id:N}",
            shipment.CarrierCode,
            shipment.ServiceCode,
            shipment.BaseShippingFeeSatang,
            shipment.InsuranceFeeSatang,
            shipment.DeclaredValueSatang,
            shipment.InsuranceCode,
            now,
            now);
    }

    private static string? RegionValue(
        FulfillmentType fulfillmentType,
        string? value) =>
        fulfillmentType == FulfillmentType.PhysicalShipment
            ? value
            : null;
}
