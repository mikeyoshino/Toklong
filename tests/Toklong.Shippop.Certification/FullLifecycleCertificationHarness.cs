using Toklong.Application.Abstractions;

namespace Toklong.Shippop.Certification;

internal enum FullLifecycleOutcome
{
    Pass,
    Fail,
    Blocked,
    CleanupRequired
}

internal sealed record FullLifecycleCheck(
    string Capability,
    FullLifecycleOutcome Outcome,
    string ReasonCode);

internal sealed record FullLifecycleCertificationResult(
    IReadOnlyList<FullLifecycleCheck> Checks)
{
    private static readonly string[] Required =
    [
        "pricelist",
        "booking",
        "confirm",
        "label",
        "tracking",
        "cancel"
    ];

    public bool Passed => Required.All(capability =>
        Checks.Single(check =>
                string.Equals(
                    check.Capability,
                    capability,
                    StringComparison.Ordinal))
            .Outcome == FullLifecycleOutcome.Pass);
}

internal sealed class FullLifecycleCertificationHarness(
    IShippingQuoteProvider quoteProvider,
    IShipmentProvider shipmentProvider)
{
    public async Task<FullLifecycleCertificationResult> RunAsync(
        ShippingQuoteRequest shipment,
        string serviceCode,
        bool mutationsEnabled,
        CancellationToken cancellationToken)
    {
        var checks = NewBlockedChecks();
        ShippingQuoteOption quote;
        try
        {
            var matches = (await quoteProvider.GetQuotesAsync(
                    shipment,
                    cancellationToken))
                .Where(option => string.Equals(
                    option.ServiceCode,
                    serviceCode,
                    StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (matches.Length != 1)
            {
                Set(
                    checks,
                    "pricelist",
                    FullLifecycleOutcome.Fail,
                    "quote_missing");
                return Result(checks);
            }
            quote = matches[0];
            if (quote.FeeSatang <= 0 ||
                !string.Equals(
                    quote.Provider,
                    shipmentProvider.ProviderName,
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(quote.CarrierCode))
            {
                Set(
                    checks,
                    "pricelist",
                    FullLifecycleOutcome.Fail,
                    "quote_price_invalid");
                return Result(checks);
            }
            Set(
                checks,
                "pricelist",
                FullLifecycleOutcome.Pass,
                "quote_valid");
        }
        catch
        {
            Set(
                checks,
                "pricelist",
                FullLifecycleOutcome.Fail,
                "quote_failed");
            return Result(checks);
        }

        if (!mutationsEnabled)
        {
            Set(
                checks,
                "booking",
                FullLifecycleOutcome.Blocked,
                "mutation_disabled");
            return Result(checks);
        }

        ShipmentReservation reservation;
        try
        {
            reservation = await shipmentProvider.ReserveAsync(
                new ShipmentReservationRequest(
                    Guid.NewGuid(),
                    shipment,
                    quote,
                    Guid.NewGuid(),
                    IsReturn: false,
                    $"certification-{Guid.NewGuid():N}"),
                cancellationToken);
        }
        catch
        {
            Set(
                checks,
                "booking",
                FullLifecycleOutcome.Fail,
                "booking_failed");
            return Result(checks);
        }
        if (!ReservationMatches(reservation, quote))
        {
            Set(
                checks,
                "booking",
                FullLifecycleOutcome.Fail,
                "booking_contract_invalid");
            return Result(checks);
        }
        Set(
            checks,
            "booking",
            FullLifecycleOutcome.Pass,
            "booking_valid");

        ShipmentConfirmation confirmation;
        try
        {
            confirmation =
                await shipmentProvider.ConfirmServiceAsync(
                    reservation.PurchaseReference,
                    reservation.ProviderTrackingCode,
                    reservation.CarrierCode,
                    reservation.ServiceCode,
                    cancellationToken);
        }
        catch
        {
            Set(
                checks,
                "confirm",
                FullLifecycleOutcome.Fail,
                "confirm_failed");
            Set(
                checks,
                "cancel",
                FullLifecycleOutcome.CleanupRequired,
                "cleanup_required");
            return Result(checks);
        }
        if (!ConfirmationMatches(
                confirmation,
                reservation))
        {
            Set(
                checks,
                "confirm",
                FullLifecycleOutcome.Fail,
                "confirm_contract_invalid");
            Set(
                checks,
                "cancel",
                FullLifecycleOutcome.CleanupRequired,
                "cleanup_required");
            return Result(checks);
        }
        Set(
            checks,
            "confirm",
            FullLifecycleOutcome.Pass,
            "confirm_valid");

        try
        {
            try
            {
                var label =
                    await shipmentProvider.GetLabelHtmlAsync(
                        new ShipmentLabelRequest(
                            reservation.PurchaseReference,
                            reservation.CarrierCode,
                            quote.ServiceName,
                            confirmation.CourierTrackingCode,
                            shipment.Origin!,
                            shipment.Destination!,
                            shipment.WeightGrams),
                        cancellationToken);
                var labelIsValid = IsValidLabel(label);
                Set(
                    checks,
                    "label",
                    labelIsValid
                        ? FullLifecycleOutcome.Pass
                        : FullLifecycleOutcome.Fail,
                    labelIsValid
                        ? "label_valid"
                        : "label_contract_invalid");
            }
            catch
            {
                Set(
                    checks,
                    "label",
                    FullLifecycleOutcome.Fail,
                    "label_failed");
            }

            try
            {
                var tracking =
                    await shipmentProvider.GetTrackingAsync(
                        reservation.ProviderTrackingCode,
                        reservation.CarrierCode,
                        cancellationToken);
                var trackingIsValid = TrackingMatches(
                    tracking,
                    reservation);
                Set(
                    checks,
                    "tracking",
                    trackingIsValid
                        ? FullLifecycleOutcome.Pass
                        : FullLifecycleOutcome.Fail,
                    trackingIsValid
                        ? "tracking_valid"
                        : "tracking_contract_invalid");
            }
            catch
            {
                Set(
                    checks,
                    "tracking",
                    FullLifecycleOutcome.Fail,
                    "tracking_failed");
            }
        }
        finally
        {
            try
            {
                await shipmentProvider.CancelServiceAsync(
                    confirmation.CourierTrackingCode,
                    reservation.ServiceCode,
                    isReturn: false,
                    CancellationToken.None);
                Set(
                    checks,
                    "cancel",
                    FullLifecycleOutcome.Pass,
                    "cancel_confirmed");
            }
            catch
            {
                Set(
                    checks,
                    "cancel",
                    FullLifecycleOutcome.CleanupRequired,
                    "cleanup_required");
            }
        }
        return Result(checks);
    }

    private static Dictionary<string, FullLifecycleCheck>
        NewBlockedChecks() =>
        new(
            new[]
            {
                "pricelist",
                "booking",
                "confirm",
                "label",
                "tracking",
                "cancel"
            }.Select(capability =>
                new KeyValuePair<string, FullLifecycleCheck>(
                    capability,
                    new FullLifecycleCheck(
                        capability,
                        FullLifecycleOutcome.Blocked,
                        "not_reached"))),
            StringComparer.Ordinal);

    private static void Set(
        IDictionary<string, FullLifecycleCheck> checks,
        string capability,
        FullLifecycleOutcome outcome,
        string reasonCode) =>
        checks[capability] = new FullLifecycleCheck(
            capability,
            outcome,
            reasonCode);

    private static FullLifecycleCertificationResult Result(
        IReadOnlyDictionary<string, FullLifecycleCheck> checks) =>
        new(
            [
                checks["pricelist"],
                checks["booking"],
                checks["confirm"],
                checks["label"],
                checks["tracking"],
                checks["cancel"]
            ]);

    private static bool IsValidLabel(string label) =>
        label.Length is >= 20 and <= 5 * 1024 * 1024 &&
        label.Contains(
            "<html",
            StringComparison.OrdinalIgnoreCase);

    private static bool ReservationMatches(
        ShipmentReservation reservation,
        ShippingQuoteOption quote) =>
        string.Equals(
            reservation.Provider,
            quote.Provider,
            StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(
            reservation.PurchaseReference) &&
        !string.IsNullOrWhiteSpace(
            reservation.ProviderTrackingCode) &&
        string.Equals(
            reservation.CarrierCode,
            quote.CarrierCode,
            StringComparison.Ordinal) &&
        string.Equals(
            reservation.ServiceCode,
            quote.ServiceCode,
            StringComparison.Ordinal) &&
        reservation.FeeSatang == quote.FeeSatang;

    private static bool ConfirmationMatches(
        ShipmentConfirmation confirmation,
        ShipmentReservation reservation) =>
        string.Equals(
            confirmation.ProviderTrackingCode,
            reservation.ProviderTrackingCode,
            StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(
            confirmation.CourierTrackingCode) &&
        string.Equals(
            confirmation.CarrierCode,
            reservation.CarrierCode,
            StringComparison.Ordinal);

    private static bool TrackingMatches(
        ShipmentTrackingUpdate tracking,
        ShipmentReservation reservation) =>
        string.Equals(
            tracking.ProviderTrackingCode,
            reservation.ProviderTrackingCode,
            StringComparison.Ordinal) &&
        string.Equals(
            tracking.CarrierCode,
            reservation.CarrierCode,
            StringComparison.Ordinal) &&
        !(string.Equals(
                tracking.EventType,
                "delivered",
                StringComparison.Ordinal) &&
            !tracking.HasTrustedOccurredAt);

    private static FullLifecycleCheck Passed(
        string capability,
        string reasonCode) =>
        new(
            capability,
            FullLifecycleOutcome.Pass,
            reasonCode);
}
