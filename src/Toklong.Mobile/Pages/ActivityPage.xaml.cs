using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Pages;

public partial class ActivityPage : ContentPage
{
    private readonly ActivityViewModel viewModel;

    public ActivityPage(ActivityViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.LoadAsync();
    }
}
