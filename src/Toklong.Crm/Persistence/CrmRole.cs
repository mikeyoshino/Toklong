namespace Toklong.Crm.Persistence;

public sealed class CrmRole
{
    private CrmRole() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
}

public sealed class CrmUserRole
{
    private CrmUserRole() { }

    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }
    public Guid? AssignedByUserId { get; private set; }

    public static CrmUserRole Assign(
        Guid userId,
        Guid roleId,
        Guid? assignedByUserId,
        DateTimeOffset now) =>
        new()
        {
            UserId = userId,
            RoleId = roleId,
            AssignedAt = now,
            AssignedByUserId = assignedByUserId
        };
}
