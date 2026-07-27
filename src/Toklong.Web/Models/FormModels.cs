using System.ComponentModel.DataAnnotations;
using Toklong.Domain.Transactions;

namespace Toklong.Web.Models;

public sealed class MustBeTrueAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is true;
}

public sealed class CreateBuyerOfferForm : IValidatableObject
{
    [Required(ErrorMessage = "กรุณากรอกเบอร์มือถือผู้ขาย")]
    [RegularExpression(
        @"^(0[689]\d{8}|\+66[689]\d{8})$",
        ErrorMessage =
            "กรุณากรอกเบอร์มือถือไทย 10 หลัก เช่น 0812345678")]
    public string SellerPhoneNumber { get; set; } = "";

    [Required(ErrorMessage = "กรุณาระบุชื่อสินค้า")]
    [StringLength(
        180,
        ErrorMessage = "ชื่อสินค้าต้องไม่เกิน 180 ตัวอักษร")]
    public string ProductName { get; set; } = "";

    public FulfillmentType FulfillmentType { get; set; } =
        FulfillmentType.PhysicalShipment;

    [Required(ErrorMessage = "กรุณาระบุรายละเอียด สภาพ อุปกรณ์ และตำหนิ")]
    [StringLength(2000, MinimumLength = 10, ErrorMessage = "รายละเอียดต้องมี 10–2,000 ตัวอักษร")]
    public string ProposedDescription { get; set; } = "";

    public ConditionCode Condition { get; set; } = ConditionCode.New;

    [StringLength(1000, ErrorMessage = "รายละเอียดตำหนิต้องไม่เกิน 1,000 ตัวอักษร")]
    public string KnownDefects { get; set; } = "";

    public string? PhotoUrl { get; set; }

    [Required(ErrorMessage = "กรุณากรอกราคา")]
    [RegularExpression(@"^\d{1,9}(\.\d{1,2})?$", ErrorMessage = "ราคาใช้ตัวเลขและทศนิยมไม่เกิน 2 ตำแหน่ง")]
    public string PriceBaht { get; set; } = "";

    public bool UseSavedAddress { get; set; }

    [StringLength(
        500,
        ErrorMessage =
            "บ้านเลขที่และรายละเอียดที่อยู่ยาวเกิน 500 ตัวอักษร")]
    public string AddressLine { get; set; } = "";

    public int? ProvinceId { get; set; }
    public int? DistrictId { get; set; }
    public int? SubdistrictId { get; set; }
    public bool RememberAddress { get; set; }

    [MustBeTrue(ErrorMessage = "กรุณายืนยันว่าจะตรวจข้อมูลฉบับสุดท้ายก่อนชำระ")]
    public bool AcceptedReviewRule { get; set; }

    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext)
    {
        if (FulfillmentType !=
            FulfillmentType.PhysicalShipment)
            yield break;
        if (UseSavedAddress)
            yield break;
        if (string.IsNullOrWhiteSpace(
                AddressLine))
            yield return new ValidationResult(
                "กรุณากรอกบ้านเลขที่และรายละเอียดที่อยู่",
                [nameof(AddressLine)]);
        if (!ProvinceId.HasValue)
            yield return new ValidationResult(
                "กรุณาเลือกจังหวัดปลายทาง",
                [nameof(ProvinceId)]);
        if (!DistrictId.HasValue)
            yield return new ValidationResult(
                "กรุณาเลือกอำเภอหรือเขตปลายทาง",
                [nameof(DistrictId)]);
        if (!SubdistrictId.HasValue)
            yield return new ValidationResult(
                "กรุณาเลือกตำบลหรือแขวงปลายทาง",
                [nameof(SubdistrictId)]);
    }
}

public sealed class SellerOfferAcceptanceForm
{
    [Required(ErrorMessage = "กรุณาเลือกบัญชีรับเงิน")]
    public Guid? PayoutAccountId { get; set; }

    [MustBeTrue(ErrorMessage = "กรุณายืนยันว่าคุณครอบครอง มีสิทธิ์โอน และรายการไม่ต้องห้าม")]
    public bool TransferRightsAttested { get; set; }

    [MustBeTrue(ErrorMessage = "กรุณายอมรับข้อตกลงผู้ขาย")]
    public bool AcceptedTerms { get; set; }

    public bool UseSavedOrigin { get; set; }

    [StringLength(
        500,
        ErrorMessage =
            "บ้านเลขที่และรายละเอียดต้นทางยาวเกิน 500 ตัวอักษร")]
    public string OriginAddressLine { get; set; } = "";

    public int? OriginProvinceId { get; set; }
    public int? OriginDistrictId { get; set; }
    public int? OriginSubdistrictId { get; set; }
    public bool RememberOrigin { get; set; }
    public string WeightGrams { get; set; } = "";
    public string WidthCentimeters { get; set; } = "";
    public string LengthCentimeters { get; set; } = "";
    public string HeightCentimeters { get; set; } = "";
    public string QuoteReference { get; set; } = "";
}

public sealed class BuyerOfferCheckoutForm
{
    [MustBeTrue(ErrorMessage = "กรุณายอมรับข้อตกลงฉบับสุดท้ายก่อนชำระ")]
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
