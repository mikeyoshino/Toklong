using Toklong.Mobile.Core;

namespace Toklong.Mobile.Controls;

public partial class ShippingProgressView : ContentView
{
    private const string Active = "#087C68";
    private const string Inactive = "#98A2B3";
    private const string ActiveSurface = "#E8F7F3";
    private const string InactiveSurface = "#FFFFFF";

    public static readonly BindableProperty TransactionProperty =
        BindableProperty.Create(
            nameof(Transaction),
            typeof(AppTransaction),
            typeof(ShippingProgressView),
            propertyChanged: OnTransactionChanged);

    public ShippingProgressView() => InitializeComponent();

    public AppTransaction? Transaction
    {
        get => (AppTransaction?)GetValue(TransactionProperty);
        set => SetValue(TransactionProperty, value);
    }

    public string StepOneColor => ColorFor(1);
    public string StepTwoColor => ColorFor(2);
    public string StepThreeColor => ColorFor(3);
    public string StepFourColor => ColorFor(4);
    public string StepOneBackground => BackgroundFor(1);
    public string StepTwoBackground => BackgroundFor(2);
    public string StepThreeBackground => BackgroundFor(3);
    public string StepFourBackground => BackgroundFor(4);
    public string ConnectorOneColor => ConnectorFor(2);
    public string ConnectorTwoColor => ConnectorFor(3);
    public string ConnectorThreeColor => ConnectorFor(4);

    private int CompletedThrough =>
        Transaction?.ShippingProgressCompletedThrough ?? 0;

    private string ColorFor(int step) =>
        CompletedThrough >= step ||
        Transaction?.ShippingProgressActiveStep == step
            ? Active
            : Inactive;

    private string BackgroundFor(int step) =>
        CompletedThrough >= step ||
        Transaction?.ShippingProgressActiveStep == step
            ? ActiveSurface
            : InactiveSurface;

    private string ConnectorFor(int nextStep) =>
        CompletedThrough >= nextStep
            ? Active
            : "#E4E7EC";

    private static void OnTransactionChanged(
        BindableObject bindable,
        object oldValue,
        object newValue)
    {
        var view = (ShippingProgressView)bindable;
        foreach (var property in new[]
                 {
                     nameof(StepOneColor),
                     nameof(StepTwoColor),
                     nameof(StepThreeColor),
                     nameof(StepFourColor),
                     nameof(StepOneBackground),
                     nameof(StepTwoBackground),
                     nameof(StepThreeBackground),
                     nameof(StepFourBackground),
                     nameof(ConnectorOneColor),
                     nameof(ConnectorTwoColor),
                     nameof(ConnectorThreeColor)
                 })
            view.OnPropertyChanged(property);
    }
}

public enum ShippingProgressIconKind
{
    Preparing,
    Accepted,
    InTransit,
    Delivered
}

public sealed class ShippingProgressIconView : GraphicsView
{
    public static readonly BindableProperty KindProperty =
        BindableProperty.Create(
            nameof(Kind),
            typeof(ShippingProgressIconKind),
            typeof(ShippingProgressIconView),
            ShippingProgressIconKind.Preparing,
            propertyChanged: Redraw);
    public static readonly BindableProperty IconColorProperty =
        BindableProperty.Create(
            nameof(IconColor),
            typeof(Color),
            typeof(ShippingProgressIconView),
            Colors.Gray,
            propertyChanged: Redraw);

    public ShippingProgressIconView()
    {
        WidthRequest = 26;
        HeightRequest = 26;
        HorizontalOptions = LayoutOptions.Center;
        VerticalOptions = LayoutOptions.Center;
        Drawable = new ShippingIconDrawable(this);
    }

    public ShippingProgressIconKind Kind
    {
        get => (ShippingProgressIconKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public Color IconColor
    {
        get => (Color)GetValue(IconColorProperty);
        set => SetValue(IconColorProperty, value);
    }

    private static void Redraw(
        BindableObject bindable,
        object oldValue,
        object newValue) =>
        ((ShippingProgressIconView)bindable).Invalidate();

    private sealed class ShippingIconDrawable(
        ShippingProgressIconView owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.StrokeColor = owner.IconColor;
            canvas.StrokeSize = 2.1f;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;
            canvas.FillColor = Colors.Transparent;
            var x = dirtyRect.Center.X - 11;
            var y = dirtyRect.Center.Y - 11;
            switch (owner.Kind)
            {
                case ShippingProgressIconKind.Preparing:
                    canvas.DrawRoundedRectangle(
                        x + 3, y + 6, 16, 13, 3);
                    canvas.DrawLine(
                        x + 7, y + 6, x + 9, y + 3);
                    canvas.DrawLine(
                        x + 15, y + 6, x + 13, y + 3);
                    canvas.DrawLine(
                        x + 8, y + 12, x + 14, y + 12);
                    break;
                case ShippingProgressIconKind.Accepted:
                    canvas.DrawRoundedRectangle(
                        x + 2, y + 7, 13, 11, 3);
                    canvas.DrawLine(
                        x + 5, y + 7, x + 7, y + 4);
                    canvas.DrawLine(
                        x + 12, y + 7, x + 10, y + 4);
                    canvas.DrawLine(
                        x + 15, y + 13, x + 18, y + 16);
                    canvas.DrawLine(
                        x + 18, y + 16, x + 22, y + 10);
                    break;
                case ShippingProgressIconKind.InTransit:
                    canvas.DrawRoundedRectangle(
                        x + 2, y + 7, 12, 9, 2);
                    canvas.DrawLine(
                        x + 14, y + 10, x + 18, y + 10);
                    canvas.DrawLine(
                        x + 18, y + 10, x + 21, y + 14);
                    canvas.DrawLine(
                        x + 21, y + 14, x + 21, y + 16);
                    canvas.DrawCircle(x + 7, y + 18, 2);
                    canvas.DrawCircle(x + 18, y + 18, 2);
                    break;
                case ShippingProgressIconKind.Delivered:
                    var path = new PathF();
                    path.MoveTo(x + 3, y + 11);
                    path.LineTo(x + 11, y + 4);
                    path.LineTo(x + 19, y + 11);
                    path.LineTo(x + 19, y + 19);
                    path.LineTo(x + 3, y + 19);
                    path.Close();
                    canvas.DrawPath(path);
                    canvas.DrawLine(
                        x + 7, y + 14, x + 10, y + 17);
                    canvas.DrawLine(
                        x + 10, y + 17, x + 16, y + 11);
                    break;
            }
        }
    }
}
