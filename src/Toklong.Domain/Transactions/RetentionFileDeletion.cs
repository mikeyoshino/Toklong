namespace Toklong.Domain.Transactions;

public sealed class RetentionFileDeletion
{
    private RetentionFileDeletion() { }

    private RetentionFileDeletion(
        Guid id,
        Guid transactionId,
        string fileReference,
        DateTimeOffset queuedAt)
    {
        Id = id;
        TransactionId = transactionId;
        FileReference = fileReference;
        QueuedAt = queuedAt;
    }

    public Guid Id { get; private set; }
    public Guid TransactionId { get; private set; }
    public string FileReference { get; private set; } = "";
    public DateTimeOffset QueuedAt { get; private set; }

    public static RetentionFileDeletion Create(
        Guid transactionId,
        string fileReference,
        DateTimeOffset queuedAt)
    {
        if (transactionId == Guid.Empty)
            throw new ArgumentException(
                "Transaction ID is required.");
        if (string.IsNullOrWhiteSpace(fileReference))
            throw new ArgumentException(
                "File reference is required.");
        return new RetentionFileDeletion(
            Guid.NewGuid(),
            transactionId,
            fileReference.Trim(),
            queuedAt);
    }
}
