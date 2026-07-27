using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class CarrierOptionTests
{
    private static readonly CarrierOption ThailandPost =
        new(
            "THAIPOST",
            "ไปรษณีย์ไทย",
            "รหัส 13 ตัว",
            "EF123456789TH",
            "^[A-Z]{2}[0-9]{9}TH$",
            "เลขไม่ถูกต้อง",
            13);

    [Fact]
    public void Tracking_is_normalized_before_local_validation()
    {
        Assert.Equal(
            "EF123456789TH",
            ThailandPost.NormalizeTracking(
                "ef-123 456 789-th"));
        Assert.True(
            ThailandPost.IsValidTrackingNumber(
                "ef-123 456 789-th"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("EF123TH")]
    [InlineData("1234567890123")]
    public void Invalid_tracking_is_rejected_locally(string input)
    {
        Assert.False(
            ThailandPost.IsValidTrackingNumber(input));
    }
}
