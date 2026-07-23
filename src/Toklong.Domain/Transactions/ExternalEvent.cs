namespace Toklong.Domain.Transactions;

public sealed class ExternalEvent
{
    private ExternalEvent() { }

    public ExternalEvent(
        Guid transactionId,
        string provider,
        string eventId,
        string eventType,
        DateTimeOffset occurredAt,
        DateTimeOffset receivedAt)
    {
        TransactionId = transactionId;
        Provider = provider;
        EventId = eventId;
        EventType = eventType;
        OccurredAt = occurredAt;
        ReceivedAt = receivedAt;
    }

    public Guid Id { get; private set; }
    public Guid TransactionId { get; private set; }
    public string Provider { get; private set; } = "";
    public string EventId { get; private set; } = "";
    public string EventType { get; private set; } = "";
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
}
