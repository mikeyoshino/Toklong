using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Transactions;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.DigitalDelivery.SubmitDigitalDelivery;

public sealed record SubmitDigitalDeliveryCommand(
    string SellerToken,
    string Statement) : IRequest<TransactionView>;

public sealed class SubmitDigitalDeliveryHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions)
    : IRequestHandler<SubmitDigitalDeliveryCommand, TransactionView>
{
    public async Task<TransactionView> Handle(
        SubmitDigitalDeliveryCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = await repository.GetBySellerTokenAsync(
                request.SellerToken, cancellationToken)
            ?? throw new NotFoundException("ไม่มีสิทธิ์เปิดรายการผู้ขายนี้");

        transaction.SubmitDigitalDelivery(
            request.SellerToken,
            request.Statement,
            clock.UtcNow,
            transitions);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return TransactionView.From(transaction);
    }
}
