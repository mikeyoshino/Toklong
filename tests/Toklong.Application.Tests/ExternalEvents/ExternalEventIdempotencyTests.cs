using Microsoft.EntityFrameworkCore;
using Toklong.Application.Features.ExternalEvents;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Application.Tests.ExternalEvents;

public sealed class ExternalEventIdempotencyTests
{
    [Fact]
    public async Task Duplicate_payment_event_produces_one_transition_and_one_audit()
    {
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ToklongDbContext(options);
        var repository = new TransactionRepository(db);
        var transitions = new TransactionTransitionService();
        var start = new DateTimeOffset(2026, 7, 20, 7, 0, 0, TimeSpan.Zero);
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ", "buyer@example.com",
            FulfillmentType.PhysicalShipment,
            "กล้องฟิล์ม", "ใช้งานได้ปกติพร้อมสายคล้อง",
            ConditionCode.UsedGood, "มีรอยเล็กน้อย",
            "https://example.com/photo.jpg",
            450_000, "mvp-th-2026-07", start, transitions);
        transaction.AcceptBuyerOffer(
            Guid.NewGuid(),
            "ผู้ขาย", "line:seller", "KBANK", "ผู้ขาย ทดสอบ", "1234567890",
            true, start.AddMinutes(1), transitions,
            shipping: TestTransactionFactory.ShippingQuote(
                start.AddMinutes(1)));
        transaction.BeginCheckout("ผู้ซื้อ", "buyer@example.com", "กรุงเทพฯ ประเทศไทย", start.AddMinutes(2), transitions);
        await repository.AddAsync(transaction, default);
        await db.SaveChangesAsync();

        var handler = new ConfirmManualPaymentHandler(repository, db, transitions);
        var command = new ConfirmManualPaymentCommand(transaction.Id, "bank-event-001", start.AddMinutes(5));

        var first = await handler.Handle(command, default);
        var duplicate = await handler.Handle(command, default);

        Assert.False(first.AlreadyProcessed);
        Assert.True(duplicate.AlreadyProcessed);
        Assert.Equal(TransactionState.PaidAwaitingShipment, transaction.State);
        Assert.Single(transaction.AuditEvents, x => x.Name == "payment.confirmed");
        Assert.Single(transaction.ExternalEvents, x => x.EventId == "bank-event-001");
    }

    [Fact]
    public async Task Duplicate_stripe_event_is_replay_safe_and_wrong_intent_is_rejected()
    {
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ToklongDbContext(options);
        var repository = new TransactionRepository(db);
        var transitions = new TransactionTransitionService();
        var start = new DateTimeOffset(
            2026,
            7,
            20,
            7,
            0,
            0,
            TimeSpan.Zero);
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ",
            "+66800000000",
            FulfillmentType.PhysicalShipment,
            "กล้องฟิล์ม",
            "ใช้งานได้ปกติพร้อมสายคล้อง",
            ConditionCode.UsedGood,
            "มีรอยเล็กน้อย",
            "https://example.com/photo.jpg",
            450_000,
            "mvp-th-2026-07",
            start,
            transitions);
        transaction.AcceptBuyerOffer(
            Guid.NewGuid(),
            "ผู้ขาย",
            "+66811111111",
            "KBANK",
            "ผู้ขาย ทดสอบ",
            "1234567890",
            true,
            start.AddMinutes(1),
            transitions,
            0,
            10_000,
            440_000,
            "fee-v1",
            TestTransactionFactory.ShippingQuote(
                start.AddMinutes(1)));
        transaction.BeginCheckout(
            "ผู้ซื้อ",
            "+66800000000",
            "กรุงเทพฯ ประเทศไทย",
            start.AddMinutes(2),
            transitions,
            "stripe",
            "pi_toklong_001",
            10_000,
            440_000,
            "fee-v1");
        await repository.AddAsync(transaction, default);
        await db.SaveChangesAsync();
        var handler = new ConfirmStripePaymentHandler(
            repository,
            db,
            new FixedClock(start.AddMinutes(5)),
            transitions);

        await Assert.ThrowsAsync<Toklong.Domain.Common.DomainException>(() =>
            handler.Handle(
                new ConfirmStripePaymentCommand(
                    transaction.Id,
                    "evt_wrong",
                    "pi_other",
                    450_000,
                    "thb",
                    start.AddMinutes(4)),
                default));
        await Assert.ThrowsAsync<Toklong.Domain.Common.DomainException>(() =>
            handler.Handle(
                new ConfirmStripePaymentCommand(
                    transaction.Id,
                    "evt_wrong_amount",
                    "pi_toklong_001",
                    1,
                    "thb",
                    start.AddMinutes(4)),
                default));

        var command = new ConfirmStripePaymentCommand(
            transaction.Id,
            "evt_stripe_001",
            "pi_toklong_001",
            455_000,
            "thb",
            start.AddMinutes(4));
        var first = await handler.Handle(command, default);
        var replay = await handler.Handle(command, default);

        Assert.False(first.AlreadyProcessed);
        Assert.True(replay.AlreadyProcessed);
        Assert.Equal(
            TransactionState.PaidAwaitingShipment,
            transaction.State);
        Assert.Single(
            transaction.AuditEvents,
            audit => audit.Name == "payment.confirmed");
        Assert.Single(
            transaction.ExternalEvents,
            external => external.Provider == "stripe");
    }

    private sealed class FixedClock(DateTimeOffset now)
        : Toklong.Application.Abstractions.IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
