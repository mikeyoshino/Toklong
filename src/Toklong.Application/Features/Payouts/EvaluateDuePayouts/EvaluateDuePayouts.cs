using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.Payouts.EvaluateDuePayouts;

public sealed record EvaluateDuePayoutsCommand : IRequest<int>;

public sealed class EvaluateDuePayoutsHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions,
    IManualPayoutProvider payoutProvider) : IRequestHandler<EvaluateDuePayoutsCommand, int>
{
    public async Task<int> Handle(EvaluateDuePayoutsCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var due = await repository.GetDueForReleaseAsync(now, cancellationToken);
        foreach (var transaction in due)
        {
            transaction.EvaluateDeadline(now, transitions);
            if (transaction.State == TransactionState.PayoutEligible)
                transaction.StartPayout(payoutProvider.CreateInstructionReference(transaction.Id), now, transitions);
        }

        if (due.Count > 0)
            await unitOfWork.SaveChangesAsync(cancellationToken);
        return due.Count;
    }
}
