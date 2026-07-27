using MediatR;
using Toklong.Application.Features.Payouts.EvaluateDuePayouts;
using Toklong.Application.Features.Refunds.ProcessRefunds;
using Toklong.Application.Features.Transactions.EvaluateDueExpirations;

namespace Toklong.Worker;

public sealed class DeadlineWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<DeadlineWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await using var scope =
                    scopeFactory.CreateAsyncScope();
                var sender = scope.ServiceProvider
                    .GetRequiredService<ISender>();
                var expired = await sender.Send(
                    new EvaluateDueExpirationsCommand(),
                    stoppingToken);
                var overdue = await sender.Send(
                    new EvaluateShipmentDeadlinesCommand(),
                    stoppingToken);
                var duePayouts = await sender.Send(
                    new EvaluateDuePayoutsCommand(),
                    stoppingToken);

                if (expired > 0 || overdue > 0 || duePayouts > 0)
                    logger.LogInformation(
                        "Deadline pass expired {ExpiredCount} offers, moved {OverdueCount} overdue fulfillments, and evaluated {PayoutCount} payouts",
                        expired,
                        overdue,
                        duePayouts);
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
                    "Deadline evaluation failed and will retry");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
