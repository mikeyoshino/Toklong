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

public sealed class ParcelProtectionOptionChangedException(
    string sanitizedReasonCode)
    : InvalidOperationException(sanitizedReasonCode);
