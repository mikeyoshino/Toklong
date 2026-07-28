using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using CorePalette = Toklong.Mobile.Core.SellerColorPalette;

namespace Toklong.Mobile.Theming;

public static class SellerColorPaletteColors
{
    public static Color Role { get; } = Color.FromArgb(CorePalette.Role);
    public static SolidColorBrush RoleBrush { get; } = new(Role);

    public static Color HeaderStart { get; } =
        Color.FromArgb(CorePalette.HeaderStart);
    public static Color HeaderMiddle { get; } =
        Color.FromArgb(CorePalette.HeaderMiddle);
    public static Color HeaderEnd { get; } =
        Color.FromArgb(CorePalette.HeaderEnd);

    public static Color Surface { get; } = Color.FromArgb(CorePalette.Surface);
    public static Color Border { get; } = Color.FromArgb(CorePalette.Border);
    public static SolidColorBrush BorderBrush { get; } = new(Border);
    public static Color Secondary { get; } =
        Color.FromArgb(CorePalette.Secondary);
    public static Color BadgeSurface { get; } =
        Color.FromArgb(CorePalette.BadgeSurface);
    public static Color Accent { get; } = Color.FromArgb(CorePalette.Accent);

    public static Color NewOfferText { get; } =
        Color.FromArgb(CorePalette.NewOfferText);
    public static SolidColorBrush NewOfferTextBrush { get; } =
        new(NewOfferText);
    public static SolidColorBrush NewOfferBorderBrush { get; } =
        new(Color.FromArgb(CorePalette.NewOfferBorder));
    public static SolidColorBrush FulfillmentBorderBrush { get; } =
        new(Color.FromArgb(CorePalette.FulfillmentBorder));
    public static SolidColorBrush InProgressBorderBrush { get; } =
        new(Color.FromArgb(CorePalette.InProgressBorder));
}
