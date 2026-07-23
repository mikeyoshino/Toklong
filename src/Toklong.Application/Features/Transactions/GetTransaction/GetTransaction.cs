using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Transactions;

namespace Toklong.Application.Features.Transactions.GetTransaction;

public sealed record GetPublicTransactionQuery(string PublicToken) : IRequest<TransactionView>;
public sealed record GetSellerTransactionQuery(string SellerToken) : IRequest<TransactionView>;
public sealed record GetBuyerTransactionQuery(string BuyerToken) : IRequest<TransactionView>;

public sealed class GetPublicTransactionHandler(ITransactionRepository repository)
    : IRequestHandler<GetPublicTransactionQuery, TransactionView>
{
    public async Task<TransactionView> Handle(GetPublicTransactionQuery request, CancellationToken cancellationToken) =>
        TransactionView.From(await repository.GetByPublicTokenAsync(request.PublicToken, cancellationToken)
            ?? throw new NotFoundException("ไม่พบลิงก์รายการ"));
}

public sealed class GetSellerTransactionHandler(ITransactionRepository repository)
    : IRequestHandler<GetSellerTransactionQuery, TransactionView>
{
    public async Task<TransactionView> Handle(GetSellerTransactionQuery request, CancellationToken cancellationToken) =>
        TransactionView.From(await repository.GetBySellerTokenAsync(request.SellerToken, cancellationToken)
            ?? throw new NotFoundException("ไม่มีสิทธิ์เปิดรายการผู้ขายนี้"));
}

public sealed class GetBuyerTransactionHandler(ITransactionRepository repository)
    : IRequestHandler<GetBuyerTransactionQuery, TransactionView>
{
    public async Task<TransactionView> Handle(GetBuyerTransactionQuery request, CancellationToken cancellationToken) =>
        TransactionView.From(await repository.GetByBuyerTokenAsync(request.BuyerToken, cancellationToken)
            ?? throw new NotFoundException("ไม่มีสิทธิ์เปิดรายการผู้ซื้อนี้"));
}
