using MediatR;
using Toklong.Application.Features.Shipping.ProcessProviderShipments;

namespace Toklong.Worker;

public sealed class ShippingOperationsWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ShippingOperationsWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        using var timer =
            new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            try
            {
                await using var scope =
                    scopeFactory.CreateAsyncScope();
                var sender = scope.ServiceProvider
                    .GetRequiredService<ISender>();
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
