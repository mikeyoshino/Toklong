using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Transactions;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.Checkout.BeginCheckout;

public sealed record BeginCheckoutCommand(
    string PublicToken,
    string BuyerDisplayName,
    string BuyerContact,
    string DeliveryAddress,
    bool AcceptedTerms) : IRequest<TransactionView>;

public sealed class BeginCheckoutHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions) : IRequestHandler<BeginCheckoutCommand, TransactionView>
{
    public async Task<TransactionView> Handle(BeginCheckoutCommand request, CancellationToken cancellationToken)
    {
        if (!request.AcceptedTerms)
            throw new ArgumentException("กรุณายอมรับข้อตกลงของรายการก่อนชำระ");
        var transaction = await repository.GetByPublicTokenAsync(request.PublicToken, cancellationToken)
            ?? throw new NotFoundException("ไม่พบลิงก์รายการ");
        transaction.BeginCheckout(request.BuyerDisplayName, request.BuyerContact, request.DeliveryAddress, clock.UtcNow, transitions);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return TransactionView.From(transaction);
    }
}
