using System.Globalization;
using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;

namespace Toklong.Application.Features.Notifications.ListNotifications;

public sealed record ListNotificationsQuery(
    string PhoneNumber,
    int MaximumCount = 50)
    : IRequest<IReadOnlyList<NotificationView>>;

public sealed record NotificationView(
    Guid Id,
    Guid TransactionId,
    string EventType,
    string Title,
    string Body,
    string DeepLink,
    DateTimeOffset CreatedAt);

public sealed class ListNotificationsHandler(
    INotificationInboxRepository repository,
    IClock clock)
    : IRequestHandler<
        ListNotificationsQuery,
        IReadOnlyList<NotificationView>>
{
    public async Task<IReadOnlyList<NotificationView>> Handle(
        ListNotificationsQuery request,
        CancellationToken cancellationToken)
    {
        var phone = ThaiMobilePhone.Normalize(request.PhoneNumber);
        var maximum = Math.Clamp(request.MaximumCount, 1, 100);
        var records = await repository.ListAsync(
            phone,
            maximum,
            clock.UtcNow,
            cancellationToken);
        return records.Select(NotificationContent.From).ToArray();
    }
}

public static class NotificationContent
{
    private static readonly CultureInfo ThaiCulture =
        CultureInfo.GetCultureInfo("th-TH");

    public static NotificationView From(
        NotificationInboxRecord record)
    {
        var amount = FormatMoney(
            record.AmountSatang,
            record.Currency);
        var (title, body, deepLink) = record.Template switch
        {
            "buyer_offer_received" => (
                "ได้รับข้อเสนอซื้อ",
                $"{record.ProductName} · {amount}",
                $"toklong://offer/{record.PublicToken}"),
            "seller_accepted" => (
                "ผู้ขายตอบรับแล้ว",
                $"{record.ProductName} · ตรวจรายละเอียดและจ่ายเงิน",
                $"toklong://transaction/{record.TransactionId:N}"),
            "payment_confirmed" => (
                "ผู้ซื้อจ่ายเงินแล้ว",
                $"{record.ProductName} · ส่งสินค้าได้แล้ว",
                $"toklong://transaction/{record.TransactionId:N}"),
            "tracking_submitted" => (
                "ผู้ขายส่งสินค้าแล้ว",
                record.ProductName,
                $"toklong://transaction/{record.TransactionId:N}"),
            "delivered" => (
                "พัสดุถึงแล้ว",
                $"{record.ProductName} · ตรวจสินค้าและยืนยันการรับ",
                $"toklong://transaction/{record.TransactionId:N}"),
            "dispute_opened" => (
                "มีการแจ้งปัญหา",
                record.ProductName,
                $"toklong://transaction/{record.TransactionId:N}"),
            "dispute_evidence_requested" => (
                "ต้องส่งหลักฐานเพิ่มเติม",
                EvidenceRequestBody(record),
                $"toklong://transaction/{record.TransactionId:N}"),
            "dispute_resolved_for_seller" => (
                "ตรวจสอบข้อโต้แย้งแล้ว",
                $"{record.ProductName} · ผลรายการเป็นการจ่ายเงินให้ผู้ขาย",
                $"toklong://transaction/{record.TransactionId:N}"),
            "dispute_resolved_for_buyer" => (
                "ตรวจสอบข้อโต้แย้งแล้ว",
                $"{record.ProductName} · กำลังดำเนินการคืนเงินให้ผู้ซื้อ",
                $"toklong://transaction/{record.TransactionId:N}"),
            "payout_started" => (
                "กำลังดำเนินการจ่ายเงิน",
                record.ProductName,
                $"toklong://transaction/{record.TransactionId:N}"),
            "payout_confirmed" => (
                "จ่ายเงินให้ผู้ขายแล้ว",
                $"{record.ProductName} · {amount}",
                $"toklong://transaction/{record.TransactionId:N}"),
            "refund_started" => (
                "กำลังดำเนินการคืนเงิน",
                record.ProductName,
                $"toklong://transaction/{record.TransactionId:N}"),
            "refund_action_required" => (
                "ต้องยืนยันข้อมูลเพื่อรับเงินคืน",
                RefundActionRequiredBody(record),
                $"toklong://transaction/{record.TransactionId:N}"),
            "refund_confirmed" => (
                "คืนเงินแล้ว",
                $"{record.ProductName} · {amount}",
                $"toklong://transaction/{record.TransactionId:N}"),
            "payout_reminder_24h" => (
                "เหลือเวลาแจ้งปัญหา 24 ชั่วโมง",
                record.ProductName,
                $"toklong://transaction/{record.TransactionId:N}"),
            _ => (
                "อัปเดตรายการ",
                record.ProductName,
                $"toklong://transaction/{record.TransactionId:N}")
        };

        return new NotificationView(
            record.Id,
            record.TransactionId,
            record.Template,
            title,
            body,
            deepLink,
            record.CreatedAt);
    }

    private static string FormatMoney(
        long amountSatang,
        string currency)
    {
        var absolute = Math.Abs(amountSatang);
        var whole = absolute / 100;
        var fraction = absolute % 100;
        var sign = amountSatang < 0 ? "-" : "";
        var amount = fraction == 0
            ? whole.ToString("N0", ThaiCulture)
            : $"{whole.ToString("N0", ThaiCulture)}.{fraction:00}";
        return currency.Equals(
                "THB",
                StringComparison.OrdinalIgnoreCase)
            ? $"{sign}฿{amount}"
            : $"{sign}{amount} {currency}";
    }

    private static string EvidenceRequestBody(
        NotificationInboxRecord record)
    {
        var detail = string.IsNullOrWhiteSpace(record.Detail)
            ? record.ProductName
            : record.Detail.Trim();
        if (!record.ActionDeadlineAt.HasValue)
            return detail;
        var bangkok = TimeZoneInfo.ConvertTime(
            record.ActionDeadlineAt.Value,
            TimeZoneInfo.FindSystemTimeZoneById(
                "Asia/Bangkok"));
        return $"{detail} · ส่งภายใน {bangkok.ToString("d MMM yyyy HH:mm", ThaiCulture)} น.";
    }

    private static string RefundActionRequiredBody(
        NotificationInboxRecord record)
    {
        var deadline = record.ActionDeadlineAt.HasValue
            ? $" ภายใน {record.ActionDeadlineAt.Value.ToLocalTime().ToString(
                "d MMM yyyy HH:mm",
                ThaiCulture)} น."
            : "";
        return $"{record.ProductName} · เปิดอีเมลจาก Stripe และส่งบัญชีที่ใช้ชำระให้ Stripe โดยตรง{deadline} TOKLONG จะไม่ขอเลขบัญชีในแอป";
    }
}
