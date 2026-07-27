namespace Toklong.Domain.Notifications;

public sealed class NotificationOutboxMessage
{
    private NotificationOutboxMessage() { }

    public Guid Id { get; private set; }
    public Guid TransactionId { get; private set; }
    public string Audience { get; private set; } = "";
    public string Recipient { get; private set; } = "";
    public string Template { get; private set; } = "";
    public string? Detail { get; private set; }
    public DateTimeOffset? ActionDeadlineAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset AvailableAt { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public string? ProviderReference { get; private set; }

    internal static NotificationOutboxMessage Create(
        Guid transactionId,
        string audience,
        string recipient,
        string template,
        DateTimeOffset createdAt,
        DateTimeOffset availableAt,
        string? detail = null,
        DateTimeOffset? actionDeadlineAt = null) =>
        new()
        {
            TransactionId = transactionId,
            Audience = audience,
            Recipient = recipient,
            Template = template,
            Detail = detail,
            ActionDeadlineAt = actionDeadlineAt,
            CreatedAt = createdAt,
            AvailableAt = availableAt
        };

    public void MarkSent(
        string providerReference,
        DateTimeOffset sentAt)
    {
        ProviderReference = providerReference.Trim();
        SentAt = sentAt;
        LastAttemptAt = sentAt;
        Attempts++;
    }

    public void MarkAttemptFailed(DateTimeOffset attemptedAt)
    {
        LastAttemptAt = attemptedAt;
        Attempts++;
        AvailableAt = attemptedAt.AddMinutes(
            Math.Min(60, 1 << Math.Min(Attempts, 5)));
    }
}
