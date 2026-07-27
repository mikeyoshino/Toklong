using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Transactions.ManageDisputeEvidence;
using Toklong.Domain.Common;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Application.Tests.Transactions;

public sealed class DisputeEvidenceManagementTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Authorized_party_upload_is_idempotent_and_audited_once()
    {
        await using var db = Database();
        var transaction = DisputedTransaction();
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        var store = new RecordingEvidenceStore();
        var handler = new SubmitDisputeEvidenceHandler(
            new TransactionRepository(db),
            db,
            new FixedClock(Now),
            store);
        var command = Command(
            transaction,
            transaction.BuyerId,
            null,
            DisputeEvidenceParty.Buyer,
            "mobile-retry-1");

        var first = await handler.Handle(command, default);
        var replay = await handler.Handle(command, default);

        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(1, store.SaveCount);
        Assert.Single(await db.DisputeEvidence.ToListAsync());
        Assert.Single(
            transaction.AuditEvents,
            item => item.Name == "dispute.evidence_submitted");
    }

    [Fact]
    public async Task Account_cannot_upload_as_the_counterparty()
    {
        await using var db = Database();
        var transaction = DisputedTransaction();
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        var store = new RecordingEvidenceStore();
        var handler = new SubmitDisputeEvidenceHandler(
            new TransactionRepository(db),
            db,
            new FixedClock(Now),
            store);

        await Assert.ThrowsAsync<DomainException>(() =>
            handler.Handle(
                Command(
                    transaction,
                    transaction.BuyerId,
                    null,
                    DisputeEvidenceParty.Seller,
                    "impersonation"),
                default));
        Assert.Equal(0, store.SaveCount);
        Assert.Empty(transaction.DisputeEvidence);
    }

    [Fact]
    public async Task Credential_like_description_is_rejected_before_file_storage()
    {
        await using var db = Database();
        var transaction = DisputedTransaction();
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        var store = new RecordingEvidenceStore();
        var handler = new SubmitDisputeEvidenceHandler(
            new TransactionRepository(db),
            db,
            new FixedClock(Now),
            store);
        var command = Command(
            transaction,
            transaction.BuyerId,
            null,
            DisputeEvidenceParty.Buyer,
            "secret",
            "recovery code: 123456");

        await Assert.ThrowsAsync<DomainException>(() =>
            handler.Handle(command, default));
        Assert.Equal(0, store.SaveCount);
        Assert.Empty(transaction.DisputeEvidence);
    }

    private static SubmitDisputeEvidenceCommand Command(
        SaleTransaction transaction,
        Guid? buyerId,
        Guid? sellerId,
        DisputeEvidenceParty party,
        string idempotencyKey,
        string description = "ภาพกล่องและตัวสินค้าหลังรับของ") =>
        new(
            transaction.Id,
            buyerId,
            sellerId,
            party,
            DisputeEvidenceType.Packaging,
            description,
            idempotencyKey,
            new DisputeEvidenceFileInput(
                "evidence.jpg",
                "image/jpeg",
                [1, 2, 3]));

    private static SaleTransaction DisputedTransaction()
    {
        var transitions = new TransactionTransitionService();
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ ทดสอบ",
            "+66811111111",
            "+66822222222",
            FulfillmentType.PhysicalShipment,
            "กล้องพร้อมเลนส์",
            "ใช้งานได้ปกติ",
            ConditionCode.UsedGood,
            "",
            null,
            450_000,
            "mvp-th-2026-07",
            Now.AddHours(-4),
            transitions);
        transaction.AcceptBuyerOffer(
            Guid.NewGuid(),
            "ผู้ขาย ทดสอบ",
            "+66822222222",
            "KBANK",
            "ผู้ขาย ทดสอบ",
            "1234567890",
            true,
            Now.AddHours(-4).AddMinutes(1),
            transitions,
            shipping: TestTransactionFactory.ShippingQuote(
                Now.AddHours(-4).AddMinutes(1)));
        transaction.BeginCheckout(
            "ผู้ซื้อ ทดสอบ",
            "+66811111111",
            "123 กรุงเทพฯ",
            Now.AddHours(-4).AddMinutes(2),
            transitions);
        transaction.ConfirmPayment(
            "payment-evidence-management",
            Now.AddHours(-4).AddMinutes(3),
            transitions);
        transaction.SubmitTracking(
            transaction.SellerAccessToken,
            "FLASH",
            "TH12345678",
            Now.AddHours(-4).AddMinutes(4),
            transitions);
        transaction.RecordCarrierEvent(
            "delivery-evidence-management",
            "delivered",
            Now.AddHours(-3),
            Now.AddHours(-3),
            transitions);
        transaction.OpenDispute(
            transaction.BuyerAccessToken!,
            DisputeReason.NotAsDescribed,
            "สินค้าไม่ตรงตามรายละเอียด",
            Now.AddHours(-2),
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

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class RecordingEvidenceStore
        : IDisputeEvidenceStore
    {
        public int SaveCount { get; private set; }

        public Task<StoredDisputeEvidenceFile> SaveImageAsync(
            DisputeEvidenceFileInput input,
            CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.FromResult(
                new StoredDisputeEvidenceFile(
                    $"evidence:{SaveCount}.bin",
                    "image/jpeg",
                    512,
                    new string('a', 64)));
        }

        public Task<DisputeEvidenceFileContent> ReadAsync(
            string storageReference,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            string storageReference,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
