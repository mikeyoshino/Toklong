namespace Toklong.Application.Abstractions;

public sealed record ParcelProtectionQuoteRequest(
    ShippingQuoteRequest Shipment,
    string CarrierCode,
    string ServiceCode,
    string DeliveryQuoteReference,
    long ItemPriceSatang);

public sealed record ProviderParcelProtectionOption(
    string Provider,
    string OptionReference,
    long IncludedCoverageLimitSatang,
    long SelectedCoverageLimitSatang,
    long ProviderCostSatang,
    string TermsVersion,
    string InsuranceCode,
    DateTimeOffset QuotedAt,
    DateTimeOffset ExpiresAt);

public sealed record ParcelProtectionAvailability(
    long IncludedCoverageLimitSatang,
    ProviderParcelProtectionOption? AddOn,
    bool ProviderCapabilityCertified);

public interface IParcelProtectionQuoteProvider
{
    Task<ParcelProtectionAvailability> GetAvailabilityAsync(
        ParcelProtectionQuoteRequest request,
        CancellationToken cancellationToken);

    Task<ProviderParcelProtectionOption> ValidateOptionAsync(
        ParcelProtectionQuoteRequest request,
        string optionReference,
        CancellationToken cancellationToken);
}

public sealed record ParcelProtectionCertificationBookingRequest(
    ParcelProtectionQuoteRequest QuoteRequest,
    ProviderParcelProtectionOption Option,
    string OperationReference);

public sealed record ParcelProtectionCertificationBooking(
    string OperationReference,
    string ProviderBookingReference,
    string OptionReference,
    long IncludedCoverageLimitSatang,
    long SelectedCoverageLimitSatang,
    long ProviderCostSatang,
    string TermsVersion,
    string InsuranceCode);

public sealed record ParcelProtectionCertificationCancellation(
    bool Cancelled,
    bool FirstCarrierScanDetected);

public sealed record ParcelProtectionCertificationParcelRequirements(
    bool WeightRequired,
    bool WidthRequired,
    bool LengthRequired,
    bool HeightRequired);

public interface IParcelProtectionCertificationOperations
{
    Task<ParcelProtectionCertificationParcelRequirements>
        GetParcelRequirementsAsync(
            ParcelProtectionQuoteRequest request,
            CancellationToken cancellationToken);

    Task<ParcelProtectionCertificationBooking> BookAsync(
        ParcelProtectionCertificationBookingRequest request,
        CancellationToken cancellationToken);

    Task<ParcelProtectionCertificationBooking?> LookupAsync(
        string operationReference,
        CancellationToken cancellationToken);

    Task<ParcelProtectionCertificationCancellation>
        CancelBeforeFirstScanAsync(
            ParcelProtectionCertificationBooking booking,
            CancellationToken cancellationToken);
}

public sealed class ParcelProtectionOptionChangedException(
    string sanitizedReasonCode)
    : InvalidOperationException(sanitizedReasonCode);
