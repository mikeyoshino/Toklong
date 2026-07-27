using Toklong.Domain.Common;
using Toklong.Domain.Transactions;

namespace Toklong.Domain.Tests.Transactions;

public sealed class SupportedCarrierCatalogTests
{
    [Theory]
    [InlineData("THAIPOST", "EF123456789TH", "EF123456789TH")]
    [InlineData("thaipost", "ef-123 456 789-th", "EF123456789TH")]
    [InlineData("FLASH", "TH1234567890", "TH1234567890")]
    [InlineData("KERRY", "kex-123456789", "KEX123456789")]
    public void Supported_carrier_accepts_and_normalizes_valid_format(
        string carrierCode,
        string input,
        string expected)
    {
        var carrier = SupportedCarrierCatalog.RequireValid(
            carrierCode,
            input);

        Assert.Equal(
            expected,
            SupportedCarrierCatalog.NormalizeTracking(input));
        Assert.Equal(
            carrierCode.ToUpperInvariant(),
            carrier.Code);
    }

    [Theory]
    [InlineData("OTHER", "ABC123456789")]
    [InlineData("THAIPOST", "1234567890123")]
    [InlineData("THAIPOST", "EF123TH")]
    [InlineData("FLASH", "SHORT")]
    [InlineData("KERRY", "มีอักษรไทย123456789")]
    public void Unsupported_carrier_or_invalid_format_is_rejected(
        string carrierCode,
        string trackingNumber)
    {
        Assert.Throws<DomainException>(
            () => SupportedCarrierCatalog.RequireValid(
                carrierCode,
                trackingNumber));
    }
}
