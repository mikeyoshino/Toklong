using Toklong.Mobile.Core;

namespace Toklong.Mobile.Controls;

public partial class TransactionProgressView : ContentView
{
    public static readonly BindableProperty TransactionProperty =
        BindableProperty.Create(
            nameof(Transaction),
            typeof(AppTransaction),
            typeof(TransactionProgressView));

    public TransactionProgressView()
    {
        InitializeComponent();
    }

    public AppTransaction? Transaction
    {
        get => (AppTransaction?)GetValue(TransactionProperty);
        set => SetValue(TransactionProperty, value);
    }
}

public sealed class TransactionProgressIconView : GraphicsView
{
    public static readonly BindableProperty GlyphProperty =
        BindableProperty.Create(
            nameof(Glyph),
            typeof(TransactionProgressGlyph),
            typeof(TransactionProgressIconView),
            TransactionProgressGlyph.Agreement,
            propertyChanged: Redraw);

    public static readonly BindableProperty IconColorProperty =
        BindableProperty.Create(
            nameof(IconColor),
            typeof(Color),
            typeof(TransactionProgressIconView),
            Colors.Gray,
            propertyChanged: Redraw);

    public TransactionProgressIconView()
    {
        WidthRequest = 26;
        HeightRequest = 26;
        HorizontalOptions = LayoutOptions.Center;
        VerticalOptions = LayoutOptions.Center;
        AutomationProperties.SetIsInAccessibleTree(
            this,
            false);
        Drawable = new TransactionProgressIconDrawable(
            this);
    }

