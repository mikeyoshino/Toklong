using System.Globalization;

namespace Toklong.Mobile.Core;

public enum CreateOfferValidationTarget
{
    SellerPhone,
    ProductName,
    ProductPhoto,
    Amount,
    DeliveryAddress,
    Condition,
    KnownDefects,
    CostPreview
}

public sealed record CreateOfferValidationError(
    CreateOfferValidationTarget Target,
    string Message);

public sealed record CreateOfferValidationResult(
    IReadOnlyList<CreateOfferValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public CreateOfferValidationTarget? FirstInvalidTarget =>
        Errors.Count == 0
            ? null
            : Errors[0].Target;
}

public sealed record CreateOfferDealValidationResult(
    string CleanSellerPhone,
    decimal AmountBaht,
    IReadOnlyList<CreateOfferValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public CreateOfferValidationTarget? FirstInvalidTarget =>
        Errors.Count == 0
            ? null
            : Errors[0].Target;
}

public static class CreateOfferStepValidator
{
    public static CreateOfferDealValidationResult ValidateDeal(
        string sellerPhone,
        string productName,
        bool hasSelectedPhoto,
        bool selectedPhotoExists,
        string amountText)
    {
        var errors = new List<CreateOfferValidationError>();
        var cleanSellerPhone =
            ThaiMobilePhoneInput.Sanitize(sellerPhone);
        if (!ThaiMobilePhoneInput.IsValid(cleanSellerPhone))
        {
            errors.Add(new(
                CreateOfferValidationTarget.SellerPhone,
                "กรอกเบอร์มือถือผู้ขาย 10 หลัก เช่น 081-234-5678"));
        }

        if (string.IsNullOrWhiteSpace(productName))
        {
            errors.Add(new(
                CreateOfferValidationTarget.ProductName,
                "ใส่ชื่อสินค้า"));
        }

        if (hasSelectedPhoto && !selectedPhotoExists)
        {
            errors.Add(new(
                CreateOfferValidationTarget.ProductPhoto,
                "ไม่พบรูปที่เลือก กรุณาเลือกรูปใหม่"));
        }

        var parsed = TryParseAmount(amountText, out var amount);
        if (!parsed)
        {
            errors.Add(new(
                CreateOfferValidationTarget.Amount,
                "ใส่ราคาที่ตกลงกันให้ถูกต้อง"));
        }
        else if (amount is < 1_000 or > 30_000 ||
                 decimal.Round(amount, 2) != amount)
        {
            errors.Add(new(
                CreateOfferValidationTarget.Amount,
                "ราคาต้องอยู่ระหว่าง 1,000–30,000 บาท และมีทศนิยมไม่เกิน 2 ตำแหน่ง"));
        }

        return new(
            cleanSellerPhone,
            parsed ? amount : 0,
            errors);
    }

    public static CreateOfferValidationResult ValidateFulfillment(
        bool isPhysical,
        bool usesSavedAddress,
        bool hasAddressLine,
        bool hasProvince,
        bool hasDistrict,
        bool hasSubdistrict)
    {
        if (!isPhysical ||
            usesSavedAddress ||
            (hasAddressLine &&
             hasProvince &&
             hasDistrict &&
             hasSubdistrict))
        {
            return new([]);
        }

        return new(
            [
                new(
                    CreateOfferValidationTarget.DeliveryAddress,
                    "กรอกบ้านเลขที่และเลือกพื้นที่จัดส่งให้ครบ")
            ]);
    }

    public static CreateOfferValidationResult ValidateReview(
        int conditionIndex,
        string knownDefects)
    {
        if (conditionIndex is < 0 or > 2)
        {
            return new(
                [
                    new(
                        CreateOfferValidationTarget.Condition,
                        "เลือกสภาพสินค้า")
                ]);
        }

        if (conditionIndex == 2 &&
            string.IsNullOrWhiteSpace(knownDefects))
        {
            return new(
                [
                    new(
                        CreateOfferValidationTarget.KnownDefects,
                        "ระบุตำหนิที่ตกลงกัน")
                ]);
        }

        return new([]);
    }

    private static bool TryParseAmount(
        string amountText,
        out decimal amount) =>
        decimal.TryParse(
            amountText,
            NumberStyles.Number,
            CultureInfo.GetCultureInfo("th-TH"),
            out amount) ||
        decimal.TryParse(
            amountText,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out amount);
}
