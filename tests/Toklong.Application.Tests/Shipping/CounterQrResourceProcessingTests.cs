using Toklong.Application.Abstractions;
using Toklong.Application.Features.Shipping;
using Toklong.Application.Features.Shipping.ProcessCounterQrResources;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Security;
using Toklong.TestSupport;

namespace Toklong.Application.Tests.Shipping;

public sealed class CounterQrResourceProcessingTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WorkNow =
        Now.AddMinutes(6);
    private readonly string keysPath = Path.Combine(
        Path.GetTempPath(),
        $"toklong-counter-worker-{Guid.NewGuid():N}");

    [Fact]
    public async Task Worker_reads_only_counter_qr_and_preserves_transaction_state()
    {
        var transaction = ConfirmedManagedTransaction();
        var resource = transaction.CurrentOutboundShipment!
            .CounterQrResource!;
        var repository = new ResourceRepository(
            transaction,
            resource);
        var provider = new ReadOnlyProvider();
        var unitOfWork = new UnitOfWork();
        var beforeState = transaction.State;
        var handler = new ProcessNextCounterQrResourceHandler(
            repository,
            provider,
            new CounterQrArtifactProtector(keysPath),
            unitOfWork,
            new FixedClock());

        Assert.True(await handler.Handle(
            new ProcessNextCounterQrResourceCommand("worker-a"),
            default));

        Assert.Equal(beforeState, transaction.State);
        Assert.Equal(CounterQrResourceStatus.Ready, resource.Status);
        Assert.NotNull(resource.ProtectedArtifact);
        Assert.Equal(1, provider.CounterQrCalls);
        Assert.Equal(0, provider.MutationCalls);
        Assert.Equal(1, unitOfWork.SaveCalls);
        var audit = Assert.Single(
            transaction.AuditEvents,
            item => item.Name == "shipping.counter_qr_ready");
        Assert.DoesNotContain("purchase-ref", audit.MetadataJson);
        Assert.DoesNotContain("provider-track", audit.MetadataJson);
        Assert.DoesNotContain("courier-track", audit.MetadataJson);
        Assert.DoesNotContain("image/png", audit.MetadataJson);
    }

    [Fact]
    public async Task Worker_does_not_fetch_counter_qr_after_refund_becomes_required()
    {
        var transaction = ConfirmedManagedTransaction();
        var transitions = new TransactionTransitionService();
        Assert.True(transaction.MarkShipmentOverdue(
            Now.AddHours(80),
            transitions));
        var resource = transaction.CurrentOutboundShipment!
            .CounterQrResource!;
        var repository = new ResourceRepository(
            transaction,
            resource);
        var provider = new ReadOnlyProvider();
        var handler = new ProcessNextCounterQrResourceHandler(
            repository,
            provider,
            new CounterQrArtifactProtector(keysPath),
            new UnitOfWork(),
            new FixedClock(Now.AddHours(80)));

        Assert.True(await handler.Handle(
            new ProcessNextCounterQrResourceCommand("worker-a"),
            default));

        Assert.Equal(TransactionState.RefundPending, transaction.State);
        Assert.Equal(CounterQrResourceStatus.Unavailable, resource.Status);
        Assert.Equal(0, provider.CounterQrCalls);
        Assert.Null(resource.ProtectedArtifact);
    }

    public void Dispose()
    {
        if (Directory.Exists(keysPath))
            Directory.Delete(keysPath, recursive: true);
    }

    private static SaleTransaction ConfirmedManagedTransaction()
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
            Now,
            transitions);
        var sellerId = Guid.NewGuid();
        transaction.AcceptBuyerOffer(
            sellerId,
            "ผู้ขาย ทดสอบ",
            "0811111111",
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
                "development-shipping",
                "quote-ref",
                "THAIPOST",
                "EMS",
                "ไปรษณีย์ไทย EMS",
                5_000,
                0,
                0,
                null,
                Now.AddHours(2),
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
                Now.AddMinutes(1),
                Now.AddMinutes(30)),
            Now.AddMinutes(1));
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
                Now.AddHours(2),
                "parcel-protection-included-v1",
                null,
                ParcelProtectionElectionStatus.Declined),
            Now.AddMinutes(1));
        var booking = ShippingOperation.Queue(
            transaction.Id,
            shipment.Id,
            ShippingOperationType.BookOutbound,
            "book-counter-test",
            ManagedShippingOperationQueue.BookingFingerprint(
                shipment),
            Now.AddMinutes(1));
        transaction.QueueManagedShipment(
            shipment,
            booking,
            ActorRole.System,
            "test",
            Now.AddMinutes(1));
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
            Now.AddMinutes(2),
            Now.AddMinutes(2));
        transaction.BeginCheckout(
            "ผู้ซื้อ ทดสอบ",
            "0800000000",
            Now.AddMinutes(3),
            transitions);
        transaction.ConfirmPayment(
            "payment-ref",
            Now.AddMinutes(4),
            transitions);
        shipment.RecordConfirmation(
            "courier-track",
            "booking",
            Now.AddMinutes(5));
        transaction.ConfirmProviderManagedShipment(
            "development-shipping",
            "provider-track",
            "courier-track",
            "THAIPOST",
            "booking",
            Now.AddMinutes(5),
            transitions);
        transaction.QueueShipmentCounterQr(
            shipment.Id,
            "shipping-worker",
            Now.AddMinutes(5));
        return transaction;
    }

    private sealed class ResourceRepository(
        SaleTransaction transaction,
        ShipmentCounterQrResource resource) :
        ICounterQrResourceRepository
    {
        private bool claimed;

        public Task<ShipmentCounterQrResource?> ClaimDueAsync(
            string workerId,
            DateTimeOffset now,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
        {
            if (claimed)
                return Task.FromResult<
                    ShipmentCounterQrResource?>(null);
            claimed = true;
            resource.Claim(workerId, now, leaseDuration);
            return Task.FromResult<
                ShipmentCounterQrResource?>(resource);
        }

        public Task<ShipmentCounterQrResource?> GetByIdAsync(
            Guid resourceId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ShipmentCounterQrResource?>(resource);

        public Task<SaleTransaction?> GetTransactionAsync(
            Guid transactionId,
            CancellationToken cancellationToken) =>
            Task.FromResult<SaleTransaction?>(transaction);
    }

    private sealed class ReadOnlyProvider : IShipmentProvider
    {
        public string ProviderName => "development-shipping";
        public int CounterQrCalls { get; private set; }
        public int MutationCalls { get; private set; }

        public Task<CounterQrReadResult> GetCounterQrAsync(
            CounterQrRequest request,
            CancellationToken cancellationToken)
        {
            CounterQrCalls++;
            var png = CounterQrTestPng.Create();
            return Task.FromResult(new CounterQrReadResult(
                CounterQrReadStatus.Ready,
                CounterQrRepresentation.ProviderPng,
                png,
                new string('a', 64),
                null,
                WorkNow,
                null));
        }

        public Task<ShipmentReservation> ReserveAsync(
            ShipmentReservationRequest request,
            CancellationToken cancellationToken)
        {
            MutationCalls++;
            throw new NotSupportedException();
        }

        public Task<ShipmentConfirmation> ConfirmAsync(
            string purchaseReference,
            string providerTrackingCode,
            string carrierCode,
            CancellationToken cancellationToken)
        {
            MutationCalls++;
            throw new NotSupportedException();
        }

        public Task CancelAsync(
            string courierTrackingCode,
            CancellationToken cancellationToken)
        {
            MutationCalls++;
            throw new NotSupportedException();
        }

        public Task<ShipmentTrackingUpdate> GetTrackingAsync(
            string providerTrackingCode,
            string carrierCode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string> GetLabelHtmlAsync(
            ShipmentLabelRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class UnitOfWork : IUnitOfWork
    {
        public int SaveCalls { get; private set; }

        public Task<int> SaveChangesAsync(
            CancellationToken cancellationToken)
        {
            SaveCalls++;
            return Task.FromResult(1);
        }
    }

    private sealed class FixedClock(
        DateTimeOffset? value = null) : IClock
    {
        public DateTimeOffset UtcNow => value ?? WorkNow;
    }
}
