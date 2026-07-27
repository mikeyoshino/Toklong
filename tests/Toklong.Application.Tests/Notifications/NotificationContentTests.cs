using Toklong.Application.Abstractions;
using Toklong.Application.Features.Notifications.ListNotifications;

namespace Toklong.Application.Tests.Notifications;

public sealed class NotificationContentTests
{
    [Fact]
    public void Buyer_offer_notification_contains_product_and_exact_amount()
    {
        var transactionId = Guid.NewGuid();
        var notification = NotificationContent.From(
            new NotificationInboxRecord(
                Guid.NewGuid(),
                transactionId,
                "buyer_offer_received",
                "กล้อง Fujifilm X-T30 II",
                450_050,
                "THB",
                "0123456789abcdef0123456789abcdef",
                DateTimeOffset.UtcNow));

        Assert.Equal(
            "ได้รับข้อเสนอซื้อ",
            notification.Title);
        Assert.Equal(
            "กล้อง Fujifilm X-T30 II · ฿4,500.50",
            notification.Body);
        Assert.Equal(
            "toklong://offer/0123456789abcdef0123456789abcdef",
            notification.DeepLink);
    }

    [Fact]
    public void Evidence_request_shows_exact_bangkok_deadline()
    {
        var notification = NotificationContent.From(
            new NotificationInboxRecord(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "dispute_evidence_requested",
                "กล้องพร้อมเลนส์",
                450_000,
                "THB",
                "public-token",
                new DateTimeOffset(
                    2026, 7, 26, 9, 0, 0,
                    TimeSpan.Zero),
                "รูปฉลากขนส่งและบรรจุภัณฑ์",
                new DateTimeOffset(
                    2026, 7, 28, 9, 0, 0,
                    TimeSpan.Zero)));

        Assert.Equal(
            "ต้องส่งหลักฐานเพิ่มเติม",
            notification.Title);
        Assert.Contains(
            "รูปฉลากขนส่งและบรรจุภัณฑ์",
            notification.Body);
        Assert.Contains("16:00", notification.Body);
        Assert.Equal(
            "dispute_evidence_requested",
            notification.EventType);
    }

    [Fact]
    public void Refund_action_message_directs_buyer_to_Stripe_without_collecting_bank_data()
    {
        var notification = NotificationContent.From(
            new NotificationInboxRecord(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "refund_action_required",
                "กล้องพร้อมเลนส์",
                455_000,
                "THB",
                "public-token",
                new DateTimeOffset(
                    2026, 7, 27, 9, 0, 0,
                    TimeSpan.Zero),
                null,
                new DateTimeOffset(
                    2026, 9, 10, 9, 0, 0,
                    TimeSpan.Zero)));

        Assert.Equal(
            "ต้องยืนยันข้อมูลเพื่อรับเงินคืน",
            notification.Title);
        Assert.Contains(
            "อีเมลจาก Stripe",
            notification.Body);
        Assert.Contains(
            "ให้ Stripe โดยตรง",
            notification.Body);
        Assert.Contains(
            "TOKLONG จะไม่ขอเลขบัญชี",
            notification.Body);
        Assert.Contains("16:00", notification.Body);
    }
}
