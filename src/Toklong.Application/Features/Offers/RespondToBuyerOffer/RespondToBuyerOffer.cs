using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Pricing;
using Toklong.Application.Transactions;
using Toklong.Domain.Common;
using Toklong.Domain.Sellers;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.Offers.RespondToBuyerOffer;

public sealed record AcceptBuyerOfferCommand(
    string PublicToken,
    Guid SellerId,
    Guid PayoutAccountId,
    bool TransferRightsAttested,
    bool SellerAcceptedTerms,
    long DisclosedBuyerProtectionFeeSatang,
    long DisclosedPlatformFeeSatang,
    long DisclosedSellerExpectedNetSatang,
    string DisclosedFeePolicyVersion,
    SellerShippingSelectionInput? Shipping = null) : IRequest<TransactionView>;

public sealed record SellerShippingSelectionInput(
    bool UseSavedOrigin,
    string? AddressLine,
    int? ProvinceId,
    int? DistrictId,
    int? SubdistrictId,
    bool RememberOrigin,
    int WeightGrams,
    int WidthCentimeters,
    int LengthCentimeters,
    int HeightCentimeters,
    string QuoteReference,
    long DisclosedShippingFeeSatang);

public sealed class AcceptBuyerOfferHandler(
    ITransactionRepository repository,
    ISellerRepository sellers,
    IPaymentFeePolicy feePolicy,
    IShippingQuoteProvider shippingQuotes,
    IShipmentProvider shipmentProvider,
    IThaiAddressCatalog addressCatalog,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions)
    : IRequestHandler<AcceptBuyerOfferCommand, TransactionView>
{
    public async Task<TransactionView> Handle(
        AcceptBuyerOfferCommand request,
        CancellationToken cancellationToken)
    {
        if (!request.SellerAcceptedTerms)
            throw new ArgumentException("กรุณายอมรับข้อตกลงของผู้ขาย");

        var transaction = await repository.GetByPublicTokenAsync(
            request.PublicToken, cancellationToken)
            ?? throw new NotFoundException("ไม่พบข้อเสนอ");
        var now = clock.UtcNow;
        if (transaction.ExpireIfDue(now, transitions))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new DomainException(
                "ข้อเสนอนี้หมดเวลาตอบรับแล้ว กรุณาให้ผู้ซื้อส่งข้อเสนอใหม่");
        }
        var seller = await sellers.GetByIdAsync(request.SellerId, cancellationToken)
            ?? throw new NotFoundException("ไม่พบโปรไฟล์ผู้ขาย");
        if (!transaction.IsIntendedSeller(seller.PhoneNumber))
            throw new ForbiddenException(
                "ไม่พบข้อเสนอสำหรับบัญชีนี้");
        var payout = seller.PayoutAccounts.SingleOrDefault(
            x => x.Id == request.PayoutAccountId)
            ?? throw new DomainException("กรุณาเลือกบัญชีรับเงินของคุณ");
        var fees = feePolicy.GetDisclosure(transaction.PriceSatang);
        if (fees.BuyerProtectionFeeSatang !=
                request.DisclosedBuyerProtectionFeeSatang ||
            fees.PlatformFeeSatang !=
                request.DisclosedPlatformFeeSatang ||
            fees.SellerExpectedNetSatang !=
                request.DisclosedSellerExpectedNetSatang ||
            !string.Equals(
                fees.PolicyVersion,
                request.DisclosedFeePolicyVersion,
                StringComparison.Ordinal))
            throw new DomainException(
                "ข้อมูลค่าบริการเปลี่ยนแล้ว กรุณาตรวจยอดล่าสุดก่อนยืนยันอีกครั้ง");
        var acceptedShipping = transaction.FulfillmentType ==
            FulfillmentType.PhysicalShipment
                ? await ResolveShippingAsync(
                    transaction,
                    seller,
                    request.Shipping,
                    now,
                    cancellationToken)
                : null;
        if (transaction.FulfillmentType ==
                FulfillmentType.DigitalHandoff &&
            request.Shipping is not null)
            throw new DomainException(
                "รายการดิจิทัลไม่ใช้ข้อมูลจัดส่ง");

        transaction.AcceptBuyerOffer(
            seller.Id,
            seller.DisplayName,
            seller.PhoneNumber,
            payout.BankCode,
            payout.AccountName,
            payout.AccountNumber,
            request.TransferRightsAttested,
            now,
            transitions,
            fees.BuyerProtectionFeeSatang,
            fees.PlatformFeeSatang,
            fees.SellerExpectedNetSatang,
            fees.PolicyVersion,
            acceptedShipping);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return TransactionView.From(transaction);
    }

    private async Task<AcceptedShippingQuote> ResolveShippingAsync(
        SaleTransaction transaction,
        SellerAccount seller,
        SellerShippingSelectionInput? input,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (input is null)
            throw new DomainException(
                "กรุณาระบุต้นทาง ขนาดพัสดุ และเลือกค่าจัดส่ง");
        var origin = input.UseSavedOrigin
            ? seller.GetSavedShippingOrigin() ??
              throw new DomainException(
                  "ยังไม่มีที่อยู่ต้นทางที่บันทึกไว้")
            : ResolveOrigin(input);
        var quoteRequest = new ShippingQuoteRequest(
            origin.PostalCode,
            transaction.DeliveryPostalCode ??
            throw new DomainException(
                "ข้อเสนอไม่มีรหัสไปรษณีย์ปลายทาง"),
            input.WeightGrams,
            input.WidthCentimeters,
            input.LengthCentimeters,
            input.HeightCentimeters,
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
            transaction.PriceSatang);
        var quote = await shippingQuotes.ValidateQuoteAsync(
            quoteRequest,
            input.QuoteReference,
            input.DisclosedShippingFeeSatang,
            cancellationToken);
        if (quote.ExpiresAt <
            now.AddHours(
                SaleTransaction.BuyerPaymentWindowHours))
            throw new DomainException(
                "ราคาค่าจัดส่งมีเวลาไม่พอสำหรับการชำระ กรุณาดูราคาใหม่");
        if (SupportedCarrierCatalog.Find(
                quote.CarrierCode) is null)
            throw new DomainException(
                "ผู้ให้บริการส่งบริษัทขนส่งที่ระบบยังไม่รองรับ");
        if (!string.Equals(
                shipmentProvider.ProviderName,
                quote.Provider,
                StringComparison.Ordinal))
            throw new DomainException(
                "ผู้ให้บริการสร้างรายการจัดส่งไม่ตรงกับราคาที่เลือก");
        var reservation = await shipmentProvider.ReserveAsync(
            new ShipmentReservationRequest(
                transaction.Id,
                quoteRequest,
                quote),
            cancellationToken);
        if (!string.Equals(
                reservation.Provider,
                quote.Provider,
                StringComparison.Ordinal) ||
            !string.Equals(
                reservation.CarrierCode,
                quote.CarrierCode,
                StringComparison.Ordinal) ||
            !string.Equals(
                reservation.ServiceCode,
                quote.ServiceCode,
                StringComparison.Ordinal) ||
            reservation.FeeSatang != quote.FeeSatang ||
            reservation.InsuranceFeeSatang !=
                quote.InsuranceFeeSatang ||
            reservation.DeclaredValueSatang !=
                quote.DeclaredValueSatang ||
            !string.Equals(
                reservation.InsuranceCode,
                quote.InsuranceCode,
                StringComparison.Ordinal))
            throw new DomainException(
                "รายการจัดส่งไม่ตรงกับราคาที่เลือก กรุณาดูราคาใหม่");
        if (input.RememberOrigin &&
            !input.UseSavedOrigin)
            seller.UpdateSavedShippingOrigin(
                origin,
                now);

        return new AcceptedShippingQuote(
            origin.ToDisplayText(),
            origin.ProvinceName,
            origin.PostalCode,
            input.WeightGrams,
            input.WidthCentimeters,
            input.LengthCentimeters,
            input.HeightCentimeters,
            quote.Provider,
            quote.QuoteReference,
            quote.CarrierCode,
            quote.ServiceCode,
            quote.ServiceName,
            quote.FeeSatang,
            quote.InsuranceFeeSatang,
            quote.DeclaredValueSatang,
            quote.InsuranceCode,
            quote.ExpiresAt,
            origin.DistrictName,
            origin.SubdistrictName,
            reservation.PurchaseReference,
            reservation.ProviderTrackingCode,
            reservation.CourierTrackingCode,
            reservation.ReservedAt,
            origin.AddressLine);
    }

    private SellerShippingOriginAddress ResolveOrigin(
        SellerShippingSelectionInput input)
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

