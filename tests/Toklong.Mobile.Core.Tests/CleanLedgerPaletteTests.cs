using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class CleanLedgerPaletteTests
{
    [Fact]
    public void Clean_ledger_palette_matches_the_approved_tokens()
    {
        Assert.Equal("#F6F8FA", CleanLedgerPalette.MistBackground);
        Assert.Equal("#12364F", CleanLedgerPalette.TrustNavy);
        Assert.Equal("#1988D3", CleanLedgerPalette.BuyerBlue);
        Assert.Equal("#E9F6FF", CleanLedgerPalette.BuyerBlueSoft);
        Assert.Equal("#55508A", CleanLedgerPalette.SellerIndigo);
        Assert.Equal("#EFEDFB", CleanLedgerPalette.SellerIndigoSoft);
        Assert.Equal("#65C8B4", CleanLedgerPalette.VerifiedMint);
        Assert.Equal("#BD563A", CleanLedgerPalette.DeadlineRust);
        Assert.Equal("#112337", CleanLedgerPalette.Ink);
        Assert.Equal("#647589", CleanLedgerPalette.MutedInk);
        Assert.Equal("#DCE5EC", CleanLedgerPalette.Line);
        Assert.Equal("#FFFFFF", CleanLedgerPalette.Surface);
    }
}
