using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Noxygen.ViewModels;

namespace Noxygen.Views;

public sealed partial class StatsPage : Page
{
    public StatsViewModel ViewModel { get; } = new();

    public StatsPage()
    {
        this.InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.InitializeAsync();
    }
}