using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Shipping.ProcessProviderShipments;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;
using Toklong.Infrastructure.Services;

namespace Toklong.Application.Tests.Shipping;

public sealed class ProviderShipmentProcessingTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 26, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Paid_reserved_shipment_is_confirmed_once_by_worker()
    {
        await using var database = Database();
        var repository = new TransactionRepository(database);
        var transitions = new TransactionTransitionService();
        var clock = new FixedClock();
        var provider =
            new DevelopmentShippingQuoteProvider(clock);
        var transaction = await ManagedPaidAsync(
            provider,
            transitions);
        await repository.AddAsync(transaction, default);
        await database.SaveChangesAsync();
        var handler = new ConfirmProviderShipmentsHandler(
            repository,
            provider,
            database,
            clock,
            transitions);

        var first = await handler.Handle(
            new ConfirmProviderShipmentsCommand(),
            default);
        var second = await handler.Handle(
            new ConfirmProviderShipmentsCommand(),
            default);

        Assert.Equal(1, first.Processed);
        Assert.Equal(0, first.Failed);
        Assert.Equal(0, second.Processed);
        var saved = await repository.GetByIdAsync(
            transaction.Id,
            default);
        Assert.NotNull(saved);
        Assert.Equal(
            TransactionState.TrackingSubmitted,
            saved.State);
        Assert.NotNull(saved.TrackingNumber);
        Assert.Single(
            saved.AuditEvents,
            item =>
                item.Name ==
                "shipment.provider_confirmed");
    }

    [Fact]
    public async Task Complete_without_trusted_time_enters_review_without_delivery_deadline()
    {
        await using var database = Database();
        var repository = new TransactionRepository(database);
        var transitions = new TransactionTransitionService();
        var development =
            new DevelopmentShippingQuoteProvider(
                new FixedClock());
        var transaction = await ManagedPaidAsync(
            development,
            transitions);
        transaction.ConfirmProviderManagedShipment(
            development.ProviderName,
            transaction.ShippingProviderTrackingCode!,
            transaction.ShippingCourierTrackingCode!,
            transaction.CarrierCode!,
            "booking",
            Now.AddMinutes(4),
            transitions);
        await repository.AddAsync(transaction, default);
        await database.SaveChangesAsync();
        var handler = new ReconcileProviderShipmentsHandler(
            repository,
            new MissingDeliveryTimeProvider(
                transaction.ShippingCourierTrackingCode!),
            database,
            new FixedClock(Now.AddMinutes(5)),
            transitions);

        var result = await handler.Handle(
            new ReconcileProviderShipmentsCommand(),
            default);

        Assert.Equal(1, result.Processed);
        Assert.Equal(0, result.Failed);
        Assert.Equal(
            TransactionState.TrackingUnverified,
            transaction.State);
        Assert.Null(transaction.DeliveredAt);
        Assert.Null(transaction.DisputeWindowStartsAt);
        Assert.Null(transaction.DisputeWindowEndsAt);
    }

    [Fact]
    public async Task Already_cancelled_provider_shipment_completes_without_second_cancel()
    {
        await using var database = Database();
        var repository = new TransactionRepository(database);
        var transitions = new TransactionTransitionService();
        var development =
            new DevelopmentShippingQuoteProvider(
                new FixedClock());
        var transaction = await ManagedPaidAsync(
            development,
            transitions);
        transaction.ConfirmProviderManagedShipment(
            development.ProviderName,
            transaction.ShippingProviderTrackingCode!,
            transaction.ShippingCourierTrackingCode!,
            transaction.CarrierCode!,
            "booking",
            Now.AddMinutes(4),
            transitions);
        transaction.MarkShipmentOverdue(
            transaction.ShipByAt!.Value.AddMinutes(1),
            transitions);
        await repository.AddAsync(transaction, default);
        await database.SaveChangesAsync();
        var provider = new AlreadyCancelledProvider();
        var handler = new CancelProviderShipmentsHandler(
            repository,
            provider,
            database,
            new FixedClock(
                transaction.ShipByAt!.Value.AddMinutes(2)),
            transitions);

        var result = await handler.Handle(
            new CancelProviderShipmentsCommand(),
            default);

        Assert.Equal(1, result.Processed);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, provider.CancelCalls);
        Assert.NotNull(transaction.ShippingCancelledAt);
        Assert.False(
            transaction.RequiresShippingCancellationBeforeRefund);
    }

    [Fact]
    public async Task Timely_scan_found_before_cancellation_returns_to_tracking_review()
    {
        await using var database = Database();
        var repository = new TransactionRepository(database);
        var transitions = new TransactionTransitionService();
        var development =
            new DevelopmentShippingQuoteProvider(
                new FixedClock());
        var transaction = await ManagedPaidAsync(
            development,
            transitions);
        transaction.ConfirmProviderManagedShipment(
            development.ProviderName,
            transaction.ShippingProviderTrackingCode!,
            transaction.ShippingCourierTrackingCode!,
            transaction.CarrierCode!,
            "booking",
            Now.AddMinutes(4),
            transitions);
        transaction.MarkShipmentOverdue(
            transaction.ShipByAt!.Value.AddMinutes(1),
            transitions);
        await repository.AddAsync(transaction, default);
        await database.SaveChangesAsync();
        var provider = new TimelyScanProvider(
            transaction.ShippingCourierTrackingCode!,
            transaction.ShipByAt.Value.AddMinutes(-1));
        var handler = new CancelProviderShipmentsHandler(
            repository,
            provider,
            database,
            new FixedClock(
                transaction.ShipByAt.Value.AddMinutes(2)),
            transitions);

        var result = await handler.Handle(
            new CancelProviderShipmentsCommand(),
            default);
        var pendingRefunds =
            await repository.GetPendingRefundsAsync(default);

        Assert.Equal(1, result.Processed);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, provider.CancelCalls);
        Assert.Equal(
            TransactionState.TrackingUnverified,
            transaction.State);
        Assert.True(
            transaction.HasTimelyTrustedCarrierAcceptance);
        Assert.Empty(pendingRefunds);
        Assert.Single(
            transaction.AuditEvents,
            item =>
            item.Name ==
                "shipment.timely_acceptance_recovered");
    }

    [Fact]
    public async Task Managed_return_delivery_is_reconciled_and_retained_once()
    {
        await using var database = Database();
        var transaction = ManagedReturn();
        await new TransactionRepository(database)
            .AddAsync(transaction, default);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        var deliveredAt = Now.AddHours(2);
        var handler = new ReconcileProviderShipmentsHandler(
            new TransactionRepository(database),
            new ManagedReturnTrackingProvider(
                "complete",
                "delivered",
                deliveredAt),
            database,
            new FixedClock(deliveredAt.AddMinutes(1)),
            new TransactionTransitionService());

        var first = await handler.Handle(
            new ReconcileProviderShipmentsCommand(),
            default);
        var second = await handler.Handle(
            new ReconcileProviderShipmentsCommand(),
            default);
        var saved = await new TransactionRepository(database)
            .GetByIdAsync(transaction.Id, default);

        Assert.Equal(1, first.Processed);
        Assert.Equal(0, second.Processed);
        Assert.NotNull(saved);
        Assert.Equal(deliveredAt, saved.ReturnDeliveredAt);
        Assert.Equal(
            ManagedShipmentStatus.Delivered,
            Assert.Single(
                saved.ManagedShipments,
                shipment =>
                    shipment.Direction ==
                        ShipmentDirection.Return).Status);
        Assert.Single(
            saved.AuditEvents,
            audit =>
                audit.Name == "shipping.return_delivered");
    }

    [Fact]
    public async Task Managed_return_problem_blocks_automatic_outcomes()
    {
        await using var database = Database();
        var transaction = ManagedReturn();
        await new TransactionRepository(database)
            .AddAsync(transaction, default);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        var handler = new ReconcileProviderShipmentsHandler(
            new TransactionRepository(database),
            new ManagedReturnTrackingProvider(
                "problem",
                "carrier_exception",
                null),
            database,
            new FixedClock(Now.AddHours(2)),
            new TransactionTransitionService());

        var result = await handler.Handle(
            new ReconcileProviderShipmentsCommand(),
            default);
        var saved = await new TransactionRepository(database)
            .GetByIdAsync(transaction.Id, default);

        Assert.Equal(1, result.Processed);
        Assert.NotNull(saved);
        Assert.True(saved.HasOpenShippingException);
        Assert.Equal(
            ManagedShipmentStatus.CarrierException,
            Assert.Single(
                saved.ManagedShipments,
                shipment =>
                    shipment.Direction ==
                        ShipmentDirection.Return).Status);
    }

    [Fact]
    public async Task Managed_return_booking_status_remains_confirmed()
    {
        await using var database = Database();
        var transaction = ManagedReturn();
        await new TransactionRepository(database)
            .AddAsync(transaction, default);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        var handler = new ReconcileProviderShipmentsHandler(
            new TransactionRepository(database),
            new ManagedReturnTrackingProvider(
                "booking",
                null,
                null),
            database,
            new FixedClock(Now.AddHours(2)),
            new TransactionTransitionService());

        var result = await handler.Handle(
            new ReconcileProviderShipmentsCommand(),
            default);
        var saved = await new TransactionRepository(database)
            .GetByIdAsync(transaction.Id, default);

        Assert.Equal(1, result.Processed);
        Assert.NotNull(saved);
        var shipment = Assert.Single(
            saved.ManagedShipments,
            item =>
                item.Direction == ShipmentDirection.Return);
        Assert.Equal(
            ManagedShipmentStatus.Confirmed,
            shipment.Status);
        Assert.Equal("booking", shipment.LastProviderStatus);
        Assert.False(saved.HasOpenShippingException);
    }

    private static SaleTransaction ManagedReturn()
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
            new ManagedShipmentDraft(
                "shippop",
                "buyer-address-snapshot",
                "seller-address-snapshot",
                "กล้อง",
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
                "return-quote-001",
                Now.AddHours(3)),
            Now);
        transaction.QueueManagedShipment(
            shipment,
            ShippingOperation.Queue(
                transaction.Id,
                shipment.Id,
                ShippingOperationType.BookReturn,
                $"book-return:{transaction.Id:N}:test",
                new string('b', 64),
                Now),
            ActorRole.Reconciliation,
            "crm-user",
            Now);
        shipment.RecordReservation(
            "return-purchase-001",
            "return-provider-track-001",
            null,
            Now.AddMinutes(1));
        shipment.RecordConfirmation(
            "EF987654321TH",
            "booking",
            Now.AddMinutes(2));
        return transaction;
    }

    private static async Task<SaleTransaction> ManagedPaidAsync(
        DevelopmentShippingQuoteProvider provider,
        TransactionTransitionService transitions)
    {
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ ทดสอบ",
            "+66899999999",
            FulfillmentType.PhysicalShipment,
            "กล้องทดสอบ",
            "กล้องใช้งานปกติพร้อมเลนส์",
            ConditionCode.UsedGood,
            "ไม่มี",
            null,
            120_000,
            "terms-v1",
            Now,
            transitions);
        var quote = new ShippingQuoteOption(
            provider.ProviderName,
            "dev-quote",
            "FLASH",
            "STANDARD",
            "Flash Express Standard",
            5_000,
            0,
            0,
            null,
            Now.AddHours(2));
        var reservation = await provider.ReserveAsync(
            new ShipmentReservationRequest(
                transaction.Id,
                new ShippingQuoteRequest(
                    "10110",
                    "10110",
                    1_200,
                    20,
                    30,
                    15),
                quote),
            default);
        transaction.AcceptBuyerOffer(
            Guid.NewGuid(),
            "ผู้ขาย ทดสอบ",
            "+66811111111",
            "KBANK",
            "ผู้ขาย ทดสอบ",
            "1234567890",
            true,
            Now.AddMinutes(1),
            transitions,
            shipping: new AcceptedShippingQuote(
                TestTransactionFactory.ShippingOriginAddress,
                TestTransactionFactory.DeliveryProvinceName,
                TestTransactionFactory.DeliveryPostalCode,
                1_200,
                20,
                30,
                15,
                quote.Provider,
                quote.QuoteReference,
                quote.CarrierCode,
                quote.ServiceCode,
                quote.ServiceName,
                quote.FeeSatang,
                0,
                0,
                null,
                quote.ExpiresAt,
                "วัฒนา",
                "คลองเตยเหนือ",
                reservation.PurchaseReference,
                reservation.ProviderTrackingCode,
                reservation.CourierTrackingCode,
                reservation.ReservedAt,
                "123 ถนนสุขุมวิท"));
        transaction.BeginCheckout(
            "ผู้ซื้อ ทดสอบ",
            "+66899999999",
            TestTransactionFactory.DeliveryAddress,
            Now.AddMinutes(2),
            transitions);
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
                .UseInMemoryDatabase(
                    Guid.NewGuid().ToString("N"))
                .Options;
        return new ToklongDbContext(options);
    }

    private sealed class FixedClock(
        DateTimeOffset? value = null) : IClock
    {
        public DateTimeOffset UtcNow =>
            value ?? Now;
    }

    private sealed class AlreadyCancelledProvider :
        IShipmentProvider
    {
        public string ProviderName =>
            "development-shipping";

        public int CancelCalls { get; private set; }

        public Task<ShipmentTrackingUpdate> GetTrackingAsync(
            string providerTrackingCode,
            string carrierCode,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new ShipmentTrackingUpdate(
                    providerTrackingCode,
                    "TH123456789012",
                    carrierCode,
                    "cancel",
                    null,
                    "already-cancelled",
                    Now));

        public Task CancelAsync(
            string courierTrackingCode,
            CancellationToken cancellationToken)
        {
            CancelCalls++;
            return Task.CompletedTask;
        }

        public Task<ShipmentReservation> ReserveAsync(
            ShipmentReservationRequest request,
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
    }

    private sealed class MissingDeliveryTimeProvider(
        string courierTrackingCode) :
        IShipmentProvider
    {
        public string ProviderName =>
            "development-shipping";

        public Task<ShipmentTrackingUpdate> GetTrackingAsync(
            string providerTrackingCode,
            string carrierCode,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new ShipmentTrackingUpdate(
                    providerTrackingCode,
                    courierTrackingCode,
                    carrierCode,
                    "complete",
                    "unverified",
                    "complete-without-trusted-time",
                    null));

        public Task CancelAsync(
            string courierTrackingCode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ShipmentReservation> ReserveAsync(
            ShipmentReservationRequest request,
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
    }

    private sealed class TimelyScanProvider(
        string courierTrackingCode,
        DateTimeOffset occurredAt) :
        IShipmentProvider
    {
        public string ProviderName =>
            "development-shipping";

        public int CancelCalls { get; private set; }

        public Task<ShipmentTrackingUpdate> GetTrackingAsync(
            string providerTrackingCode,
            string carrierCode,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new ShipmentTrackingUpdate(
                    providerTrackingCode,
                    courierTrackingCode,
                    carrierCode,
                    "shipping",
                    "in_transit",
                    "timely-scan",
                    occurredAt));

        public Task CancelAsync(
            string courierTrackingCode,
            CancellationToken cancellationToken)
        {
            CancelCalls++;
            return Task.CompletedTask;
        }

        public Task<ShipmentReservation> ReserveAsync(
            ShipmentReservationRequest request,
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
    }

    private sealed class ManagedReturnTrackingProvider(
        string providerStatus,
        string? eventType,
        DateTimeOffset? occurredAt) :
        IShipmentProvider
    {
        public string ProviderName => "shippop";

        public Task<ShipmentTrackingUpdate> GetTrackingAsync(
            string providerTrackingCode,
            string carrierCode,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new ShipmentTrackingUpdate(
                    providerTrackingCode,
                    "EF987654321TH",
                    carrierCode,
                    providerStatus,
                    eventType,
                    $"return-{providerStatus}",
                    occurredAt));

        public Task CancelAsync(
            string courierTrackingCode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ShipmentReservation> ReserveAsync(
            ShipmentReservationRequest request,
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
    }
}
