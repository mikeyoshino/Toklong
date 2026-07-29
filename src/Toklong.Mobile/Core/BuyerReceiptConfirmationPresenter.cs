using System.Globalization;

namespace Toklong.Mobile.Core;

public sealed record BuyerReceiptConfirmationPresentation(
    string Heading,
    string SupportingText,
    bool HasDeadline,
    string DeadlineText,
    string PrimaryActionText,
    string ProblemActionText,
    string ConfirmationTitle,
    string ConfirmationMessage,
    string ConfirmationAcceptText,
    string ConfirmationCancelText,
    string SuccessMessage);

public static class BuyerReceiptConfirmationPresenter
{
    private static readonly CultureInfo ThaiCulture =
        CultureInfo.GetCultureInfo("th-TH");

    public static BuyerReceiptConfirmationPresentation? Present(
        AppTransaction? transaction)
    {
        if (transaction is null ||
            transaction.Role != AppTransactionRole.Buyer ||
            transaction.Presentation.PrimaryAction !=
                TransactionAction.ConfirmReceipt)
            return null;

        if (transaction.FulfillmentType ==
                AppFulfillmentType.Physical &&
            !transaction.ActionDeadline.HasValue)
            return null;

        return transaction.FulfillmentType ==
            AppFulfillmentType.Physical
                ? Physical(transaction.ActionDeadline!.Value)
                : Digital();
    }

    private static BuyerReceiptConfirmationPresentation Physical(
        DateTimeOffset deadline) =>
        new(
            "ตรวจสินค้าให้เรียบร้อย",
            "เช็กสินค้าและอุปกรณ์ให้ครบก่อนยืนยัน เมื่อยืนยันแล้ว ระบบจะเริ่มจ่ายเงินให้ผู้ขาย",
            true,
            "แจ้งปัญหาได้ถึง " +
            deadline.ToLocalTime().ToString(
                "d MMM yyyy · HH:mm 'น.'",
                ThaiCulture),
            "ยืนยันว่าได้รับของเรียบร้อย",
            "พบปัญหากับรายการนี้",
            "ยืนยันหลังตรวจสินค้า",
            "คุณตรวจสินค้าแล้วและไม่พบปัญหา เมื่อยืนยัน ระบบจะเริ่มขั้นตอนจ่ายเงินให้ผู้ขาย",
            "ยืนยันและเริ่มจ่ายให้ผู้ขาย",
            "กลับไปตรวจสินค้า",
            "ยืนยันว่าตรวจแล้ว ระบบจะเริ่มขั้นตอนจ่ายเงินให้ผู้ขาย");

    private static BuyerReceiptConfirmationPresentation Digital() =>
        new(
            "ตรวจรายการที่ได้รับ",
            "ตรวจรายการและการเข้าถึงให้เรียบร้อยก่อนยืนยัน เมื่อยืนยันแล้ว ระบบจะเริ่มจ่ายเงินให้ผู้ขาย",
            false,
            "",
            "ยืนยันว่าได้รับเรียบร้อย",
            "พบปัญหากับรายการนี้",
            "ยืนยันหลังตรวจรายการ",
            "คุณตรวจรายการที่ได้รับแล้วและไม่พบปัญหา เมื่อยืนยัน ระบบจะเริ่มขั้นตอนจ่ายเงินให้ผู้ขาย",
            "ยืนยันและเริ่มจ่ายให้ผู้ขาย",
            "กลับไปตรวจรายการ",
            "ยืนยันว่าตรวจแล้ว ระบบจะเริ่มขั้นตอนจ่ายเงินให้ผู้ขาย");
}
