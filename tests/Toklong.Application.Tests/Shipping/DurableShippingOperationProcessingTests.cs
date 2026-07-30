using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Shipping;
using Toklong.Application.Features.Shipping.ProcessShippingOperations;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Application.Tests.Shipping;

public sealed class DurableShippingOperationProcessingTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Booking_fingerprint_changes_when_any_protection_field_changes()
    {
        var transactionId = Guid.NewGuid();
        var draft = DraftWithProtection(
                termsVersion: "parcel-protection-2026-07-30",
                optionReference: "protected-option-a") with
            {
                ParcelProtectionElection =
                    ParcelProtectionElectionStatus.Accepted,
                ParcelProtectionProviderCostSatang = 4_500,
                ParcelProtectionIncludedCoverageSatang = 100_000,
                ParcelProtectionSelectedCoverageSatang = 450_000
            };
        var shipment = ManagedShipment.CreateOutbound(
            transactionId,
            draft,
            Now);
        var changedTerms = ManagedShipment.CreateOutbound(
            transactionId,
            draft with
            {
                ParcelProtectionTermsVersion =
                    "parcel-protection-2026-08-01"
            },
            Now);
        var changedOption = ManagedShipment.CreateOutbound(
            transactionId,
            draft with
            {
                ParcelProtectionOptionReference = "protected-option-b"
            },
            Now);
        var changedElection = ManagedShipment.CreateOutbound(
            transactionId,
            draft with
            {
                ParcelProtectionElection =
                    ParcelProtectionElectionStatus.Declined
            },
            Now);
        var changedProviderCost = ManagedShipment.CreateOutbound(
            transactionId,
            draft with
            {
                ParcelProtectionProviderCostSatang = 4_600
            },
            Now);
        var changedIncludedCoverage = ManagedShipment.CreateOutbound(
            transactionId,
            draft with
            {
                ParcelProtectionIncludedCoverageSatang = 100_100
            },
            Now);
        var changedSelectedCoverage = ManagedShipment.CreateOutbound(
            transactionId,
            draft with
            {
                ParcelProtectionSelectedCoverageSatang = 450_100
            },
            Now);

        var fingerprint =
            ManagedShippingOperationQueue.BookingFingerprint(shipment);

        Assert.NotEqual(
            fingerprint,
            ManagedShippingOperationQueue.BookingFingerprint(changedTerms));
        Assert.NotEqual(
            fingerprint,
            ManagedShippingOperationQueue.BookingFingerprint(changedOption));
        Assert.NotEqual(
            fingerprint,
            ManagedShippingOperationQueue.BookingFingerprint(changedElection));
        Assert.NotEqual(
            fingerprint,
            ManagedShippingOperationQueue.BookingFingerprint(changedProviderCost));
        Assert.NotEqual(
            fingerprint,
            ManagedShippingOperationQueue.BookingFingerprint(changedIncludedCoverage));
        Assert.NotEqual(
            fingerprint,
            ManagedShippingOperationQueue.BookingFingerprint(changedSelectedCoverage));
    }

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

    [Fact]
    public async Task Unexpected_provider_failure_is_sent_to_review_not_left_processing()
    {
        await using var database = CreateDatabase();
        var (transaction, operation) = PendingAcceptance();
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        var provider = new BookingProvider
        {
            Failure = new InvalidOperationException(
                "raw provider response must not escape")
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
            ShippingOperationStatus.NeedsReview,
            operation.Status);
        Assert.Equal(
            "unexpected-provider-failure",
            operation.LastSanitizedErrorCode);
        Assert.Equal(1, provider.ReserveCalls);
    }

    [Fact]
    public async Task Changed_shipping_intent_is_rejected_before_provider_call()
    {
        await using var database = CreateDatabase();
        var (transaction, _) = PendingAcceptance();
        var shipment = Assert.Single(
            transaction.ManagedShipments);
        var mismatchedOperation = ShippingOperation.Queue(
            transaction.Id,
            shipment.Id,
            ShippingOperationType.BookOutbound,
            $"book-outbound:{transaction.Id:N}:mismatch",
            new string('f', 64),
            Now);
        transaction.QueueShippingOperation(
            mismatchedOperation,
            ActorRole.System,
            "test",
            Now);
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        var provider = new BookingProvider();
        var handler = Handler(
            database,
            mismatchedOperation,
            provider,
            new FixedClock(Now.AddMinutes(1)));

        Assert.True(await handler.Handle(
            new ProcessNextShippingOperationCommand("worker-a"),
            default));

        Assert.Equal(
            ShippingOperationStatus.NeedsReview,
            mismatchedOperation.Status);
        Assert.Equal(0, provider.ReserveCalls);
    }

    [Fact]
    public async Task Return_booking_records_separate_approved_operational_cost()
    {
        await using var database = CreateDatabase();
        var (transaction, outboundOperation) =
            PendingAcceptance();
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        var provider = new BookingProvider();
        var clock = new FixedClock(Now.AddMinutes(1));
        await Handler(
                database,
                outboundOperation,
                provider,
                clock)
            .Handle(
                new ProcessNextShippingOperationCommand("worker-a"),
                default);
        typeof(SaleTransaction)
            .GetProperty(nameof(SaleTransaction.State))!
            .SetValue(
                transaction,
                TransactionState.ResolutionPending);
        var returnShipment = ManagedShipment.CreateReturn(
            transaction.Id,
            new ManagedShipmentDraft(
                "shippop",
                "destination-ref",
                "origin-ref",
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
                "return-quote-001",
                Now.AddHours(2)),
            Now.AddMinutes(2));
        var returnOperation = ShippingOperation.Queue(
            transaction.Id,
            returnShipment.Id,
            ShippingOperationType.BookReturn,
            $"book-return:{transaction.Id:N}:test",
            ManagedShippingOperationQueue.BookingFingerprint(
                returnShipment),
            Now.AddMinutes(2));
        transaction.AuthorizeManagedReturn(
            returnShipment,
            returnOperation,
            "crm-user",
            "CASE-RETURN-001",
            "อนุมัติให้ส่งคืน",
            "crm:return:authorize:001",
            Now.AddMinutes(2));
        await database.SaveChangesAsync();
        var buyerTotalBeforeReturn =
            transaction.BuyerTotalSatang;

        await Handler(
                database,
                returnOperation,
                provider,
                new FixedClock(Now.AddMinutes(3)))
            .Handle(
                new ProcessNextShippingOperationCommand("worker-b"),
                default);

        Assert.Equal(
            ShippingOperationStatus.Succeeded,
            returnOperation.Status);
        Assert.Equal(
            "return-purchase-001",
            returnShipment.PurchaseReference);
        Assert.Equal(
            "purchase-001",
            transaction.ShippingPurchaseReference);
        Assert.Equal(
            buyerTotalBeforeReturn,
            transaction.BuyerTotalSatang);
        var cost = Assert.Single(
            transaction.ProviderShippingAdjustments);
        Assert.Equal(
            "authorized-return-cost",
            cost.ReasonCode);
        Assert.Equal(6_300, cost.AmountSatang);
        Assert.False(cost.IsOpen);
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
            ManagedShippingOperationQueue.BookingFingerprint(
                shipment),
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

    private static ManagedShipmentDraft DraftWithProtection(
        string termsVersion,
        string optionReference) =>
        new(
            "shippop",
            "origin-ref",
            "destination-ref",
            "กล้อง",
            1_200,
            20,
            30,
            15,
            "THAIPOST",
            "EMST",
            "ไปรษณีย์ไทย EMS",
            5_200,
            4_500,
            450_000,
            "FULL_VALUE",
            "quote-001",
            Now.AddHours(2),
            termsVersion,
            optionReference);

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
        public Exception? Failure { get; init; }
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
                request.IsReturn
                    ? "return-purchase-001"
                    : "purchase-001",
                request.IsReturn
                    ? "return-provider-track-001"
                    : "provider-track-001",
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
