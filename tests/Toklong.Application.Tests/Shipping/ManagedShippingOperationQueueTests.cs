using Toklong.Application.Features.Shipping;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Tests.Shipping;

public sealed class ManagedShippingOperationQueueTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Reserved_return_queues_its_own_confirmation_once()
    {
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ ทดสอบ",
            "0800000000",
            FulfillmentType.PhysicalShipment,
            "กล้อง",
            "กล้องพร้อมเลนส์",
            ConditionCode.UsedGood,
            "",
            null,
            120_000,
            "terms-v1",
            Now,
            new TransactionTransitionService());
        var shipment = ManagedShipment.CreateReturn(
            transaction.Id,
            ShipmentDraft(),
            Now);
        transaction.QueueManagedShipment(
            shipment,
            ShippingOperation.Queue(
                transaction.Id,
                shipment.Id,
                ShippingOperationType.BookReturn,
                $"book-return:{transaction.Id:N}:test",
                new string('a', 64),
                Now),
            ActorRole.Reconciliation,
            "crm-user",
            Now);
        shipment.RecordReservation(
            "return-purchase-001",
            "return-provider-track-001",
            null,
            Now.AddMinutes(1));

        ManagedShippingOperationQueue
            .QueueReturnConfirmationIfRequired(
                transaction,
                Now.AddMinutes(2));
        ManagedShippingOperationQueue
            .QueueReturnConfirmationIfRequired(
                transaction,
                Now.AddMinutes(3));

        Assert.Single(
            transaction.ShippingOperations,
            operation =>
                operation.ManagedShipmentId == shipment.Id &&
                operation.OperationType ==
                    ShippingOperationType.ConfirmReturn);
    }

    private static ManagedShipmentDraft ShipmentDraft() =>
        new(
            "shippop",
            "buyer-destination-snapshot",
            "seller-origin-snapshot",
            "กล้องพร้อมเลนส์",
            1_200,
            20,
            30,
            15,
            "THAIPOST",
            "EMST",
            "ไปรษณีย์ไทย EMS",
            5_200,
            1_100,
            120_000,
            "FULL_VALUE",
            "return-quote-reference",
            Now.AddHours(2));
}
