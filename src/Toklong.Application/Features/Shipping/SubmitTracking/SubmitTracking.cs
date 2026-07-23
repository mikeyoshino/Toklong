using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Transactions;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.Shipping.SubmitTracking;

public sealed record SubmitTrackingCommand(string SellerToken, string CarrierCode, string TrackingNumber) : IRequest<TransactionView>;

public sealed class SubmitTrackingHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions) : IRequestHandler<SubmitTrackingCommand, TransactionView>
{
    public async Task<TransactionView> Handle(SubmitTrackingCommand request, CancellationToken cancellationToken)
    {
        var transaction = await repository.GetBySellerTokenAsync(request.SellerToken, cancellationToken)
            ?? throw new NotFoundException("ไม่มีสิทธิ์เปิดรายการผู้ขายนี้");
        transaction.SubmitTracking(request.SellerToken, request.CarrierCode, request.TrackingNumber, clock.UtcNow, transitions);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return TransactionView.From(transaction);
    }
}
