using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Shipping.ProcessShippingOperations;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Application.Tests.Shipping;

public sealed class DurableShippingOperationProcessingTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Successful_booking_completes_acceptance_once()
    {
        await using var database = CreateDatabase();
        var (transaction, operation) = PendingAcceptance();
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        var clock = new FixedClock(Now.AddMinutes(1));
        var handler = Handler(
            database,
            operation,
            new BookingProvider(),
            clock);

        Assert.True(await handler.Handle(
            new ProcessNextShippingOperationCommand("worker-a"),
            default));

        Assert.Equal(
            ShippingOperationStatus.Succeeded,
            operation.Status);
        Assert.Equal(
            TransactionState.SellerAcceptedAwaitingPayment,
            transaction.State);
        Assert.Equal(
            Now.AddMinutes(1).AddHours(1),
            transaction.BuyerPaymentDeadlineAt);
        Assert.Single(
            transaction.AgreementAcceptances,
            acceptance =>
                acceptance.Role ==
                AgreementAcceptanceRole.Seller);
    }

    [Fact]
    public async Task Unknown_booking_outcome_is_not_replayed()
    {
        await using var database = CreateDatabase();
        var (transaction, operation) = PendingAcceptance();
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        var provider = new BookingProvider
        {
            Failure = new ShipmentMutationException(
                ShipmentMutationOutcome.OutcomeUnknown,
                "provider-timeout")
        };
        var handler = Handler(
            database,
            operation,
            provider,
            new FixedClock(Now.AddMinutes(1)));

        Assert.True(await handler.Handle(
            new ProcessNextShippingOperationCommand("worker-a"),
            default));
        Assert.Equal(
            ShippingOperationStatus.OutcomeUnknown,
            operation.Status);
        Assert.Equal(
            TransactionState.AwaitingSellerAcceptance,
            transaction.State);

        var second = await handler.Handle(
            new ProcessNextShippingOperationCommand("worker-b"),
            default);
        Assert.False(second);
        Assert.Equal(1, provider.ReserveCalls);
    }

    private static ProcessNextShippingOperationHandler Handler(
        ToklongDbContext database,
        ShippingOperation operation,
        IShipmentProvider provider,
        IClock clock) =>
        new(
            new SingleOperationRepository(operation),
            new TransactionRepository(database),
            provider,
            database,
            clock,
            new TransactionTransitionService());

    private static (SaleTransaction, ShippingOperation)
        PendingAcceptance()
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
        var quote = new AcceptedShippingQuote(
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
            Now.AddHours(2),
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
                Now.AddHours(2)),
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
            quote,
            shipment,
            operation);
        return (transaction, operation);
    }

    private static ToklongDbContext CreateDatabase()
    {
        var options =
            new DbContextOptionsBuilder<ToklongDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
        return new ToklongDbContext(options);
    }

    private sealed class SingleOperationRepository(
        ShippingOperation operation)
        : IShippingOperationRepository
    {
        public Task<ShippingOperation?> ClaimDueAsync(
            string workerId,
            DateTimeOffset now,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
        {
            if (operation.Status is not (
                    ShippingOperationStatus.Pending or
                    ShippingOperationStatus.RetryScheduled or
                    ShippingOperationStatus.Processing))
                return Task.FromResult<ShippingOperation?>(null);
            if (operation.Status ==
                    ShippingOperationStatus.Processing &&
                operation.LeaseExpiresAt > now)
                return Task.FromResult<ShippingOperation?>(null);
            operation.Claim(workerId, now, leaseDuration);
            return Task.FromResult<ShippingOperation?>(operation);
        }

        public Task<ShippingOperation?> GetByIdAsync(
            Guid operationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                operation.Id == operationId
                    ? operation
                    : null);
    }

    private sealed class BookingProvider : IShipmentProvider
    {
        public string ProviderName => "shippop";
        public ShipmentMutationException? Failure { get; init; }
        public int ReserveCalls { get; private set; }

        public Task<ShipmentReservation> ReserveAsync(
            ShipmentReservationRequest request,
            CancellationToken cancellationToken)
        {
            ReserveCalls++;
            if (Failure is not null)
                throw Failure;
            return Task.FromResult(new ShipmentReservation(
                ProviderName,
                "purchase-001",
                "provider-track-001",
                null,
                request.Quote.CarrierCode,
                request.Quote.ServiceCode,
                request.Quote.FeeSatang,
                request.Quote.InsuranceFeeSatang,
                request.Quote.DeclaredValueSatang,
                request.Quote.InsuranceCode,
                Now.AddMinutes(1)));
        }

        public Task<ShipmentTrackingUpdate> GetTrackingAsync(
            string providerTrackingCode,
            string carrierCode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ShipmentConfirmation> ConfirmAsync(
            string purchaseReference,
            string providerTrackingCode,
            string carrierCode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string> GetLabelHtmlAsync(
            ShipmentLabelRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task CancelAsync(
            string courierTrackingCode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