    public TransactionProgressGlyph Glyph
    {
        get => (TransactionProgressGlyph)GetValue(
            GlyphProperty);
        set => SetValue(GlyphProperty, value);
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
        ((TransactionProgressIconView)bindable)
            .Invalidate();

    private sealed class TransactionProgressIconDrawable(
        TransactionProgressIconView owner) : IDrawable
    {
        public void Draw(
            ICanvas canvas,
            RectF dirtyRect)
        {
            canvas.StrokeColor = owner.IconColor;
            canvas.StrokeSize = 2.1f;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;
            canvas.FillColor = Colors.Transparent;

            var x = dirtyRect.Center.X - 11;
            var y = dirtyRect.Center.Y - 11;
            switch (owner.Glyph)
            {
                case TransactionProgressGlyph.Agreement:
                    DrawAgreement(canvas, x, y);
                    break;
                case TransactionProgressGlyph.Payment:
                    DrawPayment(canvas, x, y);
                    break;
                case TransactionProgressGlyph.PhysicalHandoff:
                    DrawPhysicalHandoff(canvas, x, y);
                    break;
                case TransactionProgressGlyph.PhysicalReceipt:
                    DrawPhysicalReceipt(canvas, x, y);
                    break;
                case TransactionProgressGlyph.DigitalHandoff:
                    DrawDigitalHandoff(canvas, x, y);
                    break;
                case TransactionProgressGlyph.Payout:
                    DrawPayout(canvas, x, y);
                    break;
                case TransactionProgressGlyph.SellerAgreementProof:
                    DrawSellerAgreementProof(canvas, x, y);
                    break;
                case TransactionProgressGlyph.SellerPhysicalShipmentProof:
                    DrawSellerPhysicalShipmentProof(canvas, x, y);
                    break;
                case TransactionProgressGlyph.SellerPayoutProof:
                    DrawSellerPayoutProof(canvas, x, y);
                    break;
            }
        }

        private static void DrawSellerAgreementProof(
            ICanvas canvas,
            float x,
            float y)
        {
            var document = new PathF();
            document.MoveTo(x + 4, y + 2);
            document.LineTo(x + 13, y + 2);
            document.LineTo(x + 18, y + 7);
            document.LineTo(x + 18, y + 21);
            document.LineTo(x + 4, y + 21);
            document.Close();
            canvas.DrawPath(document);

            canvas.DrawLine(
                x + 13,
                y + 2,
                x + 13,
                y + 7);
            canvas.DrawLine(
                x + 13,
                y + 7,
                x + 18,
                y + 7);
            canvas.DrawLine(
                x + 7,
                y + 13,
                x + 10,
                y + 16);
            canvas.DrawLine(
                x + 10,
                y + 16,
                x + 16,
                y + 9);
        }

        private static void DrawSellerPhysicalShipmentProof(
            ICanvas canvas,
            float x,
            float y)
        {
            canvas.DrawRoundedRectangle(
                x + 2,
                y + 8,
                12,
                9,
                2);

            var cab = new PathF();
            cab.MoveTo(x + 14, y + 11);
            cab.LineTo(x + 18, y + 11);
            cab.LineTo(x + 21, y + 15);
            cab.LineTo(x + 21, y + 17);
            cab.LineTo(x + 14, y + 17);
            cab.Close();
            canvas.DrawPath(cab);

            canvas.DrawCircle(
                x + 7,
                y + 19,
                2);
            canvas.DrawCircle(
                x + 18,
                y + 19,
                2);

            canvas.DrawLine(
                x + 5,
                y + 4,
                x + 15,
                y + 4);
            canvas.DrawLine(
                x + 12,
                y + 1,
                x + 15,
                y + 4);
            canvas.DrawLine(
                x + 12,
                y + 7,
                x + 15,
                y + 4);
        }

        private static void DrawSellerPayoutProof(
            ICanvas canvas,
            float x,
            float y)
        {
            canvas.DrawRoundedRectangle(
                x + 3,
                y + 2,
                17,
                19,
                2);
            canvas.DrawLine(
                x + 7,
                y + 7,
                x + 16,
                y + 7);
            canvas.DrawLine(
                x + 7,
                y + 11,
                x + 12,
                y + 11);
            canvas.DrawLine(
                x + 12,
                y + 15,
                x + 15,
                y + 18);
            canvas.DrawLine(
                x + 15,
                y + 18,
                x + 21,
                y + 11);
        }

        private static void DrawAgreement(
            ICanvas canvas,
            float x,
            float y)
        {
            var leftSleeve = new PathF();
            leftSleeve.MoveTo(x + 2, y + 7);
            leftSleeve.LineTo(x + 5, y + 5);
            leftSleeve.LineTo(x + 8, y + 8);
            leftSleeve.LineTo(x + 6, y + 11);
            leftSleeve.LineTo(x + 2, y + 8);
            leftSleeve.Close();
            canvas.DrawPath(leftSleeve);

            var rightSleeve = new PathF();
            rightSleeve.MoveTo(x + 20, y + 7);
            rightSleeve.LineTo(x + 17, y + 5);
            rightSleeve.LineTo(x + 14, y + 8);
            rightSleeve.LineTo(x + 16, y + 11);
            rightSleeve.LineTo(x + 20, y + 8);
            rightSleeve.Close();
            canvas.DrawPath(rightSleeve);

            var joinedHands = new PathF();
            joinedHands.MoveTo(x + 7.5f, y + 8);
            joinedHands.LineTo(x + 10, y + 6.5f);
            joinedHands.CurveTo(
                x + 10.8f, y + 6,
                x + 11.7f, y + 6.3f,
                x + 12.4f, y + 7);
            joinedHands.LineTo(x + 14.5f, y + 8.5f);
            joinedHands.MoveTo(x + 6.5f, y + 10.5f);
            joinedHands.LineTo(x + 10.5f, y + 14.5f);
            joinedHands.CurveTo(
                x + 11.2f, y + 15.2f,
                x + 12.4f, y + 14.7f,
                x + 12.4f, y + 13.8f);
            joinedHands.CurveTo(
                x + 13.1f, y + 14.5f,
                x + 14.3f, y + 14,
                x + 14.3f, y + 13);
            joinedHands.CurveTo(
                x + 15.1f, y + 13.5f,
                x + 16, y + 12.7f,
                x + 15.7f, y + 11);
            canvas.DrawPath(joinedHands);

            canvas.DrawLine(
                x + 9,
                y + 9,
                x + 14.3f,
                y + 13);
            canvas.DrawLine(
                x + 8.2f,
                y + 11.8f,
                x + 9.7f,
                y + 10.3f);
            canvas.DrawLine(
                x + 10,
                y + 13.5f,
                x + 11.4f,
                y + 12);
        }

        private static void DrawPayment(
            ICanvas canvas,
            float x,
            float y)
        {
            canvas.DrawRoundedRectangle(
                x + 2,
                y + 5,
                18,
                13,
                3);
            canvas.DrawLine(
                x + 2,
                y + 9,
                x + 20,
                y + 9);
            canvas.DrawLine(
                x + 6,
                y + 14,
                x + 10,
                y + 14);
            canvas.DrawCircle(
                x + 16.5f,
                y + 14,
                1.2f);
        }

        private static void DrawPhysicalHandoff(
            ICanvas canvas,
            float x,
            float y)
        {
            DrawPackage(canvas, x, y);
            canvas.DrawLine(
                x + 13,
                y + 17,
                x + 21,
                y + 17);
            canvas.DrawLine(
                x + 18,
                y + 14,
                x + 21,
                y + 17);
            canvas.DrawLine(
                x + 18,
                y + 20,
                x + 21,
                y + 17);
        }

        private static void DrawPhysicalReceipt(
            ICanvas canvas,
            float x,
            float y)
        {
            DrawPackage(canvas, x, y);
            canvas.DrawLine(
                x + 12,
                y + 17,
                x + 15,
                y + 20);
            canvas.DrawLine(
                x + 15,
                y + 20,
                x + 21,
                y + 13);
        }

        private static void DrawDigitalHandoff(
            ICanvas canvas,
            float x,
            float y)
        {
            var document = new PathF();
            document.MoveTo(x + 4, y + 2);
            document.LineTo(x + 13, y + 2);
            document.LineTo(x + 17, y + 6);
            document.LineTo(x + 17, y + 20);
            document.LineTo(x + 4, y + 20);
            document.Close();
            canvas.DrawPath(document);
            canvas.DrawLine(
                x + 13,
                y + 2,
                x + 13,
                y + 6);
            canvas.DrawLine(
                x + 13,
                y + 6,
                x + 17,
                y + 6);
            canvas.DrawLine(
                x + 8,
                y + 14,
                x + 21,
                y + 14);
            canvas.DrawLine(
                x + 18,
                y + 11,
                x + 21,
                y + 14);
            canvas.DrawLine(
                x + 18,
                y + 17,
                x + 21,
                y + 14);
        }

        private static void DrawPayout(
            ICanvas canvas,
            float x,
            float y)
        {
            canvas.DrawRoundedRectangle(
                x + 2,
                y + 9,
                18,
                11,
                3);
            canvas.DrawLine(
                x + 7,
                y + 15,
                x + 15,
                y + 15);
            canvas.DrawLine(
                x + 11,
                y + 2,
                x + 11,
                y + 11);
            canvas.DrawLine(
                x + 7.5f,
                y + 7.5f,
                x + 11,
                y + 11);
            canvas.DrawLine(
                x + 14.5f,
                y + 7.5f,
                x + 11,
                y + 11);
        }

        private static void DrawPackage(
            ICanvas canvas,
            float x,
            float y)
        {
            var package = new PathF();
            package.MoveTo(x + 2, y + 7);
            package.LineTo(x + 8, y + 3);
            package.LineTo(x + 14, y + 7);
            package.LineTo(x + 14, y + 15);
            package.LineTo(x + 2, y + 15);
            package.Close();
            canvas.DrawPath(package);
            canvas.DrawLine(
                x + 2,
                y + 7,
                x + 8,
                y + 11);
            canvas.DrawLine(
                x + 8,
                y + 11,
                x + 14,
                y + 7);
            canvas.DrawLine(
                x + 8,
                y + 11,
                x + 8,
                y + 15);
        }
    }
}
