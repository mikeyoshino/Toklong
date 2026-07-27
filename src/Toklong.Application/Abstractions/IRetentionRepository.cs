using Toklong.Domain.Transactions;

namespace Toklong.Application.Abstractions;

public interface IRetentionRepository
{
    Task<IReadOnlyList<SaleTransaction>>
        GetDueTransactionsAsync(
            DateTimeOffset now,
            int batchSize,
            CancellationToken cancellationToken);

    Task<IReadOnlyList<FinancialRetentionRecord>>
        GetDueFinancialRecordsAsync(
            DateTimeOffset now,
            int batchSize,
            CancellationToken cancellationToken);

    Task<IReadOnlyList<RetentionFileDeletion>>
        GetPendingFileDeletionsAsync(
            int batchSize,
            CancellationToken cancellationToken);

    Task AddFinancialRecordAsync(
        FinancialRetentionRecord record,
        CancellationToken cancellationToken);

    Task AddFileDeletionAsync(
        RetentionFileDeletion deletion,
        CancellationToken cancellationToken);

    void RemoveTransaction(
        SaleTransaction transaction);

    void RemoveFinancialRecord(
        FinancialRetentionRecord record);

    void RemoveFileDeletion(
        RetentionFileDeletion deletion);
}
