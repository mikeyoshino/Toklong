using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.Shipping.ProcessProviderShipments;

public sealed record ShippingProcessingResult(
    int Processed,
    int Failed);

public sealed record ConfirmProviderShipmentsCommand :
    IRequest<ShippingProcessingResult>;

public sealed record ReconcileProviderShipmentsCommand :
    IRequest<ShippingProcessingResult>;

public sealed record CancelProviderShipmentsCommand :
    IRequest<ShippingProcessingResult>;

public sealed class ConfirmProviderShipmentsHandler(
    ITransactionRepository repository,
    IShipmentProvider shipmentProvider,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions)
    : IRequestHandler<
        ConfirmProviderShipmentsCommand,
        ShippingProcessingResult>
{
    public async Task<ShippingProcessingResult> Handle(
        ConfirmProviderShipmentsCommand request,
        CancellationToken cancellationToken)
    {
        var transactions = await repository
            .GetShipmentsAwaitingProviderConfirmationAsync(
                cancellationToken);
        var processed = 0;
        var failed = 0;
        foreach (var transaction in transactions)
        {
            var aggregateMutated = false;
            try
            {
                EnsureProvider(transaction, shipmentProvider);
                var tracking = await shipmentProvider.GetTrackingAsync(
                    transaction.ShippingProviderTrackingCode!,
                    transaction.CarrierCode!,
                    cancellationToken);
                ShipmentConfirmation confirmation;
                if (IsConfirmedStatus(
                        tracking.ProviderStatus) &&
                    !string.IsNullOrWhiteSpace(
                        tracking.CourierTrackingCode))
                {
                    confirmation = new ShipmentConfirmation(
                        tracking.ProviderTrackingCode,
                        tracking.CourierTrackingCode,
                        tracking.CarrierCode,
                        tracking.ProviderStatus,
                        clock.UtcNow);
                }
                else
                {
                    confirmation =
                        await shipmentProvider.ConfirmServiceAsync(
                            transaction.ShippingPurchaseReference!,
                            transaction.ShippingProviderTrackingCode!,
                            transaction.CarrierCode!,
                            transaction.ShippingServiceCode ??
                            throw new InvalidOperationException(
                                "shipping-service-code-missing"),
                            cancellationToken);
                }

                aggregateMutated = true;
                transaction.ConfirmProviderManagedShipment(
                    shipmentProvider.ProviderName,
                    confirmation.ProviderTrackingCode,
                    confirmation.CourierTrackingCode,
                    confirmation.CarrierCode,
                    confirmation.ProviderStatus,
                    confirmation.ConfirmedAt,
                    transitions);
                await unitOfWork.SaveChangesAsync(
                    cancellationToken);
                processed++;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                failed++;
                if (aggregateMutated)
                    break;
            }
        }
        return new(processed, failed);
    }

    private static bool IsConfirmedStatus(
        string value) =>
        value.Trim().ToLowerInvariant() is
            "booking" or "shipping" or "package_detail" or
            "problem" or "complete" or "return" or "close";

    internal static void EnsureProvider(
        SaleTransaction transaction,
        IShipmentProvider provider)
    {
        if (!string.Equals(
                transaction.ShippingQuoteProvider,
                provider.ProviderName,
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "ผู้ให้บริการขนส่งของ worker ไม่ตรงกับรายการ");
    }
}

public sealed class ReconcileProviderShipmentsHandler(
    ITransactionRepository repository,
    IShipmentProvider shipmentProvider,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions)
    : IRequestHandler<
        ReconcileProviderShipmentsCommand,
        ShippingProcessingResult>
{
    public async Task<ShippingProcessingResult> Handle(
        ReconcileProviderShipmentsCommand request,
        CancellationToken cancellationToken)
    {
        var transactions = await repository
            .GetProviderShipmentsForReconciliationAsync(
                cancellationToken);
        var processed = 0;
        var failed = 0;
        foreach (var transaction in transactions)
        {
            var aggregateMutated = false;
            try
            {
                var managedShipment =
                    SelectManagedShipment(transaction);
                if (managedShipment is not null)
                {
                    EnsureManagedProvider(
                        managedShipment,
                        shipmentProvider);
                    var managedUpdate =
                        await shipmentProvider.GetTrackingAsync(
                            managedShipment.ProviderTrackingCode!,
                            managedShipment.CarrierCode,
                            cancellationToken);
                    aggregateMutated = true;
                    ApplyManagedUpdate(
                        transaction,
                        managedShipment,
                        managedUpdate);
                    await unitOfWork.SaveChangesAsync(
                        cancellationToken);
                    processed++;
                    continue;
                }

                ConfirmProviderShipmentsHandler.EnsureProvider(
                    transaction,
                    shipmentProvider);
                var update = await shipmentProvider.GetTrackingAsync(
                    transaction.ShippingProviderTrackingCode!,
                    transaction.CarrierCode!,
                    cancellationToken);
                aggregateMutated = true;
                transaction.RecordShippingProviderReconciliation(
                    shipmentProvider.ProviderName,
                    update.ProviderStatus,
                    clock.UtcNow);
                if (ShouldApply(
                        transaction.State,
                        update.EventType) &&
                    !transaction.HasExternalEvent(
                        transaction.CarrierCode!,
                        update.EventId))
                {
                    if (update.OccurredAt.HasValue)
                        transaction.RecordCarrierEvent(
                            update.EventId,
                            update.EventType!,
                            update.OccurredAt.Value,
                            clock.UtcNow,
                            transitions,
                            update.CarrierCode,
                            update.CourierTrackingCode ??
                            transaction.TrackingNumber);
                    else
                        transaction
                            .RecordUnverifiedCarrierEvidence(
                                shipmentProvider.ProviderName,
                                update.EventId,
                                update.ProviderStatus,
                                clock.UtcNow,
                                transitions);
                }
                await unitOfWork.SaveChangesAsync(
                    cancellationToken);
                processed++;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                failed++;
                if (aggregateMutated)
                    break;
            }
        }
        return new(processed, failed);
    }

    private static ManagedShipment? SelectManagedShipment(
        SaleTransaction transaction) =>
        transaction.ManagedShipments
            .Where(shipment =>
                !string.IsNullOrWhiteSpace(
                    shipment.ProviderTrackingCode) &&
                shipment.Status is
                    ManagedShipmentStatus.Confirmed or
                    ManagedShipmentStatus.CarrierAccepted or
                    ManagedShipmentStatus.InTransit or
                    ManagedShipmentStatus.TrackingUnverified)
            .OrderByDescending(shipment =>
                shipment.Direction ==
                    ShipmentDirection.Return)
            .ThenByDescending(shipment => shipment.CreatedAt)
            .FirstOrDefault();

    private static void EnsureManagedProvider(
        ManagedShipment shipment,
        IShipmentProvider provider)
    {
        if (!string.Equals(
                shipment.Provider,
                provider.ProviderName,
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "ผู้ให้บริการขนส่งของ worker ไม่ตรงกับรายการ");
    }

    private void ApplyManagedUpdate(
        SaleTransaction transaction,
        ManagedShipment shipment,
        ShipmentTrackingUpdate update)
    {
        if (update.EventType is null)
        {
            shipment.RecordProviderReconciliation(
                update.ProviderStatus,
                clock.UtcNow);
            if (shipment.Direction ==
                ShipmentDirection.Outbound)
                transaction.RecordShippingProviderReconciliation(
                    shipmentProvider.ProviderName,
                    update.ProviderStatus,
                    clock.UtcNow);
            return;
        }

        if (shipment.Direction == ShipmentDirection.Return)
        {
            transaction.RecordManagedReturnTrackingEvent(
                shipment.Id,
                update.EventId,
                update.EventType,
                update.ProviderStatus,
                update.OccurredAt,
                shipmentProvider.ProviderName,
                clock.UtcNow);
            return;
        }

        if (update.EventType == "carrier_exception")
        {
            transaction.RecordManagedOutboundCarrierException(
                shipment.Id,
                update.EventId,
                update.ProviderStatus,
                shipmentProvider.ProviderName,
                clock.UtcNow,
                transitions);
            return;
        }

        transaction.RecordShippingProviderReconciliation(
            shipmentProvider.ProviderName,
            update.ProviderStatus,
            clock.UtcNow);
        if (!ShouldApply(
                transaction.State,
                update.EventType) ||
            transaction.HasExternalEvent(
                transaction.CarrierCode!,
                update.EventId))
            return;

        if (update.EventType == "in_transit" &&
            update.OccurredAt.HasValue)
            shipment.RecordInTransit(
                update.ProviderStatus,
                update.OccurredAt.Value,
                clock.UtcNow);
        else if (update.EventType == "delivered" &&
                 update.OccurredAt.HasValue)
            shipment.RecordTrustedDelivery(
                update.ProviderStatus,
                update.OccurredAt.Value,
                clock.UtcNow);
        else if (update.EventType == "unverified")
            shipment.RecordTrackingUnverified(
                update.ProviderStatus,
                clock.UtcNow);

        if (update.OccurredAt.HasValue)
            transaction.RecordCarrierEvent(
                update.EventId,
                update.EventType!,
                update.OccurredAt.Value,
                clock.UtcNow,
                transitions,
                update.CarrierCode,
                update.CourierTrackingCode ??
                transaction.TrackingNumber);
        else
            transaction.RecordUnverifiedCarrierEvidence(
                shipmentProvider.ProviderName,
                update.EventId,
                update.ProviderStatus,
                clock.UtcNow,
                transitions);
    }

    private static bool ShouldApply(
        TransactionState state,
        string? eventType) =>
        eventType switch
        {
            "in_transit" =>
                state is TransactionState.TrackingSubmitted or
                    TransactionState.TrackingUnverified,
            "delivered" =>
                state is TransactionState.TrackingSubmitted or
                    TransactionState.TrackingUnverified or
                    TransactionState.InTransit,
            "unverified" =>
                state is TransactionState.TrackingSubmitted or
                    TransactionState.InTransit,
            _ => false
        };
}

public sealed class CancelProviderShipmentsHandler(
    ITransactionRepository repository,
    IShipmentProvider shipmentProvider,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions)
    : IRequestHandler<
        CancelProviderShipmentsCommand,
        ShippingProcessingResult>
{
    public async Task<ShippingProcessingResult> Handle(
        CancelProviderShipmentsCommand request,
        CancellationToken cancellationToken)
    {
        var transactions = await repository
            .GetShipmentsPendingCancellationAsync(
                cancellationToken);
        var processed = 0;
        var failed = 0;
        foreach (var transaction in transactions)
        {
            var aggregateMutated = false;
            try
            {
                ConfirmProviderShipmentsHandler.EnsureProvider(
                    transaction,
                    shipmentProvider);
                if (transaction.ShippingConfirmedAt.HasValue)
                {
                    var tracking =
                        await shipmentProvider.GetTrackingAsync(
                            transaction.ShippingProviderTrackingCode!,
                            transaction.CarrierCode!,
                            cancellationToken);
                    if (tracking.EventType is
                        "in_transit" or "delivered")
                    {
                        aggregateMutated = true;
                        if (tracking.OccurredAt.HasValue)
                            transaction
                                .RecordShipmentScanDuringRefund(
                                    shipmentProvider.ProviderName,
                                    tracking.ProviderStatus,
                                    tracking.OccurredAt.Value,
                                    clock.UtcNow,
                                    transitions);
                        else
                            transaction
                                .RecordUnverifiedCarrierEvidence(
                                    shipmentProvider.ProviderName,
                                    tracking.EventId,
                                    tracking.ProviderStatus,
                                    clock.UtcNow,
                                    transitions);
                        await unitOfWork.SaveChangesAsync(
                            cancellationToken);
                        processed++;
                        continue;
                    }

                    if (!string.Equals(
                            tracking.ProviderStatus,
                            "cancel",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        await shipmentProvider.CancelServiceAsync(
                            transaction.ShippingCourierTrackingCode ??
                            transaction.TrackingNumber ??
                            transaction.ShippingProviderTrackingCode!,
                            transaction.ShippingServiceCode ??
                            throw new InvalidOperationException(
                                "shipping-service-code-missing"),
                            isReturn: false,
                            cancellationToken:
                                cancellationToken);
                    }
                }

                aggregateMutated = true;
                transaction.RecordShippingCancellation(
                    shipmentProvider.ProviderName,
                    clock.UtcNow);
                await unitOfWork.SaveChangesAsync(
                    cancellationToken);
                processed++;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                failed++;
                if (aggregateMutated)
                    break;
            }
        }
        return new(processed, failed);
    }
}
