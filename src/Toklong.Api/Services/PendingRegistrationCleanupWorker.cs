using Toklong.Application.Abstractions;

namespace Toklong.Api.Services;

public sealed class PendingRegistrationCleanupWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IConfiguration configuration,
    ILogger<PendingRegistrationCleanupWorker> logger)
    : BackgroundService
{
    private readonly TimeSpan interval = TimeSpan.FromHours(
        PositiveSetting(
            configuration,
            "RegistrationCleanup:IntervalHours",
            6));
    private readonly TimeSpan retention = TimeSpan.FromHours(
        PositiveSetting(
            configuration,
            "RegistrationCleanup:RetentionHours",
            24));

    public async Task<int> RunOnceAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<
            IPendingMobileRegistrationRepository>();
        var deleted = await repository.DeleteExpiredBeforeAsync(
            timeProvider.GetUtcNow().Subtract(retention),
            cancellationToken);
        logger.LogInformation(
            "Deleted {DeletedCount} expired mobile registration records",
            deleted);
        return deleted;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        await RunSafelyAsync(stoppingToken);
        using var timer = new PeriodicTimer(interval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunSafelyAsync(stoppingToken);
    }

    private async Task RunSafelyAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await RunOnceAsync(cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Pending mobile registration cleanup failed");
        }
    }

    private static int PositiveSetting(
        IConfiguration configuration,
        string key,
        int fallback)
    {
        var value = configuration.GetValue(key, fallback);
        return value > 0 ? value : fallback;
    }
}
