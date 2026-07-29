using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Pages;

public partial class ChangeEmailPage : ContentPage
{
    public ChangeEmailPage(ChangeEmailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
