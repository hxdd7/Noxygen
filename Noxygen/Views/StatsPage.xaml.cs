using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Noxygen.ViewModels;

namespace Noxygen.Views;

public sealed partial class StatsPage : Page
{
    private const double WideLayoutMinWidth = 620;

    public StatsViewModel ViewModel { get; } = new();

    public StatsPage()
    {
        this.InitializeComponent();
        this.DataContext = ViewModel;
        this.SizeChanged += OnPageSizeChanged;
        this.ActualThemeChanged += OnActualThemeChanged;
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCardsLayout(e.NewSize.Width);
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        ViewModel.RefreshHeatmapTheme(this.ActualTheme);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        UpdateCardsLayout(this.ActualWidth);
        await ViewModel.InitializeAsync(this.ActualTheme);
    }

    private void UpdateCardsLayout(double width)
    {
        if (width <= 0) return;

        bool isWide = width >= WideLayoutMinWidth;
        SummaryCardsWideGrid.Visibility = isWide ? Visibility.Visible : Visibility.Collapsed;
        SummaryCardsNarrowStack.Visibility = isWide ? Visibility.Collapsed : Visibility.Visible;
    }
}