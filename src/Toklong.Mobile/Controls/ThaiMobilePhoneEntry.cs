using Toklong.Mobile.Core;

namespace Toklong.Mobile.Controls;

public sealed class ThaiMobilePhoneEntry : Entry
{
    public static readonly BindableProperty PhoneNumberProperty =
        BindableProperty.Create(
            nameof(PhoneNumber),
            typeof(string),
            typeof(ThaiMobilePhoneEntry),
            "",
            BindingMode.TwoWay,
            propertyChanged: static (bindable, _, value) =>
                ((ThaiMobilePhoneEntry)bindable).Synchronize(value as string));

    private bool isSynchronizing;

    public ThaiMobilePhoneEntry()
    {
        Keyboard = Keyboard.Telephone;
        MaxLength = ThaiMobilePhoneInput.FormattedLocalNumberLength;
        TextChanged += OnTextChanged;
    }

    public string PhoneNumber
    {
        get => (string)GetValue(PhoneNumberProperty);
        set => SetValue(PhoneNumberProperty, value);
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs eventArgs)
        => Synchronize(eventArgs.NewTextValue);

    internal string ApplyNativeInput(string? value)
    {
        Synchronize(value);
        return PhoneNumber;
    }

    private void Synchronize(string? value)
    {
        if (isSynchronizing)
            return;

        isSynchronizing = true;
        try
        {
            var formatted = ThaiMobilePhoneInput.Format(value);

            if (!string.Equals(Text, formatted, StringComparison.Ordinal))
                Text = formatted;
            if (!string.Equals(
                    PhoneNumber,
                    formatted,
                    StringComparison.Ordinal))
                PhoneNumber = formatted;

            CursorPosition = formatted.Length;
        }
        finally
        {
            isSynchronizing = false;
        }
    }
}
