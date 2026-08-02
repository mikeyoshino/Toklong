namespace Toklong.Mobile.Core;

public static class TransactionStatePresenter
{
    private const string Blue = "#145FC7";
    private const string BlueSoft = "#EAF4FF";
    private const string Amber = "#8A5100";
    private const string AmberSoft = "#FFF4DC";
    private const string Green = "#087C68";
    private const string GreenSoft = "#EAFBF7";
    private const string Red = "#C52F4D";
    private const string RedSoft = "#FFF1F3";
    private const string Gray = "#475467";
    private const string GraySoft = "#F1F4F7";

    public static TransactionPresentation Present(
        string state,
        AppTransactionRole role,
        AppFulfillmentType fulfillmentType,
        string? expirationReason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);

        return state switch
        {
            "BuyerOfferDraft" when role == AppTransactionRole.Buyer =>
                Action("ยังไม่ได้ส่งให้ผู้ขาย", "ส่งให้ผู้ขาย", TransactionAction.ShareWithSeller),
            "AwaitingSellerAcceptance" when
                role == AppTransactionRole.Buyer =>
                Progress("รอผู้ขายเตรียมขาย"),
            "AwaitingSellerAcceptance" =>
                Action(
                    "มีรายการรอเตรียมขาย",
                    "เตรียมขาย",
                    TransactionAction.ReviewSellerOffer),
            "SellerAcceptedAwaitingPayment" when role == AppTransactionRole.Buyer =>
                Action(
                    "ผู้ขายพร้อมขายแล้ว",
                    "ตรวจยอดและชำระ",
                    TransactionAction.ReviewAndPay),
            "SellerAcceptedAwaitingPayment" =>
                Progress("รอผู้ซื้อจ่ายเงิน"),
            "PaymentPending" when role == AppTransactionRole.Buyer =>
                Action(
                    "กำลังเช็กการจ่ายเงิน",
                    "จ่ายเงินต่อ",
                    TransactionAction.ReviewAndPay),
            "CheckoutStarted" or "PaymentPending" =>
                Progress("กำลังเช็กการจ่ายเงิน"),
            "PaidAwaitingShipment" when role == AppTransactionRole.Seller =>
                Action("ส่งสินค้าได้", "เพิ่มเลขพัสดุ", TransactionAction.AddTracking),
            "PaidAwaitingDigitalDelivery" when role == AppTransactionRole.Seller =>
                Action("ส่งของดิจิทัลได้", "แจ้งว่าส่งแล้ว", TransactionAction.ConfirmDigitalHandoff),
            "PaidAwaitingShipment" or "PaidAwaitingDigitalDelivery" =>
                Progress("รอผู้ขายส่งของ"),
            "DigitalDeliverySubmitted" when role == AppTransactionRole.Buyer =>
                Action("เช็กของที่ได้รับ", "ตรวจแล้ว / มีปัญหา", TransactionAction.ConfirmReceipt),
            "DeliveredDisputeWindow" when role == AppTransactionRole.Buyer =>
                Action("พัสดุถึงแล้ว", "ตรวจแล้ว / มีปัญหา", TransactionAction.ConfirmReceipt),
            "TrackingSubmitted" =>
                Progress("กำลังเช็กเลขพัสดุ"),
            "TrackingUnverified" when role == AppTransactionRole.Seller =>
                Action("เช็กเลขพัสดุไม่ได้", "แก้เลขพัสดุ", TransactionAction.AddTracking),
            "TrackingUnverified" =>
                Progress("กำลังเช็กเลขพัสดุ"),
            "InTransit" =>
                Progress("กำลังจัดส่ง"),
            "CarrierException" =>
                Progress("การจัดส่งต้องตรวจสอบ") with
                {
                    PrimaryAction =
                        TransactionAction.ViewStatus,
                    PrimaryActionLabel = "ดูรายละเอียด"
                },
            "DigitalDeliverySubmitted" =>
                Progress("รอผู้ซื้อยืนยันรับของ"),
            "DeliveredDisputeWindow" =>
                Progress("ผู้ซื้อกำลังตรวจสินค้า"),
            "Disputed" or "ResolutionPending" =>
                Warning("หยุดจ่ายเงินชั่วคราว"),
            "ShipmentOverdue" when role == AppTransactionRole.Seller &&
                                   fulfillmentType == AppFulfillmentType.Physical =>
                Danger("เลยกำหนดส่ง", "เพิ่มเลขพัสดุ", TransactionAction.AddTracking),
            "ShipmentOverdue" =>
                Danger("เลยกำหนดส่ง", "ดูสถานะ", TransactionAction.ViewStatus),
            "PayoutEligible" or "PayoutPending" when
                role == AppTransactionRole.Buyer =>
                Progress("ยืนยันรับของแล้ว"),
            "PayoutEligible" or "PayoutPending" =>
                Progress("กำลังดำเนินการรับเงิน"),
            "BuyerConfirmedReceipt" when role == AppTransactionRole.Buyer =>
                Progress("ยืนยันรับของแล้ว"),
            "BuyerConfirmedReceipt" =>
                Progress("ผู้ซื้อยืนยันรับของแล้ว"),
            "PaidOut" =>
                Complete("เสร็จแล้ว"),
            "RefundPending" =>
                Progress("กำลังคืนเงิน"),
            "Refunded" =>
                Complete("คืนเงินแล้ว"),
            "Expired" when expirationReason == "SellerDidNotRespond" =>
                Complete("ผู้ขายไม่ได้ตอบ"),
            "Expired" when expirationReason == "BuyerDidNotPay" &&
                           role == AppTransactionRole.Seller =>
                Complete("ผู้ซื้อไม่ได้จ่าย"),
            "Expired" when expirationReason == "BuyerDidNotPay" =>
                Complete("หมดเวลาชำระ"),
            "Expired" =>
                Complete("รายการหมดอายุ"),
            "Cancelled" =>
                Complete("ยกเลิกแล้ว"),
            _ =>
                Progress("กำลังอัปเดต")
        };
    }

    private static TransactionPresentation Action(
        string status,
        string action,
        TransactionAction actionKind) =>
        new(status, TransactionBucket.ActionRequired, Amber, AmberSoft, actionKind, action);

    private static TransactionPresentation Progress(string status) =>
        new(status, TransactionBucket.InProgress, Blue, BlueSoft,
            TransactionAction.ViewStatus, "ดูรายละเอียด");

    private static TransactionPresentation Warning(string status) =>
        new(status, TransactionBucket.InProgress, Amber, AmberSoft,
            TransactionAction.ViewStatus, "ดูรายละเอียด");

    private static TransactionPresentation Danger(
        string status,
        string action,
        TransactionAction actionKind) =>
        new(status, TransactionBucket.ActionRequired, Red, RedSoft, actionKind, action);

    private static TransactionPresentation Complete(string status) =>
        new(status, TransactionBucket.Completed, Green, GreenSoft,
            TransactionAction.ViewStatus, "ดูรายละเอียด");
}
