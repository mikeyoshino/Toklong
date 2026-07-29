using System.Windows.Input;

namespace Toklong.Mobile.Controls;

public partial class OtpVerificationFormView : ContentView
{
    public static readonly BindableProperty CodeProperty =
        BindableProperty.Create(
            nameof(Code),
            typeof(string),
            typeof(OtpVerificationFormView),
            string.Empty,
            BindingMode.TwoWay);

    public static readonly BindableProperty ConfirmCommandProperty =
        BindableProperty.Create(
            nameof(ConfirmCommand),
            typeof(ICommand),
            typeof(OtpVerificationFormView));

    public static readonly BindableProperty CanConfirmProperty =
        BindableProperty.Create(
            nameof(CanConfirm),
            typeof(bool),
            typeof(OtpVerificationFormView),
            true);

    public static readonly BindableProperty IsBusyProperty =
        BindableProperty.Create(
            nameof(IsBusy),
            typeof(bool),
            typeof(OtpVerificationFormView),
            false,
            propertyChanged: OnDisplayedTextInputChanged);

    public static readonly BindableProperty ConfirmTextProperty =
        BindableProperty.Create(
            nameof(ConfirmText),
            typeof(string),
            typeof(OtpVerificationFormView),
            "ยืนยัน",
            propertyChanged: OnDisplayedTextInputChanged);

    public static readonly BindableProperty BusyTextProperty =
        BindableProperty.Create(
            nameof(BusyText),
            typeof(string),
            typeof(OtpVerificationFormView),
            "กำลังยืนยัน...",
            propertyChanged: OnDisplayedTextInputChanged);

    public static readonly BindableProperty ConfirmSemanticDescriptionProperty =
        BindableProperty.Create(
            nameof(ConfirmSemanticDescription),
            typeof(string),
            typeof(OtpVerificationFormView),
            "ยืนยันรหัส 6 หลัก");

    public static readonly BindableProperty DevelopmentHintProperty =
        BindableProperty.Create(
            nameof(DevelopmentHint),
            typeof(string),
            typeof(OtpVerificationFormView),
            string.Empty);

    public static readonly BindableProperty HasDevelopmentHintProperty =
        BindableProperty.Create(
            nameof(HasDevelopmentHint),
            typeof(bool),
            typeof(OtpVerificationFormView),
            false);

    public OtpVerificationFormView() => InitializeComponent();

    public void FocusInput() => OtpInput.FocusInput();

    public string Code
    {
        get => (string)GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    public ICommand? ConfirmCommand
    {
        get => (ICommand?)GetValue(ConfirmCommandProperty);
        set => SetValue(ConfirmCommandProperty, value);
    }

    public bool CanConfirm
    {
        get => (bool)GetValue(CanConfirmProperty);
        set => SetValue(CanConfirmProperty, value);
    }

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public string ConfirmText
    {
        get => (string)GetValue(ConfirmTextProperty);
        set => SetValue(ConfirmTextProperty, value);
    }

    public string BusyText
    {
        get => (string)GetValue(BusyTextProperty);
        set => SetValue(BusyTextProperty, value);
    }

    public string ConfirmSemanticDescription
    {
        get => (string)GetValue(ConfirmSemanticDescriptionProperty);
        set => SetValue(ConfirmSemanticDescriptionProperty, value);
    }

    public string DevelopmentHint
    {
        get => (string)GetValue(DevelopmentHintProperty);
        set => SetValue(DevelopmentHintProperty, value);
    }

    public bool HasDevelopmentHint
    {
        get => (bool)GetValue(HasDevelopmentHintProperty);
        set => SetValue(HasDevelopmentHintProperty, value);
    }

    public string DisplayedConfirmText =>
        IsBusy ? BusyText : ConfirmText;

    private static void OnDisplayedTextInputChanged(
        BindableObject bindable,
        object oldValue,
        object newValue) =>
        ((OtpVerificationFormView)bindable)
            .OnPropertyChanged(nameof(DisplayedConfirmText));
}
