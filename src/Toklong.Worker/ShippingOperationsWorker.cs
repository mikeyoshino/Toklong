using MediatR;
using Microsoft.Extensions.Options;
using Toklong.Application.Features.Shipping.ProcessShippingOperations;
using Toklong.Application.Features.Shipping.ProcessProviderShipments;
using Toklong.Domain.Transactions;
using Toklong.Application.Features.Shipping.ProcessCounterQrResources;

namespace Toklong.Worker;

public sealed class ShippingOperationsWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ShippingWorkerOptions> configuredOptions,
    ILogger<ShippingOperationsWorker> logger)
    : BackgroundService
{
    private static readonly IReadOnlySet<
        ShippingOperationType>
        ConfirmationTypes =
            new HashSet<ShippingOperationType>
            {
                ShippingOperationType
                    .ConfirmOutbound,
                ShippingOperationType
                    .ConfirmReturn
            };

    private static readonly IReadOnlySet<
        ShippingOperationType>
        OtherMutationTypes =
            new HashSet<ShippingOperationType>
            {
                ShippingOperationType
                    .BookOutbound,
                ShippingOperationType
                    .BookReturn,
                ShippingOperationType
                    .CancelOutbound,
                ShippingOperationType
                    .CancelReturn
            };

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        var options = configuredOptions.Value;
        var nextTrackingAt = DateTimeOffset.UtcNow;
        while (!stoppingToken
            .IsCancellationRequested)
        {
            var batchWasFull = false;
            try
            {
                await using var scope =
                    scopeFactory.CreateAsyncScope();
                var sender = scope.ServiceProvider
                    .GetRequiredService<ISender>();
                var confirmationsProcessed =
                    await sender.Send(
                    new ProcessShippingOperationBatchCommand(
                        Environment.MachineName,
                        ConfirmationTypes,
                        Math.Clamp(
                            options
                                .ConfirmationBatchSize,
                            1,
                            100),
                        options.LeaseSeconds,
                        options.MaximumAttempts),
                    stoppingToken);
                var otherProcessed =
                    await sender.Send(
                    new ProcessShippingOperationBatchCommand(
                        Environment.MachineName,
                        OtherMutationTypes,
                        Math.Clamp(
                            options
                                .OtherMutationBatchSize,
                            1,
                            100),
                        options.LeaseSeconds,
                        options.MaximumAttempts),
                    stoppingToken);
                var counterQrQueued = await sender.Send(
                    new QueueEligibleCounterQrResourcesCommand(),
                    stoppingToken);
                var counterQrProcessed =
                    await sender.Send(
                        new ProcessCounterQrResourceBatchCommand(
                            Environment.MachineName,
                            Math.Clamp(
                                options.OtherMutationBatchSize,
                                1,
                                100),
                            options.LeaseSeconds,
                            options.MaximumAttempts),
                        stoppingToken);
                if (confirmationsProcessed > 0 ||
                    otherProcessed > 0 ||
                    counterQrQueued > 0 ||
                    counterQrProcessed > 0)
                    logger.LogInformation(
                        "Processed {ConfirmationCount} confirmation, {OtherCount} other shipping operations, queued {CounterQrQueuedCount} Counter QR resources, and completed {CounterQrCount} Counter QR reads",
                        confirmationsProcessed,
                        otherProcessed,
                        counterQrQueued,
                        counterQrProcessed);
                batchWasFull =
                    confirmationsProcessed >=
                        Math.Clamp(
                            options
                                .ConfirmationBatchSize,
                            1,
                            100) ||
                    otherProcessed >=
                        Math.Clamp(
                            options
                                .OtherMutationBatchSize,
                            1,
                            100) ||
                    counterQrProcessed >=
                        Math.Clamp(
                            options.OtherMutationBatchSize,
                            1,
                            100);

                if (DateTimeOffset.UtcNow >=
                    nextTrackingAt)
                {
                    var confirmations =
                        await sender.Send(
                            new ConfirmProviderShipmentsCommand(),
                            stoppingToken);
                    var tracking = await sender.Send(
                        new ReconcileProviderShipmentsCommand(),
                        stoppingToken);
                    var cancellations =
                        await sender.Send(
                            new CancelProviderShipmentsCommand(),
                            stoppingToken);
                    if (confirmations.Processed > 0 ||
                        tracking.Processed > 0 ||
                        cancellations.Processed > 0)
                        logger.LogInformation(
                            "Shipping pass confirmed {ConfirmationCount}, reconciled {TrackingCount}, and cancelled {CancellationCount} shipments",
                            confirmations.Processed,
                            tracking.Processed,
                            cancellations.Processed);
                    var failures =
                        confirmations.Failed +
                        tracking.Failed +
                        cancellations.Failed;
                    if (failures > 0)
                        logger.LogWarning(
                            "Shipping pass will retry {FailureCount} failed operations",
                            failures);
                    var jitterRange = Math.Clamp(
                        options.TrackingJitterSeconds,
                        0,
                        120);
                    var jitter = jitterRange == 0
                        ? 0
                        : Random.Shared.Next(
                            0,
                            jitterRange + 1);
                    nextTrackingAt =
                        DateTimeOffset.UtcNow
                            .AddSeconds(
                                Math.Clamp(
                                    options
                                        .TrackingIntervalSeconds,
                                    30,
                                    3_600) +
                                jitter);
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Shipping operations failed and will retry");
            }
            if (!batchWasFull)
                await Task.Delay(
                    TimeSpan.FromSeconds(
                        Math.Clamp(
                            options
                                .OperationIdleSeconds,
                            1,
                            60)),
                    stoppingToken);
        }
    }
}
