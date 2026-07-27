using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Transactions;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.Transactions.ListTransactions;

public sealed record ListPartyTransactionsQuery(
    Guid? BuyerId,
    Guid? SellerId,
    string? SellerPhoneNumber)
    : IRequest<IReadOnlyList<TransactionView>>;

public sealed class ListPartyTransactionsHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions)
    : IRequestHandler<ListPartyTransactionsQuery, IReadOnlyList<TransactionView>>
{
    public async Task<IReadOnlyList<TransactionView>> Handle(
        ListPartyTransactionsQuery request,
        CancellationToken cancellationToken)
    {
        if (!request.BuyerId.HasValue &&
            !request.SellerId.HasValue &&
            string.IsNullOrWhiteSpace(
                request.SellerPhoneNumber))
            return [];

        var sellerPhoneNumber =
            string.IsNullOrWhiteSpace(
                request.SellerPhoneNumber)
                ? null
                : ThaiMobilePhone.Normalize(
                    request.SellerPhoneNumber);
        var transactions = await repository.GetForPartiesAsync(
            request.BuyerId,
            request.SellerId,
            sellerPhoneNumber,
            cancellationToken);
        var now = clock.UtcNow;
        var changed = transactions.Count(transaction =>
            transaction.ExpireIfDue(now, transitions));
        if (changed > 0)
            await unitOfWork.SaveChangesAsync(cancellationToken);
        return transactions.Select(TransactionView.From).ToArray();
    }
}
