using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Transactions;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.Transactions.GetTransaction;

public sealed record GetPublicTransactionQuery(string PublicToken) : IRequest<TransactionView>;
public sealed record GetSellerTransactionQuery(string SellerToken) : IRequest<TransactionView>;
public sealed record GetBuyerTransactionQuery(string BuyerToken) : IRequest<TransactionView>;

public sealed class GetPublicTransactionHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions)
    : IRequestHandler<GetPublicTransactionQuery, TransactionView>
{
    public async Task<TransactionView> Handle(
        GetPublicTransactionQuery request,
        CancellationToken cancellationToken) =>
        await LoadAsync(
            await repository.GetByPublicTokenAsync(
                request.PublicToken,
                cancellationToken),
            "ไม่พบลิงก์รายการ",
            cancellationToken);

    private async Task<TransactionView> LoadAsync(
        SaleTransaction? transaction,
        string notFoundMessage,
        CancellationToken cancellationToken)
    {
        if (transaction is null)
            throw new NotFoundException(notFoundMessage);
        if (transaction.ExpireIfDue(clock.UtcNow, transitions))
            await unitOfWork.SaveChangesAsync(cancellationToken);
        return TransactionView.From(transaction);
    }
}

public sealed class GetSellerTransactionHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions)
    : IRequestHandler<GetSellerTransactionQuery, TransactionView>
{
    public async Task<TransactionView> Handle(
        GetSellerTransactionQuery request,
        CancellationToken cancellationToken)
    {
        var transaction = await repository.GetBySellerTokenAsync(
            request.SellerToken,
            cancellationToken)
            ?? throw new NotFoundException(
                "ไม่มีสิทธิ์เปิดรายการผู้ขายนี้");
        if (transaction.ExpireIfDue(clock.UtcNow, transitions))
            await unitOfWork.SaveChangesAsync(cancellationToken);
        return TransactionView.From(transaction);
    }
}

public sealed class GetBuyerTransactionHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions)
    : IRequestHandler<GetBuyerTransactionQuery, TransactionView>
{
    public async Task<TransactionView> Handle(
        GetBuyerTransactionQuery request,
        CancellationToken cancellationToken)
    {
        var transaction = await repository.GetByBuyerTokenAsync(
            request.BuyerToken,
            cancellationToken)
            ?? throw new NotFoundException(
                "ไม่มีสิทธิ์เปิดรายการผู้ซื้อนี้");
        if (transaction.ExpireIfDue(clock.UtcNow, transitions))
            await unitOfWork.SaveChangesAsync(cancellationToken);
        return TransactionView.From(transaction);
    }
}
