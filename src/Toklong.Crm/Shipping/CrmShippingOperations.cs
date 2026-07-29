using System.Security.Claims;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Shipping.ManageShippingExceptions;
using Toklong.Crm.Authentication;
using Toklong.Crm.Persistence;
using Toklong.Domain.Transactions;

namespace Toklong.Crm.Shipping;

public sealed record CrmShippingCaseDetail(
    Guid TransactionId,
    string ProductName,
    TransactionState State,
    string? ServiceName,
    string? TrackingNumber,
    bool ReturnRequired,
    DateTimeOffset? ReturnDeliveredAt,
    string? ManualReturnResolutionReference,
    IReadOnlyList<CrmShippingOperationView> Operations,
    IReadOnlyList<CrmShippingAdjustmentView> Adjustments,
    IReadOnlyList<CrmInsuranceCaseView> InsuranceCases,
    IReadOnlyList<CrmShippingAuditView> AuditEvents);

public sealed record CrmShippingOperationView(
    Guid Id,
    ShippingOperationType OperationType,
    ShippingOperationStatus Status,
    string? SanitizedErrorCode,
    DateTimeOffset CreatedAt);

public sealed record CrmShippingAdjustmentView(
    Guid Id,
    string ProviderReference,
    long AmountSatang,
    string Currency,
    string ReasonCode,
    string CrmCaseReference,
    DateTimeOffset OccurredAt,
    bool IsOpen,
    string? ResolutionCode);

public sealed record CrmInsuranceCaseView(
    Guid Id,
    string ProviderCaseReference,
    ShippingInsuranceCaseStatus Status,
    string ReasonCode,
    long DeclaredValueSatang,
    long ClaimedAmountSatang,
    string Currency,
    string CrmCaseReference,
    string? ProviderResultCode);

public sealed record CrmShippingAuditView(
    string Name,
    ActorRole ActorRole,
    DateTimeOffset CreatedAt,
    string CorrelationId);

