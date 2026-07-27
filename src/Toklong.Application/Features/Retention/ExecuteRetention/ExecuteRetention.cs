using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.Retention.ExecuteRetention;

public sealed record PreviewRetentionQuery(
    int BatchSize = 100)
    : IRequest<RetentionPreview>;

public sealed record ExecuteRetentionCommand(
    int BatchSize = 100)
    : IRequest<RetentionExecutionResult>;

public sealed record ExecuteRetentionFileDeletionsCommand(
    int BatchSize = 100)
    : IRequest<int>;

public sealed record RetentionCandidate(
    Guid TransactionId,
    TransactionState TerminalState,
    DateTimeOffset ExpiresAt);

public sealed record FinancialRetentionCandidate(
    Guid TransactionId,
    DateTimeOffset ExpiresAt);

public sealed record RetentionPreview(
    DateTimeOffset EvaluatedAt,
    IReadOnlyList<RetentionCandidate>
        TransactionEvidence,
    IReadOnlyList<FinancialRetentionCandidate>
        FinancialRecords);

public sealed record RetentionExecutionResult(
    DateTimeOffset ExecutedAt,
    int PurgedTransactions,
    int PurgedFinancialRecords);

public sealed class PreviewRetentionHandler(
    IRetentionRepository repository,
    IClock clock)
    : IRequestHandler<
        PreviewRetentionQuery,
        RetentionPreview>
{
    public async Task<RetentionPreview> Handle(
        PreviewRetentionQuery request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var batchSize = ValidBatchSize(
            request.BatchSize);
        var transactions =
            await repository.GetDueTransactionsAsync(
                now,
                batchSize,
                cancellationToken);
        var financial =
            await repository.GetDueFinancialRecordsAsync(
                now,
                batchSize,
                cancellationToken);
        return new RetentionPreview(
            now,
            transactions.Select(transaction =>
                    new RetentionCandidate(
                        transaction.Id,
                        transaction.State,
                        transaction.RetentionExpiresAt!.Value))
                .ToArray(),
            financial.Select(record =>
                    new FinancialRetentionCandidate(
                        record.TransactionId,
                        record.FinancialRetentionExpiresAt))
                .ToArray());
    }

    internal static int ValidBatchSize(int value)
    {
        if (value is < 1 or > 500)
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Batch size must be between 1 and 500.");
        return value;
    }
}

public sealed class ExecuteRetentionHandler(
    IRetentionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<
        ExecuteRetentionCommand,
        RetentionExecutionResult>
{
    public async Task<RetentionExecutionResult> Handle(
        ExecuteRetentionCommand request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var batchSize =
            PreviewRetentionHandler.ValidBatchSize(
                request.BatchSize);
        var transactions =
            await repository.GetDueTransactionsAsync(
                now,
                batchSize,
                cancellationToken);
        foreach (var transaction in transactions)
        {
            await repository.AddFinancialRecordAsync(
                FinancialRetentionRecord.Create(
                    transaction,
                    now),
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(
                    transaction.PhotoUrl))
                await repository.AddFileDeletionAsync(
                    RetentionFileDeletion.Create(
                        transaction.Id,
                        transaction.PhotoUrl,
                        now),
                    cancellationToken);
            foreach (var evidence in
                     transaction.DisputeEvidence)
                await repository.AddFileDeletionAsync(
                    RetentionFileDeletion.Create(
                        transaction.Id,
                        evidence.StorageReference,
                        now),
                    cancellationToken);
            repository.RemoveTransaction(
                transaction);
        }

        var financial =
            await repository.GetDueFinancialRecordsAsync(
                now,
                batchSize,
                cancellationToken);
        foreach (var record in financial)
            repository.RemoveFinancialRecord(record);

        if (transactions.Count > 0 ||
            financial.Count > 0)
            await unitOfWork.SaveChangesAsync(
                cancellationToken);

        return new RetentionExecutionResult(
            now,
            transactions.Count,
            financial.Count);
    }
}

public sealed class ExecuteRetentionFileDeletionsHandler(
    IRetentionRepository repository,
    IImportedProductImageStore imageStore,
    IDisputeEvidenceStore evidenceStore,
    IUnitOfWork unitOfWork)
    : IRequestHandler<
        ExecuteRetentionFileDeletionsCommand,
        int>
{
    public async Task<int> Handle(
        ExecuteRetentionFileDeletionsCommand request,
        CancellationToken cancellationToken)
    {
        var batchSize =
            PreviewRetentionHandler.ValidBatchSize(
                request.BatchSize);
        var deletions =
            await repository
                .GetPendingFileDeletionsAsync(
                    batchSize,
                    cancellationToken);
        foreach (var deletion in deletions)
        {
            await imageStore.DeleteAsync(
                deletion.FileReference,
                cancellationToken);
            await evidenceStore.DeleteAsync(
                deletion.FileReference,
                cancellationToken);
            repository.RemoveFileDeletion(
                deletion);
        }
        if (deletions.Count > 0)
            await unitOfWork.SaveChangesAsync(
                cancellationToken);
        return deletions.Count;
    }
}
