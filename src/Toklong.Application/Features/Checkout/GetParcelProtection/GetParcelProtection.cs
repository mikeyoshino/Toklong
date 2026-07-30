using System.Text.RegularExpressions;
using System.Text.Json;
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
    ITransactionRepository repository,
    IClock clock)
    : IRequestHandler<GetParcelProtectionQuery, BuyerParcelProtectionView>
{
    public async Task<BuyerParcelProtectionView> Handle(
        GetParcelProtectionQuery request, CancellationToken cancellationToken)
    {
        var transaction = await repository.GetByIdAsync(
            request.TransactionId, cancellationToken)
            ?? throw new NotFoundException("ไม่พบรายการ");
        ParcelProtectionCheckout.RequireBuyer(transaction, request.BuyerId);
        return ParcelProtectionCheckout.FromStored(transaction, clock.UtcNow);
    }
}

internal static partial class ParcelProtectionCheckout
{
    internal const string IncludedTermsVersion = "parcel-protection-included-v1";

    internal static void RequireBuyer(SaleTransaction transaction, Guid buyerId)
    {
        if (transaction.BuyerId != buyerId)
            throw new ForbiddenException(
                "บัญชีผู้ซื้อนี้ไม่มีสิทธิ์เลือกความคุ้มครองพัสดุ");
    }

    internal static string RequireSafeIdempotencyKey(string? idempotencyKey)
    {
        var value = idempotencyKey ?? "";
        if (!SafeIdempotencyKeyPattern().IsMatch(value))
            throw new DomainException("รหัสป้องกันการทำซ้ำไม่ถูกต้อง");
        return value;
    }

    internal static BuyerParcelProtectionView FromStored(
        SaleTransaction transaction, DateTimeOffset? now = null)
    {
        var prepared = transaction.AuditEvents
            .Where(audit => audit.Name is "parcel_protection.offered" or
                "parcel_protection.unavailable" or
                "parcel_protection.included")
            .OrderByDescending(audit => audit.CreatedAt)
            .Select(audit => TryReadPreparedOffer(audit.MetadataJson, transaction))
            .FirstOrDefault(offer => offer is not null &&
                (!offer.ExpiresAt.HasValue || offer.ExpiresAt > now));
        if (transaction.ParcelProtectionElection == ParcelProtectionElectionStatus.Pending &&
            prepared is not null)
            return new BuyerParcelProtectionView(prepared.RequiresChoice,
                prepared.AddOnAvailable, prepared.IncludedCoverageLimitSatang,
                prepared.MaximumCoverageLimitSatang, prepared.CustomerPriceSatang,
                prepared.OptionReference, prepared.TermsVersion, prepared.ExpiresAt,
                prepared.Election.ToString(),
                transaction.ParcelProtectionBookingReady, false);
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

    private static ParcelProtectionPreparedOffer? TryReadPreparedOffer(
        string value, SaleTransaction transaction)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !HasCurrentPreparedOfferShape(root))
                return null;
            var offer = JsonSerializer.Deserialize<ParcelProtectionPreparedOffer>(value);
            return offer is not null && IsValidPreparedPresentation(transaction, offer)
                ? offer
                : null;
        }
        catch (JsonException) { return null; }
    }

    private static bool HasCurrentPreparedOfferShape(JsonElement root)
    {
        if (!HasInt(root, "MetadataVersion", out var metadataVersion) ||
            metadataVersion != ParcelProtectionPreparedOffer.CurrentMetadataVersion ||
            !HasBoolean(root, "RequiresChoice") ||
            !HasBoolean(root, "AddOnAvailable") ||
            !HasInt(root, "IncludedCoverageLimitSatang", out var includedCoverage) ||
            includedCoverage < 0 ||
            !HasNullableInt(root, "MaximumCoverageLimitSatang", out var maximumCoverage) ||
            !HasNullableInt(root, "CustomerPriceSatang", out var customerPrice) ||
            !HasNullableString(root, "OptionReference") ||
            !HasRequiredString(root, "TermsVersion") ||
            !HasNullableDateTimeOffset(root, "ExpiresAt") ||
            !HasInt(root, "Election", out var election) ||
            !Enum.IsDefined((ParcelProtectionElectionStatus)election))
            return false;

        if (maximumCoverage.HasValue && maximumCoverage < includedCoverage ||
            customerPrice.HasValue && customerPrice <= 0)
            return false;

        return true;
    }

    private static bool IsValidPreparedPresentation(
        SaleTransaction transaction, ParcelProtectionPreparedOffer offer)
    {
        if (offer.Election is not (ParcelProtectionElectionStatus.Pending or
            ParcelProtectionElectionStatus.Unavailable) ||
            offer.IncludedCoverageLimitSatang < 0 ||
            offer.RequiresChoice != (transaction.PriceSatang >
                offer.IncludedCoverageLimitSatang && offer.AddOnAvailable))
            return false;

        if (!offer.AddOnAvailable)
        {
            if (offer.RequiresChoice || offer.MaximumCoverageLimitSatang.HasValue ||
                offer.CustomerPriceSatang.HasValue ||
                offer.OptionReference is not null || offer.ExpiresAt.HasValue)
                return false;
            return offer.Election == (transaction.PriceSatang >
                offer.IncludedCoverageLimitSatang
                    ? ParcelProtectionElectionStatus.Unavailable
                    : ParcelProtectionElectionStatus.Pending);
        }

        return offer.Election == ParcelProtectionElectionStatus.Pending &&
            offer.MaximumCoverageLimitSatang > 0 &&
            offer.CustomerPriceSatang > 0 &&
            !string.IsNullOrWhiteSpace(offer.OptionReference) &&
            offer.ExpiresAt.HasValue;
    }

    private static bool HasBoolean(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property) ||
            property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return false;
        return true;
    }

    private static bool HasInt(JsonElement root, string name, out long value)
    {
        value = 0;
        return root.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out value);
    }

    private static bool HasNullableInt(JsonElement root, string name, out long? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out var property)) return false;
        if (property.ValueKind == JsonValueKind.Null) return true;
        if (property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt64(out var parsed))
            return false;
        value = parsed;
        return true;
    }

    private static bool HasNullableString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property)) return false;
        if (property.ValueKind == JsonValueKind.Null) return true;
        if (property.ValueKind != JsonValueKind.String) return false;
        return true;
    }

    private static bool HasRequiredString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var property) &&
        property.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(property.GetString());

    private static bool HasNullableDateTimeOffset(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property)) return false;
        if (property.ValueKind == JsonValueKind.Null) return true;
        return property.ValueKind == JsonValueKind.String &&
            property.TryGetDateTimeOffset(out _);
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
