using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class SellerGraphiteNavyColorTests
{
    [Fact]
    public void Palette_exposes_the_approved_graphite_tokens()
    {
        Assert.Equal("#3B5266", SellerColorPalette.Role);
        Assert.Equal("#4B6073", SellerColorPalette.HeaderStart);
        Assert.Equal("#3D5163", SellerColorPalette.HeaderMiddle);
        Assert.Equal("#304354", SellerColorPalette.HeaderEnd);
        Assert.Equal("#EDF2F5", SellerColorPalette.Surface);
        Assert.Equal("#C8D4DC", SellerColorPalette.Border);
        Assert.Equal("#DCE7EC", SellerColorPalette.Secondary);
        Assert.Equal("#F3F7F9", SellerColorPalette.BadgeSurface);
        Assert.Equal("#8DE8D2", SellerColorPalette.Accent);
        Assert.Equal("#DDB866", SellerColorPalette.NewOfferBorder);
        Assert.Equal("#8A5100", SellerColorPalette.NewOfferText);
        Assert.Equal("#9BAEBC", SellerColorPalette.FulfillmentBorder);
        Assert.Equal("#9CC4EC", SellerColorPalette.InProgressBorder);
    }

    [Fact]
    public void Seller_uses_graphite_while_buyer_palette_is_unchanged()
    {
        var seller = Create(AppTransactionRole.Seller, "PaidOut");
        var buyer = Create(AppTransactionRole.Buyer, "PaidOut");

        Assert.Equal(SellerColorPalette.Role, seller.RoleColor);
        Assert.Equal(SellerColorPalette.Surface, seller.RoleBackground);
        Assert.Equal(
            SellerColorPalette.HeaderStart,
            seller.RoleHeaderStart);
        Assert.Equal(
            SellerColorPalette.HeaderMiddle,
            seller.RoleHeaderMiddle);
        Assert.Equal(
            SellerColorPalette.HeaderEnd,
            seller.RoleHeaderEnd);
        Assert.Equal(SellerColorPalette.Surface, seller.RolePageTint);
        Assert.Equal(
            SellerColorPalette.BadgeSurface,
            seller.RolePageMiddle);
        Assert.Equal(
            SellerColorPalette.Secondary,
            seller.RoleHeaderSecondary);
        Assert.Equal(SellerColorPalette.Accent, seller.RoleDot);
        Assert.Equal(
            SellerColorPalette.Surface,
            seller.ProgressOne.BackgroundColor);
        Assert.Equal(
            SellerColorPalette.Role,
            seller.ProgressOne.StrokeColor);
        Assert.Equal(
            SellerColorPalette.Role,
            seller.ProgressConnectorOneColor);

        Assert.Equal("#145FC7", buyer.RoleColor);
        Assert.Equal("#EAF4FF", buyer.RoleBackground);
        Assert.Equal("#3C8AF1", buyer.RoleHeaderStart);
        Assert.Equal("#236DCE", buyer.RoleHeaderMiddle);
        Assert.Equal("#185CB9", buyer.RoleHeaderEnd);
    }

    private static AppTransaction Create(
        AppTransactionRole role,
        string state) =>
        new(
            Guid.Parse("00000000-0000-0000-0000-0000000000A1"),
            "กล้องทดสอบ",
            3_000_000,
            "THB",
            role,
            AppFulfillmentType.Physical,
            state,
            DateTimeOffset.Parse("2026-07-28T12:00:00+07:00"),
            DateTimeOffset.Parse("2026-07-29T18:00:00+07:00"),
            "คู่สัญญาทดสอบ");
}
