namespace Toklong.Mobile.Controls;

public partial class OtpCodeInput : ContentView
{
    private static readonly Color FilledLine =
        Color.FromArgb("#2B7FFF");
    private static readonly Color EmptyLine =
        Color.FromArgb("#D0D5DD");
    private bool synchronizing;

    public static readonly BindableProperty CodeProperty =
        BindableProperty.Create(
            nameof(Code),
            typeof(string),
            typeof(OtpCodeInput),
            "",
            BindingMode.TwoWay,
            propertyChanged: OnCodeChanged);

    public OtpCodeInput()
    {
        InitializeComponent();
        UpdateVisuals("");
    }

    public string Code
    {
        get => (string)GetValue(CodeProperty);
        set => SetValue(CodeProperty, Normalize(value));
    }

    public void FocusInput() => CodeEntry.Focus();

    private static void OnCodeChanged(
        BindableObject bindable,
        object oldValue,
        object newValue)
    {
        var control = (OtpCodeInput)bindable;
        var normalized = Normalize(newValue as string);
        if (!string.Equals(
                newValue as string,
                normalized,
                StringComparison.Ordinal))
        {
            control.Code = normalized;
            return;
        }

        if (!string.Equals(
                control.CodeEntry.Text,
                normalized,
                StringComparison.Ordinal))
        {
            control.synchronizing = true;
            control.CodeEntry.Text = normalized;
            control.synchronizing = false;
        }
        control.UpdateVisuals(normalized);
    }

    private void OnCodeEntryTextChanged(
        object? sender,
        TextChangedEventArgs eventArgs)
    {
        if (synchronizing)
            return;

        var normalized = Normalize(eventArgs.NewTextValue);
        if (!string.Equals(
                eventArgs.NewTextValue,
                normalized,
                StringComparison.Ordinal))
        {
            synchronizing = true;
            CodeEntry.Text = normalized;
            synchronizing = false;
        }
        Code = normalized;
    }

    private void OnCodeEntryHandlerChanged(
        object? sender,
        EventArgs eventArgs)
    {
#if IOS
        if (CodeEntry.Handler?.PlatformView is UIKit.UITextField textField)
            textField.TextContentType = UIKit.UITextContentType.OneTimeCode;
#endif
    }

    private void UpdateVisuals(string value)
    {
        var labels = new[]
        {
            DigitOne,
            DigitTwo,
            DigitThree,
            DigitFour,
            DigitFive,
            DigitSix
        };
        var lines = new[]
        {
            LineOne,
            LineTwo,
            LineThree,
            LineFour,
            LineFive,
            LineSix
        };

        for (var index = 0; index < labels.Length; index++)
        {
            labels[index].Text = index < value.Length
                ? value[index].ToString()
                : "";
            lines[index].Color =
                index < value.Length ? FilledLine : EmptyLine;
        }
    }

    private static string Normalize(string? value) =>
        new((value ?? "")
            .Where(char.IsAsciiDigit)
            .Take(6)
            .ToArray());
}
