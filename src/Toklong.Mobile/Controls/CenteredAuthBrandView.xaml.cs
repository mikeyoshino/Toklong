namespace Toklong.Mobile.Controls;

public partial class CenteredAuthBrandView : ContentView
{
    public static readonly BindableProperty MarkSizeProperty =
        BindableProperty.Create(
            nameof(MarkSize),
            typeof(double),
            typeof(CenteredAuthBrandView),
            82d);

    public static readonly BindableProperty WordmarkSizeProperty =
        BindableProperty.Create(
            nameof(WordmarkSize),
            typeof(double),
            typeof(CenteredAuthBrandView),
            25d);

    public CenteredAuthBrandView()
    {
        InitializeComponent();
    }

    public double MarkSize
    {
        get => (double)GetValue(MarkSizeProperty);
        set => SetValue(MarkSizeProperty, value);
    }

    public double WordmarkSize
    {
        get => (double)GetValue(WordmarkSizeProperty);
        set => SetValue(WordmarkSizeProperty, value);
    }
}
