using MediatR;
using Toklong.Application.Features.Retention.ExecuteRetention;

namespace Toklong.Worker;

public sealed class RetentionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<RetentionWorker> logger)
    : BackgroundService
{
    private const int BatchSize = 100;
    private const int MaximumBatchesPerRun = 10;

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        using var timer =
            new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try
            {
                await ExecuteDueRetentionAsync(
                    stoppingToken);
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
                    "Retention execution failed and will retry");
            }
        }
        while (await timer.WaitForNextTickAsync(
                   stoppingToken));
    }

    private async Task ExecuteDueRetentionAsync(
        CancellationToken cancellationToken)
    {
        var transactionCount = 0;
        var financialCount = 0;
        for (var batch = 0;
             batch < MaximumBatchesPerRun;
             batch++)
        {
            await using var scope =
                scopeFactory.CreateAsyncScope();
            var sender = scope.ServiceProvider
                .GetRequiredService<ISender>();
            var result = await sender.Send(
                new ExecuteRetentionCommand(BatchSize),
                cancellationToken);
            var deletedFiles = await sender.Send(
                new ExecuteRetentionFileDeletionsCommand(
                    BatchSize),
                cancellationToken);
            transactionCount +=
                result.PurgedTransactions;
            financialCount +=
                result.PurgedFinancialRecords;
            if (result.PurgedTransactions <
                    BatchSize &&
                result.PurgedFinancialRecords <
                    BatchSize &&
                deletedFiles < BatchSize)
                break;
        }

        if (transactionCount > 0 ||
            financialCount > 0)
            logger.LogInformation(
                "Retention purged {TransactionCount} transaction evidence records and {FinancialCount} expired financial records",
                transactionCount,
                financialCount);
    }
}
