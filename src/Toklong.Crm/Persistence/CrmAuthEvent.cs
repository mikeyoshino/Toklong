namespace Toklong.Crm.Persistence;

public sealed class CrmAuthEvent
{
    private CrmAuthEvent() { }

    public Guid Id { get; private set; }
    public Guid? UserId { get; private set; }
    public string Name { get; private set; } = "";
    public string SubjectReferenceHash { get; private set; } = "";
    public string CorrelationId { get; private set; } = "";
    public string MetadataJson { get; private set; } = "{}";
    public DateTimeOffset CreatedAt { get; private set; }

    public static CrmAuthEvent Create(
        Guid? userId,
        string name,
        string subjectReferenceHash,
        string correlationId,
        DateTimeOffset now,
        string metadataJson = "{}") =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = Required(name),
            SubjectReferenceHash = Required(
                subjectReferenceHash),
            CorrelationId = Required(correlationId),
            MetadataJson = string.IsNullOrWhiteSpace(
                    metadataJson)
                ? "{}"
                : metadataJson,
            CreatedAt = now
        };

    private static string Required(string? value)
    {
        var clean = value?.Trim() ?? "";
        return clean.Length > 0
            ? clean
            : throw new InvalidOperationException(
                "CRM auth event value is required.");
    }
}
