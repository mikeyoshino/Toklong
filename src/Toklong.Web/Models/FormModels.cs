using System.ComponentModel.DataAnnotations;
using Toklong.Domain.Transactions;

namespace Toklong.Web.Models;

public sealed class MustBeTrueAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is true;
}

public sealed class CreateSaleForm
{
    [Required(ErrorMessage = "กรุณาเลือกบัญชีรับเงิน")]
    public Guid? PayoutAccountId { get; set; }

    public FulfillmentType FulfillmentType { get; set; } =
        FulfillmentType.PhysicalShipment;

    [Required(ErrorMessage = "กรุณาระบุรายการที่ตกลงซื้อขาย")]
    [StringLength(180, ErrorMessage = "ชื่อรายการยาวเกิน 180 ตัวอักษร")]
    public string ProductName { get; set; } = "";

    public string Category { get; set; } = "งานอดิเรกและของใช้";

    public ConditionCode Condition { get; set; } = ConditionCode.AsDescribed;

    [Required(ErrorMessage = "กรุณาระบุรายละเอียดข้อตกลง")]
    [StringLength(2000, MinimumLength = 10, ErrorMessage = "รายละเอียดต้องมี 10–2,000 ตัวอักษร")]
    public string Description { get; set; } = "";

    public string KnownDefects { get; set; } = "";

    [Required(ErrorMessage = "กรุณาเพิ่มรูปประกอบข้อตกลงอย่างน้อย 1 รูป")]
    public string PhotoUrl { get; set; } = "";

    [Required(ErrorMessage = "กรุณากรอกราคา")]
    [RegularExpression(@"^\d{1,9}(\.\d{1,2})?$", ErrorMessage = "ราคาใช้ตัวเลขและทศนิยมไม่เกิน 2 ตำแหน่ง")]
    public string PriceBaht { get; set; } = "";

    [Required(ErrorMessage = "กรุณากรอกค่าส่ง (ใส่ 0 หากส่งฟรี)")]
    [RegularExpression(@"^\d{1,7}(\.\d{1,2})?$", ErrorMessage = "ค่าส่งใช้ตัวเลขและทศนิยมไม่เกิน 2 ตำแหน่ง")]
    public string ShippingFeeBaht { get; set; } = "0";

    public int ShipByDurationHours { get; set; } = 48;

    [MustBeTrue(ErrorMessage = "กรุณายืนยันว่าคุณครอบครองหรือควบคุมรายการ มีสิทธิ์โอน และไม่ใช่รายการต้องห้าม")]
    public bool ProhibitedGoodsAttested { get; set; }

    [MustBeTrue(ErrorMessage = "กรุณายอมรับข้อตกลงผู้ขาย")]
    public bool AcceptedTerms { get; set; }
}

public sealed class DigitalDeliveryForm
{
    [Required(ErrorMessage = "กรุณาระบุว่าส่งมอบผ่านช่องทางใด")]
    [StringLength(500, MinimumLength = 5, ErrorMessage = "รายละเอียดการส่งมอบต้องมี 5–500 ตัวอักษร")]
    public string Statement { get; set; } = "";
}

public sealed class PayoutAccountForm
{
    public Guid? AccountId { get; set; }

    [Required(ErrorMessage = "กรุณาเลือกธนาคาร")]
    public string BankCode { get; set; } = "KBANK";

    [Required(ErrorMessage = "กรุณากรอกชื่อบัญชี")]
    [StringLength(160, ErrorMessage = "ชื่อบัญชียาวเกิน 160 ตัวอักษร")]
    public string AccountName { get; set; } = "";

    [RegularExpression(@"^[0-9]{10,15}$", ErrorMessage = "เลขบัญชีต้องเป็นตัวเลข 10–15 หลัก")]
    public string AccountNumber { get; set; } = "";
}

public sealed class CheckoutForm
{
    [Required(ErrorMessage = "กรุณากรอกชื่อผู้รับ")]
    public string BuyerDisplayName { get; set; } = "";

    [Required(ErrorMessage = "กรุณากรอกเบอร์โทรหรืออีเมล")]
    public string BuyerContact { get; set; } = "";

    [StringLength(1000, MinimumLength = 10, ErrorMessage = "กรุณากรอกที่อยู่ให้ครบถ้วน")]
    public string DeliveryAddress { get; set; } = "";

    [MustBeTrue(ErrorMessage = "กรุณายอมรับข้อตกลงของรายการ")]
    public bool AcceptedTerms { get; set; }
}

public sealed class TrackingForm
{
    [Required(ErrorMessage = "กรุณาเลือกขนส่ง")]
    public string CarrierCode { get; set; } = "THAIPOST";

    [Required(ErrorMessage = "กรุณากรอกหมายเลขติดตาม")]
    [RegularExpression(@"^[A-Za-z0-9-]{8,40}$", ErrorMessage = "หมายเลขติดตามต้องมี 8–40 ตัวอักษร")]
    public string TrackingNumber { get; set; } = "";
}

public sealed class DisputeForm
{
    public DisputeReason Reason { get; set; } = DisputeReason.NotAsDescribed;

    [Required(ErrorMessage = "กรุณาอธิบายปัญหา")]
    [StringLength(2000, MinimumLength = 10, ErrorMessage = "รายละเอียดต้องมี 10–2,000 ตัวอักษร")]
    public string Statement { get; set; } = "";
}

public static class MoneyParser
{
    public static long ToSatang(string baht)
    {
        if (!decimal.TryParse(baht, System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out var amount))
            throw new ValidationException("ยอดเงินไม่ถูกต้อง");
        return checked(decimal.ToInt64(amount * 100m));
    }
}
