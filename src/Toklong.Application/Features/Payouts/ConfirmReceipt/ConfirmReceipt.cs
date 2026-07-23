using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Transactions;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.Payouts.ConfirmReceipt;

public sealed record ConfirmReceiptCommand(string BuyerToken) : IRequest<TransactionView>;

public sealed class ConfirmReceiptHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions,
    IManualPayoutProvider payoutProvider) : IRequestHandler<ConfirmReceiptCommand, TransactionView>
{
    public async Task<TransactionView> Handle(ConfirmReceiptCommand request, CancellationToken cancellationToken)
    {
        var transaction = await repository.GetByBuyerTokenAsync(request.BuyerToken, cancellationToken)
            ?? throw new NotFoundException("ไม่มีสิทธิ์เปิดรายการผู้ซื้อนี้");
        var now = clock.UtcNow;
        transaction.ConfirmReceipt(request.BuyerToken, now, transitions);
        transaction.StartPayout(payoutProvider.CreateInstructionReference(transaction.Id), now, transitions);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return TransactionView.From(transaction);
    }
}
