using System.Text.RegularExpressions;
using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Domain.Common;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.Checkout.GetParcelProtection;

public sealed record GetParcelProtectionQuery(Guid TransactionId, Guid BuyerId)
    : IRequest<BuyerParcelProtectionView>;

public sealed record BuyerParcelProtectionView(
    bool RequiresChoice,
    bool AddOnAvailable,
    long IncludedCoverageLimitSatang,
    long? MaximumCoverageLimitSatang,
    long? CustomerPriceSatang,
    string? OptionReference,
    string TermsVersion,
    DateTimeOffset? ExpiresAt,
    string Election,
    bool BookingReady,
    bool ReconfirmationRequired);

public sealed class GetParcelProtectionHandler(
    ITransactionRepository repository)
    : IRequestHandler<GetParcelProtectionQuery, BuyerParcelProtectionView>
{
    public async Task<BuyerParcelProtectionView> Handle(
        GetParcelProtectionQuery request, CancellationToken cancellationToken)
    {
        var transaction = await repository.GetByIdAsync(
            request.TransactionId, cancellationToken)
            ?? throw new NotFoundException("ไม่พบรายการ");
        ParcelProtectionCheckout.RequireBuyer(transaction, request.BuyerId);
        return ParcelProtectionCheckout.FromStored(transaction);
    }
}

internal static partial class ParcelProtectionCheckout
{
    internal const string IncludedTermsVersion = "parcel-protection-included-v1";

    internal static void RequireBuyer(SaleTransaction transaction, Guid buyerId)
    {
        if (transaction.BuyerId != buyerId)
            throw new DomainException("บัญชีผู้ซื้อนี้ไม่มีสิทธิ์เลือกความคุ้มครองพัสดุ");
    }

    internal static string RequireSafeIdempotencyKey(string? idempotencyKey)
    {
        var value = idempotencyKey ?? "";
        if (!SafeIdempotencyKeyPattern().IsMatch(value))
            throw new DomainException("รหัสป้องกันการทำซ้ำไม่ถูกต้อง");
        return value;
    }

    internal static BuyerParcelProtectionView FromStored(SaleTransaction transaction)
    {
        var notApplicable = transaction.FulfillmentType ==
            FulfillmentType.DigitalHandoff;
        var isPending = transaction.ParcelProtectionElection ==
            ParcelProtectionElectionStatus.Pending;
        var reconfirmationRequired = transaction.ParcelProtectionElection ==
            ParcelProtectionElectionStatus.ReconfirmationRequired;
        var requiresChoice = !notApplicable && isPending &&
            transaction.ParcelProtectionIncludedCoverageSatang > 0 &&
            transaction.PriceSatang > transaction.ParcelProtectionIncludedCoverageSatang;
        return new BuyerParcelProtectionView(
            requiresChoice,
            transaction.ParcelProtectionElection == ParcelProtectionElectionStatus.Accepted,
            transaction.ParcelProtectionIncludedCoverageSatang,
            transaction.ParcelProtectionSelectedCoverageSatang > 0
                ? transaction.ParcelProtectionSelectedCoverageSatang : null,
            transaction.ParcelInsuranceFeeSatang > 0
                ? transaction.ParcelInsuranceFeeSatang : null,
            transaction.ParcelProtectionOptionReference,
            transaction.ParcelProtectionTermsVersion ?? IncludedTermsVersion,
            transaction.ParcelProtectionExpiresAt,
            notApplicable ? ParcelProtectionElectionStatus.NotApplicable.ToString()
                : transaction.ParcelProtectionElection.ToString(),
            transaction.ParcelProtectionBookingReady,
            reconfirmationRequired);
    }

    internal static ShippingQuoteRequest BuildShipmentRequest(
        SaleTransaction transaction)
    {
        if (transaction.FulfillmentType != FulfillmentType.PhysicalShipment ||
            string.IsNullOrWhiteSpace(transaction.ShippingOriginPostalCode) ||
            string.IsNullOrWhiteSpace(transaction.DeliveryPostalCode) ||
            !transaction.PackageWeightGrams.HasValue ||
            !transaction.PackageWidthCentimeters.HasValue ||
            !transaction.PackageLengthCentimeters.HasValue ||
            !transaction.PackageHeightCentimeters.HasValue)
            throw new DomainException("ข้อมูลพัสดุที่ผู้ขายยืนยันไม่ครบ");

        var origin = new ShippingContactAddress(
            transaction.SellerDisplayName,
            transaction.SellerContact,
            transaction.ShippingOriginAddressLine ?? transaction.ShippingOriginAddress ??
                throw new DomainException("ข้อเสนอไม่มีที่อยู่ต้นทาง"),
            transaction.ShippingOriginSubdistrictName ??
                throw new DomainException("ข้อเสนอไม่มีตำบลหรือแขวงต้นทาง"),
            transaction.ShippingOriginDistrictName ??
                throw new DomainException("ข้อเสนอไม่มีอำเภอหรือเขตต้นทาง"),
            transaction.ShippingOriginProvinceName ??
                throw new DomainException("ข้อเสนอไม่มีจังหวัดต้นทาง"),
            transaction.ShippingOriginPostalCode);
        var destination = new ShippingContactAddress(
            transaction.BuyerDisplayName ?? throw new DomainException("ข้อเสนอไม่มีชื่อผู้รับ"),
            transaction.BuyerContact ?? throw new DomainException("ข้อเสนอไม่มีเบอร์ผู้รับ"),
            transaction.DeliveryAddressLine ?? transaction.DeliveryAddress ??
                throw new DomainException("ข้อเสนอไม่มีที่อยู่ปลายทาง"),
            transaction.DeliverySubdistrictName ??
                throw new DomainException("ข้อเสนอไม่มีตำบลหรือแขวงปลายทาง"),
            transaction.DeliveryDistrictName ??
                throw new DomainException("ข้อเสนอไม่มีอำเภอหรือเขตปลายทาง"),
            transaction.DeliveryProvinceName ??
                throw new DomainException("ข้อเสนอไม่มีจังหวัดปลายทาง"),
            transaction.DeliveryPostalCode);
        return new ShippingQuoteRequest(
            transaction.ShippingOriginPostalCode,
            transaction.DeliveryPostalCode,
            transaction.PackageWeightGrams.Value,
            transaction.PackageWidthCentimeters.Value,
            transaction.PackageLengthCentimeters.Value,
            transaction.PackageHeightCentimeters.Value,
            origin,
            destination,
            transaction.ProductName,
            DeclaredValueSatang: transaction.PriceSatang);
    }

    internal static ParcelProtectionQuoteRequest BuildProtectionRequest(
        SaleTransaction transaction) => new(
            BuildShipmentRequest(transaction),
            transaction.CarrierCode ?? throw new DomainException("ไม่พบผู้ให้บริการขนส่งที่เลือก"),
            transaction.ShippingServiceCode ?? throw new DomainException("ไม่พบบริการขนส่งที่เลือก"),
            transaction.ShippingQuoteReference ?? throw new DomainException("ไม่พบราคาอ้างอิงการจัดส่ง"),
            transaction.PriceSatang);

    [GeneratedRegex("^[A-Za-z0-9:_-]{16,80}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeIdempotencyKeyPattern();
}
