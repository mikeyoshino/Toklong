using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;

namespace Toklong.Infrastructure.Persistence;

public sealed class NotificationInboxRepository(
    ToklongDbContext database) : INotificationInboxRepository
{
    public async Task<IReadOnlyList<NotificationInboxRecord>> ListAsync(
        string recipientPhoneNumber,
        int maximumCount,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await (
            from notification in database.NotificationOutbox
            join transaction in database.Transactions
                on notification.TransactionId equals transaction.Id
            where notification.Recipient == recipientPhoneNumber &&
                  notification.AvailableAt <= now
            orderby notification.CreatedAt descending
            select new NotificationInboxRecord(
                notification.Id,
                notification.TransactionId,
                notification.Template,
                transaction.ProductName,
                transaction.PriceSatang,
                transaction.Currency,
                transaction.PublicToken,
                notification.CreatedAt,
                notification.Detail,
                notification.ActionDeadlineAt))
            .Take(maximumCount)
            .ToListAsync(cancellationToken);
}
