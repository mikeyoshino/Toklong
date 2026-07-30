using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Checkout.ChooseParcelProtection;
using Toklong.Application.Features.Checkout.GetParcelProtection;
using Toklong.Application.Pricing;
using Toklong.Domain.Common;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Application.Tests.Checkout;

public sealed class ParcelProtectionChangeTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Buyer_can_change_a_pending_booking_intent_before_payment()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Handler.Handle(fixture.ChooseAccepted(), default);

        var result = await fixture.Handler.Handle(
            fixture.ChooseDeclined("change-before-claim-01"), default);

        Assert.Equal("preparing_shipping", result.BookingStatus);
        Assert.Equal(ShippingOperationStatus.Superseded,
            fixture.Transaction.ShippingOperations.First().Status);
        Assert.Equal(ParcelProtectionElectionStatus.Declined,
            fixture.Transaction.ParcelProtectionElection);
        Assert.Equal(0, fixture.Transaction.ParcelInsuranceFeeSatang);
        Assert.Equal(2, fixture.Transaction.ManagedShipments.Count);
    }

    [Fact]
    public async Task Buyer_cannot_change_after_payment_intent_exists()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Handler.Handle(fixture.ChooseAccepted(), default);
        fixture.Transaction.BeginCheckout(
            "ผู้ซื้อทดสอบ", "0800000000", Now.AddMinutes(2),
            new TransactionTransitionService(), "manual-bank", "pi_test", 0, 0,
            fixture.Transaction.PriceSatang, "fee-v1");

        await Assert.ThrowsAsync<DomainException>(() => fixture.Handler.Handle(
            fixture.ChooseDeclined("change-too-late-001"), default));
    }

    [Fact]
    public async Task Buyer_change_after_reservation_queues_cancellation_before_rebooking()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Handler.Handle(fixture.ChooseAccepted(), default);
        var shipment = fixture.Transaction.CurrentOutboundShipment!;
        shipment.RecordReservation("purchase-001", "provider-001", null, Now.AddMinutes(2));
        var booking = Assert.Single(fixture.Transaction.ShippingOperations);
        booking.Claim("worker-a", Now.AddMinutes(2), TimeSpan.FromMinutes(5));
        booking.Succeed("worker-a", "purchase-001", "provider-001", Now.AddMinutes(2));

        var result = await fixture.Handler.Handle(
            fixture.ChooseDeclined("change-after-reserve-01"), default);

        Assert.Equal("cancelling_shipping", result.BookingStatus);
        Assert.Equal(ParcelProtectionElectionStatus.Accepted,
            fixture.Transaction.ParcelProtectionElection);
        Assert.Single(fixture.Transaction.ParcelProtectionChangeRequests);
        Assert.Contains(fixture.Transaction.ShippingOperations, operation =>
            operation.OperationType == ShippingOperationType.CancelOutbound &&
            operation.Status == ShippingOperationStatus.Pending);
        Assert.Single(fixture.Transaction.ManagedShipments);
    }

    private sealed class Fixture(
        ToklongDbContext database,
        SaleTransaction transaction,
        MutableClock clock,
        ProtectionProvider provider)
        : IAsyncDisposable
    {
        public ToklongDbContext Database { get; } = database;
        public SaleTransaction Transaction { get; } = transaction;
        public ChooseParcelProtectionHandler Handler { get; } = new(
            new TransactionRepository(database), provider,
            new ParcelProtectionPricingPolicy(), database, clock);

        public static async Task<Fixture> CreateAsync()
        {
            var database = new ToklongDbContext(
                new DbContextOptionsBuilder<ToklongDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var buyerId = Guid.NewGuid();
            var transaction = TestTransactionFactory.CreateBuyerOffer(
                buyerId, "ผู้ซื้อทดสอบ", "0800000000",
                FulfillmentType.PhysicalShipment, "กล้อง", "กล้องพร้อมเลนส์",
                ConditionCode.UsedGood, "ไม่มี", null, 450_000, "terms-v1", Now,
                new TransactionTransitionService());
            transaction.AcceptBuyerOffer(
                Guid.NewGuid(), "ผู้ขายทดสอบ", "0811111111", "KBANK",
                "ผู้ขายทดสอบ", "1234567890", true, Now,
                new TransactionTransitionService(), 0, 0, 450_000, "fee-v1",
                new AcceptedShippingQuote(
                    TestTransactionFactory.ShippingOriginAddress,
                    TestTransactionFactory.DeliveryProvinceName,
                    TestTransactionFactory.DeliveryPostalCode,
                    1_200, 20, 30, 15, "development-shipping", "quote-001",
                    "THAIPOST", "EMST", "EMS", 5_000, 0, 0, null,
                    Now.AddHours(2), TestTransactionFactory.DeliveryDistrictName,
                    TestTransactionFactory.DeliverySubdistrictName,
                    OriginAddressLine: TestTransactionFactory.ShippingOriginAddress));
            database.Transactions.Add(transaction);
            await database.SaveChangesAsync();
            return new Fixture(database, transaction,
                new MutableClock(Now.AddMinutes(1)), new ProtectionProvider());
        }

        public ChooseParcelProtectionCommand ChooseAccepted() => new(
            Transaction.Id, Transaction.BuyerId!.Value, true, "protected-option",
            6_000, "choose-accepted-001");

        public ChooseParcelProtectionCommand ChooseDeclined(string key) => new(
            Transaction.Id, Transaction.BuyerId!.Value, false, null, null, key);

        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }

    private sealed class MutableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class ProtectionProvider : IParcelProtectionQuoteProvider
    {
        private static readonly ProviderParcelProtectionOption Option = new(
            "development-shipping", "protected-option", 100_000, 450_000,
            4_500, "parcel-protection-v1", "DEV_FULL_VALUE", Now,
            Now.AddHours(1));

        public Task<ParcelProtectionAvailability> GetAvailabilityAsync(
            ParcelProtectionQuoteRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ParcelProtectionAvailability(100_000, Option, true));

        public Task<ProviderParcelProtectionOption> ValidateOptionAsync(
            ParcelProtectionQuoteRequest request, string optionReference,
            CancellationToken cancellationToken) => Task.FromResult(Option);
    }
}
