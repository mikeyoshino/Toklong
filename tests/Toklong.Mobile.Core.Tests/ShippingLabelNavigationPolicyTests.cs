using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class ShippingLabelNavigationPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("about:blank")]
    [InlineData("data:text/html,<html></html>")]
    [InlineData("file:///private/var/mobile/label.html")]
    public void InternalPreviewNavigationIsAllowed(
        string? url)
    {
        Assert.False(
            ShippingLabelNavigationPolicy.ShouldCancel(
                url));
    }

    [Theory]
    [InlineData("https://example.com/label")]
    [InlineData("http://example.com/label")]
    [InlineData("javascript:alert(1)")]
    [InlineData("toklong://transaction/123")]
    public void ExternalOrExecutableNavigationIsCancelled(
        string url)
    {
        Assert.True(
            ShippingLabelNavigationPolicy.ShouldCancel(
                url));
    }
}
