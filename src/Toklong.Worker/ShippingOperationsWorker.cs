using MediatR;
using Microsoft.Extensions.Options;
using Toklong.Application.Features.Shipping.ProcessShippingOperations;
using Toklong.Application.Features.Shipping.ProcessProviderShipments;

namespace Toklong.Worker;

public sealed class ShippingOperationsWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ShippingWorkerOptions> configuredOptions,
    ILogger<ShippingOperationsWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        var options = configuredOptions.Value;
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(
                Math.Clamp(
                    options.OperationIdleSeconds,
                    1,
                    60)));
        var nextTrackingAt = DateTimeOffset.UtcNow;
        do
        {
            try
            {
                await using var scope =
                    scopeFactory.CreateAsyncScope();
                var sender = scope.ServiceProvider
                    .GetRequiredService<ISender>();
                var processedOperation = await sender.Send(
                    new ProcessNextShippingOperationCommand(
                        Environment.MachineName,
                        options.LeaseSeconds,
                        options.MaximumAttempts),
                    stoppingToken);
                if (processedOperation)
                    logger.LogInformation(
                        "Processed one durable shipping operation");

                if (DateTimeOffset.UtcNow < nextTrackingAt)
                    continue;
                var confirmations = await sender.Send(
                    new ConfirmProviderShipmentsCommand(),
                    stoppingToken);
                var tracking = await sender.Send(
                    new ReconcileProviderShipmentsCommand(),
                    stoppingToken);
                var cancellations = await sender.Send(
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
                nextTrackingAt = DateTimeOffset.UtcNow.AddSeconds(
                    Math.Clamp(
                        options.TrackingIntervalSeconds,
                        30,
                        3_600) + jitter);
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
        }
        while (await timer.WaitForNextTickAsync(
            stoppingToken));
    }
}
