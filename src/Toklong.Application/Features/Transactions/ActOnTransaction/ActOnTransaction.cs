using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Transactions;
using Toklong.Domain.Common;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.Transactions.ActOnTransaction;

public sealed record SubmitTrackingForSellerCommand(
    Guid TransactionId,
    Guid SellerId,
    string CarrierCode,
    string TrackingNumber) : IRequest<TransactionView>;

public sealed class SubmitTrackingForSellerHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions)
    : IRequestHandler<SubmitTrackingForSellerCommand, TransactionView>
{
    public async Task<TransactionView> Handle(
        SubmitTrackingForSellerCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = await AuthorizedSellerTransaction.GetAsync(
            repository,
            request.TransactionId,
            request.SellerId,
            cancellationToken);
        transaction.SubmitTracking(
            transaction.SellerAccessToken,
            request.CarrierCode,
            request.TrackingNumber,
            clock.UtcNow,
            transitions);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return TransactionView.From(transaction);
    }
}

public sealed record SubmitDigitalHandoffForSellerCommand(
    Guid TransactionId,
    Guid SellerId,
    string Statement) : IRequest<TransactionView>;

public sealed class SubmitDigitalHandoffForSellerHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions)
    : IRequestHandler<SubmitDigitalHandoffForSellerCommand, TransactionView>
{
    public async Task<TransactionView> Handle(
        SubmitDigitalHandoffForSellerCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = await AuthorizedSellerTransaction.GetAsync(
            repository,
            request.TransactionId,
            request.SellerId,
            cancellationToken);
        transaction.SubmitDigitalDelivery(
            transaction.SellerAccessToken,
            request.Statement,
            clock.UtcNow,
            transitions);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return TransactionView.From(transaction);
    }
}

public sealed record ConfirmReceiptForBuyerCommand(
    Guid TransactionId,
    Guid BuyerId) : IRequest<TransactionView>;

public sealed class ConfirmReceiptForBuyerHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions,
    IPayoutProvider payoutProvider)
    : IRequestHandler<ConfirmReceiptForBuyerCommand, TransactionView>
{
    public async Task<TransactionView> Handle(
        ConfirmReceiptForBuyerCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = await AuthorizedBuyerTransaction.GetAsync(
            repository,
            request.TransactionId,
            request.BuyerId,
            cancellationToken);
        var now = clock.UtcNow;
        transaction.ConfirmReceipt(
            transaction.BuyerAccessToken!,
            now,
            transitions);
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

public sealed record OpenDisputeForBuyerCommand(
    Guid TransactionId,
    Guid BuyerId,
    DisputeReason Reason,
    string Statement) : IRequest<TransactionView>;

public sealed class OpenDisputeForBuyerHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions)
    : IRequestHandler<OpenDisputeForBuyerCommand, TransactionView>
{
    public async Task<TransactionView> Handle(
        OpenDisputeForBuyerCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = await AuthorizedBuyerTransaction.GetAsync(
            repository,
            request.TransactionId,
            request.BuyerId,
            cancellationToken);
        transaction.OpenDispute(
            transaction.BuyerAccessToken!,
            request.Reason,
            request.Statement,
            clock.UtcNow,
            transitions);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return TransactionView.From(transaction);
    }
}

internal static class AuthorizedSellerTransaction
{
    public static async Task<SaleTransaction> GetAsync(
        ITransactionRepository repository,
        Guid transactionId,
        Guid sellerId,
        CancellationToken cancellationToken)
    {
        var transaction = await repository.GetByIdAsync(
            transactionId,
            cancellationToken)
            ?? throw new NotFoundException("ไม่พบรายการ");
        if (transaction.SellerId != sellerId)
            throw new DomainException("บัญชีนี้ไม่มีสิทธิ์จัดการรายการขายนี้");
        return transaction;
    }
}

internal static class AuthorizedBuyerTransaction
{
    public static async Task<SaleTransaction> GetAsync(
        ITransactionRepository repository,
        Guid transactionId,
        Guid buyerId,
        CancellationToken cancellationToken)
    {
        var transaction = await repository.GetByIdAsync(
            transactionId,
            cancellationToken)
            ?? throw new NotFoundException("ไม่พบรายการ");
        if (transaction.BuyerId != buyerId ||
            string.IsNullOrWhiteSpace(transaction.BuyerAccessToken))
            throw new DomainException("บัญชีนี้ไม่มีสิทธิ์จัดการรายการซื้อนี้");
        return transaction;
    }
}
