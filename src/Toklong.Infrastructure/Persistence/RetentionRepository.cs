using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Domain.Transactions;

namespace Toklong.Infrastructure.Persistence;

public sealed class RetentionRepository(
    ToklongDbContext dbContext)
    : IRetentionRepository
{
    public async Task<IReadOnlyList<SaleTransaction>>
        GetDueTransactionsAsync(
            DateTimeOffset now,
            int batchSize,
            CancellationToken cancellationToken) =>
        await dbContext.Transactions
            .Include(transaction =>
                transaction.DisputeEvidence)
            .Where(transaction =>
                transaction.RetentionExpiresAt <= now &&
                transaction.LegalHoldPlacedAt == null &&
                (transaction.State ==
                     TransactionState.PaidOut ||
                 transaction.State ==
                     TransactionState.Refunded ||
                 transaction.State ==
                     TransactionState.Cancelled ||
                 transaction.State ==
                     TransactionState.Expired))
            .OrderBy(transaction =>
                transaction.RetentionExpiresAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FinancialRetentionRecord>>
        GetDueFinancialRecordsAsync(
            DateTimeOffset now,
            int batchSize,
            CancellationToken cancellationToken) =>
        await dbContext.FinancialRetentionRecords
            .Where(record =>
                record.FinancialRetentionExpiresAt <=
                now)
            .OrderBy(record =>
                record.FinancialRetentionExpiresAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RetentionFileDeletion>>
        GetPendingFileDeletionsAsync(
            int batchSize,
            CancellationToken cancellationToken) =>
        await dbContext.RetentionFileDeletions
            .OrderBy(deletion => deletion.QueuedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

    public Task AddFinancialRecordAsync(
        FinancialRetentionRecord record,
        CancellationToken cancellationToken) =>
        dbContext.FinancialRetentionRecords
            .AddAsync(record, cancellationToken)
            .AsTask();

    public Task AddFileDeletionAsync(
        RetentionFileDeletion deletion,
        CancellationToken cancellationToken) =>
        dbContext.RetentionFileDeletions
            .AddAsync(deletion, cancellationToken)
            .AsTask();

    public void RemoveTransaction(
        SaleTransaction transaction) =>
        dbContext.Transactions.Remove(transaction);

    public void RemoveFinancialRecord(
        FinancialRetentionRecord record) =>
        dbContext.FinancialRetentionRecords
            .Remove(record);

    public void RemoveFileDeletion(
        RetentionFileDeletion deletion) =>
        dbContext.RetentionFileDeletions
            .Remove(deletion);
}
