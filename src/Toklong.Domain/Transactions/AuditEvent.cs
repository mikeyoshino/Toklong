namespace Toklong.Domain.Transactions;

public sealed class AuditEvent
{
    private AuditEvent() { }

    internal AuditEvent(
        Guid transactionId,
        ActorRole actorRole,
        string actorId,
        string name,
        TransactionState fromState,
        TransactionState toState,
        DateTimeOffset createdAt,
        string correlationId,
        string idempotencyKey,
        string metadataJson)
    {
        TransactionId = transactionId;
        ActorRole = actorRole;
        ActorId = actorId;
        Name = name;
        FromState = fromState;
        ToState = toState;
        CreatedAt = createdAt;
        CorrelationId = correlationId;
        IdempotencyKey = idempotencyKey;
        MetadataJson = metadataJson;
    }

    public Guid Id { get; private set; }
    public Guid TransactionId { get; private set; }
    public ActorRole ActorRole { get; private set; }
    public string ActorId { get; private set; } = "";
    public string Name { get; private set; } = "";
    public TransactionState FromState { get; private set; }
    public TransactionState ToState { get; private set; }
    public string MetadataJson { get; private set; } = "{}";
    public DateTimeOffset CreatedAt { get; private set; }
    public string CorrelationId { get; private set; } = "";
    public string IdempotencyKey { get; private set; } = "";
}
