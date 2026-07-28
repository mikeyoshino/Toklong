using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Toklong.Mobile.Core.Tests;

public sealed class BrandAssetConsistencyTests
{
    private static readonly string[] ProgressSemantics =
    [
        "agreement",
        "payment",
        "physical_handoff",
        "physical_receipt",
        "digital_handoff",
        "payout"
    ];

    private static readonly string[] ProgressVariants =
    [
        "buyer_completed",
        "seller_completed",
        "disabled"
    ];

    public static TheoryData<string> ProgressAssets
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var semantic in ProgressSemantics)
            foreach (var variant in ProgressVariants)
                data.Add($"progress_{semantic}_{variant}.svg");
            return data;
        }
    }

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

    [Theory]
    [MemberData(nameof(ProgressAssets))]
    public void ProgressAssetUsesApprovedRoundedGeometry(string fileName)
    {
        var document = XDocument.Load(BrandPath(fileName));
        var content = Read(fileName);
        var primary = document
            .Descendants()
            .Single(element =>
                (string?)element.Attribute("id") == "rail-primary");
        var secondary = document
            .Descendants()
            .Single(element =>
                (string?)element.Attribute("id") == "rail-secondary");
        var node = document
            .Descendants()
            .Single(element =>
                (string?)element.Attribute("id") == "rail-node");

        Assert.Equal(
            "0 0 48 48",
            (string?)document.Root!.Attribute("viewBox"));
        Assert.Equal("round", Attr(primary, "stroke-linecap"));
        Assert.Equal("round", Attr(primary, "stroke-linejoin"));
        Assert.Equal("round", Attr(secondary, "stroke-linecap"));
        Assert.Equal("round", Attr(secondary, "stroke-linejoin"));
        Assert.InRange(ParseStroke(primary), 2.5m, 3m);
        Assert.InRange(ParseStroke(secondary), 2.5m, 3m);
        Assert.Equal("circle", node.Name.LocalName);
        Assert.InRange(ParseDecimal(node, "r"), 3.5m, 4.5m);
        Assert.DoesNotContain(
            "<text",
            content,
            StringComparison.OrdinalIgnoreCase);

        if (fileName.EndsWith(
                "_buyer_completed.svg",
                StringComparison.Ordinal))
        {
            Assert.Equal(
                "#145FC7",
                Attr(primary, "stroke"),
                ignoreCase: true);
            Assert.Equal(
                "#2B7FFF",
                Attr(secondary, "stroke"),
                ignoreCase: true);
            Assert.Equal(
                "#65D6BF",
                Attr(node, "fill"),
                ignoreCase: true);
            Assert.DoesNotContain(
                "#6548C7",
                content,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "#8067DE",
                content,
                StringComparison.OrdinalIgnoreCase);
        }
        else if (fileName.EndsWith(
                     "_seller_completed.svg",
                     StringComparison.Ordinal))
        {
            Assert.Equal(
                SellerColorPalette.Role,
                Attr(primary, "stroke"),
                ignoreCase: true);
            Assert.Equal(
                SellerColorPalette.HeaderStart,
                Attr(secondary, "stroke"),
                ignoreCase: true);
            Assert.Equal(
                SellerColorPalette.Accent,
                Attr(node, "fill"),
                ignoreCase: true);
            Assert.DoesNotContain(
                "#145FC7",
                content,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "#2B7FFF",
                content,
                StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Equal(
                "#98A2B3",
                Attr(primary, "stroke"),
                ignoreCase: true);
            Assert.Equal(
                "#98A2B3",
                Attr(secondary, "stroke"),
                ignoreCase: true);
            Assert.Equal(
                "#D6DCE5",
                Attr(node, "fill"),
                ignoreCase: true);

            foreach (var forbidden in new[]
                     {
                         "#145FC7",
                         "#2B7FFF",
                         "#6548C7",
                         "#8067DE",
                         "#65D6BF",
                         "#087C68"
                     })
            {
                Assert.DoesNotContain(
                    forbidden,
                    content,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void ProgressAssetManifestContainsOnlyRailMorphFamily()
    {
        var actual = Directory
            .GetFiles(BrandDirectory(), "progress_*.svg")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expected = ProgressSemantics
            .SelectMany(semantic =>
                ProgressVariants.Select(
                    variant =>
                        $"progress_{semantic}_{variant}.svg"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
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
        Path.Combine(BrandDirectory(), fileName);

    private static string BrandDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "Brand");

    private static string Attr(XElement element, string name) =>
        (string?)element.Attribute(name)
        ?? throw new Xunit.Sdk.XunitException(
            $"Missing {name} on {element.Name}");

    private static decimal ParseDecimal(
        XElement element,
        string name) =>
        decimal.Parse(
            Attr(element, name),
            System.Globalization.CultureInfo.InvariantCulture);

    private static decimal ParseStroke(XElement element) =>
        ParseDecimal(element, "stroke-width");
}
