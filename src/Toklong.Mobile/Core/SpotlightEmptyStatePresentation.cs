using System.ComponentModel;

namespace Toklong.Mobile.Core;

public sealed class SpotlightEmptyStatePresentation(
    RoleFilter role,
    bool hasSpotlight) : INotifyPropertyChanged
{
    private RoleFilter role = role;
    private bool hasSpotlight = hasSpotlight;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool ShowBuyerSpotlightEmptyState =>
        role == RoleFilter.Buying && !hasSpotlight;

    public void SetRole(RoleFilter value)
    {
        var previous = ShowBuyerSpotlightEmptyState;
        role = value;
        NotifyIfChanged(previous);
    }

    public void SetHasSpotlight(bool value)
    {
        var previous = ShowBuyerSpotlightEmptyState;
        hasSpotlight = value;
        NotifyIfChanged(previous);
    }

    private void NotifyIfChanged(bool previous)
    {
        if (previous == ShowBuyerSpotlightEmptyState)
            return;

        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(
                nameof(ShowBuyerSpotlightEmptyState)));
    }
}
