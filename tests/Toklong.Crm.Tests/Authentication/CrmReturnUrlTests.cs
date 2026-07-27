using Toklong.Crm.Authentication;

namespace Toklong.Crm.Tests.Authentication;

public sealed class CrmReturnUrlTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("//evil.example")]
    [InlineData("https://evil.example")]
    [InlineData("relative/path")]
    public void External_or_invalid_return_url_falls_back_to_root(
        string? value)
    {
        Assert.Equal("/", CrmReturnUrl.Safe(value));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/disputes")]
    [InlineData("/disputes?status=open")]
    public void Local_return_url_is_preserved(string value)
    {
        Assert.Equal(value, CrmReturnUrl.Safe(value));
    }
}
