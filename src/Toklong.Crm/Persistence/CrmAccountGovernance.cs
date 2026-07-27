namespace Toklong.Crm.Persistence;

public enum CrmRoleChangeRequestStatus
{
    PendingApproval,
    Applied
}

public sealed class CrmRoleChangeRequest
{
    private CrmRoleChangeRequest() { }

    public Guid Id { get; private set; }
    public Guid TargetUserId { get; private set; }
    public Guid RoleId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public CrmRoleChangeRequestStatus Status { get; private set; }
    public long Version { get; private set; }

    public static CrmRoleChangeRequest CreateSuperAdminGrant(
        Guid targetUserId,
        Guid requestedByUserId,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            TargetUserId = targetUserId,
            RoleId = CrmRoleIds.SuperAdmin,
            RequestedByUserId = requestedByUserId,
            RequestedAt = now,
            Status =
                CrmRoleChangeRequestStatus.PendingApproval
        };

    public void Approve(
        Guid approvedByUserId,
        DateTimeOffset now)
    {
        if (approvedByUserId == RequestedByUserId)
            throw new InvalidOperationException(
                "ผู้ขอเพิ่ม Super Admin ห้ามอนุมัติเอง");
        if (Status !=
            CrmRoleChangeRequestStatus.PendingApproval)
            throw new InvalidOperationException(
                "คำขอนี้ไม่ได้รออนุมัติ");
        ApprovedByUserId = approvedByUserId;
        ApprovedAt = now;
        Status = CrmRoleChangeRequestStatus.Applied;
        Version++;
    }
}

public sealed class CrmAccountEvent
{
    private CrmAccountEvent() { }

    public Guid Id { get; private set; }
    public Guid TargetUserId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string Name { get; private set; } = "";
    public string MetadataJson { get; private set; } = "{}";
    public DateTimeOffset CreatedAt { get; private set; }

    public static CrmAccountEvent Create(
        Guid targetUserId,
        Guid actorUserId,
        string name,
        string metadataJson,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            TargetUserId = targetUserId,
            ActorUserId = actorUserId,
            Name = CrmCaseEvent.Required(
                name,
                120,
                "ชื่อ account event"),
            MetadataJson = CrmCaseEvent.Required(
                metadataJson,
                2000,
                "account event metadata"),
            CreatedAt = now
        };
}
