using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Transactions;
using Toklong.Application.Common;
using Toklong.Application.Pricing;
using Toklong.Domain.Buyers;
using Toklong.Domain.Common;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.Offers.CreateBuyerOffer;

public sealed record CreateBuyerOfferCommand(
    Guid BuyerId,
    string SellerPhoneNumber,
    FulfillmentType FulfillmentType,
    string ProductName,
    string ProposedDescription,
    ConditionCode Condition,
    string KnownDefects,
    string? PhotoUrl,
    long PriceSatang,
    bool UseSavedAddress,
    OfferDeliveryAddressInput? DeliveryAddress,
    bool RememberAddress) :
    IRequest<TransactionView>;

public sealed record OfferDeliveryAddressInput(
    string AddressLine,
    int ProvinceId,
    int DistrictId,
    int SubdistrictId);

public sealed class CreateBuyerOfferHandler(
    ITransactionRepository repository,
    IBuyerRepository buyers,
    IThaiAddressCatalog addressCatalog,
    IUnitOfWork unitOfWork,
    IClock clock,
    IPaymentFeePolicy feePolicy,
    TransactionTransitionService transitions)
    : IRequestHandler<CreateBuyerOfferCommand, TransactionView>
{
    public async Task<TransactionView> Handle(
        CreateBuyerOfferCommand request,
        CancellationToken cancellationToken)
    {
        var buyer = await buyers.GetByIdAsync(
            request.BuyerId, cancellationToken)
            ?? throw new NotFoundException("กรุณาเข้าสู่ระบบผู้ซื้อก่อนสร้างข้อเสนอ");
        var sellerPhone = ThaiMobilePhone.Normalize(
            request.SellerPhoneNumber);
        if (string.Equals(
                sellerPhone,
                buyer.PhoneNumber,
                StringComparison.Ordinal))
            throw new DomainException(
                "เบอร์ผู้ขายต้องไม่ใช่เบอร์เดียวกับผู้ซื้อ");
        var productName = request.ProductName.Trim();
        feePolicy.EnsureItemPriceAllowed(request.PriceSatang);
        var category = request.FulfillmentType == FulfillmentType.DigitalHandoff
            ? "สินค้าดิจิทัล"
            : "งานอดิเรกและของใช้";
        var policy = ProductPolicy.Evaluate(
            request.FulfillmentType,
            category,
            productName,
            request.ProposedDescription);
        if (!policy.Allowed)
        {
            await repository.AddRiskEventAsync(
                new ActivationRiskEvent(policy.ReasonCode, category, clock.UtcNow),
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new DomainException(policy.UserMessage);
        }
        var deliveryAddress =
            request.FulfillmentType ==
            FulfillmentType.PhysicalShipment
                ? ResolveDeliveryAddress(
                    buyer,
                    request)
                : null;

        var transaction = SaleTransaction.CreateBuyerOffer(
            buyer.Id,
            buyer.FullName,
            buyer.PhoneNumber,
            sellerPhone,
            request.FulfillmentType,
            productName,
            request.ProposedDescription,
            request.Condition,
            request.KnownDefects,
            request.PhotoUrl,
            request.PriceSatang,
            deliveryAddress?.ToDisplayText(),
            deliveryAddress?.ProvinceName,
            deliveryAddress?.PostalCode,
            "mvp-th-2026-07",
            clock.UtcNow,
            transitions,
            deliveryAddress?.DistrictName,
            deliveryAddress?.SubdistrictName,
            deliveryAddress?.AddressLine);

        await repository.AddAsync(transaction, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return TransactionView.From(transaction);
    }

    private BuyerDeliveryAddress ResolveDeliveryAddress(
        BuyerAccount buyer,
        CreateBuyerOfferCommand request)
    {
        if (request.UseSavedAddress)
            return buyer.GetSavedDeliveryAddress()
                ?? throw new DomainException(
                    "ยังไม่มีที่อยู่ที่บันทึกไว้");

        var input = request.DeliveryAddress
            ?? throw new DomainException(
                "กรุณาระบุที่อยู่จัดส่ง");
        var resolved = addressCatalog.Resolve(
            input.AddressLine,
            input.ProvinceId,
            input.DistrictId,
            input.SubdistrictId);
        if (request.RememberAddress)
            buyer.UpdateSavedDeliveryAddress(
                resolved,
                clock.UtcNow);
        return resolved;
    }

}
