using Toklong.Domain.Transactions;

namespace Toklong.Crm.Disputes;

public sealed record CrmAuditDisplay(
    string Title,
    string Description,
    string ActorLabel,
    string FromStateLabel,
    string ToStateLabel,
    string Tone);

public static class CrmAuditPresentation
{
    public static CrmAuditDisplay For(
        CrmCoreAuditView audit)
    {
        var (title, description, tone) =
            EventText(audit.Name);
        return new CrmAuditDisplay(
            title,
            description,
            ActorLabel(audit.ActorRole),
            StateLabel(audit.FromState),
            StateLabel(audit.ToState),
            tone);
    }

    private static (
        string Title,
        string Description,
        string Tone) EventText(string name) =>
        name switch
        {
            "buyer_offer.created" =>
                (
                    "ผู้ซื้อสร้างข้อเสนอ",
                    "ส่งรายละเอียดสินค้าและเงื่อนไขให้ผู้ขายตรวจสอบ",
                    "info"),
            "buyer_offer.seller_accepted" =>
                (
                    "ผู้ขายยอมรับข้อเสนอ",
                    "ผู้ซื้อสามารถดำเนินการชำระเงินตามเวลาที่กำหนด",
                    "info"),
            "buyer_offer.seller_declined" =>
                (
                    "ผู้ขายปฏิเสธข้อเสนอ",
                    "รายการนี้จะไม่เข้าสู่ขั้นตอนชำระเงิน",
                    "neutral"),
            "buyer_offer.seller_response_expired" =>
                (
                    "ผู้ขายไม่ตอบรับภายในกำหนด",
                    "ระบบปิดข้อเสนอที่หมดเวลาโดยอัตโนมัติ",
                    "warning"),
            "buyer_offer.payment_window_expired" =>
                (
                    "หมดเวลาชำระเงิน",
                    "ผู้ซื้อไม่ได้ชำระเงินภายในเวลาที่กำหนด",
                    "warning"),
            "checkout.started" =>
                (
                    "ผู้ซื้อเริ่มชำระเงิน",
                    "ระบบสร้างขั้นตอนชำระเงินสำหรับรายการนี้",
                    "info"),
            "payment.awaiting_verification" =>
                (
                    "กำลังรอยืนยันการชำระเงิน",
                    "ยังไม่ถือว่าชำระสำเร็จจนกว่าผู้ให้บริการจะยืนยัน",
                    "info"),
            "payment.confirmed" =>
                (
                    "ยืนยันการชำระเงินแล้ว",
                    "ผู้ให้บริการชำระเงินยืนยันว่ารับเงินสำเร็จ",
                    "success"),
            "payment.confirmed_after_deadline_refund_required" =>
                (
                    "รับเงินหลังหมดเวลา",
                    "ระบบหยุดรายการและกำหนดให้คืนเงินแก่ผู้ซื้อ",
                    "warning"),
            "payment.reconciled_duplicate_confirmation" =>
                (
                    "ตรวจพบคำยืนยันชำระเงินซ้ำ",
                    "ระบบตรวจสอบแล้วและไม่บันทึกการชำระซ้ำ",
                    "neutral"),
            "shipment.provider_confirmed" =>
                (
                    "ผู้ให้บริการยืนยันการจัดส่ง",
                    "ข้อมูลการจัดส่งได้รับการยืนยันจากระบบขนส่ง",
                    "info"),
            "shipment.tracking_submitted" =>
                (
                    "เพิ่มหมายเลขติดตามแล้ว",
                    "ผู้ขายส่งข้อมูลขนส่งเพื่อเริ่มติดตามพัสดุ",
                    "info"),
            "shipment.provider_status_reconciled" =>
                (
                    "อัปเดตสถานะการจัดส่ง",
                    "ระบบตรวจสอบสถานะล่าสุดกับผู้ให้บริการขนส่ง",
                    "info"),
            "shipment.provider_cancelled" =>
                (
                    "ยกเลิกการจัดส่งแล้ว",
                    "ผู้ให้บริการยืนยันการยกเลิกรายการจัดส่ง",
                    "warning"),
            "shipment.cancellation_skipped_after_carrier_scan" =>
                (
                    "ไม่สามารถยกเลิกการจัดส่งได้",
                    "พัสดุถูกบริษัทขนส่งรับเข้าระบบแล้ว",
                    "warning"),
            "shipment.timely_acceptance_recovered" =>
                (
                    "ยืนยันว่าผู้ขายส่งพัสดุทันเวลา",
                    "พบข้อมูลขนส่งว่ารับพัสดุก่อนกำหนด ระบบจึงหยุดการคืนเงินอัตโนมัติและส่งต่อให้ตรวจสอบปัญหาขนส่ง",
                    "warning"),
            "carrier.delivered" =>
                (
                    "ขนส่งยืนยันว่าส่งถึงแล้ว",
                    "เริ่มช่วงเวลาให้ผู้ซื้อตรวจสอบสินค้า 72 ชั่วโมง",
                    "success"),
            "carrier.in_transit" =>
                (
                    "ขนส่งรับพัสดุเข้าระบบแล้ว",
                    "พัสดุอยู่ระหว่างนำส่งและติดตามสถานะได้",
                    "info"),
            "carrier.unverified" =>
                (
                    "ยังยืนยันสถานะพัสดุไม่ได้",
                    "ระบบหยุดการจ่ายเงินอัตโนมัติจนกว่าจะตรวจสอบการส่งถึงได้",
                    "warning"),
            "digital_delivery.submitted" =>
                (
                    "ผู้ขายแจ้งส่งมอบดิจิทัลแล้ว",
                    "ยังต้องรอผู้ซื้อยืนยันหรือเจ้าหน้าที่ตรวจสอบ",
                    "info"),
            "buyer.receipt_confirmed" =>
                (
                    "ผู้ซื้อยืนยันว่าได้รับสินค้า",
                    "ผู้ซื้อยอมรับสินค้าหลังตรวจสอบและไม่มีข้อโต้แย้งเปิดอยู่",
                    "success"),
            "dispute.opened" =>
                (
                    "ผู้ซื้อแจ้งปัญหา",
                    "ระบบหยุดการจ่ายเงินให้ผู้ขายทันทีระหว่างตรวจสอบ",
                    "danger"),
            "dispute.evidence_requested" =>
                (
                    "เจ้าหน้าที่ขอหลักฐานเพิ่ม",
                    "ระบบแจ้งฝ่ายที่เกี่ยวข้องพร้อมกำหนดเวลาส่งหลักฐาน",
                    "warning"),
            "dispute.evidence_submitted" =>
                (
                    "ได้รับหลักฐานเพิ่มเติม",
                    "ฝ่ายที่เกี่ยวข้องส่งหลักฐานเข้ามาในเคส",
                    "info"),
            "dispute.review_started" =>
                (
                    "เริ่มพิจารณาผลข้อโต้แย้ง",
                    "คำแนะนำผ่านการตรวจและเข้าสู่ขั้นตอนตัดสิน",
                    "warning"),
            "dispute.resolved_for_buyer" =>
                (
                    "ตัดสินให้คืนเงินผู้ซื้อ",
                    "รายการเข้าสู่ขั้นตอนรอผู้ให้บริการยืนยันการคืนเงินจริง",
                    "danger"),
            "dispute.resolved_for_seller" =>
                (
                    "ตัดสินให้จ่ายเงินผู้ขาย",
                    "รายการผ่านการอนุมัติและพร้อมเข้าสู่ขั้นตอนจ่ายเงิน",
                    "success"),
            "payout.eligible_buyer_confirmation" =>
                (
                    "รายการพร้อมจ่ายผู้ขาย",
                    "ผู้ซื้อยืนยันรับสินค้าหลังตรวจสอบแล้ว",
                    "success"),
            "payout.eligible_deadline" =>
                (
                    "ครบกำหนดตรวจสินค้า",
                    "ไม่มีข้อโต้แย้งเปิดอยู่และรายการพร้อมจ่ายผู้ขาย",
                    "success"),
            "payout.eligible_digital_manual_review" =>
                (
                    "อนุมัติการส่งมอบดิจิทัล",
                    "เจ้าหน้าที่ตรวจสอบและอนุญาตให้เริ่มจ่ายผู้ขาย",
                    "success"),
            "payout.instruction_created" =>
                (
                    "ส่งคำสั่งจ่ายเงินแล้ว",
                    "กำลังรอผู้ให้บริการยืนยันว่าจ่ายเงินสำเร็จ",
                    "info"),
            "payout.confirmed" =>
                (
                    "จ่ายเงินผู้ขายสำเร็จ",
                    "ผู้ให้บริการยืนยันการจ่ายเงินเรียบร้อยแล้ว",
                    "success"),
            "refund.instruction_created" =>
                (
                    "ส่งคำสั่งคืนเงินแล้ว",
                    "กำลังรอผู้ให้บริการยืนยันว่าคืนเงินจริง",
                    "warning"),
            "refund.action_required" =>
                (
                    "ผู้ซื้อต้องยืนยันข้อมูลรับเงินคืน",
                    "Stripe ส่งขั้นตอนไปทางอีเมลแล้ว TOKLONG ไม่ขอเลขบัญชีจากผู้ซื้อ",
                    "warning"),
            "refund.processing" =>
                (
                    "ผู้ให้บริการกำลังคืนเงิน",
                    "ผู้ซื้อดำเนินการแล้วและกำลังรอผลยืนยันจาก Stripe",
                    "info"),
            "refund.confirmed" or "refund.succeeded" =>
                (
                    "คืนเงินผู้ซื้อสำเร็จ",
                    "ผู้ให้บริการยืนยันการคืนเงินเรียบร้อยแล้ว",
                    "success"),
            "refund.required_fulfillment_overdue" =>
                (
                    "ต้องคืนเงินเพราะส่งมอบเกินกำหนด",
                    "ผู้ขายไม่ได้ส่งมอบภายในเวลาที่ตกลง",
                    "danger"),
            "fulfillment.deadline_missed" =>
                (
                    "ส่งมอบไม่ทันกำหนด",
                    "ระบบหยุดการดำเนินการปกติเพื่อจัดการเงินของผู้ซื้อ",
                    "danger"),
            "retention.legal_hold_placed" =>
                (
                    "ระงับการลบข้อมูลตามคำสั่ง",
                    "เก็บข้อมูลรายการนี้ต่อจนกว่าจะยกเลิกการระงับ",
                    "warning"),
            "retention.legal_hold_released" =>
                (
                    "ยกเลิกการระงับการลบข้อมูล",
                    "ข้อมูลกลับเข้าสู่นโยบายอายุการเก็บตามปกติ",
                    "neutral"),
            "sale_link.activation_blocked" =>
                (
                    "ไม่อนุญาตให้เปิดใช้ข้อตกลง",
                    "สินค้าไม่ผ่านเงื่อนไขความปลอดภัยหรือรายการที่รองรับ",
                    "danger"),
            _ =>
                (
                    "ระบบบันทึกการเปลี่ยนแปลง",
                    "เปิดข้อมูลสำหรับตรวจสอบด้านล่างหากต้องการรหัสอ้างอิง",
                    "neutral")
        };

