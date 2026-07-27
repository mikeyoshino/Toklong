using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Transactions.EvaluateDueExpirations;
using Toklong.Domain.Buyers;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Application.Tests.Transactions;

public sealed class OfferExpirationTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 7, 25, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Deadline_job_expires_only_due_unpaid_offers()
    {
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ToklongDbContext(options);
        var repository = new TransactionRepository(db);
        var transitions = new TransactionTransitionService();
        var due = Offer(Start, transitions);
        var notDue = Offer(Start.AddHours(23), transitions);
        await repository.AddAsync(due, default);
        await repository.AddAsync(notDue, default);
        await db.SaveChangesAsync();

        var handler = new EvaluateDueExpirationsHandler(
            repository,
            db,
            new FixedClock(Start.AddHours(24)),
            transitions);

        var changed = await handler.Handle(
            new EvaluateDueExpirationsCommand(),
            default);

        Assert.Equal(1, changed);
        Assert.Equal(TransactionState.Expired, due.State);
        Assert.Equal(
            TransactionExpirationReason.SellerDidNotRespond,
            due.ExpirationReason);
        Assert.Equal(
            TransactionState.AwaitingSellerAcceptance,
            notDue.State);
    }

    private static SaleTransaction Offer(
        DateTimeOffset now,
        TransactionTransitionService transitions) =>
        TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ ทดสอบ",
            "+66811111111",
            FulfillmentType.PhysicalShipment,
            "รายละเอียดสินค้า",
            "กล้องพร้อมเลนส์ ใช้งานได้ปกติ",
            ConditionCode.UsedGood,
            "ไม่มี",
            "https://example.com/photo.jpg",
            450_000,
            "mvp-th-2026-07",
            now,
            transitions);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
