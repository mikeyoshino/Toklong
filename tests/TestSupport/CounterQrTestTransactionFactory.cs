using Toklong.Application.Features.Shipping;
using Toklong.Domain.Transactions;

namespace Toklong.TestSupport;

public static class CounterQrTestTransactionFactory
{
    public static SaleTransaction ConfirmedManagedTransaction(
        DateTimeOffset now,
        out Guid sellerId,
        Guid? requestedSellerId = null)
    {
        var transitions = new TransactionTransitionService();
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ ทดสอบ",
            "0800000000",
            FulfillmentType.PhysicalShipment,
            "กล้อง",
            "กล้องพร้อมเลนส์",
            ConditionCode.UsedGood,
            "ไม่มี",
            null,
            120_000,
            "terms-v1",
            now,
            transitions);
        sellerId = requestedSellerId ?? Guid.NewGuid();
        transaction.AcceptBuyerOffer(
            sellerId,
            "ผู้ขาย ทดสอบ",
            "0811111111",
            "KBANK",
            "ผู้ขาย ทดสอบ",
            "1234567890",
            true,
            now.AddMinutes(1),
            transitions,
            shipping: new AcceptedShippingQuote(
                TestTransactionFactory.ShippingOriginAddress,
                TestTransactionFactory.DeliveryProvinceName,
                TestTransactionFactory.DeliveryPostalCode,
                1_200,
                20,
                30,
                15,
                "development-shipping",
                "quote-ref",
                "THAIPOST",
                "EMS",
                "ไปรษณีย์ไทย EMS",
                5_000,
                0,
                0,
                null,
                now.AddHours(2),
                TestTransactionFactory.DeliveryDistrictName,
                TestTransactionFactory.DeliverySubdistrictName,
                OriginAddressLine:
                    TestTransactionFactory.ShippingOriginAddress));
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
                now.AddMinutes(1),
                now.AddMinutes(30)),
            now.AddMinutes(1));
        var shipment = ManagedShipment.CreateOutbound(
            transaction.Id,
            new ManagedShipmentDraft(
                "development-shipping",
                "origin-ref",
                "destination-ref",
                transaction.ProductName,
                1_200,
                20,
                30,
                15,
                "THAIPOST",
                "EMS",
                "ไปรษณีย์ไทย EMS",
                5_000,
                0,
                0,
                null,
                "quote-ref",
                now.AddHours(2),
                "parcel-protection-included-v1",
                null,
                ParcelProtectionElectionStatus.Declined),
            now.AddMinutes(1));
        var booking = ShippingOperation.Queue(
            transaction.Id,
            shipment.Id,
            ShippingOperationType.BookOutbound,
            "book-counter-test",
            ManagedShippingOperationQueue.BookingFingerprint(
                shipment),
            now.AddMinutes(1));
        transaction.QueueManagedShipment(
            shipment,
            booking,
            ActorRole.System,
            "test",
            now.AddMinutes(1));
        transaction.CompleteBuyerCheckoutShipmentBooking(
            shipment.Id,
            "development-shipping",
            "purchase-ref",
            "provider-track",
            "courier-track",
            "THAIPOST",
            "EMS",
            5_000,
            0,
            0,
            null,
            now.AddMinutes(2),
            now.AddMinutes(2));
        transaction.BeginCheckout(
            "ผู้ซื้อ ทดสอบ",
            "0800000000",
            now.AddMinutes(3),
            transitions);
        transaction.ConfirmPayment(
            "payment-ref",
            now.AddMinutes(4),
            transitions);
        shipment.RecordConfirmation(
            "courier-track",
            "booking",
            now.AddMinutes(5));
        transaction.ConfirmProviderManagedShipment(
            "development-shipping",
            "provider-track",
            "courier-track",
            "THAIPOST",
            "booking",
            now.AddMinutes(5),
            transitions);
        transaction.QueueShipmentCounterQr(
            shipment.Id,
            "shipping-worker",
            now.AddMinutes(5));
        return transaction;
    }
}
