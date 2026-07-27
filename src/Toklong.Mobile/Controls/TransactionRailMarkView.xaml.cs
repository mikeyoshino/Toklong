namespace Toklong.Mobile.Controls;

public partial class TransactionRailMarkView : ContentView
{
    private const uint ArrivalMilliseconds = 250;
    private const uint ConnectionMilliseconds = 400;
    private const uint ConfirmationMilliseconds = 200;
    private const uint WordmarkMilliseconds = 350;

    public TransactionRailMarkView()
    {
        InitializeComponent();
        ShowInitialState();
    }

    public void ShowInitialState()
    {
        CancelMotion();
        UpperRail.TranslationX = -22;
        LowerRail.TranslationX = 22;
        ConfirmationNode.Opacity = 0;
        ConfirmationNode.Scale = 0.2;
        ConfirmationPulse.Opacity = 0;
        ConfirmationPulse.Scale = 0.65;
        Wordmark.Opacity = 0;
        Wordmark.TranslationX = -7;
    }

    public void ShowCompletedState()
    {
        CancelMotion();
        UpperRail.TranslationX = 0;
        LowerRail.TranslationX = 0;
        ConfirmationNode.Opacity = 1;
        ConfirmationNode.Scale = 1;
        ConfirmationPulse.Opacity = 0;
        ConfirmationPulse.Scale = 1.8;
        Wordmark.Opacity = 1;
        Wordmark.TranslationX = 0;
    }

    public async Task PlayAsync(
        CancellationToken cancellationToken = default)
    {
        ShowInitialState();
        using var registration =
            cancellationToken.Register(CancelMotion);

        await Task.WhenAll(
            UpperRail.TranslateToAsync(
                -10,
                0,
                ArrivalMilliseconds,
                Easing.CubicOut),
            LowerRail.TranslateToAsync(
                10,
                0,
                ArrivalMilliseconds,
                Easing.CubicOut));
        cancellationToken.ThrowIfCancellationRequested();

        await Task.WhenAll(
            UpperRail.TranslateToAsync(
                0,
                0,
                ConnectionMilliseconds,
                Easing.SinInOut),
            LowerRail.TranslateToAsync(
                0,
                0,
                ConnectionMilliseconds,
                Easing.SinInOut));
        cancellationToken.ThrowIfCancellationRequested();

        ConfirmationNode.Opacity = 1;
        ConfirmationPulse.Opacity = 0.38;
        await Task.WhenAll(
            ConfirmationNode.ScaleToAsync(
                1,
                ConfirmationMilliseconds,
                Easing.CubicOut),
            ConfirmationPulse.ScaleToAsync(
                1.8,
                ConfirmationMilliseconds,
                Easing.CubicOut),
            ConfirmationPulse.FadeToAsync(
                0,
                ConfirmationMilliseconds,
                Easing.CubicOut));
        cancellationToken.ThrowIfCancellationRequested();

        await Task.WhenAll(
            Wordmark.TranslateToAsync(
                0,
                0,
                WordmarkMilliseconds,
                Easing.CubicOut),
            Wordmark.FadeToAsync(
                1,
                WordmarkMilliseconds,
                Easing.CubicOut));
    }

    public void CancelMotion()
    {
        UpperRail.CancelAnimations();
        LowerRail.CancelAnimations();
        ConfirmationNode.CancelAnimations();
        ConfirmationPulse.CancelAnimations();
        Wordmark.CancelAnimations();
    }
}
