using System.Net;
using Toklong.Application.Abstractions;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Services;

namespace Toklong.Application.Tests.Listings;

public sealed class ListingImportTests
{
    [Theory]
    [InlineData("http://localhost:5180/offers/create")]
    [InlineData("http://127.0.0.1/admin")]
    [InlineData("http://10.0.0.5/private")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://192.168.1.10/")]
    [InlineData("http://[::1]/")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://service.internal/listing")]
    public void Private_or_unsupported_urls_are_rejected(string source)
    {
        Assert.False(PublicListingUrl.TryParse(source, out _));
    }

    [Fact]
    public void Public_https_listing_url_is_accepted()
    {
        Assert.True(PublicListingUrl.TryParse(
            "https://www.facebook.com/marketplace/item/123456789", out var result));
        Assert.Equal("www.facebook.com", result!.Host);
        Assert.True(PublicListingUrl.IsPublicAddress(IPAddress.Parse("8.8.8.8")));
    }

    [Fact]
    public void Open_graph_metadata_is_converted_to_a_sale_draft()
    {
        const string html = """
            <html><head>
              <meta content="กล้องฟิล์ม Olympus mju มือสอง" property="og:title">
              <meta property="og:description" content="ทำงานปกติ มีรอยเล็กน้อยตามการใช้งาน">
              <meta property="og:image" content="https://images.example.com/camera.jpg">
              <meta property="product:price:amount" content="4,500.00">
            </head></html>
            """;

        var result = ListingImportService.Extract(
            new Uri("https://www.facebook.com/marketplace/item/123"), html);

        Assert.Equal("Facebook Marketplace", result.SourceSite);
        Assert.Equal("กล้องฟิล์ม Olympus mju มือสอง", result.ProductName);
        Assert.Equal(4500m, result.PriceBaht);
        Assert.Equal("กล้องและอุปกรณ์", result.Category);
        Assert.Equal(ConditionCode.UsedGood, result.Condition);
        Assert.Equal("https://images.example.com/camera.jpg", result.PhotoUrl);
    }

    [Fact]
    public void Json_ld_product_is_preferred_and_defect_condition_is_inferred()
    {
        const string html = """
            <script type="application/ld+json">
            {
              "@context": "https://schema.org",
              "@type": "Product",
              "name": "หูฟัง Bluetooth",
              "description": "เสียงปกติ แต่มีตำหนิและรอยที่กล่อง",
              "image": ["https://cdn.example.com/headphone.png"],
              "offers": { "price": "1290", "priceCurrency": "THB" }
            }
            </script>
            """;

        var result = ListingImportService.Extract(
            new Uri("https://shop.example.com/listing/42"), html);

        Assert.Equal("หูฟัง Bluetooth", result.ProductName);
        Assert.Equal(1290m, result.PriceBaht);
        Assert.Equal("อิเล็กทรอนิกส์", result.Category);
        Assert.Equal(ConditionCode.UsedDefects, result.Condition);
    }
}
