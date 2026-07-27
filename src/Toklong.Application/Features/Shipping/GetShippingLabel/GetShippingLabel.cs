using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Domain.Common;

namespace Toklong.Application.Features.Shipping.GetShippingLabel;

public sealed record GetShippingLabelQuery(
    Guid TransactionId,
    Guid SellerId) : IRequest<string>;

public sealed class GetShippingLabelHandler(
    ITransactionRepository repository,
    IShipmentProvider shipmentProvider)
    : IRequestHandler<GetShippingLabelQuery, string>
{
    public async Task<string> Handle(
        GetShippingLabelQuery request,
        CancellationToken cancellationToken)
    {
        var transaction = await repository.GetByIdAsync(
            request.TransactionId,
            cancellationToken)
            ?? throw new NotFoundException("ไม่พบรายการ");
        if (transaction.SellerId != request.SellerId)
            throw new ForbiddenException(
                "ไม่มีสิทธิ์เปิดใบปะหน้าของรายการนี้");
        if (!transaction.PaymentConfirmedAt.HasValue ||
            !transaction.ShippingConfirmedAt.HasValue)
            throw new DomainException(
                "กำลังออกเลขพัสดุ กรุณารอสักครู่แล้วลองใหม่");
        if (!transaction.IsProviderManagedShipment ||
            !string.Equals(
                transaction.ShippingQuoteProvider,
                shipmentProvider.ProviderName,
                StringComparison.Ordinal))
            throw new DomainException(
                "รายการนี้ไม่มีใบปะหน้าจากระบบขนส่ง");
        return await shipmentProvider.GetLabelHtmlAsync(
            new ShipmentLabelRequest(
                transaction.ShippingPurchaseReference!,
                Required(
                    transaction.CarrierCode,
                    "บริษัทขนส่ง"),
                Required(
                    transaction.ShippingServiceName,
                    "บริการขนส่ง"),
                Required(
                    transaction.TrackingNumber,
                    "เลขพัสดุ"),
                new ShippingContactAddress(
                    Required(
                        transaction.SellerDisplayName,
                        "ชื่อผู้ส่ง"),
                    Required(
                        transaction.SellerContact,
                        "เบอร์โทรศัพท์ผู้ส่ง"),
                    Required(
                        transaction.ShippingOriginAddressLine ??
                        transaction.ShippingOriginAddress,
                        "ที่อยู่ต้นทาง"),
                    transaction.ShippingOriginSubdistrictName ?? "",
                    transaction.ShippingOriginDistrictName ?? "",
                    Required(
                        transaction.ShippingOriginProvinceName,
                        "จังหวัดต้นทาง"),
                    Required(
                        transaction.ShippingOriginPostalCode,
                        "รหัสไปรษณีย์ต้นทาง")),
                new ShippingContactAddress(
                    Required(
                        transaction.BuyerDisplayName,
                        "ชื่อผู้รับ"),
                    Required(
                        transaction.BuyerContact,
                        "เบอร์โทรศัพท์ผู้รับ"),
                    Required(
                        transaction.DeliveryAddressLine ??
                        transaction.DeliveryAddress,
                        "ที่อยู่ปลายทาง"),
                    transaction.DeliverySubdistrictName ?? "",
                    transaction.DeliveryDistrictName ?? "",
                    Required(
                        transaction.DeliveryProvinceName,
                        "จังหวัดปลายทาง"),
                    Required(
                        transaction.DeliveryPostalCode,
                        "รหัสไปรษณีย์ปลายทาง")),
                transaction.PackageWeightGrams
                ?? throw new DomainException(
                    "ไม่พบน้ำหนักพัสดุ")),
            cancellationToken);
    }

    private static string Required(
        string? value,
        string label) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new DomainException(
                $"ไม่พบ{label}สำหรับใบปะหน้า")
            : value.Trim();
}
