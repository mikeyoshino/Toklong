namespace Toklong.Domain.Transactions;

public sealed class ActivationRiskEvent
{
    private ActivationRiskEvent() { }

    public ActivationRiskEvent(string reasonCode, string category, DateTimeOffset createdAt)
    {
        ReasonCode = reasonCode;
        Category = category;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public string EventType { get; private set; } = "sale_link.activation_blocked";
    public string ReasonCode { get; private set; } = "";
    public string Category { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
}
