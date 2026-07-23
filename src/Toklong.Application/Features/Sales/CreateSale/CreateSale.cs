using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Transactions;
using Toklong.Domain.Common;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.Sales.CreateSale;

public sealed record CreateSaleCommand(
    Guid SellerId,
    Guid PayoutAccountId,
    FulfillmentType FulfillmentType,
    string ProductName,
    string Category,
    ConditionCode Condition,
    string Description,
    string KnownDefects,
    string PhotoUrl,
    long PriceSatang,
    long ShippingFeeSatang,
    int ShipByDurationHours,
    bool SellerAcceptedTerms,
    bool ProhibitedGoodsAttested) : IRequest<TransactionView>;

public sealed class CreateSaleHandler(
    ITransactionRepository repository,
    ISellerRepository sellers,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions) : IRequestHandler<CreateSaleCommand, TransactionView>
{
    public async Task<TransactionView> Handle(CreateSaleCommand request, CancellationToken cancellationToken)
    {
        if (!request.SellerAcceptedTerms)
            throw new ArgumentException("กรุณายอมรับข้อตกลงของผู้ขาย");
        if (!request.ProhibitedGoodsAttested)
            throw new ArgumentException("กรุณายืนยันว่าสินค้าไม่อยู่ในรายการต้องห้าม");
        var seller = await sellers.GetByIdAsync(request.SellerId, cancellationToken)
            ?? throw new NotFoundException("ไม่พบโปรไฟล์ผู้ขาย");
        var payout = seller.PayoutAccounts.SingleOrDefault(
            x => x.Id == request.PayoutAccountId)
            ?? throw new DomainException("กรุณาเลือกบัญชีรับเงินของคุณ");
        var policy = ProductPolicy.Evaluate(
            request.FulfillmentType,
            request.Category,
            request.ProductName,
            request.Description);
        if (!policy.Allowed)
        {
            await repository.AddRiskEventAsync(
                new ActivationRiskEvent(policy.ReasonCode, request.Category, clock.UtcNow),
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new DomainException(policy.UserMessage);
        }

        var transaction = SaleTransaction.CreateAndActivate(
            seller.Id,
            seller.DisplayName, seller.PhoneNumber,
            payout.BankCode, payout.AccountName, payout.AccountNumber,
            request.FulfillmentType,
            request.ProductName, request.Category,
            request.Condition, request.Description, request.KnownDefects, request.PhotoUrl,
            request.PriceSatang, request.ShippingFeeSatang, request.ShipByDurationHours,
            "mvp-th-2026-07", clock.UtcNow, transitions);

        await repository.AddAsync(transaction, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return TransactionView.From(transaction);
    }
}
