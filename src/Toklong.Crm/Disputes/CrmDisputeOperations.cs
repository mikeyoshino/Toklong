using System.Security.Claims;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Refunds.ProcessRefunds;
using Toklong.Application.Features.Transactions.ManageDisputeEvidence;
using Toklong.Application.Transactions;
using Toklong.Crm.Authentication;
using Toklong.Crm.Persistence;
using Toklong.Domain.Transactions;

namespace Toklong.Crm.Disputes;

public sealed record CrmDisputeQueueItem(
    Guid CaseId,
    Guid TransactionId,
    string CaseNumber,
    CrmDisputeCaseStatus Status,
    string ProductName,
    DisputeReason? Reason,
    long AmountSatang,
    string Currency,
    DateTimeOffset OpenedAt,
    DateTimeOffset AssignmentDueAt,
    DateTimeOffset FirstReviewDueAt,
    DateTimeOffset? ApprovalDueAt,
    Guid? AssignedUserId,
    string? AssignedDisplayName);

public sealed record CrmDisputeDetail(
    CrmDisputeCase Case,
    TransactionView Transaction,
    IReadOnlyList<CrmCaseNote> Notes,
    IReadOnlyList<CrmEvidenceRequest> EvidenceRequests,
    IReadOnlyList<CrmResolutionAction> Resolutions,
    IReadOnlyList<CrmCaseEvent> Events,
    IReadOnlyList<CrmCoreAuditView> CoreAuditEvents,
    IReadOnlyList<CrmPartyEvidenceView> PartyEvidence);

public sealed record CrmPartyEvidenceView(
    Guid Id,
    DisputeEvidenceParty Party,
    DisputeEvidenceType EvidenceType,
    string Description,
    long LengthBytes,
    string Sha256,
    DateTimeOffset SubmittedAt);

public sealed record CrmEvidenceDownload(
    byte[] Content,
    string ContentType,
    string Sha256);

public sealed record CrmCoreAuditView(
    string Name,
    TransactionState FromState,
    TransactionState ToState,
    ActorRole ActorRole,
    string ActorReference,
    string CorrelationId,
    string IdempotencyKey,
    string MetadataJson,
    DateTimeOffset CreatedAt);