public sealed class CrmShippingOperations(
    CrmDbContext crm,
    ITransactionRepository transactions,
    ISender sender,
    TimeProvider timeProvider)
{
    public async Task<CrmShippingCaseDetail> GetAsync(
        Guid transactionId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        await RequireActorAsync(
            principal,
            requireStepUp: false,
            cancellationToken);
        var transaction = await transactions.GetByIdAsync(
            transactionId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "ไม่พบรายการ");
        return new CrmShippingCaseDetail(
            transaction.Id,
            transaction.ProductName,
            transaction.State,
            transaction.ShippingServiceName,
            transaction.TrackingNumber ??
            transaction.ShippingCourierTrackingCode,
            transaction.ReturnRequired,
            transaction.ReturnDeliveredAt,
            transaction.ManualReturnResolutionReference,
            transaction.ShippingOperations
                .OrderByDescending(item => item.CreatedAt)
                .Select(item =>
                    new CrmShippingOperationView(
                        item.Id,
                        item.OperationType,
                        item.Status,
                        item.LastSanitizedErrorCode,
                        item.CreatedAt))
                .ToList(),
            transaction.ProviderShippingAdjustments
                .OrderByDescending(item => item.RecordedAt)
                .Select(item =>
                    new CrmShippingAdjustmentView(
                        item.Id,
                        item.ProviderReference,
                        item.AmountSatang,
                        item.Currency,
                        item.ReasonCode,
                        item.CrmCaseReference,
                        item.ProviderOccurredAt,
                        item.IsOpen,
                        item.ResolutionCode))
                .ToList(),
            transaction.ShippingInsuranceCases
                .OrderByDescending(item => item.OpenedAt)
                .Select(item =>
                    new CrmInsuranceCaseView(
                        item.Id,
                        item.ProviderCaseReference,
                        item.Status,
                        item.ReasonCode,
                        item.DeclaredValueSatang,
                        item.ClaimedAmountSatang,
                        item.Currency,
                        item.CrmCaseReference,
                        item.ProviderResultCode))
                .ToList(),
            transaction.AuditEvents
                .Where(item =>
                    item.Name.StartsWith(
                        "shipping.",
                        StringComparison.Ordinal) ||
                    item.Name.StartsWith(
                        "carrier.",
                        StringComparison.Ordinal))
                .OrderByDescending(item => item.CreatedAt)
                .Select(item =>
                    new CrmShippingAuditView(
                        item.Name,
                        item.ActorRole,
                        item.CreatedAt,
                        item.CorrelationId))
                .ToList());
    }

    public async Task ResolveInsuranceAsync(
        Guid transactionId,
        Guid insuranceCaseId,
        string providerResultCode,
        string providerResolutionReference,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(
            principal,
            requireStepUp: true,
            cancellationToken);
        await sender.Send(
            new ResolveInsuranceCaseCommand(
                transactionId,
                insuranceCaseId,
                actor.Id.ToString("N"),
                Required(providerResultCode, "ผลจากผู้ให้บริการ"),
                Required(
                    providerResolutionReference,
                    "เลขอ้างอิงผล")),
            cancellationToken);
    }

    public async Task ResolveAdjustmentAsync(
        Guid transactionId,
        Guid adjustmentId,
        string resolutionCode,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(
            principal,
            requireStepUp: true,
            cancellationToken);
        await sender.Send(
            new ResolveShippingAdjustmentCommand(
                transactionId,
                adjustmentId,
                Required(
                    resolutionCode,
                    "ผลการตรวจยอดปรับ"),
                actor.Id.ToString("N")),
            cancellationToken);
    }

    public async Task ResolveCarrierExceptionAsync(
        Guid transactionId,
        TransactionState targetState,
        string reason,
        string caseReference,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(
            principal,
            requireStepUp: true,
            cancellationToken);
        await sender.Send(
            new ResolveCarrierExceptionCommand(
                transactionId,
                targetState,
                actor.Id.ToString("N"),
                Required(reason, "เหตุผล"),
                Required(caseReference, "เลขเคส"),
                $"crm-carrier-resolution:{transactionId:N}:{caseReference.Trim()}"),
            cancellationToken);
    }

    public async Task AuthorizeManualReturnResolutionAsync(
        Guid transactionId,
        string reference,
        string reason,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(
            principal,
            requireStepUp: true,
            cancellationToken);
        var cleanReference = Required(
            reference,
            "เลขอ้างอิงผลส่งคืน");
        await sender.Send(
            new AuthorizeManualReturnResolutionCommand(
                transactionId,
                cleanReference,
                actor.Id.ToString("N"),
                Required(reason, "เหตุผล"),
                $"crm-return-resolution:{transactionId:N}:{cleanReference}"),
            cancellationToken);
    }

    public async Task AuthorizeShippingOperationRetryAsync(
        Guid transactionId,
        Guid operationId,
        string reason,
        string providerOutcomeReference,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(
            principal,
            requireStepUp: true,
            cancellationToken);
        var cleanReference = Required(
            providerOutcomeReference,
            "เลขอ้างอิงผลตรวจผู้ให้บริการ");
        await sender.Send(
            new AuthorizeShippingOperationRetryCommand(
                transactionId,
                operationId,
                actor.Id.ToString("N"),
                Required(reason, "เหตุผล"),
                cleanReference,
                $"crm-shipping-retry:{operationId:N}:{cleanReference}"),
            cancellationToken);
    }

    private async Task<CrmUser> RequireActorAsync(
        ClaimsPrincipal principal,
        bool requireStepUp,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(
                principal.FindFirstValue(
                    CrmAuthenticationDefaults.UserIdClaim),
                out var userId))
            throw new UnauthorizedAccessException(
                "ไม่พบ CRM user identity");
        var actor = await crm.Users.SingleOrDefaultAsync(
            item => item.Id == userId,
            cancellationToken);
        if (actor is null || !actor.IsActive)
            throw new UnauthorizedAccessException(
                "บัญชี CRM ไม่พร้อมใช้งาน");
        var roleId = requireStepUp
            ? CrmRoleIds.SuperAdmin
            : CrmRoleIds.Admin;
        var authorized = await crm.UserRoles.AnyAsync(
            item =>
                item.UserId == userId &&
                (item.RoleId == roleId ||
                 item.RoleId == CrmRoleIds.SuperAdmin),
            cancellationToken);
        if (!authorized)
            throw new UnauthorizedAccessException(
                "ไม่มีสิทธิ์จัดการเคสขนส่ง");
        if (requireStepUp)
        {
            var rawAuthenticatedAt = principal.FindFirstValue(
                CrmAuthenticationDefaults.AuthenticatedAtClaim);
            if (!long.TryParse(
                    rawAuthenticatedAt,
                    out var authenticatedAt) ||
                timeProvider.GetUtcNow() -
                DateTimeOffset.FromUnixTimeSeconds(
                    authenticatedAt) >
                TimeSpan.FromMinutes(30))
                throw new UnauthorizedAccessException(
                    "กรุณาเข้าสู่ระบบอีกครั้งก่อนปิดเคส");
        }
        return actor;
    }

    private static string Required(
        string value,
        string label)
    {
        var clean = value?.Trim() ?? "";
        if (clean.Length == 0)
            throw new ArgumentException($"{label}ไม่ถูกต้อง");
        return clean;
    }
}
