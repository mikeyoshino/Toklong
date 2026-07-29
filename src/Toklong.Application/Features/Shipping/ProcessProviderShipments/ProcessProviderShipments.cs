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
                        await shipmentProvider.ConfirmAsync(
                            transaction.ShippingPurchaseReference!,
                            transaction.ShippingProviderTrackingCode!,
                            transaction.CarrierCode!,
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
                        await shipmentProvider.CancelAsync(
                            transaction.ShippingCourierTrackingCode ??
                            transaction.TrackingNumber ??
                            transaction.ShippingProviderTrackingCode!,
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
