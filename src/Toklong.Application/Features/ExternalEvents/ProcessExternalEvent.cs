using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Transactions;
using Toklong.Application.Features.Shipping;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.ExternalEvents;

public sealed record ConfirmManualPaymentCommand(Guid TransactionId, string EventId, DateTimeOffset ConfirmedAt) : IRequest<ExternalEventResult>;
public sealed record RecordCarrierEventCommand(
    Guid TransactionId,
    string EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    string CarrierCode,
    string TrackingNumber) : IRequest<ExternalEventResult>;
public sealed record ConfirmManualPayoutCommand(Guid TransactionId, string EventId, DateTimeOffset ConfirmedAt) : IRequest<ExternalEventResult>;
public sealed record ConfirmStripePaymentCommand(
    Guid TransactionId,
    string EventId,
    string PaymentIntentId,
    long AmountSatang,
    string Currency,
    DateTimeOffset ConfirmedAt) : IRequest<ExternalEventResult>;
public sealed record ConfirmStripeRefundCommand(
    Guid TransactionId,
    string EventId,
    string RefundId,
    string PaymentIntentId,
    long AmountSatang,
    string Currency,
    DateTimeOffset ConfirmedAt) : IRequest<ExternalEventResult>;
public sealed record RecordStripeRefundProgressCommand(
    Guid TransactionId,
    string EventId,
    string RefundId,
    string PaymentIntentId,
    long AmountSatang,
    string Currency,
    string Status,
    DateTimeOffset OccurredAt,
    DateTimeOffset? ActionExpiresAt,
    DateTimeOffset? InstructionsSentAt)
    : IRequest<ExternalEventResult>;
public sealed record ExternalEventResult(bool AlreadyProcessed, TransactionView Transaction);

public sealed class ConfirmManualPaymentHandler(
    ITransactionRepository repository, IUnitOfWork unitOfWork, TransactionTransitionService transitions)
    : IRequestHandler<ConfirmManualPaymentCommand, ExternalEventResult>
{
    public async Task<ExternalEventResult> Handle(ConfirmManualPaymentCommand request, CancellationToken cancellationToken)
    {
        var transaction = await repository.GetByIdAsync(request.TransactionId, cancellationToken)
            ?? throw new NotFoundException("ไม่พบรายการ");
        if (transaction.HasExternalEvent(
                "manual-bank",
                request.EventId))
            return new(true, TransactionView.From(transaction));
        transaction.ConfirmPayment(request.EventId, request.ConfirmedAt, transitions);
        ManagedShippingOperationQueue.QueueConfirmationIfRequired(
            transaction,
            request.ConfirmedAt,
            ActorRole.Reconciliation,
            "manual-bank");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new(false, TransactionView.From(transaction));
    }
}

public sealed class RecordCarrierEventHandler(
    ITransactionRepository repository, IUnitOfWork unitOfWork, IClock clock, TransactionTransitionService transitions)
    : IRequestHandler<RecordCarrierEventCommand, ExternalEventResult>
{
    public async Task<ExternalEventResult> Handle(RecordCarrierEventCommand request, CancellationToken cancellationToken)
    {
        var transaction = await repository.GetByIdAsync(request.TransactionId, cancellationToken)
            ?? throw new NotFoundException("ไม่พบรายการ");
        var provider = transaction.CarrierCode ?? "carrier";
        if (transaction.HasExternalEvent(provider, request.EventId))
            return new(true, TransactionView.From(transaction));
        transaction.RecordCarrierEvent(
            request.EventId,
            request.EventType,
            request.OccurredAt,
            clock.UtcNow,
            transitions,
            request.CarrierCode,
            request.TrackingNumber);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new(false, TransactionView.From(transaction));
    }
}

public sealed class ConfirmManualPayoutHandler(
    ITransactionRepository repository, IUnitOfWork unitOfWork, TransactionTransitionService transitions)
    : IRequestHandler<ConfirmManualPayoutCommand, ExternalEventResult>
{
    public async Task<ExternalEventResult> Handle(ConfirmManualPayoutCommand request, CancellationToken cancellationToken)
    {
        var transaction = await repository.GetByIdAsync(request.TransactionId, cancellationToken)
            ?? throw new NotFoundException("ไม่พบรายการ");
        if (transaction.HasExternalEvent(
                transaction.PayoutProvider,
                request.EventId))
            return new(true, TransactionView.From(transaction));
        transaction.ConfirmPayout(request.EventId, request.ConfirmedAt, transitions);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new(false, TransactionView.From(transaction));
    }
}

public sealed class ConfirmStripePaymentHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions)
    : IRequestHandler<ConfirmStripePaymentCommand, ExternalEventResult>
{
    public async Task<ExternalEventResult> Handle(
        ConfirmStripePaymentCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = await repository.GetByIdAsync(
            request.TransactionId,
            cancellationToken)
            ?? throw new NotFoundException("ไม่พบรายการ");
        if (transaction.HasExternalEvent("stripe", request.EventId))
            return new(true, TransactionView.From(transaction));

        transaction.ConfirmStripePayment(
            request.EventId,
            request.PaymentIntentId,
            request.AmountSatang,
            request.Currency,
            request.ConfirmedAt,
            clock.UtcNow,
            transitions);
        ManagedShippingOperationQueue.QueueConfirmationIfRequired(
            transaction,
            clock.UtcNow,
            ActorRole.PaymentProvider,
            "stripe");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new(false, TransactionView.From(transaction));
    }
}

public sealed class ConfirmStripeRefundHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions)
    : IRequestHandler<ConfirmStripeRefundCommand, ExternalEventResult>
{
    public async Task<ExternalEventResult> Handle(
        ConfirmStripeRefundCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = await repository.GetByIdAsync(
            request.TransactionId,
            cancellationToken)
            ?? throw new NotFoundException("ไม่พบรายการ");
        if (transaction.HasExternalEvent("stripe", request.EventId))
            return new(true, TransactionView.From(transaction));
        transaction.ConfirmRefund(
            "stripe",
            request.EventId,
            request.RefundId,
            request.PaymentIntentId,
            request.AmountSatang,
            request.Currency,
            request.ConfirmedAt,
            clock.UtcNow,
            transitions);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new(false, TransactionView.From(transaction));
    }
}

public sealed class RecordStripeRefundProgressHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<
        RecordStripeRefundProgressCommand,
        ExternalEventResult>
{
    public async Task<ExternalEventResult> Handle(
        RecordStripeRefundProgressCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = await repository.GetByIdAsync(
            request.TransactionId,
            cancellationToken)
            ?? throw new NotFoundException("ไม่พบรายการ");
        if (transaction.HasExternalEvent(
                "stripe",
                request.EventId))
            return new(
                true,
                TransactionView.From(transaction));

        transaction.RecordRefundProgress(
            "stripe",
            request.EventId,
            request.RefundId,
            request.PaymentIntentId,
            request.AmountSatang,
            request.Currency,
            request.Status,
            request.OccurredAt,
            clock.UtcNow,
            request.ActionExpiresAt,
            request.InstructionsSentAt);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new(
            false,
            TransactionView.From(transaction));
    }
}
