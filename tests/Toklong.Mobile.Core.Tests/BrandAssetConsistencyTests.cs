using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Toklong.Mobile.Core.Tests;

public sealed class BrandAssetConsistencyTests
{
    public static TheoryData<string> ProgressAssets => new()
    {
        "progress_agreement_completed.svg",
        "progress_agreement_disabled.svg",
        "progress_payment_completed.svg",
        "progress_payment_disabled.svg",
        "progress_parcel_handoff_completed.svg",
        "progress_parcel_handoff_disabled.svg",
        "progress_parcel_received_completed.svg",
        "progress_parcel_received_disabled.svg",
        "progress_digital_handoff_completed.svg",
        "progress_digital_handoff_disabled.svg",
        "progress_payout_completed.svg",
        "progress_payout_disabled.svg"
    };

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
        var svg = document.Root!;
        var content = Read(fileName);

        Assert.Equal("0 0 48 48", (string?)svg.Attribute("viewBox"));
        Assert.Contains("stroke-linecap=\"round\"", content);
        Assert.DoesNotContain(
            "<text",
            content,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "#2B7FFF",
            content,
            StringComparison.OrdinalIgnoreCase);

        if (fileName.EndsWith("_completed.svg", StringComparison.Ordinal))
        {
            Assert.Contains(
                "#FFFFFF",
                content,
                StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Contains(
                "#98A2B3",
                content,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "#65D6BF",
                content,
                StringComparison.OrdinalIgnoreCase);
        }
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
