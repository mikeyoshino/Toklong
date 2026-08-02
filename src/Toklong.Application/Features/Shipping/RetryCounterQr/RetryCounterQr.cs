using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Domain.Common;
using Toklong.Application.Common;

namespace Toklong.Application.Features.Shipping.RetryCounterQr;

public sealed record RetryCounterQrCommand(
    Guid TransactionId,
    Guid SellerId) : IRequest<bool>;

public sealed class RetryCounterQrHandler(
    ITransactionRepository transactions,
    IUnitOfWork unitOfWork,
    IClock clock) : IRequestHandler<RetryCounterQrCommand, bool>
{
    public async Task<bool> Handle(
        RetryCounterQrCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = await transactions.GetByIdAsync(
            request.TransactionId,
            cancellationToken)
            ?? throw new DomainException("ไม่พบรายการซื้อขาย");
        if (transaction.SellerId != request.SellerId)
            throw new ForbiddenException(
                "เฉพาะผู้ขายของรายการนี้เท่านั้น");
        var shipment = transaction.CurrentOutboundShipment;
        if (shipment is null ||
            !transaction.IsCounterQrAccessAllowed(shipment))
            throw new ForbiddenException(
                "รายการนี้ไม่อนุญาตให้ขอ QR เคาน์เตอร์ใหม่");
        var resource = shipment.CounterQrResource
            ?? throw new DomainException(
                "ยังไม่มี QR สำหรับรายการจัดส่งนี้");
        var changed = resource.RequestRetry(clock.UtcNow);
        if (changed)
        {
            transaction.RecordShipmentCounterQrOutcome(
                resource.Id,
                "retry_requested",
                null,
                request.SellerId.ToString("N"),
                clock.UtcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return changed;
    }
}
