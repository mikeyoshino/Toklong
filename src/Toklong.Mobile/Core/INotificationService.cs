namespace Toklong.Mobile.Core;

public sealed record AppNotification(
    Guid Id,
    Guid TransactionId,
    string EventType,
    string Title,
    string Body,
    string DeepLink,
    DateTimeOffset CreatedAt)
{
    public string Icon => EventType switch
    {
        "buyer_offer_received" => "ui_offer.png",
        "payment_confirmed" => "ui_money.png",
        "tracking_submitted" or "delivered" => "ui_truck.png",
        "payout_confirmed" or "refund_confirmed" =>
            "ui_check_money.png",
        _ => "ui_bell.png"
    };

    public string TimeText =>
        CreatedAt.ToLocalTime().ToString("d MMM · HH:mm");
}

public interface INotificationService
{
    Task<IReadOnlyList<AppNotification>> GetNotificationsAsync(
        CancellationToken cancellationToken = default);
}
