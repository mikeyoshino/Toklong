using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;

namespace Toklong.Application.Features.Retention.ManageLegalHold;

public sealed record PlaceLegalHoldCommand(
    Guid TransactionId,
    string Reference,
    string Reason) : IRequest;

public sealed record ReleaseLegalHoldCommand(
    Guid TransactionId,
    string Reference) : IRequest;

public sealed class PlaceLegalHoldHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<PlaceLegalHoldCommand>
{
    public async Task Handle(
        PlaceLegalHoldCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = await repository.GetByIdAsync(
            request.TransactionId,
            cancellationToken)
            ?? throw new NotFoundException("ไม่พบรายการ");
        transaction.PlaceLegalHold(
            request.Reference,
            request.Reason,
            clock.UtcNow);
        await unitOfWork.SaveChangesAsync(
            cancellationToken);
    }
}

public sealed class ReleaseLegalHoldHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<ReleaseLegalHoldCommand>
{
    public async Task Handle(
        ReleaseLegalHoldCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = await repository.GetByIdAsync(
            request.TransactionId,
            cancellationToken)
            ?? throw new NotFoundException("ไม่พบรายการ");
        transaction.ReleaseLegalHold(
            request.Reference,
            clock.UtcNow);
        await unitOfWork.SaveChangesAsync(
            cancellationToken);
    }
}
