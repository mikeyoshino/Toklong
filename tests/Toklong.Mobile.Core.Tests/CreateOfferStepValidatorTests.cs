using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class CreateOfferStepValidatorTests
{
    [Fact]
    public void Deal_reports_every_invalid_field_and_phone_first()
    {
        var result = CreateOfferStepValidator.ValidateDeal(
            "",
            " ",
            hasSelectedPhoto: false,
            selectedPhotoExists: false,
            "");

        Assert.False(result.IsValid);
        Assert.Equal(
            CreateOfferValidationTarget.SellerPhone,
            result.FirstInvalidTarget);
        Assert.Collection(
            result.Errors,
            error => Assert.Equal(
                CreateOfferValidationTarget.SellerPhone,
                error.Target),
            error => Assert.Equal(
                CreateOfferValidationTarget.ProductName,
                error.Target),
            error => Assert.Equal(
                CreateOfferValidationTarget.Amount,
                error.Target));
    }

    [Theory]
    [InlineData("081-234-5678", "1,000", 1000)]
    [InlineData("0812345678", "30000.00", 30000)]
    public void Deal_accepts_valid_phone_product_and_price(
        string phone,
        string amount,
        decimal expectedAmount)
    {
        var result = CreateOfferStepValidator.ValidateDeal(
            phone,
            "กล้องมือสอง",
            hasSelectedPhoto: false,
            selectedPhotoExists: false,
            amount);

        Assert.True(result.IsValid);
        Assert.Equal("0812345678", result.CleanSellerPhone);
        Assert.Equal(expectedAmount, result.AmountBaht);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("999.99")]
    [InlineData("30000.01")]
    [InlineData("1000.001")]
    public void Deal_rejects_price_outside_supported_boundary(
        string amount)
    {
        var result = CreateOfferStepValidator.ValidateDeal(
            "0812345678",
            "กล้องมือสอง",
            hasSelectedPhoto: false,
            selectedPhotoExists: false,
            amount);

        Assert.False(result.IsValid);
        Assert.Equal(
            CreateOfferValidationTarget.Amount,
            result.FirstInvalidTarget);
    }

    [Fact]
    public void Deal_reports_missing_selected_photo()
    {
        var result = CreateOfferStepValidator.ValidateDeal(
            "0812345678",
            "กล้องมือสอง",
            hasSelectedPhoto: true,
            selectedPhotoExists: false,
            "1500");

        Assert.False(result.IsValid);
        Assert.Equal(
            CreateOfferValidationTarget.ProductPhoto,
            result.FirstInvalidTarget);
    }

    [Theory]
    [InlineData(false, false, false, false, false, true)]
    [InlineData(true, true, false, false, false, true)]
    [InlineData(true, false, true, true, true, true)]
    [InlineData(true, false, true, false, true, false)]
    public void Fulfillment_requires_complete_address_only_for_physical(
        bool isPhysical,
        bool usesSavedAddress,
        bool hasAddressLine,
        bool hasProvince,
        bool hasDistrict,
        bool expectedValid)
    {
        var result = CreateOfferStepValidator.ValidateFulfillment(
            isPhysical,
            usesSavedAddress,
            hasAddressLine,
            hasProvince,
            hasDistrict,
            hasSubdistrict: hasDistrict);

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData(-1, false, false)]
    [InlineData(0, false, true)]
    [InlineData(1, false, true)]
    [InlineData(2, false, false)]
    [InlineData(2, true, true)]
    public void Review_requires_condition_and_defect_description_when_applicable(
        int conditionIndex,
        bool hasKnownDefects,
        bool expectedValid)
    {
        var result = CreateOfferStepValidator.ValidateReview(
            conditionIndex,
            hasKnownDefects ? "รอยขีดด้านข้าง" : "");

        Assert.Equal(expectedValid, result.IsValid);
    }
}
