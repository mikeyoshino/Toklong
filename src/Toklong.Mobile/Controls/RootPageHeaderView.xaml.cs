using Toklong.Mobile.Pages;

namespace Toklong.Mobile.Controls;

public partial class RootPageHeaderView : ContentView
{
    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(
            nameof(Title),
            typeof(string),
            typeof(RootPageHeaderView),
            "");
    public static readonly BindableProperty SubtitleProperty =
        BindableProperty.Create(
            nameof(Subtitle),
            typeof(string),
            typeof(RootPageHeaderView),
            "");
    public static readonly BindableProperty AccentColorProperty =
        BindableProperty.Create(
            nameof(AccentColor),
            typeof(Color),
            typeof(RootPageHeaderView),
            Colors.Blue);

    public RootPageHeaderView() => InitializeComponent();

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public Color AccentColor
    {
        get => (Color)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    private async void OnActivityClicked(object? sender, EventArgs args) =>
        await Shell.Current.GoToAsync(nameof(ActivityPage));

    private async void OnAccountClicked(object? sender, EventArgs args) =>
        await Shell.Current.GoToAsync(nameof(AccountPage));
}
