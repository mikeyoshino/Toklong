using Microsoft.EntityFrameworkCore;
using Toklong.Application.Features.Retention.ExecuteRetention;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Application.Tests.Retention;

public sealed class RetentionLifecycleTests
{
    private static readonly DateTimeOffset Start =
        new(2020, 7, 25, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Legal_hold_blocks_due_purge_until_released()
    {
        await using var db = CreateDatabase();
        var transaction = ExpiredOffer();
        transaction.PlaceLegalHold(
            "CASE-001",
            "อยู่ระหว่างกระบวนการทางกฎหมาย",
            transaction.RetentionStartsAt!.Value
                .AddDays(1));
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        var now = transaction.RetentionExpiresAt!.Value
            .AddDays(1);
        var repository = new RetentionRepository(db);

        var heldPreview =
            await new PreviewRetentionHandler(
                repository,
                new FixedClock(now))
                .Handle(
                    new PreviewRetentionQuery(),
                    default);

        Assert.Empty(heldPreview.TransactionEvidence);
        transaction.ReleaseLegalHold(
            "CASE-001",
            now);
        transaction.PlaceLegalHold(
            "CASE-001",
            "replayed request",
            now.AddSeconds(1));
        Assert.False(transaction.HasActiveLegalHold);
        await db.SaveChangesAsync();
        var preview =
            await new PreviewRetentionHandler(
                repository,
                new FixedClock(now))
                .Handle(
                    new PreviewRetentionQuery(),
                    default);
        Assert.Contains(
            preview.TransactionEvidence,
            item =>
                item.TransactionId == transaction.Id);

        var result =
            await new ExecuteRetentionHandler(
                repository,
                db,
                new FixedClock(now))
                .Handle(
                    new ExecuteRetentionCommand(),
                    default);

        Assert.Equal(1, result.PurgedTransactions);
        Assert.False(await db.Transactions.AnyAsync(
            item => item.Id == transaction.Id));
        var financial = await db
            .FinancialRetentionRecords
            .SingleAsync(
                item =>
                    item.TransactionId ==
                    transaction.Id);
        Assert.Equal(
            transaction.RetentionStartsAt!.Value
                .AddYears(
                    SaleTransaction
                        .FinancialRetentionYears),
            financial.FinancialRetentionExpiresAt);
        Assert.Null(financial.PaymentReference);
        Assert.DoesNotContain(
            typeof(FinancialRetentionRecord)
                .GetProperties(),
            property => property.Name is
                "BuyerContact" or
                "SellerContact" or
                "DeliveryAddress" or
                "ProductSnapshotJson");
        var deletion = await db
            .RetentionFileDeletions
            .SingleAsync(item =>
                item.TransactionId ==
                transaction.Id);
        Assert.Equal(
            "https://example.com/photo.jpg",
            deletion.FileReference);
        var imageStore = new RecordingImageStore();
        var evidenceStore = new RecordingEvidenceStore();
        var deletedFiles =
            await new ExecuteRetentionFileDeletionsHandler(
                repository,
                imageStore,
                evidenceStore,
                db)
                .Handle(
                    new ExecuteRetentionFileDeletionsCommand(),
                    default);
        Assert.Equal(1, deletedFiles);
        Assert.Equal(
            deletion.FileReference,
            Assert.Single(imageStore.Deleted));
        Assert.Equal(
            deletion.FileReference,
            Assert.Single(evidenceStore.Deleted));
        Assert.False(await db
            .RetentionFileDeletions
            .AnyAsync());
    }

    [Fact]
    public async Task Financial_tombstone_is_deleted_after_seven_years()
    {
        await using var db = CreateDatabase();
        var transaction = ExpiredOffer();
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        var repository = new RetentionRepository(db);
        var evidencePurgeTime =
            transaction.RetentionExpiresAt!.Value;
        await new ExecuteRetentionHandler(
                repository,
                db,
                new FixedClock(evidencePurgeTime))
            .Handle(
                new ExecuteRetentionCommand(),
                default);
        var financialExpiry =
            transaction.RetentionStartsAt!.Value
                .AddYears(
                    SaleTransaction
                        .FinancialRetentionYears);

        var result =
            await new ExecuteRetentionHandler(
                repository,
                db,
                new FixedClock(financialExpiry))
                .Handle(
                    new ExecuteRetentionCommand(),
                    default);

        Assert.Equal(
            1,
            result.PurgedFinancialRecords);
        Assert.False(await db
            .FinancialRetentionRecords
            .AnyAsync(
                item =>
                    item.TransactionId ==
                    transaction.Id));
    }

    [Fact]
    public async Task Party_evidence_file_is_queued_and_deleted_with_transaction()
    {
        await using var db = CreateDatabase();
        var transaction = RefundedDisputeWithEvidence();
        var evidenceReference = Assert.Single(
            transaction.DisputeEvidence).StorageReference;
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        var repository = new RetentionRepository(db);
        var now = transaction.RetentionExpiresAt!.Value;

        var result = await new ExecuteRetentionHandler(
                repository,
                db,
                new FixedClock(now))
            .Handle(
                new ExecuteRetentionCommand(),
                default);

        Assert.Equal(1, result.PurgedTransactions);
        var pending = await db.RetentionFileDeletions
            .OrderBy(item => item.FileReference)
            .ToListAsync();
        Assert.Equal(2, pending.Count);
        Assert.Contains(
            pending,
            item => item.FileReference == evidenceReference);
        var imageStore = new RecordingImageStore();
        var evidenceStore = new RecordingEvidenceStore();

        var deleted = await new ExecuteRetentionFileDeletionsHandler(
                repository,
                imageStore,
                evidenceStore,
                db)
            .Handle(
                new ExecuteRetentionFileDeletionsCommand(),
                default);

        Assert.Equal(2, deleted);
        Assert.Contains(
            evidenceReference,
            evidenceStore.Deleted);
        Assert.Empty(db.RetentionFileDeletions);
    }

    private static SaleTransaction ExpiredOffer()
    {
        var transitions =
            new TransactionTransitionService();
        var transaction =
            TestTransactionFactory.CreateBuyerOffer(
                Guid.NewGuid(),
                "ผู้ซื้อ ทดสอบ",
                "+66811111111",
                FulfillmentType.PhysicalShipment,
                "กล้องพร้อมเลนส์",
                "กล้องใช้งานได้ปกติ",
                ConditionCode.UsedGood,
                "",
                "https://example.com/photo.jpg",
                450_000,
                "mvp-th-2026-07",
                Start,
                transitions);
        var terminalAt =
            transaction.SellerAcceptanceDeadlineAt;
        Assert.True(transaction.ExpireIfDue(
            terminalAt,
            transitions));
        Assert.Equal(
            terminalAt,
            transaction.RetentionStartsAt);
        Assert.Equal(
            terminalAt.AddYears(
                SaleTransaction
                    .EvidenceRetentionYears),
            transaction.RetentionExpiresAt);
        return transaction;
    }

    private static SaleTransaction RefundedDisputeWithEvidence()
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
            "https://example.com/photo.jpg",
            450_000,
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
            shipping: TestTransactionFactory.ShippingQuote(
                Start.AddMinutes(1)));
        transaction.BeginCheckout(
            "ผู้ซื้อ ทดสอบ",
            "+66811111111",
            "123 กรุงเทพฯ",
            Start.AddMinutes(2),
            transitions);
        transaction.ConfirmPayment(
            "payment-retention-evidence",
            Start.AddMinutes(3),
            transitions);
        transaction.SubmitTracking(
            transaction.SellerAccessToken,
            "FLASH",
            "TH12345678",
            Start.AddMinutes(4),
            transitions);
        transaction.RecordCarrierEvent(
            "delivery-retention-evidence",
            "delivered",
            Start.AddHours(1),
            Start.AddHours(1),
            transitions);
        transaction.OpenDispute(
            transaction.BuyerAccessToken!,
            DisputeReason.NotAsDescribed,
            "สินค้าไม่ตรงตามรายละเอียด",
            Start.AddHours(2),
            transitions);
        transaction.RecordDisputeEvidence(
            Guid.NewGuid(),
            DisputeEvidenceParty.Buyer,
            transaction.BuyerId!.Value,
            DisputeEvidenceType.Item,
            "ภาพสภาพสินค้าหลังเปิดกล่อง",
            "evidence:retention-test.bin",
            "image/jpeg",
            512,
            new string('a', 64),
            "retention-evidence",
            Start.AddHours(3));
        transaction.BeginDisputeResolution(
            "review-retention-evidence",
            "admin",
            "{}",
            "begin-review-retention-evidence",
            Start.AddHours(3).AddMinutes(1),
            transitions);
        transaction.ResolveDisputeForRefund(
            "review-retention-evidence",
            "super-admin",
            "{}",
            "resolve-retention-evidence",
            Start.AddHours(4),
            transitions);
        transaction.ConfirmRefund(
            "manual-bank",
            "refund-event-retention-evidence",
            "refund-retention-evidence",
            transaction.PaymentReference!,
            transaction.BuyerTotalSatang,
            transaction.Currency,
            Start.AddHours(5),
            Start.AddHours(5),
            transitions);
        return transaction;
    }

    private static ToklongDbContext CreateDatabase()
    {
        var options =
            new DbContextOptionsBuilder<ToklongDbContext>()
                .UseInMemoryDatabase(
                    Guid.NewGuid().ToString())
                .Options;
        return new ToklongDbContext(options);
    }

    private sealed class FixedClock(
        DateTimeOffset now)
        : Toklong.Application.Abstractions.IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class RecordingImageStore
        : Toklong.Application.Abstractions
            .IImportedProductImageStore
    {
        public List<string> Deleted { get; } = [];

        public Task<string> SaveAsync(
            Toklong.Application.Abstractions
                .ListingImageInput image,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            string fileReference,
            CancellationToken cancellationToken)
        {
            Deleted.Add(fileReference);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingEvidenceStore
        : Toklong.Application.Abstractions
            .IDisputeEvidenceStore
    {
        public List<string> Deleted { get; } = [];

        public Task<Toklong.Application.Abstractions
            .StoredDisputeEvidenceFile> SaveImageAsync(
            Toklong.Application.Abstractions
                .DisputeEvidenceFileInput input,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Toklong.Application.Abstractions
            .DisputeEvidenceFileContent> ReadAsync(
            string storageReference,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            string storageReference,
            CancellationToken cancellationToken)
        {
            Deleted.Add(storageReference);
            return Task.CompletedTask;
        }
    }
}
