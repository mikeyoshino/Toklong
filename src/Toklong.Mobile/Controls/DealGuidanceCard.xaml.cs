using Toklong.Mobile.Core;

namespace Toklong.Mobile.Controls;

public partial class DealGuidanceCard : ContentView
{
    public static readonly BindableProperty TransactionProperty =
        BindableProperty.Create(
            nameof(Transaction),
            typeof(AppTransaction),
            typeof(DealGuidanceCard));

    public DealGuidanceCard() => InitializeComponent();

    public AppTransaction? Transaction
    {
        get => (AppTransaction?)GetValue(TransactionProperty);
        set => SetValue(TransactionProperty, value);
    }
}
