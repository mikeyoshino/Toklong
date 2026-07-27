using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Transactions;
using Toklong.Domain.Common;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.Checkout.BeginCheckout;

public sealed record BeginBuyerOfferCheckoutCommand(
    string BuyerToken,
    Guid BuyerId,
    bool AcceptedTerms) : IRequest<TransactionView>;

public sealed class BeginBuyerOfferCheckoutHandler(
    ITransactionRepository repository,
    IBuyerRepository buyers,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions)
    : IRequestHandler<BeginBuyerOfferCheckoutCommand, TransactionView>
{
    public async Task<TransactionView> Handle(
        BeginBuyerOfferCheckoutCommand request,
        CancellationToken cancellationToken)
    {
        if (!request.AcceptedTerms)
            throw new ArgumentException("กรุณายอมรับข้อตกลงของรายการก่อนชำระ");
        var transaction = await repository.GetByBuyerTokenAsync(
            request.BuyerToken, cancellationToken)
            ?? throw new NotFoundException("ไม่พบข้อเสนอของผู้ซื้อ");
        if (transaction.BuyerId != request.BuyerId)
            throw new DomainException("บัญชีผู้ซื้อนี้ไม่มีสิทธิ์ชำระข้อเสนอ");
        var now = clock.UtcNow;
        if (transaction.ExpireIfDue(now, transitions))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new DomainException(
                "หมดเวลาชำระแล้ว กรุณาส่งข้อเสนอใหม่ให้ผู้ขายยืนยัน");
        }

        var buyer = await buyers.GetByIdAsync(request.BuyerId, cancellationToken)
            ?? throw new NotFoundException("ไม่พบโปรไฟล์ผู้ซื้อ");
        transaction.BeginCheckout(
            buyer.FullName,
            buyer.PhoneNumber,
            now,
            transitions,
            transaction.PaymentProvider,
            null,
            transaction.BuyerProtectionFeeSatang,
            transaction.PlatformFeeSatang,
            transaction.SellerExpectedNetSatang,
            transaction.FeePolicyVersion);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return TransactionView.From(transaction);
    }
}
