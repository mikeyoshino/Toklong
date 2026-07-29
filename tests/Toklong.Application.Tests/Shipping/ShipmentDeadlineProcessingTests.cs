using Microsoft.EntityFrameworkCore;
using Toklong.Application.Features.Refunds.ProcessRefunds;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Application.Tests.Shipping;

public sealed class ShipmentDeadlineProcessingTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Overdue_managed_shipment_queues_durable_cancellation()
    {
        await using var database = Database();
        var transaction = PaidManagedShipment();
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        var dueAt = transaction.ShipByAt!.Value.AddMinutes(1);
        database.ChangeTracker.Clear();
        var handler = new EvaluateShipmentDeadlinesHandler(
            new TransactionRepository(database),
            database,
            new FixedClock(dueAt),
            new TransactionTransitionService());

        var changed = await handler.Handle(
            new EvaluateShipmentDeadlinesCommand(),
            default);

        Assert.Equal(1, changed);
        var saved = await new TransactionRepository(database)
            .GetByIdAsync(transaction.Id, default);
        Assert.NotNull(saved);
        Assert.Equal(
            TransactionState.RefundPending,
            saved.State);
        Assert.Contains(
            saved.ShippingOperations,
            operation =>
                operation.OperationType ==
                    ShippingOperationType.CancelOutbound &&
                operation.Status ==
                    ShippingOperationStatus.Pending);
    }

    private static SaleTransaction PaidManagedShipment()
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
            "",
            null,
            120_000,
            "terms-v1",
            Now,
            transitions);
        var shipping = new AcceptedShippingQuote(
            TestTransactionFactory.ShippingOriginAddress,
            TestTransactionFactory.DeliveryProvinceName,
            TestTransactionFactory.DeliveryPostalCode,
            1_200,
            20,
            30,
            15,
            "shippop",
            "quote-001",
            "THAIPOST",
            "EMST",
            "ไปรษณีย์ไทย EMS",
            5_200,
            1_100,
            120_000,
            "FULL_VALUE",
            Now.AddHours(3),
            TestTransactionFactory.DeliveryDistrictName,
            TestTransactionFactory.DeliverySubdistrictName,
            OriginAddressLine:
                TestTransactionFactory.ShippingOriginAddress);
        var shipment = ManagedShipment.CreateOutbound(
            transaction.Id,
            new ManagedShipmentDraft(
                "shippop",
                "origin-ref",
                "destination-ref",
                transaction.ProductName,
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
                "quote-001",
                Now.AddHours(3)),
            Now);
        var operation = ShippingOperation.Queue(
            transaction.Id,
            shipment.Id,
            ShippingOperationType.BookOutbound,
            $"book-outbound:{transaction.Id:N}:test",
            new string('a', 64),
            Now);
        transaction.BeginManagedSellerAcceptance(
            Guid.NewGuid(),
            "ผู้ขาย ทดสอบ",
            "0811111111",
            "KBANK",
            "ผู้ขาย ทดสอบ",
            "1234567890",
            true,
            Now,
            5_900,
            0,
            120_000,
            "fee-v1",
            shipping,
            shipment,
            operation);
        transaction.CompleteManagedSellerAcceptance(
            shipment.Id,
            "shippop",
            "purchase-001",
            "provider-track-001",
            "EF123456789TH",
            "THAIPOST",
            "EMST",
            5_200,
            1_100,
            120_000,
            "FULL_VALUE",
            Now.AddMinutes(1),
            Now.AddMinutes(1),
            transitions);
        transaction.BeginCheckout(
            "ผู้ซื้อ ทดสอบ",
            "0800000000",
            TestTransactionFactory.DeliveryAddress,
            Now.AddMinutes(2),
            transitions,
            platformFeeSatang: 0,
            sellerExpectedNetSatang: 120_000,
            feePolicyVersion: "fee-v1",
            buyerProtectionFeeSatang: 5_900);
        transaction.ConfirmPayment(
            "payment-confirmed",
            Now.AddMinutes(3),
            transitions);
        return transaction;
    }

    private static ToklongDbContext Database()
    {
        var options =
            new DbContextOptionsBuilder<ToklongDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
        return new ToklongDbContext(options);
    }

    private sealed class FixedClock(DateTimeOffset now) :
        Toklong.Application.Abstractions.IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
