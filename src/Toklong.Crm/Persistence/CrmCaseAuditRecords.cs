namespace Toklong.Crm.Persistence;

public sealed class CrmCaseAssignment
{
    private CrmCaseAssignment() { }

    public Guid Id { get; private set; }
    public Guid CaseId { get; private set; }
    public Guid AssigneeUserId { get; private set; }
    public Guid AssignedByUserId { get; private set; }
    public string Reason { get; private set; } = "";
    public DateTimeOffset AssignedAt { get; private set; }

    public static CrmCaseAssignment Create(
        Guid caseId,
        Guid assigneeUserId,
        Guid assignedByUserId,
        string reason,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            CaseId = caseId,
            AssigneeUserId = assigneeUserId,
            AssignedByUserId = assignedByUserId,
            Reason = CrmCaseEvent.Required(
                reason,
                500,
                "เหตุผลการมอบหมาย"),
            AssignedAt = now
        };
}

public sealed class CrmSensitiveAccessEvent
{
    private CrmSensitiveAccessEvent() { }

    public Guid Id { get; private set; }
    public Guid CaseId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string ResourceType { get; private set; } = "";
    public string ResourceReference { get; private set; } = "";
    public string Purpose { get; private set; } = "";
    public string CorrelationId { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }

    public static CrmSensitiveAccessEvent Create(
        Guid caseId,
        Guid actorUserId,
        string resourceType,
        string resourceReference,
        string purpose,
        string correlationId,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            CaseId = caseId,
            ActorUserId = actorUserId,
            ResourceType = CrmCaseEvent.Required(
                resourceType,
                80,
                "resource type"),
            ResourceReference = CrmCaseEvent.Required(
                resourceReference,
                160,
                "resource reference"),
            Purpose = CrmCaseEvent.Required(
                purpose,
                500,
                "วัตถุประสงค์"),
            CorrelationId = CrmCaseEvent.Required(
                correlationId,
                160,
                "correlation ID"),
            CreatedAt = now
        };
}
