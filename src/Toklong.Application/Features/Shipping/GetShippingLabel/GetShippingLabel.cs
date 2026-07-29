using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Domain.Common;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.Shipping.GetShippingLabel;

public sealed record GetShippingLabelQuery(
    Guid TransactionId,
    Guid PartyId,
    ShipmentDirection Direction =
        ShipmentDirection.Outbound) : IRequest<string>;

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
        var authorizedPartyId = request.Direction ==
                                ShipmentDirection.Return
            ? transaction.BuyerId
            : transaction.SellerId;
        if (authorizedPartyId != request.PartyId)
            throw new ForbiddenException(
                "ไม่มีสิทธิ์เปิดใบปะหน้าของรายการนี้");
        if (request.Direction == ShipmentDirection.Return)
            return await GetReturnLabelAsync(
                transaction,
                cancellationToken);
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

    private async Task<string> GetReturnLabelAsync(
        SaleTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (!transaction.ReturnRequired)
            throw new DomainException(
                "รายการนี้ยังไม่ได้รับอนุมัติให้ส่งคืน");
        var shipment = transaction.ManagedShipments.SingleOrDefault(
            item => item.Direction == ShipmentDirection.Return)
            ?? throw new DomainException(
                "กำลังสร้างรายการส่งคืน กรุณารอสักครู่");
        if (shipment.Status is not (
                ManagedShipmentStatus.Confirmed or
                ManagedShipmentStatus.CarrierAccepted or
                ManagedShipmentStatus.InTransit or
                ManagedShipmentStatus.TrackingUnverified or
                ManagedShipmentStatus.CarrierException or
                ManagedShipmentStatus.Delivered) ||
            string.IsNullOrWhiteSpace(
                shipment.PurchaseReference) ||
            string.IsNullOrWhiteSpace(
                shipment.CourierTrackingCode))
            throw new DomainException(
                "กำลังออกเลขพัสดุส่งคืน กรุณารอสักครู่แล้วลองใหม่");
        if (!string.Equals(
                shipment.Provider,
                shipmentProvider.ProviderName,
                StringComparison.Ordinal))
            throw new DomainException(
                "รายการนี้ไม่มีใบปะหน้าส่งคืนจากระบบขนส่ง");

        return await shipmentProvider.GetLabelHtmlAsync(
            new ShipmentLabelRequest(
                shipment.PurchaseReference,
                shipment.CarrierCode,
                shipment.ServiceName,
                shipment.CourierTrackingCode,
                BuyerContact(transaction),
                SellerContact(transaction),
                shipment.WeightGrams),
            cancellationToken);
    }

    private static ShippingContactAddress SellerContact(
        SaleTransaction transaction) =>
        new(
            Required(
                transaction.SellerDisplayName,
                "ชื่อผู้ขาย"),
            Required(
                transaction.SellerContact,
                "เบอร์โทรศัพท์ผู้ขาย"),
            Required(
                transaction.ShippingOriginAddressLine ??
                transaction.ShippingOriginAddress,
                "ที่อยู่ผู้ขาย"),
            transaction.ShippingOriginSubdistrictName ?? "",
            transaction.ShippingOriginDistrictName ?? "",
            Required(
                transaction.ShippingOriginProvinceName,
                "จังหวัดผู้ขาย"),
            Required(
                transaction.ShippingOriginPostalCode,
                "รหัสไปรษณีย์ผู้ขาย"));

    private static ShippingContactAddress BuyerContact(
        SaleTransaction transaction) =>
        new(
            Required(
                transaction.BuyerDisplayName,
                "ชื่อผู้ซื้อ"),
            Required(
                transaction.BuyerContact,
                "เบอร์โทรศัพท์ผู้ซื้อ"),
            Required(
                transaction.DeliveryAddressLine ??
                transaction.DeliveryAddress,
                "ที่อยู่ผู้ซื้อ"),
            transaction.DeliverySubdistrictName ?? "",
            transaction.DeliveryDistrictName ?? "",
            Required(
                transaction.DeliveryProvinceName,
                "จังหวัดผู้ซื้อ"),
            Required(
                transaction.DeliveryPostalCode,
                "รหัสไปรษณีย์ผู้ซื้อ"));

    private static string Required(
        string? value,
        string label) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new DomainException(
                $"ไม่พบ{label}สำหรับใบปะหน้า")
            : value.Trim();
}
