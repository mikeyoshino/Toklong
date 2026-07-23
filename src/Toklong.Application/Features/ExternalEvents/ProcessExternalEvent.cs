using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Transactions;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.ExternalEvents;

public sealed record ConfirmManualPaymentCommand(Guid TransactionId, string EventId, DateTimeOffset ConfirmedAt) : IRequest<ExternalEventResult>;
public sealed record RecordCarrierEventCommand(Guid TransactionId, string EventId, string EventType, DateTimeOffset OccurredAt) : IRequest<ExternalEventResult>;
public sealed record ConfirmManualPayoutCommand(Guid TransactionId, string EventId, DateTimeOffset ConfirmedAt) : IRequest<ExternalEventResult>;
public sealed record ExternalEventResult(bool AlreadyProcessed, TransactionView Transaction);

public sealed class ConfirmManualPaymentHandler(
    ITransactionRepository repository, IUnitOfWork unitOfWork, TransactionTransitionService transitions)
    : IRequestHandler<ConfirmManualPaymentCommand, ExternalEventResult>
{
    public async Task<ExternalEventResult> Handle(ConfirmManualPaymentCommand request, CancellationToken cancellationToken)
    {
        var transaction = await repository.GetByIdAsync(request.TransactionId, cancellationToken)
            ?? throw new NotFoundException("ไม่พบรายการ");
        if (transaction.HasExternalEvent("manual-bank", request.EventId))
            return new(true, TransactionView.From(transaction));
        transaction.ConfirmPayment(request.EventId, request.ConfirmedAt, transitions);
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
        transaction.RecordCarrierEvent(request.EventId, request.EventType, request.OccurredAt, clock.UtcNow, transitions);
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
        if (transaction.HasExternalEvent("manual-bank", request.EventId))
            return new(true, TransactionView.From(transaction));
        transaction.ConfirmPayout(request.EventId, request.ConfirmedAt, transitions);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new(false, TransactionView.From(transaction));
    }
}
