using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Domain.Transactions;
using Toklong.Application.Features.Shipping;

namespace Toklong.Application.Features.Transactions.EvaluateDueExpirations;

public sealed record EvaluateDueExpirationsCommand : IRequest<int>;

public sealed class EvaluateDueExpirationsHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions)
    : IRequestHandler<EvaluateDueExpirationsCommand, int>
{
    public async Task<int> Handle(
        EvaluateDueExpirationsCommand request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var due = await repository.GetDueForExpirationAsync(
            now,
            cancellationToken);
        var changed = 0;

        foreach (var transaction in due)
        {
            if (transaction.ExpireIfDue(now, transitions))
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
