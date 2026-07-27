namespace Toklong.Application.Abstractions;

public sealed record ShippingQuoteRequest(
    string OriginPostalCode,
    string DestinationPostalCode,
    int WeightGrams,
    int WidthCentimeters,
    int LengthCentimeters,
    int HeightCentimeters,
    ShippingContactAddress? Origin = null,
    ShippingContactAddress? Destination = null,
    string ParcelName = "สินค้า");

public sealed record ShippingContactAddress(
    string Name,
    string PhoneNumber,
    string AddressLine,
    string SubdistrictName,
    string DistrictName,
    string ProvinceName,
    string PostalCode);

public sealed record ShippingQuoteOption(
    string Provider,
    string QuoteReference,
    string CarrierCode,
    string ServiceCode,
    string ServiceName,
    long FeeSatang,
    DateTimeOffset ExpiresAt);

public interface IShippingQuoteProvider
{
    Task<IReadOnlyList<ShippingQuoteOption>> GetQuotesAsync(
        ShippingQuoteRequest request,
        CancellationToken cancellationToken);

    Task<ShippingQuoteOption> ValidateQuoteAsync(
        ShippingQuoteRequest request,
        string quoteReference,
        long disclosedFeeSatang,
        CancellationToken cancellationToken);
}

public sealed record ShipmentReservationRequest(
    Guid TransactionId,
    ShippingQuoteRequest Shipment,
    ShippingQuoteOption Quote);

public sealed record ShipmentReservation(
    string Provider,
    string PurchaseReference,
    string ProviderTrackingCode,
    string? CourierTrackingCode,
    string CarrierCode,
    string ServiceCode,
    long FeeSatang,
    DateTimeOffset ReservedAt);

public sealed record ShipmentConfirmation(
    string ProviderTrackingCode,
    string CourierTrackingCode,
    string CarrierCode,
    string ProviderStatus,
    DateTimeOffset ConfirmedAt);

public sealed record ShipmentTrackingUpdate(
    string ProviderTrackingCode,
    string? CourierTrackingCode,
    string CarrierCode,
    string ProviderStatus,
    string? EventType,
    string EventId,
    DateTimeOffset OccurredAt);

public sealed record ShipmentLabelRequest(
    string PurchaseReference,
    string CarrierCode,
    string ServiceName,
    string TrackingNumber,
    ShippingContactAddress Origin,
    ShippingContactAddress Destination,
    int WeightGrams);

public interface IShipmentProvider
{
    string ProviderName { get; }

    Task<ShipmentReservation> ReserveAsync(
        ShipmentReservationRequest request,
        CancellationToken cancellationToken);

    Task<ShipmentTrackingUpdate> GetTrackingAsync(
        string providerTrackingCode,
        string carrierCode,
        CancellationToken cancellationToken);

    Task<ShipmentConfirmation> ConfirmAsync(
        string purchaseReference,
        string providerTrackingCode,
        string carrierCode,
        CancellationToken cancellationToken);

    Task<string> GetLabelHtmlAsync(
        ShipmentLabelRequest request,
        CancellationToken cancellationToken);

    Task CancelAsync(
        string courierTrackingCode,
        CancellationToken cancellationToken);
}
