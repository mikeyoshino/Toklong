using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Notifications.ListNotifications;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Worker;

public sealed class NotificationOutboxWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationOutboxWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
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
                    "Notification outbox dispatch failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task DispatchBatchAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        var provider = scope.ServiceProvider
            .GetRequiredService<INotificationProvider>();
        var now = DateTimeOffset.UtcNow;
        var messages = await database.NotificationOutbox
            .Where(message =>
                message.SentAt == null &&
                message.AvailableAt <= now &&
                message.Attempts < 10)
            .OrderBy(message => message.AvailableAt)
            .Take(25)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            try
            {
                var transaction = await database.Transactions
                    .SingleAsync(
                        item => item.Id == message.TransactionId,
                        cancellationToken);
                var content = NotificationContent.From(
                    new NotificationInboxRecord(
                        message.Id,
                        message.TransactionId,
                        message.Template,
                        transaction.ProductName,
                        transaction.PriceSatang,
                        transaction.Currency,
                        transaction.PublicToken,
                        message.CreatedAt,
                        message.Detail,
                        message.ActionDeadlineAt));
                var result = await provider.SendAsync(
                    message.Id,
                    message.Recipient,
                    message.Template,
                    message.TransactionId,
                    content.Title,
                    content.Body,
                    content.DeepLink,
                    cancellationToken);
                message.MarkSent(
                    result.ProviderReference,
                    DateTimeOffset.UtcNow);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                message.MarkAttemptFailed(DateTimeOffset.UtcNow);
                logger.LogWarning(
                    exception,
                    "Notification {NotificationId} for transaction {TransactionId} will retry",
                    message.Id,
                    message.TransactionId);
            }

            await database.SaveChangesAsync(cancellationToken);
        }
    }
}
