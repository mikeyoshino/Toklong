namespace Toklong.Application.Abstractions;

public sealed record NotificationInboxRecord(
    Guid Id,
    Guid TransactionId,
    string Template,
    string ProductName,
    long AmountSatang,
    string Currency,
    string PublicToken,
    DateTimeOffset CreatedAt,
    string? Detail = null,
    DateTimeOffset? ActionDeadlineAt = null);

public interface INotificationInboxRepository
{
    Task<IReadOnlyList<NotificationInboxRecord>> ListAsync(
        string recipientPhoneNumber,
        int maximumCount,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
