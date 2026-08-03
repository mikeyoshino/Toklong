using Toklong.Mobile.Core;

namespace Toklong.Mobile.Controls;

public partial class RoleTransactionHeader : ContentView
{
    public static readonly BindableProperty TransactionProperty =
        BindableProperty.Create(
            nameof(Transaction),
            typeof(AppTransaction),
            typeof(RoleTransactionHeader));

    public RoleTransactionHeader() => InitializeComponent();

    public AppTransaction? Transaction
    {
        get => (AppTransaction?)GetValue(TransactionProperty);
        set => SetValue(TransactionProperty, value);
    }
}
