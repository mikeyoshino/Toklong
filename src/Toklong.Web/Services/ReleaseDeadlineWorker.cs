using MediatR;
using Toklong.Application.Features.Payouts.EvaluateDuePayouts;

namespace Toklong.Web.Services;

public sealed class ReleaseDeadlineWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ReleaseDeadlineWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();
                var count = await sender.Send(new EvaluateDuePayoutsCommand(), stoppingToken);
                if (count > 0)
                    logger.LogInformation("Evaluated {Count} transactions due for payout", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Release deadline evaluation failed and will retry");
            }

            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }
}
