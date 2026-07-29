using System.Security.Cryptography;
using System.Text;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.Shipping;

public static class ManagedShippingOperationQueue
{
    public static void QueueConfirmationIfRequired(
        SaleTransaction transaction,
        DateTimeOffset now,
        ActorRole actorRole,
        string actorId)
    {
        var shipment = transaction.ManagedShipments.SingleOrDefault(
            item => item.Direction == ShipmentDirection.Outbound &&
                    item.Status == ManagedShipmentStatus.Reserved);
        if (shipment is null)
            return;
        var reference = shipment.PurchaseReference ?? "";
        var fingerprint = Fingerprint(
            $"confirm|{transaction.Id:N}|{shipment.Id:N}|{reference}");
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
        var shipment = transaction.ManagedShipments.SingleOrDefault(
            item => item.Direction == ShipmentDirection.Outbound &&
                    item.Status is
                        ManagedShipmentStatus.Reserved or
                        ManagedShipmentStatus.Confirmed);
        if (shipment is null ||
            shipment.FirstCarrierScanAt.HasValue)
            return;
        var reference = shipment.PurchaseReference ?? "";
        var fingerprint = Fingerprint(
            $"cancel|{transaction.Id:N}|{shipment.Id:N}|{reference}");
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

    private static string Fingerprint(string value) =>
        Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
