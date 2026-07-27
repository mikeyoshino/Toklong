using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Toklong.Mobile.Core.Tests;

public sealed class BrandAssetConsistencyTests
{
    [Theory]
    [InlineData("brand_mark.svg")]
    [InlineData("appiconfg.svg")]
    [InlineData("splash.svg")]
    [InlineData("ui_ai_assist.svg")]
    public void BrandAsset_ContainsTransactionRailGeometry(string fileName)
    {
        var document = XDocument.Load(BrandPath(fileName));
        var rail = document
            .Descendants()
            .SingleOrDefault(element =>
                (string?)element.Attribute("id") == "transaction-rail");

        Assert.NotNull(rail);
        Assert.Equal(2, rail.Elements().Count(element => element.Name.LocalName == "path"));
    }

    [Fact]
    public void CompletedBrandMarks_ContainOneMintConfirmationNode()
    {
        foreach (var fileName in new[]
                 {
                     "brand_mark.svg",
                     "appiconfg.svg",
                     "ui_ai_assist.svg"
                 })
        {
            var document = XDocument.Load(BrandPath(fileName));
            var rail = document
                .Descendants()
                .Single(element =>
                    (string?)element.Attribute("id") == "transaction-rail");

            var node = Assert.Single(
                rail.Elements(),
                element => element.Name.LocalName == "circle");

            Assert.Equal(
                "#65D6BF",
                (string?)node.Attribute("fill"),
                ignoreCase: true);
        }
    }

    [Fact]
    public void BrandPalette_RemainsApproved()
    {
        Assert.Contains("#2B7FFF", Read("appicon.svg"));
        Assert.Contains("#65D6BF", Read("appiconfg.svg"));
        Assert.Contains("#F6FAFF", Read("splash.svg"));
    }

    [Fact]
    public void LandingHeaderAndFooter_UseTheSameMark()
    {
        var landing = Read("landing.html");

        Assert.Equal(
            2,
            Regex.Matches(
                landing,
                "data-logo-mark=\"transaction-rail\"").Count);
    }

    private static string Read(string fileName) =>
        File.ReadAllText(BrandPath(fileName));

    private static string BrandPath(string fileName) =>
        Path.Combine(
            AppContext.BaseDirectory,
            "Brand",
            fileName);
}
