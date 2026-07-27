using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class ThaiMobilePhoneInputTests
{
    [Theory]
    [InlineData("08a-123 45678", "0812345678")]
    [InlineData("081234567890", "0812345678")]
    [InlineData("๐๘๑๒๓๔๕๖๗๘", "")]
    public void Input_keeps_only_ten_ascii_digits(
        string input,
        string expected)
    {
        Assert.Equal(expected, ThaiMobilePhoneInput.Sanitize(input));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("092", "092")]
    [InlineData("0921", "092-1")]
    [InlineData("092103", "092-103")]
    [InlineData("0921031", "092-103-1")]
    [InlineData("0921031202", "092-103-1202")]
    [InlineData("092-103-120299", "092-103-1202")]
    [InlineData("09a2 103b1202", "092-103-1202")]
    public void Input_formats_as_three_three_four_and_ignores_extra_digits(
        string input,
        string expected)
    {
        Assert.Equal(expected, ThaiMobilePhoneInput.Format(input));
    }

    [Theory]
    [InlineData("0612345678")]
    [InlineData("061-234-5678")]
    [InlineData("0812345678")]
    [InlineData("081-234-5678")]
    [InlineData("0912345678")]
    public void Thai_mobile_prefixes_are_valid(string input)
    {
        Assert.True(ThaiMobilePhoneInput.IsValid(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0212345678")]
    [InlineData("0712345678")]
    [InlineData("081234567")]
    [InlineData("08123456789")]
    public void Invalid_numbers_are_rejected(string input)
    {
        Assert.False(ThaiMobilePhoneInput.IsValid(input));
    }
}
