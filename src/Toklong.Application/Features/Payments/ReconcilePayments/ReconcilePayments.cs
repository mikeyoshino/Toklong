using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Domain.Transactions;
using Toklong.Application.Features.Shipping;

namespace Toklong.Application.Features.Payments.ReconcilePayments;

public sealed record ReconcilePendingPaymentsCommand : IRequest<int>;

public sealed class ReconcilePendingPaymentsHandler(
    ITransactionRepository repository,
    IPaymentReconciliationProvider provider,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions)
    : IRequestHandler<ReconcilePendingPaymentsCommand, int>
{
    public async Task<int> Handle(
        ReconcilePendingPaymentsCommand request,
        CancellationToken cancellationToken)
    {
        var pending = await repository
            .GetPendingProviderPaymentsAsync(cancellationToken);
        var changed = 0;
        foreach (var transaction in pending)
        {
            if (string.IsNullOrWhiteSpace(
                    transaction.PaymentReference))
                continue;
            var result = await provider.ReconcileAsync(
                transaction.Id,
                transaction.PaymentReference,
                cancellationToken);
            if (!result.Succeeded ||
                transaction.HasExternalEvent(
                    "stripe",
                    result.EventId))
                continue;
            transaction.ConfirmStripePayment(
                result.EventId,
                transaction.PaymentReference,
                result.AmountSatang,
                result.Currency,
                result.OccurredAt,
                clock.UtcNow,
                transitions);
            ManagedShippingOperationQueue.QueueConfirmationIfRequired(
                transaction,
                clock.UtcNow,
                ActorRole.Reconciliation,
                "stripe-reconciliation");
            changed++;
        }

        if (changed > 0)
            await unitOfWork.SaveChangesAsync(cancellationToken);
        return changed;
    }
}
