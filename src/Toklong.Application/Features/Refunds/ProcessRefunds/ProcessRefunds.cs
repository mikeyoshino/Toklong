using MediatR;
using System.Text.Json;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Features.Shipping;
using Toklong.Domain.Common;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.Refunds.ProcessRefunds;

public sealed record EvaluateShipmentDeadlinesCommand : IRequest<int>;

public sealed class EvaluateShipmentDeadlinesHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions)
    : IRequestHandler<EvaluateShipmentDeadlinesCommand, int>
{
    public async Task<int> Handle(
        EvaluateShipmentDeadlinesCommand request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var due = await repository.GetDueForShipmentDeadlineAsync(
            now,
            cancellationToken);
        var changed = 0;
        foreach (var transaction in due)
        {
            if (transaction.MarkShipmentOverdue(now, transitions))
            {
                ManagedShippingOperationQueue
                    .QueueCancellationIfRequired(
                        transaction,
                        now);
                changed++;
            }
        }

        if (changed > 0)
            await unitOfWork.SaveChangesAsync(cancellationToken);
        return changed;
    }
}

public sealed record CreatePendingRefundsCommand : IRequest<int>;

public sealed class CreatePendingRefundsHandler(
    ITransactionRepository repository,
    IRefundProvider refundProvider,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<CreatePendingRefundsCommand, int>
{
    public async Task<int> Handle(
        CreatePendingRefundsCommand request,
        CancellationToken cancellationToken)
    {
        var pending = await repository.GetPendingRefundsAsync(
            cancellationToken);
        var changed = 0;
        foreach (var transaction in pending)
        {
            var transactionChanged = false;
            if (string.IsNullOrWhiteSpace(transaction.PaymentReference))
                throw new InvalidOperationException(
                    $"รายการ {transaction.Id} ไม่มีเลขอ้างอิงการชำระ");
            var prepared = await refundProvider.CreateFullRefundAsync(
                transaction.Id,
                transaction.PaymentReference,
                transaction.BuyerTotalSatang,
                transaction.Currency,
                transaction.RefundReference,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(transaction.RefundReference))
            {
                transaction.RecordRefundInstruction(
                    "stripe",
                    prepared.ProviderReference,
                    clock.UtcNow,
                    prepared.Status);
                transactionChanged = true;
            }
            if (string.Equals(
                    prepared.Status,
                    "requires_action",
                    StringComparison.OrdinalIgnoreCase))
            {
                var eventId =
                    $"stripe-refund-create:{prepared.ProviderReference}:requires_action";
                if (!transaction.HasExternalEvent(
                        "stripe",
                        eventId))
                {
                    transaction.RecordRefundProgress(
                        "stripe",
                        eventId,
                        prepared.ProviderReference,
                        transaction.PaymentReference,
                        transaction.BuyerTotalSatang,
                        transaction.Currency,
                        prepared.Status,
                        clock.UtcNow,
                        clock.UtcNow,
                        prepared.ActionExpiresAt,
                        prepared.InstructionsSentAt);
                    transactionChanged = true;
                }
            }
            if (transactionChanged)
                changed++;
        }

        if (changed > 0)
            await unitOfWork.SaveChangesAsync(cancellationToken);
        return changed;
    }
}

public sealed record ReconcilePendingRefundsCommand : IRequest<int>;

public sealed class ReconcilePendingRefundsHandler(
    ITransactionRepository repository,
    IRefundReconciliationProvider refundProvider,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions)
    : IRequestHandler<ReconcilePendingRefundsCommand, int>
{
    public async Task<int> Handle(
        ReconcilePendingRefundsCommand request,
        CancellationToken cancellationToken)
    {
        var pending = await repository.GetPendingRefundsAsync(
            cancellationToken);
        var changed = 0;
        foreach (var transaction in pending)
        {
            if (string.IsNullOrWhiteSpace(
                    transaction.RefundReference))
                continue;
            var result = await refundProvider.ReconcileAsync(
                transaction.Id,
                transaction.RefundReference,
                cancellationToken);
            if (transaction.HasExternalEvent(
                    "stripe",
                    result.EventId))
                continue;
            if (result.Succeeded)
                transaction.ConfirmRefund(
                    "stripe",
                    result.EventId,
                    result.RefundReference,
                    result.PaymentReference,
                    result.AmountSatang,
                    result.Currency,
                    result.OccurredAt,
                    clock.UtcNow,
                    transitions);
            else if (result.Status is "pending" or
                     "requires_action")
                transaction.RecordRefundProgress(
                    "stripe",
                    result.EventId,
                    result.RefundReference,
                    result.PaymentReference,
                    result.AmountSatang,
                    result.Currency,
                    result.Status,
                    result.OccurredAt,
                    clock.UtcNow,
                    result.ActionExpiresAt,
                    result.InstructionsSentAt);
            else
                continue;
            changed++;
        }

        if (changed > 0)
            await unitOfWork.SaveChangesAsync(cancellationToken);
        return changed;
    }
}

public sealed record ResolveDisputeCommand(
    Guid TransactionId,
    string ReviewReference,
    DisputeResolution Resolution,
    DisputeDecisionAudit Audit) : IRequest;

public sealed record DisputeDecisionAudit(
    Guid CaseId,
    Guid ActionId,
    Guid RecommendedByUserId,
    Guid ApprovedByUserId,
    string ReasonCode,
    string Rationale,
    string IdempotencyKey);

public enum DisputeResolution
{
    FullPayout,
    FullRefund
}

public sealed class ResolveDisputeHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions)
    : IRequestHandler<ResolveDisputeCommand>
{
    public async Task Handle(
        ResolveDisputeCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = await repository.GetByIdAsync(
            request.TransactionId,
            cancellationToken)
            ?? throw new NotFoundException("ไม่พบรายการ");
        ValidateAudit(request.Audit);
        var metadata = JsonSerializer.Serialize(new
        {
            crmCaseId = request.Audit.CaseId,
            resolutionActionId = request.Audit.ActionId,
            recommendedByUserId =
                request.Audit.RecommendedByUserId,
            approvedByUserId =
                request.Audit.ApprovedByUserId,
            reasonCode = request.Audit.ReasonCode,
            rationale = request.Audit.Rationale
        });
        var now = clock.UtcNow;
        var resolvedState = request.Resolution ==
                            DisputeResolution.FullPayout
            ? TransactionState.PayoutEligible
            : TransactionState.RefundPending;
        if (transaction.State == resolvedState &&
            string.Equals(
                transaction.DisputeResolutionReference,
                request.ReviewReference.Trim(),
                StringComparison.Ordinal))
            return;
        if (transaction.State == TransactionState.Disputed)
            transaction.BeginDisputeResolution(
                request.ReviewReference,
                request.Audit.RecommendedByUserId
                    .ToString("N"),
                metadata,
                $"{request.Audit.IdempotencyKey}:review",
                now,
                transitions);
        if (transaction.State != TransactionState.ResolutionPending)
            throw new DomainException(
                "รายการนี้ไม่อยู่ในขั้นตอนตัดสินข้อโต้แย้ง");

        if (request.Resolution == DisputeResolution.FullPayout)
            transaction.ResolveDisputeForPayout(
                request.ReviewReference,
                request.Audit.ApprovedByUserId
                    .ToString("N"),
                metadata,
                $"{request.Audit.IdempotencyKey}:payout",
                now,
                transitions);
        else
            transaction.ResolveDisputeForRefund(
                request.ReviewReference,
                request.Audit.ApprovedByUserId
                    .ToString("N"),
                metadata,
                $"{request.Audit.IdempotencyKey}:refund",
                now,
                transitions);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateAudit(
        DisputeDecisionAudit audit)
    {
        if (audit.CaseId == Guid.Empty ||
            audit.ActionId == Guid.Empty ||
            audit.RecommendedByUserId == Guid.Empty ||
            audit.ApprovedByUserId == Guid.Empty ||
            audit.RecommendedByUserId ==
                audit.ApprovedByUserId ||
            string.IsNullOrWhiteSpace(audit.ReasonCode) ||
            string.IsNullOrWhiteSpace(audit.Rationale) ||
            string.IsNullOrWhiteSpace(audit.IdempotencyKey))
            throw new DomainException(
                "ข้อมูล two-person approval ไม่ครบถ้วน");
    }
}
