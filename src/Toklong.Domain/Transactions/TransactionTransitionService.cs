using Toklong.Domain.Common;

namespace Toklong.Domain.Transactions;

public sealed class TransactionTransitionService
{
    private static readonly IReadOnlyDictionary<TransactionState, TransactionState[]> Allowed =
        new Dictionary<TransactionState, TransactionState[]>
        {
            [TransactionState.BuyerOfferDraft] = [TransactionState.AwaitingSellerAcceptance],
            [TransactionState.AwaitingSellerAcceptance] =
                [TransactionState.SellerAcceptedAwaitingPayment, TransactionState.Cancelled, TransactionState.Expired],
            [TransactionState.SellerAcceptedAwaitingPayment] =
                [TransactionState.CheckoutStarted, TransactionState.Cancelled, TransactionState.Expired],
            [TransactionState.LinkActive] = [TransactionState.CheckoutStarted, TransactionState.Cancelled, TransactionState.Expired],
            [TransactionState.CheckoutStarted] = [TransactionState.PaymentPending],
            [TransactionState.PaymentPending] = [TransactionState.PaidAwaitingShipment, TransactionState.PaidAwaitingDigitalDelivery, TransactionState.LinkActive, TransactionState.RefundPending, TransactionState.Expired],
            [TransactionState.PaidAwaitingShipment] = [TransactionState.TrackingSubmitted, TransactionState.ShipmentOverdue, TransactionState.RefundPending],
            [TransactionState.PaidAwaitingDigitalDelivery] = [TransactionState.DigitalDeliverySubmitted, TransactionState.ShipmentOverdue, TransactionState.RefundPending],
            [TransactionState.DigitalDeliverySubmitted] = [TransactionState.BuyerConfirmedReceipt, TransactionState.Disputed, TransactionState.PayoutEligible, TransactionState.RefundPending],
            [TransactionState.TrackingSubmitted] = [TransactionState.InTransit, TransactionState.TrackingUnverified, TransactionState.DeliveredDisputeWindow, TransactionState.ShipmentOverdue],
            [TransactionState.TrackingUnverified] =
                [TransactionState.TrackingSubmitted, TransactionState.InTransit, TransactionState.DeliveredDisputeWindow, TransactionState.PayoutEligible, TransactionState.ShipmentOverdue, TransactionState.RefundPending],
            [TransactionState.InTransit] = [TransactionState.DeliveredDisputeWindow, TransactionState.TrackingUnverified],
            [TransactionState.DeliveredDisputeWindow] = [TransactionState.BuyerConfirmedReceipt, TransactionState.Disputed, TransactionState.PayoutEligible],
            [TransactionState.BuyerConfirmedReceipt] = [TransactionState.PayoutEligible],
            [TransactionState.Disputed] = [TransactionState.ResolutionPending],
            [TransactionState.ResolutionPending] = [TransactionState.PayoutEligible, TransactionState.RefundPending],
            [TransactionState.PayoutEligible] = [TransactionState.PayoutPending],
            [TransactionState.PayoutPending] = [TransactionState.PaidOut],
            [TransactionState.ShipmentOverdue] = [TransactionState.RefundPending],
            [TransactionState.RefundPending] =
                [TransactionState.TrackingUnverified, TransactionState.Refunded],
            [TransactionState.Expired] = [TransactionState.PaidAwaitingShipment, TransactionState.PaidAwaitingDigitalDelivery, TransactionState.RefundPending]
        };

    private static readonly IReadOnlyDictionary<TransactionState, ActorRole[]> Roles =
        new Dictionary<TransactionState, ActorRole[]>
        {
            [TransactionState.AwaitingSellerAcceptance] = [ActorRole.Buyer],
            [TransactionState.SellerAcceptedAwaitingPayment] = [ActorRole.Seller],
            [TransactionState.CheckoutStarted] = [ActorRole.Buyer],
            [TransactionState.PaymentPending] = [ActorRole.Buyer],
            [TransactionState.PaidAwaitingShipment] = [ActorRole.PaymentProvider, ActorRole.Reconciliation],
            [TransactionState.PaidAwaitingDigitalDelivery] = [ActorRole.PaymentProvider, ActorRole.Reconciliation],
            [TransactionState.DigitalDeliverySubmitted] = [ActorRole.Seller],
            [TransactionState.TrackingSubmitted] =
                [ActorRole.Seller, ActorRole.Reconciliation],
            [TransactionState.TrackingUnverified] = [ActorRole.CarrierProvider, ActorRole.Reconciliation, ActorRole.System],
            [TransactionState.InTransit] = [ActorRole.CarrierProvider, ActorRole.Reconciliation],
            [TransactionState.DeliveredDisputeWindow] = [ActorRole.CarrierProvider, ActorRole.Reconciliation],
            [TransactionState.BuyerConfirmedReceipt] = [ActorRole.Buyer],
            [TransactionState.Disputed] = [ActorRole.Buyer],
            [TransactionState.PayoutEligible] = [ActorRole.Buyer, ActorRole.System, ActorRole.Reconciliation],
            [TransactionState.PayoutPending] = [ActorRole.System, ActorRole.Reconciliation],
            [TransactionState.PaidOut] = [ActorRole.PaymentProvider, ActorRole.Reconciliation],
            [TransactionState.ShipmentOverdue] = [ActorRole.System],
            [TransactionState.RefundPending] = [ActorRole.System, ActorRole.PaymentProvider, ActorRole.Reconciliation],
            [TransactionState.Refunded] = [ActorRole.PaymentProvider, ActorRole.Reconciliation],
            [TransactionState.Cancelled] = [ActorRole.Buyer, ActorRole.Seller],
            [TransactionState.Expired] = [ActorRole.System],
            [TransactionState.ResolutionPending] = [ActorRole.Reconciliation]
        };

    public void Transition(
        SaleTransaction transaction,
        TransactionState target,
        ActorRole role,
        string actorId,
        string eventName,
        DateTimeOffset occurredAt,
        string correlationId,
        string idempotencyKey,
        string metadataJson = "{}")
    {
        var source = transaction.State;
        if (!Allowed.TryGetValue(source, out var targets) || !targets.Contains(target))
            throw new DomainException($"ไม่อนุญาตให้เปลี่ยนสถานะจาก {source} เป็น {target}");

        if (!Roles.TryGetValue(target, out var roles) || !roles.Contains(role))
            throw new DomainException($"บทบาท {role} ไม่มีสิทธิ์เปลี่ยนสถานะเป็น {target}");

        transaction.ApplyTransition(
            target,
            occurredAt);
        transaction.AddAudit(new AuditEvent(
            transaction.Id, role, actorId, eventName, source, target, occurredAt,
            correlationId, idempotencyKey, metadataJson));
        transaction.QueueTransitionNotifications(
            eventName,
            occurredAt);
    }
}
