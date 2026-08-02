using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.Shipping.GetCounterQr;

public sealed record GetCounterQrQuery(
    Guid TransactionId,
    Guid SellerId) : IRequest<CounterQrArtifact>;

public sealed class GetCounterQrHandler(
    ITransactionRepository transactions,
    ICounterQrArtifactProtector artifactProtector,
    IClock clock) : IRequestHandler<
        GetCounterQrQuery,
        CounterQrArtifact>
{
    public async Task<CounterQrArtifact> Handle(
        GetCounterQrQuery request,
        CancellationToken cancellationToken)
    {
        var transaction = await transactions.GetByIdAsync(
            request.TransactionId,
            cancellationToken)
            ?? throw new NotFoundException("ไม่พบรายการ");
        if (transaction.SellerId != request.SellerId)
            throw new ForbiddenException(
                "เฉพาะผู้ขายของรายการนี้เท่านั้น");
        var shipment = transaction.CurrentOutboundShipment;
        if (shipment is null ||
            !transaction.IsCounterQrAccessAllowed(shipment))
            throw new ForbiddenException(
                "รายการนี้ไม่อนุญาตให้เปิด QR เคาน์เตอร์");
        var resource = shipment.CounterQrResource
            ?? throw new CounterQrNotReadyException(
                "กำลังเตรียม QR เคาน์เตอร์");
        if (resource.Status != CounterQrResourceStatus.Ready ||
            resource.ProtectedArtifact is null ||
            resource.ProtectionVersion is null ||
            resource.ArtifactSha256 is null ||
            resource.ProviderExpiresAt <= clock.UtcNow)
            throw new CounterQrNotReadyException(
                resource.Status ==
                    CounterQrResourceStatus.Unavailable
                    ? "ยังไม่สามารถโหลด QR เคาน์เตอร์ได้"
                    : "กำลังเตรียม QR เคาน์เตอร์");
        return artifactProtector.Unprotect(
            new ProtectedCounterQrArtifact(
                resource.ProtectedArtifact,
                resource.ProtectionVersion,
                resource.ArtifactSha256));
    }
}
