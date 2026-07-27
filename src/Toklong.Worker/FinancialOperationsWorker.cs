using MediatR;
using Toklong.Application.Features.Payments.ReconcilePayments;
using Toklong.Application.Features.Refunds.ProcessRefunds;

namespace Toklong.Worker;

public sealed class FinancialOperationsWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<FinancialOperationsWorker> logger)
    : BackgroundService
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
                var payments = await sender.Send(
                    new ReconcilePendingPaymentsCommand(),
                    stoppingToken);
                var refunds = await sender.Send(
                    new CreatePendingRefundsCommand(),
                    stoppingToken);
                var reconciledRefunds = await sender.Send(
                    new ReconcilePendingRefundsCommand(),
                    stoppingToken);

                if (payments > 0 ||
                    refunds > 0 ||
                    reconciledRefunds > 0)
                    logger.LogInformation(
                        "Financial pass confirmed {PaymentCount} payments, created {RefundCount} refunds, and reconciled {ReconciledRefundCount} refunds",
                        payments,
                        refunds,
                        reconciledRefunds);
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
                    "Financial reconciliation failed and will retry");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
