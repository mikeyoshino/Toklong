using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.Shipping;

public static class ManagedShippingOperationQueue
{
    public static string BookingFingerprint(
        ManagedShipment shipment)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        return Fingerprint(
            JsonSerializer.Serialize(new
            {
                shipment.TransactionId,
                shipment.Direction,
                shipment.Provider,
                shipment.OriginPrivateSnapshotReference,
                shipment.DestinationPrivateSnapshotReference,
                shipment.ParcelName,
                shipment.WeightGrams,
                shipment.WidthCentimeters,
                shipment.LengthCentimeters,
                shipment.HeightCentimeters,
                shipment.CarrierCode,
                shipment.ServiceCode,
                shipment.BaseShippingFeeSatang,
                shipment.InsuranceFeeSatang,
                shipment.DeclaredValueSatang,
                shipment.InsuranceCode,
                shipment.QuoteReference,
                shipment.QuoteExpiresAt,
                shipment.ParcelProtectionTermsVersion,
                shipment.ParcelProtectionOptionReference,
                shipment.ParcelProtectionElection,
                shipment.ParcelProtectionProviderCostSatang,
                shipment.ParcelProtectionIncludedCoverageSatang,
                shipment.ParcelProtectionSelectedCoverageSatang
            }));
    }

    public static string ConfirmationFingerprint(
        SaleTransaction transaction,
        ManagedShipment shipment)
    {
        var prefix = shipment.Direction ==
                     ShipmentDirection.Return
            ? "confirm-return"
            : "confirm";
        return Fingerprint(
            $"{prefix}|{transaction.Id:N}|{shipment.Id:N}|{shipment.PurchaseReference ?? ""}");
    }

    public static string CancellationFingerprint(
        SaleTransaction transaction,
        ManagedShipment shipment)
    {
        var prefix = shipment.Direction ==
                     ShipmentDirection.Return
            ? "cancel-return"
            : "cancel";
        return Fingerprint(
            $"{prefix}|{transaction.Id:N}|{shipment.Id:N}|{shipment.PurchaseReference ?? ""}");
    }

    public static void QueueConfirmationIfRequired(
        SaleTransaction transaction,
        DateTimeOffset now,
        ActorRole actorRole,
        string actorId)
    {
        var shipment = transaction.CurrentOutboundShipment;
        if (shipment?.Status != ManagedShipmentStatus.Reserved)
            return;
        var fingerprint = ConfirmationFingerprint(
            transaction,
            shipment);
        transaction.QueueShippingOperation(
            ShippingOperation.Queue(
                transaction.Id,
                shipment.Id,
                ShippingOperationType.ConfirmOutbound,
                $"confirm-outbound:{transaction.Id:N}:{fingerprint}",
                fingerprint,
                now),
            actorRole,
            actorId,
            now);
    }

    public static void QueueCancellationIfRequired(
        SaleTransaction transaction,
        DateTimeOffset now)
    {
        var shipment = transaction.CurrentOutboundShipment;
        if (shipment is null || shipment.Status is not (
                ManagedShipmentStatus.Reserved or
                ManagedShipmentStatus.Confirmed) ||
            shipment.FirstCarrierScanAt.HasValue)
            return;
        var fingerprint = CancellationFingerprint(
            transaction,
            shipment);
        transaction.QueueShippingOperation(
            ShippingOperation.Queue(
                transaction.Id,
                shipment.Id,
                ShippingOperationType.CancelOutbound,
                $"cancel-outbound:{transaction.Id:N}:{fingerprint}",
                fingerprint,
                now),
            ActorRole.System,
            "payment-deadline-job",
            now);
    }

    public static void QueueReturnConfirmationIfRequired(
        SaleTransaction transaction,
        DateTimeOffset now)
    {
        var shipment = transaction.ManagedShipments.SingleOrDefault(
            item => item.Direction == ShipmentDirection.Return &&
                    item.Status == ManagedShipmentStatus.Reserved);
        if (shipment is null)
            return;
        var fingerprint = ConfirmationFingerprint(
            transaction,
            shipment);
        transaction.QueueShippingOperation(
            ShippingOperation.Queue(
                transaction.Id,
                shipment.Id,
                ShippingOperationType.ConfirmReturn,
                $"confirm-return:{transaction.Id:N}:{fingerprint}",
                fingerprint,
                now),
            ActorRole.System,
            "shipping-orchestrator",
            now);
    }

    private static string Fingerprint(string value) =>
        Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
