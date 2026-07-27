using System.Security.Cryptography;
using System.Text;

namespace Toklong.Crm.Persistence;

public enum CrmCaseParty
{
    Buyer,
    Seller,
    Both
}

public enum CrmResolutionOutcome
{
    FullRefund,
    FullPayout
}

public enum CrmResolutionActionStatus
{
    PendingApproval,
    Returned,
    Approved,
    Applied
}

public sealed class CrmCaseEvent
{
    private CrmCaseEvent() { }

    public Guid Id { get; private set; }
    public Guid CaseId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string Name { get; private set; } = "";
    public string MetadataJson { get; private set; } = "{}";
    public string IdempotencyKey { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }

    public static CrmCaseEvent Create(
        Guid caseId,
        Guid actorUserId,
        string name,
        string metadataJson,
        string idempotencyKey,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            CaseId = caseId,
            ActorUserId = actorUserId,
            Name = Required(name, 120, "ชื่อ event"),
            MetadataJson = Required(
                metadataJson,
                4000,
                "event metadata"),
            IdempotencyKey = Required(
                idempotencyKey,
                160,
                "idempotency key"),
            CreatedAt = now
        };

    internal static string Required(
        string? value,
        int maximumLength,
        string label)
    {
        var clean = value?.Trim() ?? "";
        if (clean.Length == 0 || clean.Length > maximumLength)
            throw new InvalidOperationException(
                $"{label}ไม่ถูกต้อง");
        return clean;
    }
}

public sealed class CrmCaseNote
{
    private CrmCaseNote() { }

    public Guid Id { get; private set; }
    public Guid CaseId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public string Body { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }

    public static CrmCaseNote Create(
        Guid caseId,
        Guid authorUserId,
        string body,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            CaseId = caseId,
            AuthorUserId = authorUserId,
            Body = CrmSensitiveContentGuard
                .RejectReusableCredentials(
                    CrmCaseEvent.Required(
                        body,
                        4000,
                        "บันทึกภายใน")),
            CreatedAt = now
        };
}

public sealed class CrmEvidenceRequest
{
    private CrmEvidenceRequest() { }

    public Guid Id { get; private set; }
    public Guid CaseId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public CrmCaseParty Party { get; private set; }
    public string RequiredEvidence { get; private set; } = "";
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset DueAt { get; private set; }

    public static CrmEvidenceRequest Create(
        Guid caseId,
        Guid requestedByUserId,
        CrmCaseParty party,
        string requiredEvidence,
        DateTimeOffset now)
    {
        var cleanEvidence = CrmCaseEvent.Required(
            requiredEvidence,
            500,
            "หลักฐานที่ต้องการ");
        var identity = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                $"{caseId:N}|{party}|{cleanEvidence}"));
        return new CrmEvidenceRequest
        {
            Id = new Guid(identity.AsSpan(0, 16)),
            CaseId = caseId,
            RequestedByUserId = requestedByUserId,
            Party = party,
            RequiredEvidence = cleanEvidence,
            RequestedAt = now,
            DueAt = now.AddHours(48)
        };
    }
}

public sealed class CrmResolutionAction
{
    private static readonly HashSet<string> SupportedReasonCodes =
    [
        "ITEM_NOT_RECEIVED",
        "WRONG_ITEM",
        "MATERIALLY_NOT_AS_DESCRIBED",
        "UNDISCLOSED_DAMAGE",
        "MISSING_PARTS",
        "SUSPECTED_COUNTERFEIT",
        "EMPTY_OR_TAMPERED_PARCEL",
        "DIGITAL_NOT_RECEIVED",
        "DIGITAL_NOT_TRANSFERABLE",
        "OTHER"
    ];

    private CrmResolutionAction() { }

    public Guid Id { get; private set; }
    public Guid CaseId { get; private set; }
    public CrmResolutionOutcome Outcome { get; private set; }
    public string ReasonCode { get; private set; } = "";
    public string Rationale { get; private set; } = "";
    public Guid RecommendedByUserId { get; private set; }
    public DateTimeOffset RecommendedAt { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public Guid? ReturnedByUserId { get; private set; }
    public DateTimeOffset? ReturnedAt { get; private set; }
    public string? ReturnedReason { get; private set; }
    public DateTimeOffset? AppliedAt { get; private set; }
    public string ReviewReference { get; private set; } = "";
    public string IdempotencyKey { get; private set; } = "";
    public CrmResolutionActionStatus Status { get; private set; }
    public long Version { get; private set; }

    public static CrmResolutionAction Recommend(
        Guid caseId,
        CrmResolutionOutcome outcome,
        string reasonCode,
        string rationale,
        Guid recommendedByUserId,
        DateTimeOffset now)
    {
        var id = Guid.NewGuid();
        var cleanReasonCode = CrmCaseEvent.Required(
            reasonCode,
            80,
            "reason code").ToUpperInvariant();
        if (!SupportedReasonCodes.Contains(cleanReasonCode))
            throw new InvalidOperationException(
                "ไม่รองรับ reason code นี้");
        return new CrmResolutionAction
        {
            Id = id,
            CaseId = caseId,
            Outcome = outcome,
            ReasonCode = cleanReasonCode,
            Rationale = CrmSensitiveContentGuard
                .RejectReusableCredentials(
                    CrmCaseEvent.Required(
                        rationale,
                        4000,
                        "เหตุผลประกอบ")),
            RecommendedByUserId = recommendedByUserId,
            RecommendedAt = now,
            ReviewReference =
                $"CRM-{caseId.ToString("N")[..8].ToUpperInvariant()}-{id.ToString("N")[..8].ToUpperInvariant()}",
            IdempotencyKey = $"crm-dispute-resolution:{id:N}",
            Status = CrmResolutionActionStatus.PendingApproval
        };
    }

    public void Approve(
        Guid approvedByUserId,
        DateTimeOffset now)
    {
        if (approvedByUserId == RecommendedByUserId)
            throw new InvalidOperationException(
                "ผู้แนะนำผลห้ามอนุมัติเคสของตนเอง");
        if (Status != CrmResolutionActionStatus.PendingApproval)
            throw new InvalidOperationException(
                "คำแนะนำนี้ไม่ได้รออนุมัติ");
        ApprovedByUserId = approvedByUserId;
        ApprovedAt = now;
        Status = CrmResolutionActionStatus.Approved;
        Version++;
    }

    public void MarkApplied(DateTimeOffset now)
    {
        if (Status != CrmResolutionActionStatus.Approved)
            throw new InvalidOperationException(
                "ผลนี้ยังไม่ได้รับอนุมัติ");
        AppliedAt = now;
        Status = CrmResolutionActionStatus.Applied;
        Version++;
    }

    public void ReturnForMoreWork(
        Guid returnedByUserId,
        string reason,
        DateTimeOffset now)
    {
        if (Status != CrmResolutionActionStatus.PendingApproval)
            throw new InvalidOperationException(
                "คำแนะนำนี้ไม่ได้รออนุมัติ");
        ReturnedByUserId = returnedByUserId;
        ReturnedAt = now;
        ReturnedReason = CrmSensitiveContentGuard
            .RejectReusableCredentials(
                CrmCaseEvent.Required(
                    reason,
                    2000,
                    "เหตุผลที่ส่งกลับ"));
        Status = CrmResolutionActionStatus.Returned;
        Version++;
    }
}
