using System.ComponentModel;

namespace Toklong.Mobile.Core;

public sealed class SpotlightGradientPresentation :
    INotifyPropertyChanged
{
    private string start = CleanLedgerPalette.TrustNavy;
    private string middle = "#14608A";
    private string end = CleanLedgerPalette.BuyerBlue;

    public SpotlightGradientPresentation(AppTransaction? spotlight) =>
        SetSpotlight(spotlight);

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Start => start;
    public string Middle => middle;
    public string End => end;

    public void SetSpotlight(AppTransaction? spotlight)
    {
        if (spotlight is null)
            return;

        SetColor(ref start, spotlight.RoleHeaderStart, nameof(Start));
        SetColor(ref middle, spotlight.RoleHeaderMiddle, nameof(Middle));
        SetColor(ref end, spotlight.RoleHeaderEnd, nameof(End));
    }

    private void SetColor(
        ref string field,
        string value,
        string propertyName)
    {
        if (field == value)
            return;

        field = value;
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}
