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
