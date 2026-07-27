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
    IPayoutProvider payoutProvider) : IRequestHandler<ConfirmReceiptCommand, TransactionView>
{
    public async Task<TransactionView> Handle(ConfirmReceiptCommand request, CancellationToken cancellationToken)
    {
        var transaction = await repository.GetByBuyerTokenAsync(request.BuyerToken, cancellationToken)
            ?? throw new NotFoundException("ไม่มีสิทธิ์เปิดรายการผู้ซื้อนี้");
        var now = clock.UtcNow;
        transaction.ConfirmReceipt(request.BuyerToken, now, transitions);
        var payout = await payoutProvider.CreateInstructionAsync(
            transaction.Id,
            transaction.SellerExpectedNetSatang,
            transaction.Currency,
            transaction.PayoutBankCode,
            transaction.PayoutAccountName,
            transaction.PayoutAccountNumber,
            cancellationToken);
        transaction.StartPayout(
            payout.ProviderReference,
            now,
            transitions,
            payout.Provider);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return TransactionView.From(transaction);
    }
}
