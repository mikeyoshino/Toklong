using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Transactions;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.Disputes.OpenDispute;

public sealed record OpenDisputeCommand(
    string BuyerToken,
    DisputeReason Reason,
    string Statement) : IRequest<TransactionView>;

public sealed class OpenDisputeHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions) : IRequestHandler<OpenDisputeCommand, TransactionView>
{
    public async Task<TransactionView> Handle(OpenDisputeCommand request, CancellationToken cancellationToken)
    {
        var transaction = await repository.GetByBuyerTokenAsync(request.BuyerToken, cancellationToken)
            ?? throw new NotFoundException("ไม่มีสิทธิ์เปิดรายการผู้ซื้อนี้");
        transaction.OpenDispute(request.BuyerToken, request.Reason, request.Statement, clock.UtcNow, transitions);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return TransactionView.From(transaction);
    }
}
