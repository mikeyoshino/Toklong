using MediatR;
using Microsoft.EntityFrameworkCore;
using Toklong.Application.Features.ExternalEvents;
using Toklong.Application.Features.Shipping.ProcessProviderShipments;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Api.Services;

public sealed class DevelopmentDemoSimulationOptions
{
    public const string SectionName = "DevelopmentDemoSimulation";

    public bool Enabled { get; init; }
    public int StepIntervalSeconds { get; init; } = 3;

    public TimeSpan StepInterval =>
        TimeSpan.FromSeconds(Math.Clamp(StepIntervalSeconds, 1, 30));

    public static DevelopmentDemoSimulationOptions From(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var options = configuration
            .GetSection(SectionName)
            .Get<DevelopmentDemoSimulationOptions>()
            ?? new DevelopmentDemoSimulationOptions();

        if (options.Enabled && !environment.IsDevelopment())
            throw new InvalidOperationException(
                "Development demo simulation can run only in Development");

        return options;
    }
}

public sealed class DevelopmentDemoSimulationWorker(
    IServiceScopeFactory scopeFactory,
    DevelopmentDemoSimulationOptions options,
    TimeProvider timeProvider,
    ILogger<DevelopmentDemoSimulationWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(options.StepInterval, stoppingToken);
            try
            {
                var advanced = await RunOneStepAsync(stoppingToken);
                if (advanced > 0)
                    logger.LogInformation(
                        "Development demo simulation advanced {Count} transaction(s)",
                        advanced);
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
                    "Development demo simulation failed and will retry");
            }
        }
    }

    public async Task<int> RunOneStepAsync(
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var confirmations = await sender.Send(
            new ConfirmProviderShipmentsCommand(),
            cancellationToken);
        var candidates = await database.Transactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.State == TransactionState.TrackingSubmitted ||
                transaction.State == TransactionState.InTransit ||
                (transaction.State == TransactionState.PayoutPending &&
                 transaction.PayoutProvider == "manual-bank"))
            .Select(transaction => new DemoCandidate(
                transaction.Id,
                transaction.State,
                transaction.CarrierCode,
                transaction.TrackingNumber))
            .ToListAsync(cancellationToken);

        var advanced = confirmations.Processed;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await AdvanceAsync(
                        sender,
                        candidate,
                        cancellationToken))
                    advanced++;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Development demo simulation skipped transaction {TransactionId}",
                    candidate.Id);
            }
        }

        return advanced;
    }

    private async Task<bool> AdvanceAsync(
        ISender sender,
        DemoCandidate candidate,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        switch (candidate.State)
        {
            case TransactionState.TrackingSubmitted:
            case TransactionState.InTransit:
                if (string.IsNullOrWhiteSpace(candidate.CarrierCode) ||
                    string.IsNullOrWhiteSpace(candidate.TrackingNumber))
                    return false;

                var eventType =
                    candidate.State == TransactionState.TrackingSubmitted
                        ? "in_transit"
                        : "delivered";
                await sender.Send(
                    new RecordCarrierEventCommand(
                        candidate.Id,
                        $"demo-{eventType}-{candidate.Id:N}",
                        eventType,
                        now,
                        candidate.CarrierCode,
                        candidate.TrackingNumber),
                    cancellationToken);
                return true;

            case TransactionState.PayoutPending:
                await sender.Send(
                    new ConfirmManualPayoutCommand(
                        candidate.Id,
                        $"demo-payout-{candidate.Id:N}",
                        now),
                    cancellationToken);
                return true;

            default:
                return false;
        }
    }

    private sealed record DemoCandidate(
        Guid Id,
        TransactionState State,
        string? CarrierCode,
        string? TrackingNumber);
}