public sealed class CrmDisputeOperations(
    CrmDbContext crm,
    ITransactionRepository transactions,
    ISender sender,
    TimeProvider timeProvider,
    WorkforceIdentityOptions workforce,
    IDisputeEvidenceStore evidenceStore)
{
    public async Task<IReadOnlyList<CrmDisputeQueueItem>>
        GetQueueAsync(
            ClaimsPrincipal principal,
            CancellationToken cancellationToken)
    {
        await RequireActorAsync(
            principal,
            false,
            cancellationToken);
        var openTransactions =
            await transactions.GetOpenDisputesAsync(
                cancellationToken);
        await SynchronizeCasesAsync(
            openTransactions,
            cancellationToken);
        var cases = await crm.DisputeCases
            .Where(item =>
                item.Status != CrmDisputeCaseStatus.Closed)
            .OrderBy(item => item.OpenedAt)
            .ToListAsync(cancellationToken);
        var transactionById = openTransactions.ToDictionary(
            item => item.Id);
        foreach (var missingId in cases
                     .Select(item => item.TransactionId)
                     .Where(id =>
                         !transactionById.ContainsKey(id)))
        {
            var transaction = await transactions.GetByIdAsync(
                missingId,
                cancellationToken);
            if (transaction is not null)
                transactionById[missingId] = transaction;
        }
        foreach (var item in cases.Where(item =>
                     transactionById.TryGetValue(
                         item.TransactionId,
                         out var transaction) &&
                     transaction.State is
                         TransactionState.Refunded or
                         TransactionState.PaidOut))
            item.Close(timeProvider.GetUtcNow());
        if (crm.ChangeTracker.HasChanges())
            await crm.SaveChangesAsync(cancellationToken);
        var assignedUserIds = cases
            .Where(item => item.AssignedUserId.HasValue)
            .Select(item => item.AssignedUserId!.Value)
            .Distinct()
            .ToArray();
        var assignedNames = await crm.Users
            .Where(item => assignedUserIds.Contains(item.Id))
            .ToDictionaryAsync(
                item => item.Id,
                item => item.DisplayName,
                cancellationToken);
        return cases
            .Where(item =>
                item.Status != CrmDisputeCaseStatus.Closed)
            .Where(item =>
                transactionById.ContainsKey(item.TransactionId))
            .Select(item =>
            {
                var transaction =
                    transactionById[item.TransactionId];
                return new CrmDisputeQueueItem(
                    item.Id,
                    item.TransactionId,
                    item.CaseNumber,
                    item.Status,
                    transaction.ProductName,
                    transaction.DisputeReason,
                    transaction.BuyerTotalSatang,
                    transaction.Currency,
                    item.OpenedAt,
                    item.AssignmentDueAt,
                    item.FirstReviewDueAt,
                    item.ApprovalDueAt,
                    item.AssignedUserId,
                    item.AssignedUserId is { } assignedUserId &&
                    assignedNames.TryGetValue(
                        assignedUserId,
                        out var assignedName)
                        ? assignedName
                        : null);
            })
            .ToList();
    }

    public async Task<CrmDisputeDetail> GetDetailAsync(
        Guid caseId,
        ClaimsPrincipal principal,
        string purpose,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(
            principal,
            false,
            cancellationToken);
        var disputeCase = await crm.DisputeCases
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == caseId,
                cancellationToken)
            ?? throw new InvalidOperationException("ไม่พบเคส");
        var transaction = await transactions.GetByIdAsync(
            disputeCase.TransactionId,
            cancellationToken)
            ?? throw new InvalidOperationException("ไม่พบรายการ");
        crm.SensitiveAccessEvents.Add(
            CrmSensitiveAccessEvent.Create(
                caseId,
                actor.Id,
                "transaction_dispute_record",
                transaction.Id.ToString("N"),
                purpose,
                correlationId,
                timeProvider.GetUtcNow()));
        await crm.SaveChangesAsync(cancellationToken);
        return new CrmDisputeDetail(
            disputeCase,
            TransactionView.From(transaction),
            await crm.CaseNotes.AsNoTracking()
                .Where(item => item.CaseId == caseId)
                .OrderByDescending(item => item.CreatedAt)
                .ToListAsync(cancellationToken),
            await crm.EvidenceRequests.AsNoTracking()
                .Where(item => item.CaseId == caseId)
                .OrderByDescending(item => item.RequestedAt)
                .ToListAsync(cancellationToken),
            await crm.ResolutionActions.AsNoTracking()
                .Where(item => item.CaseId == caseId)
                .OrderByDescending(item => item.RecommendedAt)
                .ToListAsync(cancellationToken),
            await crm.CaseEvents.AsNoTracking()
                .Where(item => item.CaseId == caseId)
                .OrderByDescending(item => item.CreatedAt)
                .ToListAsync(cancellationToken),
            transaction.AuditEvents
                .OrderByDescending(item => item.CreatedAt)
                .Select(item => new CrmCoreAuditView(
                    item.Name,
                    item.FromState,
                    item.ToState,
                    item.ActorRole,
                    CrmAuditReference.FromActorId(item.ActorId),
                    item.CorrelationId,
                    item.IdempotencyKey,
                    item.MetadataJson,
                    item.CreatedAt))
                .ToList(),
            transaction.DisputeEvidence
                .OrderByDescending(item => item.SubmittedAt)
                .Select(item => new CrmPartyEvidenceView(
                    item.Id,
                    item.Party,
                    item.EvidenceType,
                    item.Description,
                    item.LengthBytes,
                    item.Sha256,
                    item.SubmittedAt))
                .ToList());
    }

    public async Task<CrmEvidenceDownload>
        DownloadEvidenceAsync(
            Guid caseId,
            Guid evidenceId,
            ClaimsPrincipal principal,
            string purpose,
            string correlationId,
            CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(
            principal,
            false,
            cancellationToken);
        var disputeCase = await crm.DisputeCases
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == caseId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "ไม่พบเคส");
        var transaction = await transactions.GetByIdAsync(
            disputeCase.TransactionId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "ไม่พบรายการ");
        var evidence = transaction.DisputeEvidence
            .SingleOrDefault(item => item.Id == evidenceId)
            ?? throw new InvalidOperationException(
                "ไม่พบหลักฐาน");
        crm.SensitiveAccessEvents.Add(
            CrmSensitiveAccessEvent.Create(
                caseId,
                actor.Id,
                "party_dispute_evidence",
                evidence.Id.ToString("N"),
                purpose,
                correlationId,
                timeProvider.GetUtcNow()));
        await crm.SaveChangesAsync(cancellationToken);
        var file = await evidenceStore.ReadAsync(
            evidence.StorageReference,
            cancellationToken);
        var actualHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256
                    .HashData(file.Content))
            .ToLowerInvariant();
        if (!System.Security.Cryptography
                .CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(evidence.Sha256),
                    Convert.FromHexString(actualHash)))
            throw new InvalidOperationException(
                "การตรวจสอบความสมบูรณ์ของหลักฐานล้มเหลว");
        return new CrmEvidenceDownload(
            file.Content,
            file.ContentType,
            actualHash);
    }

    public async Task ClaimAsync(
        Guid caseId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(
            principal,
            false,
            cancellationToken);
        var disputeCase = await GetCaseAsync(
            caseId,
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        disputeCase.Claim(actor.Id);
        crm.CaseAssignments.Add(
            CrmCaseAssignment.Create(
                disputeCase.Id,
                actor.Id,
                actor.Id,
                "รับเคสเพื่อตรวจสอบ",
                now));
        AddEvent(
            disputeCase.Id,
            actor.Id,
            "case.claimed",
            new { assignedUserId = actor.Id });
        await crm.SaveChangesAsync(cancellationToken);
    }

    public async Task AddNoteAsync(
        Guid caseId,
        string body,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(
            principal,
            false,
            cancellationToken);
        _ = await GetCaseAsync(caseId, cancellationToken);
        crm.CaseNotes.Add(
            CrmCaseNote.Create(
                caseId,
                actor.Id,
                body,
                timeProvider.GetUtcNow()));
        AddEvent(
            caseId,
            actor.Id,
            "case.note_added",
            new { });
        await crm.SaveChangesAsync(cancellationToken);
    }

    public async Task RequestEvidenceAsync(
        Guid caseId,
        CrmCaseParty party,
        string requiredEvidence,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(
            principal,
            false,
            cancellationToken);
        var disputeCase = await GetCaseAsync(
            caseId,
            cancellationToken);
        var request = CrmEvidenceRequest.Create(
            caseId,
            actor.Id,
            party,
            requiredEvidence,
            timeProvider.GetUtcNow());
        var existing = await crm.EvidenceRequests
            .SingleOrDefaultAsync(
                item => item.Id == request.Id,
                cancellationToken);
        if (existing is null)
        {
            crm.EvidenceRequests.Add(request);
            disputeCase.AwaitEvidence();
            AddEvent(
                caseId,
                actor.Id,
                "case.evidence_requested",
                new
                {
                    requestId = request.Id,
                    party = party.ToString(),
                    dueAt = request.DueAt
                });
            await crm.SaveChangesAsync(cancellationToken);
        }
        else
        {
            request = existing;
        }

        var targetParties = party switch
        {
            CrmCaseParty.Buyer =>
                new[] { DisputeEvidenceParty.Buyer },
            CrmCaseParty.Seller =>
                new[] { DisputeEvidenceParty.Seller },
            _ => new[]
            {
                DisputeEvidenceParty.Buyer,
                DisputeEvidenceParty.Seller
            }
        };
        foreach (var targetParty in targetParties)
            await sender.Send(
                new NotifyDisputeEvidenceRequestCommand(
                    disputeCase.TransactionId,
                    request.Id,
                    targetParty,
                    actor.Id,
                    request.RequiredEvidence,
                    request.DueAt),
                cancellationToken);
    }

    public async Task RecommendAsync(
        Guid caseId,
        CrmResolutionOutcome outcome,
        string reasonCode,
        string rationale,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(
            principal,
            false,
            cancellationToken);
        var disputeCase = await GetCaseAsync(
            caseId,
            cancellationToken);
        if (await crm.ResolutionActions.AnyAsync(
                item =>
                    item.CaseId == caseId &&
                    (item.Status ==
                         CrmResolutionActionStatus
                             .PendingApproval ||
                     item.Status ==
                         CrmResolutionActionStatus.Approved),
                cancellationToken))
            throw new InvalidOperationException(
                "เคสนี้มีคำแนะนำที่ยังดำเนินการไม่จบ");
        var now = timeProvider.GetUtcNow();
        var resolution = CrmResolutionAction.Recommend(
            caseId,
            outcome,
            reasonCode,
            rationale,
            actor.Id,
            now);
        crm.ResolutionActions.Add(resolution);
        disputeCase.ReadyForApproval(now);
        AddEvent(
            caseId,
            actor.Id,
            "resolution.recommended",
            new
            {
                actionId = resolution.Id,
                outcome = outcome.ToString(),
                reasonCode
            });
        await crm.SaveChangesAsync(cancellationToken);
    }

    public async Task ReturnForMoreWorkAsync(
        Guid caseId,
        Guid actionId,
        string reason,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(
            principal,
            true,
            cancellationToken);
        var disputeCase = await GetCaseAsync(
            caseId,
            cancellationToken);
        var action = await crm.ResolutionActions
            .SingleOrDefaultAsync(
                item =>
                    item.Id == actionId &&
                    item.CaseId == caseId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "ไม่พบคำแนะนำผล");
        action.ReturnForMoreWork(
            actor.Id,
            reason,
            timeProvider.GetUtcNow());
        disputeCase.ReturnToReview();
        AddEvent(
            caseId,
            actor.Id,
            "resolution.returned_for_more_work",
            new { actionId });
        await crm.SaveChangesAsync(cancellationToken);
    }

    public async Task ApproveAndApplyAsync(
        Guid caseId,
        Guid actionId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (!workforce.FinancialActionsEnabled)
            throw new InvalidOperationException(
                "Financial actions ยังไม่ผ่าน production gate");
        var actor = await RequireActorAsync(
            principal,
            true,
            cancellationToken);
        var activeSuperAdmins = await (
                from user in crm.Users
                join role in crm.UserRoles
                    on user.Id equals role.UserId
                where user.Status == CrmUserStatus.Active &&
                      role.RoleId == CrmRoleIds.SuperAdmin
                select user.Id)
            .Distinct()
            .CountAsync(cancellationToken);
        if (activeSuperAdmins < 2)
            throw new InvalidOperationException(
                "ต้องมี Super Admin ที่ใช้งานได้อย่างน้อย 2 คนก่อนอนุมัติผล");
        var disputeCase = await GetCaseAsync(
            caseId,
            cancellationToken);
        var action = await crm.ResolutionActions
            .SingleOrDefaultAsync(
                item =>
                    item.Id == actionId &&
                    item.CaseId == caseId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "ไม่พบคำแนะนำผล");

        if (action.Status ==
            CrmResolutionActionStatus.PendingApproval)
        {
            var materiallyReviewed = await crm.CaseEvents
                .AnyAsync(
                    item =>
                        item.CaseId == caseId &&
                        item.ActorUserId == actor.Id &&
                        (item.Name == "case.claimed" ||
                         item.Name == "case.note_added" ||
                         item.Name ==
                             "case.evidence_requested" ||
                         item.Name ==
                             "resolution.recommended"),
                    cancellationToken);
            if (action.RecommendedByUserId == actor.Id ||
                materiallyReviewed)
            {
                AddEvent(
                    caseId,
                    actor.Id,
                    "resolution.reviewer_approval_denied",
                    new { actionId });
                await crm.SaveChangesAsync(cancellationToken);
                throw new InvalidOperationException(
                    "ผู้ตรวจหรือผู้แนะนำผลห้ามอนุมัติเคสเดียวกัน");
            }
            action.Approve(actor.Id, timeProvider.GetUtcNow());
            disputeCase.MarkApproved();
            AddEvent(
                caseId,
                actor.Id,
                "resolution.approved",
                new
                {
                    actionId,
                    recommendedBy =
                        action.RecommendedByUserId,
                    approvedBy = actor.Id
                });
            await crm.SaveChangesAsync(cancellationToken);
        }
        else if (action.Status ==
                 CrmResolutionActionStatus.Applied)
        {
            return;
        }
        else if (action.ApprovedByUserId != actor.Id)
        {
            throw new InvalidOperationException(
                "ผลนี้อนุมัติโดย Super Admin คนอื่นแล้ว");
        }

        await sender.Send(
            new ResolveDisputeCommand(
                disputeCase.TransactionId,
                action.ReviewReference,
                action.Outcome ==
                    CrmResolutionOutcome.FullRefund
                    ? DisputeResolution.FullRefund
                    : DisputeResolution.FullPayout,
                new DisputeDecisionAudit(
                    caseId,
                    action.Id,
                    action.RecommendedByUserId,
                    actor.Id,
                    action.ReasonCode,
                    action.Rationale,
                    action.IdempotencyKey)),
            cancellationToken);
        action.MarkApplied(timeProvider.GetUtcNow());
        AddEvent(
            caseId,
            actor.Id,
            "resolution.applied",
            new
            {
                actionId,
                transactionId =
                    disputeCase.TransactionId
            });
        await crm.SaveChangesAsync(cancellationToken);
    }

    private async Task SynchronizeCasesAsync(
        IReadOnlyList<SaleTransaction> openTransactions,
        CancellationToken cancellationToken)
    {
        var ids = openTransactions.Select(item => item.Id)
            .ToArray();
        var existing = await crm.DisputeCases
            .Where(item => ids.Contains(item.TransactionId))
            .Select(item => item.TransactionId)
            .ToListAsync(cancellationToken);
        var existingIds = existing.ToHashSet();
        foreach (var transaction in openTransactions
                     .Where(item => !existingIds.Contains(item.Id)))
        {
            var openedAt = transaction.DisputeOpenedAt ??
                           transaction.CreatedAt;
            var disputeCase = CrmDisputeCase.Create(
                transaction.Id,
                openedAt);
            crm.DisputeCases.Add(disputeCase);
        }
        if (crm.ChangeTracker.HasChanges())
            await crm.SaveChangesAsync(cancellationToken);
    }

    private async Task<CrmDisputeCase> GetCaseAsync(
        Guid caseId,
        CancellationToken cancellationToken) =>
        await crm.DisputeCases.SingleOrDefaultAsync(
            item => item.Id == caseId,
            cancellationToken)
        ?? throw new InvalidOperationException("ไม่พบเคส");

    private async Task<CrmUser> RequireActorAsync(
        ClaimsPrincipal principal,
        bool requireSuperAdmin,
        CancellationToken cancellationToken)
    {
        var rawId = principal.FindFirstValue(
            CrmAuthenticationDefaults.UserIdClaim);
        if (!Guid.TryParse(rawId, out var userId))
            throw new UnauthorizedAccessException(
                "ไม่พบ CRM user identity");
        var actor = await crm.Users.SingleOrDefaultAsync(
            item => item.Id == userId,
            cancellationToken);
        if (actor is null || !actor.IsActive)
            throw new UnauthorizedAccessException(
                "บัญชี CRM ไม่พร้อมใช้งาน");
        var requiredRoleId = requireSuperAdmin
            ? CrmRoleIds.SuperAdmin
            : (Guid?)null;
        var authorized = await crm.UserRoles.AnyAsync(
            item =>
                item.UserId == userId &&
                (!requiredRoleId.HasValue ||
                 item.RoleId == requiredRoleId.Value),
            cancellationToken);
        if (!authorized)
            throw new UnauthorizedAccessException(
                "ไม่มีสิทธิ์ดำเนินการนี้");
        if (requireSuperAdmin)
        {
            var authenticatedAtValue =
                principal.FindFirstValue(
                    CrmAuthenticationDefaults
                        .AuthenticatedAtClaim);
            if (!long.TryParse(
                    authenticatedAtValue,
                    out var authenticatedAtSeconds) ||
                timeProvider.GetUtcNow() -
                DateTimeOffset.FromUnixTimeSeconds(
                    authenticatedAtSeconds) >
                TimeSpan.FromMinutes(30))
                throw new UnauthorizedAccessException(
                    "กรุณาออกจากระบบและเข้าสู่ระบบอีกครั้งก่อนอนุมัติรายการสำคัญ");
        }
        return actor;
    }

    private void AddEvent(
        Guid caseId,
        Guid actorUserId,
        string name,
        object metadata)
    {
        var id = Guid.NewGuid();
        crm.CaseEvents.Add(
            CrmCaseEvent.Create(
                caseId,
                actorUserId,
                name,
                JsonSerializer.Serialize(metadata),
                $"crm:{caseId:N}:{name}:{id:N}",
                timeProvider.GetUtcNow()));
    }
}
