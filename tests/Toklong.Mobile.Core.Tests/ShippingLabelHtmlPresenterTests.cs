using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class ShippingLabelHtmlPresenterTests
{
    [Fact]
    public void PreviewAddsMobileViewportAndRemovesExecutableContent()
    {
        const string html = """
            <!doctype html>
            <html lang="th">
            <head><title>label</title></head>
            <body onload="steal()">
              <script>alert('x')</script>
              <a href="javascript:steal()">เปิด</a>
              <svg><rect width="10" height="10"/></svg>
            </body>
            </html>
            """;

        var preview =
            ShippingLabelHtmlPresenter.PreparePreview(html);

        Assert.Contains(
            "maximum-scale=5",
            preview,
            StringComparison.Ordinal);
        Assert.Contains(
            "Content-Security-Policy",
            preview,
            StringComparison.Ordinal);
        Assert.Contains(
            "<svg>",
            preview,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "<script",
            preview,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "onload",
            preview,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "javascript:",
            preview,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<body>not a document</body>")]
    public void PreviewRejectsMissingOrInvalidHtml(
        string html)
    {
        Assert.Throws<InvalidOperationException>(() =>
            ShippingLabelHtmlPresenter.PreparePreview(html));
    }
}
