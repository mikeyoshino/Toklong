using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Transactions.ActOnTransaction;
using Toklong.Domain.Common;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Application.Tests.Transactions;

public sealed class MobileTransactionActionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 25, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Authenticated_seller_can_submit_tracking_without_exposing_token()
    {
        await using var db = CreateDatabase();
        var repository = new TransactionRepository(db);
        var transitions = new TransactionTransitionService();
        var sellerId = Guid.NewGuid();
        var transaction = PaidPhysical(sellerId, transitions);
        await repository.AddAsync(transaction, default);
        await db.SaveChangesAsync();

        var result = await new SubmitTrackingForSellerHandler(
            repository,
            db,
            new FixedClock(Now.AddHours(1)),
            transitions).Handle(
            new SubmitTrackingForSellerCommand(
                transaction.Id,
                sellerId,
                "FLASH",
                "TH123456789"),
            default);

        Assert.Equal(TransactionState.TrackingSubmitted, result.State);
        Assert.Equal("FLASH", result.CarrierCode);
        Assert.Contains(
            result.AuditEvents,
            audit => audit.Name == "shipment.tracking_submitted");
    }

    [Fact]
    public async Task Different_seller_is_rejected_before_state_transition()
    {
        await using var db = CreateDatabase();
        var repository = new TransactionRepository(db);
        var transitions = new TransactionTransitionService();
        var transaction = PaidPhysical(Guid.NewGuid(), transitions);
        await repository.AddAsync(transaction, default);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainException>(() =>
            new SubmitTrackingForSellerHandler(
                repository,
                db,
                new FixedClock(Now.AddHours(1)),
                transitions).Handle(
                new SubmitTrackingForSellerCommand(
                    transaction.Id,
                    Guid.NewGuid(),
                    "FLASH",
                    "TH123456789"),
                default));

        Assert.Equal(
            TransactionState.PaidAwaitingShipment,
            transaction.State);
    }

    [Theory]
    [InlineData("OTHER", "TH1234567890")]
    [InlineData("THAIPOST", "1234567890123")]
    [InlineData("FLASH", "SHORT")]
    public async Task Unsupported_carrier_or_invalid_tracking_is_rejected(
        string carrierCode,
        string trackingNumber)
    {
        await using var db = CreateDatabase();
        var repository = new TransactionRepository(db);
        var transitions = new TransactionTransitionService();
        var sellerId = Guid.NewGuid();
        var transaction = PaidPhysical(sellerId, transitions);
        await repository.AddAsync(transaction, default);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainException>(() =>
            new SubmitTrackingForSellerHandler(
                repository,
                db,
                new FixedClock(Now.AddHours(1)),
                transitions).Handle(
                new SubmitTrackingForSellerCommand(
                    transaction.Id,
                    sellerId,
                    carrierCode,
                    trackingNumber),
                default));

        Assert.Equal(
            TransactionState.PaidAwaitingShipment,
            transaction.State);
    }

    private static SaleTransaction PaidPhysical(
        Guid sellerId,
        TransactionTransitionService transitions)
    {
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ ทดสอบ",
            "+66811111111",
            FulfillmentType.PhysicalShipment,
            "กล้องพร้อมเลนส์",
            "ใช้งานปกติ มีรอยตามรูป พร้อมสายคล้อง",
            ConditionCode.UsedDefects,
            "มีรอยด้านข้าง",
            "https://example.com/photo.jpg",
            450_000,
            "mvp-th-2026-07",
            Now,
            transitions);
        transaction.AcceptBuyerOffer(
            sellerId,
            "ผู้ขาย ทดสอบ",
            "+66822222222",
            "KBANK",
            "ผู้ขาย ทดสอบ",
            "1234567890",
            true,
            Now.AddMinutes(1),
            transitions,
            0,
            10_000,
            440_000,
            "test-fee-v1",
            TestTransactionFactory.ShippingQuote(
                Now.AddMinutes(1)));
        transaction.BeginCheckout(
            "ผู้ซื้อ ทดสอบ",
            "+66811111111",
            "123 กรุงเทพฯ",
            Now.AddMinutes(2),
            transitions,
            "stripe",
            "pi_mobile_test",
            10_000,
            440_000,
            "test-fee-v1");
        transaction.ConfirmStripePayment(
            "evt_mobile_test",
            "pi_mobile_test",
            455_000,
            "thb",
            Now.AddMinutes(3),
            Now.AddMinutes(3),
            transitions);
        return transaction;
    }

    private static ToklongDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ToklongDbContext(options);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
