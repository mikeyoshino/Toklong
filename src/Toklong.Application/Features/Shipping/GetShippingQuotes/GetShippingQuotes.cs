using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Domain.Common;
using Toklong.Domain.Sellers;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.Shipping.GetShippingQuotes;

public sealed record GetShippingQuotesQuery(
    string PublicToken,
    Guid SellerId,
    bool UseSavedOrigin,
    string? AddressLine,
    int? ProvinceId,
    int? DistrictId,
    int? SubdistrictId,
    int WeightGrams,
    int WidthCentimeters,
    int LengthCentimeters,
    int HeightCentimeters)
    : IRequest<IReadOnlyList<ShippingQuoteOption>>;

public sealed class GetShippingQuotesHandler(
    ITransactionRepository transactions,
    ISellerRepository sellers,
    IThaiAddressCatalog addressCatalog,
    IShippingQuoteProvider shippingQuotes)
    : IRequestHandler<
        GetShippingQuotesQuery,
        IReadOnlyList<ShippingQuoteOption>>
{
    public async Task<IReadOnlyList<ShippingQuoteOption>> Handle(
        GetShippingQuotesQuery request,
        CancellationToken cancellationToken)
    {
        var transaction = await transactions.GetByPublicTokenAsync(
            request.PublicToken,
            cancellationToken)
            ?? throw new NotFoundException("ไม่พบข้อเสนอ");
        var seller = await sellers.GetByIdAsync(
            request.SellerId,
            cancellationToken)
            ?? throw new NotFoundException("ไม่พบโปรไฟล์ผู้ขาย");
        if (!transaction.IsIntendedSeller(
                seller.PhoneNumber))
            throw new ForbiddenException(
                "ไม่พบข้อเสนอสำหรับบัญชีนี้");
        if (transaction.State !=
            TransactionState.AwaitingSellerAcceptance)
            throw new DomainException(
                "ข้อเสนอนี้ไม่อยู่ในสถานะที่ดูค่าจัดส่งได้");
        if (transaction.FulfillmentType !=
            FulfillmentType.PhysicalShipment)
            throw new DomainException(
                "รายการดิจิทัลไม่ใช้ค่าจัดส่ง");

        var origin = request.UseSavedOrigin
            ? seller.GetSavedShippingOrigin() ??
              throw new DomainException(
                  "ยังไม่มีที่อยู่ต้นทางที่บันทึกไว้")
            : ResolveOrigin(request);
        return await shippingQuotes.GetQuotesAsync(
            new ShippingQuoteRequest(
                origin.PostalCode,
                transaction.DeliveryPostalCode ??
                throw new DomainException(
                    "ข้อเสนอไม่มีรหัสไปรษณีย์ปลายทาง"),
                request.WeightGrams,
                request.WidthCentimeters,
                request.LengthCentimeters,
                request.HeightCentimeters,
                new ShippingContactAddress(
                    seller.DisplayName,
                    seller.PhoneNumber,
                    origin.AddressLine,
                    origin.SubdistrictName,
                    origin.DistrictName,
                    origin.ProvinceName,
                    origin.PostalCode),
                new ShippingContactAddress(
                    transaction.BuyerDisplayName ??
                    throw new DomainException(
                        "ข้อเสนอไม่มีชื่อผู้รับ"),
                    transaction.BuyerContact ??
                    throw new DomainException(
                        "ข้อเสนอไม่มีเบอร์ผู้รับ"),
                    transaction.DeliveryAddressLine ??
                    transaction.DeliveryAddress ??
                    throw new DomainException(
                        "ข้อเสนอไม่มีที่อยู่ปลายทาง"),
                    transaction.DeliverySubdistrictName ??
                    throw new DomainException(
                        "ข้อเสนอไม่มีตำบลหรือแขวงปลายทาง"),
                    transaction.DeliveryDistrictName ??
                    throw new DomainException(
                        "ข้อเสนอไม่มีอำเภอหรือเขตปลายทาง"),
                    transaction.DeliveryProvinceName ??
                    throw new DomainException(
                        "ข้อเสนอไม่มีจังหวัดปลายทาง"),
                    transaction.DeliveryPostalCode ??
                    throw new DomainException(
                        "ข้อเสนอไม่มีรหัสไปรษณีย์ปลายทาง")),
                transaction.ProductName,
                transaction.PriceSatang),
            cancellationToken);
    }

    private SellerShippingOriginAddress ResolveOrigin(
        GetShippingQuotesQuery input)
    {
        if (!input.ProvinceId.HasValue ||
            !input.DistrictId.HasValue ||
            !input.SubdistrictId.HasValue)
            throw new DomainException(
                "กรุณาเลือกที่อยู่ต้นทางให้ครบ");
        var resolved = addressCatalog.Resolve(
            input.AddressLine ?? "",
            input.ProvinceId.Value,
            input.DistrictId.Value,
            input.SubdistrictId.Value);
        return new SellerShippingOriginAddress(
            resolved.AddressLine,
            resolved.ProvinceId,
            resolved.ProvinceName,
            resolved.DistrictId,
            resolved.DistrictName,
            resolved.SubdistrictId,
            resolved.SubdistrictName,
            resolved.PostalCode);
    }
}
