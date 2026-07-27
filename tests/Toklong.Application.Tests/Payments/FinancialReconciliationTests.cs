using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Payments.ReconcilePayments;
using Toklong.Application.Features.Refunds.ProcessRefunds;
using Toklong.Domain.Common;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Application.Tests.Payments;

public sealed class FinancialReconciliationTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 7, 27, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Payment_reconciliation_uses_provider_time_and_is_replay_safe()
    {
        await using var database = Database();
        var transaction = PendingPayment();
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        var confirmedAt = Start.AddMinutes(10);
        var provider = new PaymentProvider(
            new PaymentReconciliationResult(
                true,
                "stripe-reconcile:pi_reconcile:ch_001",
                transaction.BuyerTotalSatang,
                transaction.Currency,
                confirmedAt));
        var handler = new ReconcilePendingPaymentsHandler(
            new TransactionRepository(database),
            provider,
            database,
            new FixedClock(Start.AddMinutes(20)),
            new TransactionTransitionService());

        var first = await handler.Handle(
            new ReconcilePendingPaymentsCommand(),
            default);
        var replay = await handler.Handle(
            new ReconcilePendingPaymentsCommand(),
            default);

        Assert.Equal(1, first);
        Assert.Equal(0, replay);
        Assert.Equal(transaction.Id, provider.TransactionId);
        Assert.Equal("pi_reconcile", provider.PaymentReference);
        Assert.Equal(confirmedAt, transaction.PaymentConfirmedAt);
        Assert.Equal(confirmedAt.AddHours(72), transaction.ShipByAt);
        Assert.Equal(
            TransactionState.PaidAwaitingShipment,
            transaction.State);
        Assert.Single(
            transaction.ExternalEvents,
            item => item.EventId ==
                    "stripe-reconcile:pi_reconcile:ch_001");
    }

    [Fact]
    public async Task Refund_instruction_uses_complete_immutable_buyer_total()
    {
        await using var database = Database();
        var transaction = PendingRefund();
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        var provider = new RefundCreationProvider();
        var handler = new CreatePendingRefundsHandler(
            new TransactionRepository(database),
            provider,
            database,
            new FixedClock(Start.AddHours(2)));

        var changed = await handler.Handle(
            new CreatePendingRefundsCommand(),
            default);

        Assert.Equal(1, changed);
        Assert.Equal(transaction.Id, provider.TransactionId);
        Assert.Equal(
            transaction.BuyerTotalSatang,
            provider.AmountSatang);
        Assert.Equal(110_900, provider.AmountSatang);
        Assert.Equal("THB", provider.Currency);
        Assert.Equal("re_reconcile", transaction.RefundReference);
        Assert.Equal(TransactionState.RefundPending, transaction.State);
    }

    [Fact]
    public async Task Refund_creation_response_records_requires_action_immediately()
    {
        await using var database = Database();
        var transaction = PendingRefund();
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        var expiresAt = Start.AddDays(45);
        var handler = new CreatePendingRefundsHandler(
            new TransactionRepository(database),
            new RefundCreationProvider(
                "requires_action",
                expiresAt,
                Start.AddHours(2)),
            database,
            new FixedClock(Start.AddHours(2)));

        var changed = await handler.Handle(
            new CreatePendingRefundsCommand(),
            default);
        var replay = await handler.Handle(
            new CreatePendingRefundsCommand(),
            default);

        Assert.Equal(1, changed);
        Assert.Equal(0, replay);
        Assert.Equal(
            "requires_action",
            transaction.RefundProviderStatus);
        Assert.Equal(
            expiresAt,
            transaction.RefundActionExpiresAt);
        Assert.Single(
            transaction.Notifications,
            item => item.Template ==
                    "refund_action_required");
    }

    [Fact]
    public async Task Refund_reconciliation_confirms_once_after_matching_provider_result()
    {
        await using var database = Database();
        var transaction = PendingRefund();
        transaction.RecordRefundInstruction(
            "stripe",
            "re_reconcile",
            Start.AddHours(2));
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        var refundedAt = Start.AddHours(3);
        var provider = new RefundProvider(
            Result(
                transaction,
                true,
                refundedAt));
        var handler = new ReconcilePendingRefundsHandler(
            new TransactionRepository(database),
            provider,
            database,
            new FixedClock(Start.AddHours(4)),
            new TransactionTransitionService());

        var first = await handler.Handle(
            new ReconcilePendingRefundsCommand(),
            default);
        var replay = await handler.Handle(
            new ReconcilePendingRefundsCommand(),
            default);

        Assert.Equal(1, first);
        Assert.Equal(0, replay);
        Assert.Equal(transaction.Id, provider.TransactionId);
        Assert.Equal("re_reconcile", provider.RefundReference);
        Assert.Equal(TransactionState.Refunded, transaction.State);
        Assert.Equal(
            "succeeded",
            transaction.RefundProviderStatus);
        Assert.Equal(refundedAt, transaction.RefundConfirmedAt);
        Assert.Single(
            transaction.ExternalEvents,
            item => item.EventId ==
                    "stripe-refund-reconcile:re_reconcile:succeeded");
    }

    [Fact]
    public async Task Refund_reconciliation_rejects_mismatched_amount()
    {
        await using var database = Database();
        var transaction = PendingRefund();
        transaction.RecordRefundInstruction(
            "stripe",
            "re_reconcile",
            Start.AddHours(2));
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        var result = Result(
            transaction,
            true,
            Start.AddHours(3)) with
        {
            AmountSatang = transaction.BuyerTotalSatang - 1
        };
        var handler = new ReconcilePendingRefundsHandler(
            new TransactionRepository(database),
            new RefundProvider(result),
            database,
            new FixedClock(Start.AddHours(4)),
            new TransactionTransitionService());

        await Assert.ThrowsAsync<DomainException>(() =>
            handler.Handle(
                new ReconcilePendingRefundsCommand(),
                default));

        Assert.Equal(TransactionState.RefundPending, transaction.State);
        Assert.Null(transaction.RefundConfirmedAt);
        Assert.DoesNotContain(
            transaction.ExternalEvents,
            item => item.EventId ==
                    "stripe-refund-reconcile:re_reconcile:succeeded");
    }

    [Fact]
    public async Task Requires_action_refund_is_recorded_without_marking_transaction_refunded()
    {
        await using var database = Database();
        var transaction = PendingRefund();
        transaction.RecordRefundInstruction(
            "stripe",
            "re_reconcile",
            Start.AddHours(2));
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        var handler = new ReconcilePendingRefundsHandler(
            new TransactionRepository(database),
            new RefundProvider(
                Result(
                    transaction,
                    false,
                    Start.AddHours(3)) with
                {
                    Status = "requires_action",
                    EventId =
                        "stripe-refund-reconcile:re_reconcile:requires_action"
                }),
            database,
            new FixedClock(Start.AddHours(4)),
            new TransactionTransitionService());

        var changed = await handler.Handle(
            new ReconcilePendingRefundsCommand(),
            default);

        Assert.Equal(1, changed);
        Assert.Equal(TransactionState.RefundPending, transaction.State);
        Assert.Equal(
            "requires_action",
            transaction.RefundProviderStatus);
        Assert.Null(transaction.RefundConfirmedAt);
        Assert.Single(
            transaction.Notifications,
            item => item.Template ==
                    "refund_action_required");
        Assert.Single(
            transaction.ExternalEvents,
            item => item.EventType ==
                    "refund.requires_action");
    }

    [Fact]
    public async Task Refund_action_notification_is_once_per_action_cycle()
    {
        await using var database = Database();
        var transaction = PendingRefund();
        transaction.RecordRefundInstruction(
            "stripe",
            "re_reconcile",
            Start.AddHours(2));
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        var expiresAt = Start.AddDays(45);

        async Task<int> ReconcileAsync(
            string status,
            string eventId)
        {
            var result = Result(
                transaction,
                false,
                Start.AddHours(3)) with
            {
                Status = status,
                EventId = eventId,
                ActionExpiresAt =
                    status == "requires_action"
                        ? expiresAt
                        : null,
                InstructionsSentAt =
                    status == "requires_action"
                        ? Start.AddHours(3)
                        : null
            };
            return await new ReconcilePendingRefundsHandler(
                    new TransactionRepository(database),
                    new RefundProvider(result),
                    database,
                    new FixedClock(Start.AddHours(4)),
                    new TransactionTransitionService())
                .Handle(
                    new ReconcilePendingRefundsCommand(),
                    default);
        }

        Assert.Equal(
            1,
            await ReconcileAsync(
                "requires_action",
                "refund-action-1"));
        Assert.Equal(
            0,
            await ReconcileAsync(
                "requires_action",
                "refund-action-1"));
        Assert.Equal(
            1,
            await ReconcileAsync(
                "pending",
                "refund-pending-1"));
        Assert.Equal(
            1,
            await ReconcileAsync(
                "requires_action",
                "refund-action-2"));

        Assert.Equal(
            "requires_action",
            transaction.RefundProviderStatus);
        Assert.Equal(
            expiresAt,
            transaction.RefundActionExpiresAt);
        Assert.Equal(
            2,
            transaction.Notifications.Count(item =>
                item.Template ==
                "refund_action_required"));
        Assert.All(
            transaction.Notifications.Where(item =>
                item.Template ==
                "refund_action_required"),
            item => Assert.Null(item.Detail));
    }

    private static SaleTransaction PendingPayment()
    {
        var transitions = new TransactionTransitionService();
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ ทดสอบ",
            "+66811111111",
            FulfillmentType.PhysicalShipment,
            "กล้องทดสอบ",
            "กล้องพร้อมอุปกรณ์ตามภาพ",
            ConditionCode.UsedGood,
            "ไม่มี",
            null,
            100_000,
            "mvp-th-2026-07",
            Start,
            transitions);
        transaction.AcceptBuyerOffer(
            Guid.NewGuid(),
            "ผู้ขาย ทดสอบ",
            "+66822222222",
            "KBANK",
            "ผู้ขาย ทดสอบ",
            "1234567890",
            true,
            Start.AddMinutes(1),
            transitions,
            5_900,
            0,
            100_000,
            "buyer-protection-v2",
            TestTransactionFactory.ShippingQuote(
                Start.AddMinutes(1)));
        transaction.BeginCheckout(
            "ผู้ซื้อ ทดสอบ",
            "+66811111111",
            Start.AddMinutes(2),
            transitions,
            "stripe",
            "pi_reconcile",
            5_900,
            0,
            100_000,
            "buyer-protection-v2");
        return transaction;
    }

    private static SaleTransaction PendingRefund()
    {
        var transaction = PendingPayment();
        var confirmedAt =
            transaction.BuyerPaymentDeadlineAt!.Value
                .AddSeconds(1);
        transaction.ConfirmStripePayment(
            "evt_late_reconcile",
            "pi_reconcile",
            transaction.BuyerTotalSatang,
            transaction.Currency,
            confirmedAt,
            confirmedAt.AddSeconds(1),
            new TransactionTransitionService());
        return transaction;
    }

    private static RefundReconciliationResult Result(
        SaleTransaction transaction,
        bool succeeded,
        DateTimeOffset occurredAt) =>
        new(
            succeeded,
            "stripe-refund-reconcile:re_reconcile:succeeded",
            "re_reconcile",
            "pi_reconcile",
            transaction.BuyerTotalSatang,
            transaction.Currency,
            occurredAt,
            succeeded ? "succeeded" : "pending");

    private static ToklongDbContext Database()
    {
        var options =
            new DbContextOptionsBuilder<ToklongDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
        return new ToklongDbContext(options);
    }

    private sealed class PaymentProvider(
        PaymentReconciliationResult result)
        : IPaymentReconciliationProvider
    {
        public Guid TransactionId { get; private set; }
        public string PaymentReference { get; private set; } = "";

        public Task<PaymentReconciliationResult> ReconcileAsync(
            Guid transactionId,
            string paymentReference,
            CancellationToken cancellationToken)
        {
            TransactionId = transactionId;
            PaymentReference = paymentReference;
            return Task.FromResult(result);
        }
    }

    private sealed class RefundCreationProvider(
        string status = "pending",
        DateTimeOffset? actionExpiresAt = null,
        DateTimeOffset? instructionsSentAt = null)
        : IRefundProvider
    {
        public Guid TransactionId { get; private set; }
        public long AmountSatang { get; private set; }
        public string Currency { get; private set; } = "";

        public Task<RefundPreparation> CreateFullRefundAsync(
            Guid transactionId,
            string paymentReference,
            long amountSatang,
            string currency,
            string? existingRefundReference,
            CancellationToken cancellationToken)
        {
            TransactionId = transactionId;
            AmountSatang = amountSatang;
            Currency = currency;
            return Task.FromResult(
                new RefundPreparation(
                    "re_reconcile",
                    status,
                    actionExpiresAt,
                    instructionsSentAt));
        }
    }

    private sealed class RefundProvider(
        RefundReconciliationResult result)
        : IRefundReconciliationProvider
    {
        public Guid TransactionId { get; private set; }
        public string RefundReference { get; private set; } = "";

        public Task<RefundReconciliationResult> ReconcileAsync(
            Guid transactionId,
            string refundReference,
            CancellationToken cancellationToken)
        {
            TransactionId = transactionId;
            RefundReference = refundReference;
            return Task.FromResult(result);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