    public static string ActorLabel(ActorRole role) =>
        role switch
        {
            ActorRole.Buyer => "ผู้ซื้อ",
            ActorRole.Seller => "ผู้ขาย",
            ActorRole.PaymentProvider =>
                "ผู้ให้บริการชำระเงิน",
            ActorRole.CarrierProvider =>
                "ผู้ให้บริการขนส่ง",
            ActorRole.Reconciliation =>
                "ทีมตรวจสอบ TOKLONG",
            _ => "ระบบ TOKLONG"
        };

    public static string StateLabel(TransactionState state) =>
        state switch
        {
            TransactionState.SellerDraft =>
                "ฉบับร่างของผู้ขาย",
            TransactionState.BuyerOfferDraft =>
                "ฉบับร่างของผู้ซื้อ",
            TransactionState.AwaitingSellerAcceptance =>
                "รอผู้ขายตอบรับ",
            TransactionState.SellerAcceptedAwaitingPayment =>
                "รอผู้ซื้อชำระเงิน",
            TransactionState.LinkActive =>
                "พร้อมให้ชำระเงิน",
            TransactionState.CheckoutStarted =>
                "กำลังชำระเงิน",
            TransactionState.PaymentPending =>
                "รอยืนยันการชำระเงิน",
            TransactionState.PaidAwaitingShipment =>
                "ชำระแล้ว รอจัดส่ง",
            TransactionState.PaidAwaitingDigitalDelivery =>
                "ชำระแล้ว รอส่งมอบดิจิทัล",
            TransactionState.DigitalDeliverySubmitted =>
                "แจ้งส่งมอบดิจิทัลแล้ว",
            TransactionState.TrackingSubmitted =>
                "เพิ่มหมายเลขติดตามแล้ว",
            TransactionState.TrackingUnverified =>
                "ยังยืนยันการขนส่งไม่ได้",
            TransactionState.InTransit =>
                "อยู่ระหว่างขนส่ง",
            TransactionState.DeliveredDisputeWindow =>
                "ส่งถึงแล้ว อยู่ในช่วงตรวจสินค้า",
            TransactionState.BuyerConfirmedReceipt =>
                "ผู้ซื้อยืนยันรับสินค้า",
            TransactionState.Disputed =>
                "มีข้อโต้แย้ง",
            TransactionState.ResolutionPending =>
                "กำลังพิจารณาผล",
            TransactionState.PayoutEligible =>
                "พร้อมจ่ายผู้ขาย",
            TransactionState.PayoutPending =>
                "กำลังจ่ายผู้ขาย",
            TransactionState.PaidOut =>
                "จ่ายผู้ขายสำเร็จ",
            TransactionState.ShipmentOverdue =>
                "ส่งมอบเกินกำหนด",
            TransactionState.RefundPending =>
                "กำลังคืนเงิน",
            TransactionState.Refunded =>
                "คืนเงินสำเร็จ",
            TransactionState.Expired =>
                "หมดเวลา",
            _ => "ยกเลิกแล้ว"
        };
}
