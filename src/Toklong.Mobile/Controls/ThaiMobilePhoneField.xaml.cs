namespace Toklong.Mobile.Controls;

public partial class ThaiMobilePhoneField : ContentView
{
    public static readonly BindableProperty PhoneNumberProperty =
        BindableProperty.Create(
            nameof(PhoneNumber),
            typeof(string),
            typeof(ThaiMobilePhoneField),
            "",
            BindingMode.TwoWay);

    public ThaiMobilePhoneField()
    {
        InitializeComponent();
    }

    public string PhoneNumber
    {
        get => (string)GetValue(PhoneNumberProperty);
        set => SetValue(PhoneNumberProperty, value);
    }
}
