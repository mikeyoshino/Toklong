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
        var transaction = SaleTransaction.CreateAndActivate(
            Guid.NewGuid(),
            "ผู้ขาย", "line:seller", "KBANK", "ผู้ขาย ทดสอบ", "1234567890",
            FulfillmentType.PhysicalShipment,
            "กล้องฟิล์ม", "กล้องและอุปกรณ์", ConditionCode.UsedGood,
            "ใช้งานได้ปกติพร้อมสายคล้อง", "มีรอยเล็กน้อย", "https://example.com/photo.jpg",
            450_000, 6_000, 48, "mvp-th-2026-07", start, transitions);
        transaction.BeginCheckout("ผู้ซื้อ", "0800000000", "กรุงเทพฯ ประเทศไทย", start.AddMinutes(2), transitions);
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
}