public sealed record DeclineBuyerOfferCommand(
    string PublicToken,
    Guid SellerId) : IRequest<TransactionView>;

public sealed class DeclineBuyerOfferHandler(
    ITransactionRepository repository,
    ISellerRepository sellers,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions)
    : IRequestHandler<DeclineBuyerOfferCommand, TransactionView>
{
    public async Task<TransactionView> Handle(
        DeclineBuyerOfferCommand request,
        CancellationToken cancellationToken)
    {
        var seller = await sellers.GetByIdAsync(
                request.SellerId,
                cancellationToken)
            ?? throw new NotFoundException("ไม่พบโปรไฟล์ผู้ขาย");
        var transaction = await repository.GetByPublicTokenAsync(
            request.PublicToken, cancellationToken)
            ?? throw new NotFoundException("ไม่พบข้อเสนอ");
        if (!transaction.IsIntendedSeller(seller.PhoneNumber))
            throw new ForbiddenException(
                "ไม่พบข้อเสนอสำหรับบัญชีนี้");
        var now = clock.UtcNow;
        if (transaction.ExpireIfDue(now, transitions))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new DomainException(
                "ข้อเสนอนี้หมดเวลาตอบรับแล้ว");
        }

        transaction.DeclineBuyerOffer(
            request.SellerId, now, transitions);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return TransactionView.From(transaction);
    }
}
