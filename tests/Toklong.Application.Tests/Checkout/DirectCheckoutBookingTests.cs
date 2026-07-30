using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Features.Checkout.BookShipmentForPayment;
using Toklong.Application.Features.Shipping;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Tests.Checkout;

public sealed class DirectCheckoutBookingTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Direct_booking_uses_attempt_reference_and_persists_success()
    {
        var fixture = CreateFixture();

        var result = await fixture.Handler.BookAsync(
            fixture.Transaction,
            fixture.BuyerId,
            "checkout-direct-001",
            default);

        Assert.Equal(DirectBookingState.Ready, result.State);
        Assert.Equal(
            fixture.Attempts.Stored!.ProviderReference,
            fixture.Provider.LastRequest!
                .OperationReference);
        Assert.Equal(
            BookingAttemptStatus.Succeeded,
            fixture.Attempts.Stored.Status);
        Assert.True(
            fixture.Transaction
                .ParcelProtectionBookingReady);
    }

    [Fact]
    public async Task Provider_timeout_marks_attempt_and_does_not_replay()
    {
        var fixture = CreateFixture();
        fixture.Provider.Exception =
            new ShipmentMutationException(
                ShipmentMutationOutcome.OutcomeUnknown,
                "shippop-timeout");

        var result = await fixture.Handler.BookAsync(
            fixture.Transaction,
            fixture.BuyerId,
            "checkout-timeout-001",
            default);
        var repeated = await fixture.Handler.BookAsync(
            fixture.Transaction,
            fixture.BuyerId,
            "checkout-timeout-001",
            default);

        Assert.Equal(
            DirectBookingState.TimedOut,
            result.State);
        Assert.Equal(
            DirectBookingState.TimedOut,
            repeated.State);
        Assert.Equal(1, fixture.Provider.ReserveCalls);
    }

    [Fact]
    public async Task Price_mismatch_fails_before_booking_is_applied()
    {
        var fixture = CreateFixture();
        fixture.Provider.FeeAdjustmentSatang = 1;

        var result = await fixture.Handler.BookAsync(
            fixture.Transaction,
            fixture.BuyerId,
            "checkout-mismatch-001",
            default);

        Assert.Equal(
            DirectBookingState.ReconfirmationRequired,
            result.State);
        Assert.False(
            fixture.Transaction
                .ParcelProtectionBookingReady);
        Assert.Equal(
            BookingAttemptStatus.Failed,
            fixture.Attempts.Stored!.Status);
    }

    private static Fixture CreateFixture()
    {
        var buyerId = Guid.NewGuid();
        var transaction =
            TestTransactionFactory.CreateBuyerOffer(
                buyerId,
                "ผู้ซื้อทดสอบ",
                "0800000000",
                FulfillmentType.PhysicalShipment,
                "กล้อง",
                "กล้องพร้อมเลนส์",
                ConditionCode.UsedGood,
                "ไม่มี",
                null,
                100_000,
                "terms-v1",
                Now,
                new TransactionTransitionService());
        var quote = new AcceptedShippingQuote(
            TestTransactionFactory
                .ShippingOriginAddress,
            TestTransactionFactory
                .DeliveryProvinceName,
            TestTransactionFactory
                .DeliveryPostalCode,
            1_200,
            20,
            30,
            15,
            "development-shipping",
            "quote-001",
            "THAIPOST",
            "EMST",
            "EMS",
            5_000,
            0,
            0,
            null,
            Now.AddHours(2),
            TestTransactionFactory
                .DeliveryDistrictName,
            TestTransactionFactory
                .DeliverySubdistrictName,
            OriginAddressLine:
                TestTransactionFactory
                    .ShippingOriginAddress);
        transaction.AcceptBuyerOffer(
            Guid.NewGuid(),
            "ผู้ขายทดสอบ",
            "0811111111",
            "KBANK",
            "ผู้ขายทดสอบ",
            "1234567890",
            true,
            Now,
            new TransactionTransitionService(),
            sellerExpectedNetSatang: 100_000,
            feePolicyVersion: "fee-v1",
            shipping: quote);
        transaction.RecordParcelProtectionElection(
            buyerId,
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
                Now.AddHours(1)),
            Now.AddMinutes(1));
        var shipment = ManagedShipment.CreateOutbound(
            transaction.Id,
            new ManagedShipmentDraft(
                "development-shipping",
                $"origin:{transaction.Id:N}",
                $"destination:{transaction.Id:N}",
                transaction.ProductName,
                1_200,
                20,
                30,
                15,
                "THAIPOST",
                "EMST",
                "EMS",
                5_000,
                0,
                0,
                null,
                "quote-001",
                Now.AddHours(2),
                "parcel-protection-included-v1",
                null,
                ParcelProtectionElectionStatus.Declined,
                0,
                0,
                0),
            Now.AddMinutes(1));
        transaction.QueueBuyerCheckoutShipmentIntent(
            shipment,
            buyerId,
            "choice-direct-001",
            Now.AddMinutes(1));
        var attempts = new FakeAttempts();
        var provider = new FakeProvider();
        var handler =
            new BookShipmentForPaymentHandler(
                null!,
                attempts,
                provider,
                new FakeUnitOfWork(),
                new FixedClock(
                    Now.AddMinutes(2)));
        return new(
            transaction,
            buyerId,
            attempts,
            provider,
            handler);
    }

    private sealed record Fixture(
        SaleTransaction Transaction,
        Guid BuyerId,
        FakeAttempts Attempts,
        FakeProvider Provider,
        BookShipmentForPaymentHandler Handler);

    private sealed class FakeAttempts
        : IBookingAttemptRepository
    {
        public BookingAttempt? Stored { get; private set; }

        public Task<AcquireBookingAttemptResult>
            AcquireAsync(
                AcquireBookingAttempt request,
                CancellationToken cancellationToken)
        {
            if (Stored is not null)
                return Task.FromResult(new
                    AcquireBookingAttemptResult(
                        Stored,
                        Stored.Status switch
                        {
                            BookingAttemptStatus
                                .CallingProvider =>
                                BookingAttemptAcquireState
                                    .InProgress,
                            BookingAttemptStatus
                                .Succeeded =>
                                BookingAttemptAcquireState
                                    .Succeeded,
                            BookingAttemptStatus
                                .Failed =>
                                BookingAttemptAcquireState
                                    .Failed,
                            BookingAttemptStatus
                                .TimedOut =>
                                BookingAttemptAcquireState
                                    .TimedOut,
                            _ => throw new
                                InvalidOperationException()
                        }));
            Stored = BookingAttempt.Create(
                request.TransactionId,
                request.ManagedShipmentId,
                request.BuyerId,
                request.IdempotencyKey,
                request.RequestFingerprint,
                1,
                request.Now);
            Stored.Claim(request.Now);
            return Task.FromResult(new
                AcquireBookingAttemptResult(
                    Stored,
                    BookingAttemptAcquireState
                        .Acquired));
        }

        public Task<BookingAttempt?> GetAsync(
            Guid transactionId,
            string idempotencyKey,
            CancellationToken cancellationToken) =>
            Task.FromResult(Stored);
    }

    private sealed class FakeProvider
        : IShipmentProvider
    {
        public string ProviderName =>
            "development-shipping";
        public int ReserveCalls { get; private set; }
        public long FeeAdjustmentSatang { get; set; }
        public Exception? Exception { get; set; }
        public ShipmentReservationRequest?
            LastRequest { get; private set; }

        public Task<ShipmentReservation> ReserveAsync(
            ShipmentReservationRequest request,
            CancellationToken cancellationToken)
        {
            ReserveCalls++;
            LastRequest = request;
            if (Exception is not null)
                throw Exception;
            return Task.FromResult(
                new ShipmentReservation(
                    ProviderName,
                    "purchase-1",
                    "provider-track-1",
                    "courier-track-1",
                    request.Quote.CarrierCode,
                    request.Quote.ServiceCode,
                    request.Quote.FeeSatang +
                    FeeAdjustmentSatang,
                    request.Quote
                        .InsuranceFeeSatang,
                    request.Quote
                        .DeclaredValueSatang,
                    request.Quote.InsuranceCode,
                    Now.AddMinutes(2)));
        }

        public Task<ShipmentTrackingUpdate>
            GetTrackingAsync(
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

    private sealed class FakeUnitOfWork
        : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(1);
    }

    private sealed class FixedClock(
        DateTimeOffset now)
        : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
