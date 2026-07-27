using Microsoft.EntityFrameworkCore;
using Toklong.Application.Common;
using Toklong.Application.Features.Refunds.ProcessRefunds;
using Toklong.Domain.Common;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Application.Tests.Refunds;

public sealed class DisputeResolutionApprovalTests
{
    [Fact]
    public async Task Distinct_human_approval_is_audited_and_replay_safe()
    {
        await using var database = Database();
        var transaction = DisputedTransaction();
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        var recommendedBy = Guid.NewGuid();
        var approvedBy = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var command = new ResolveDisputeCommand(
            transaction.Id,
            "CRM-CASE-ACTION",
            DisputeResolution.FullRefund,
            new DisputeDecisionAudit(
                Guid.NewGuid(),
                actionId,
                recommendedBy,
                approvedBy,
                "MATERIALLY_NOT_AS_DESCRIBED",
                "ภาพหลักฐานตรงกับรายละเอียดปัญหา",
                $"crm-dispute-resolution:{actionId:N}"));
        var handler = Handler(database);

        await handler.Handle(
            command,
            CancellationToken.None);
        await handler.Handle(
            command,
            CancellationToken.None);

        Assert.Equal(
            TransactionState.RefundPending,
            transaction.State);
        Assert.Equal(
            1,
            transaction.AuditEvents.Count(item =>
                item.Name ==
                "dispute.resolved_for_buyer"));
        var audit = Assert.Single(
            transaction.AuditEvents,
            item => item.Name ==
                    "dispute.resolved_for_buyer");
        Assert.Equal(
            approvedBy.ToString("N"),
            audit.ActorId);
        Assert.Contains(
            actionId.ToString(),
            audit.MetadataJson,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            transaction.Notifications,
            item => item.Template == "refund_started" &&
                    item.Audience == "buyer");
        Assert.Contains(
            transaction.Notifications,
            item =>
                item.Template ==
                    "dispute_resolved_for_buyer" &&
                item.Audience == "seller");
    }

    [Fact]
    public async Task Same_recommender_and_approver_is_rejected()
    {
        await using var database = Database();
        var transaction = DisputedTransaction();
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        var actor = Guid.NewGuid();

        await Assert.ThrowsAsync<DomainException>(
            () => Handler(database).Handle(
                new ResolveDisputeCommand(
                    transaction.Id,
                    "CRM-INVALID",
                    DisputeResolution.FullPayout,
                    new DisputeDecisionAudit(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        actor,
                        actor,
                        "ITEM_NOT_RECEIVED",
                        "ทดสอบ",
                        $"crm:{Guid.NewGuid():N}")),
                CancellationToken.None));

        Assert.Equal(
            TransactionState.Disputed,
            transaction.State);
    }

    private static ResolveDisputeHandler Handler(
        ToklongDbContext database) =>
        new(
            new TransactionRepository(database),
            database,
            new FixedClock(
                DateTimeOffset.UtcNow.AddMinutes(1)),
            new TransactionTransitionService());

    private static SaleTransaction DisputedTransaction()
    {
        var now = DateTimeOffset.UtcNow.AddHours(-5);
        var transitions = new TransactionTransitionService();
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ ทดสอบ",
            "+66811111111",
            FulfillmentType.PhysicalShipment,
            "กล้องพร้อมเลนส์",
            "ใช้งานปกติ มีรอยตามรูป",
            ConditionCode.UsedDefects,
            "มีรอยด้านข้าง",
            null,
            450_000,
            "mvp-th-2026-07",
            now,
            transitions);
        transaction.AcceptBuyerOffer(
            Guid.NewGuid(),
            "ผู้ขาย ทดสอบ",
            "+66822222222",
            "KBANK",
            "ผู้ขาย ทดสอบ",
            "1234567890",
            true,
            now.AddMinutes(1),
            transitions,
            shipping: TestTransactionFactory.ShippingQuote(
                now.AddMinutes(1)));
        transaction.BeginCheckout(
            "ผู้ซื้อ ทดสอบ",
            "+66811111111",
            "123 กรุงเทพฯ",
            now.AddMinutes(2),
            transitions);
        transaction.ConfirmPayment(
            "payment-dispute-approval",
            now.AddMinutes(3),
            transitions);
        transaction.SubmitTracking(
            transaction.SellerAccessToken,
            "FLASH",
            "TH12345678",
            now.AddMinutes(4),
            transitions);
        transaction.RecordCarrierEvent(
            "delivery-dispute-approval",
            "delivered",
            now.AddHours(1),
            now.AddHours(1),
            transitions);
        transaction.OpenDispute(
            transaction.BuyerAccessToken!,
            DisputeReason.NotAsDescribed,
            "สินค้าไม่ตรงตามรายละเอียด",
            now.AddHours(2),
            transitions);
        return transaction;
    }

    private static ToklongDbContext Database()
    {
        var options =
            new DbContextOptionsBuilder<ToklongDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
        return new ToklongDbContext(options);
    }

    private sealed class FixedClock(DateTimeOffset now)
        : Toklong.Application.Abstractions.IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
