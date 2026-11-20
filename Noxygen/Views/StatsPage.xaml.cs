using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Noxygen.ViewModels;
using System;

namespace Noxygen.Views;

public sealed partial class StatsPage : Page
{
    private const double WideLayoutMinWidth = 620;
    private const double HeatmapSideBySideMinWidth = 960;

    private bool _userHasScrolledHeatmap;
    private bool _pendingScrollToEnd = true;

    public StatsViewModel ViewModel { get; } = new();

    public StatsPage()
    {
        this.InitializeComponent();
        this.DataContext = ViewModel;
        this.SizeChanged += OnPageSizeChanged;
        this.ActualThemeChanged += OnActualThemeChanged;
        HeatmapHorizontalScrollViewer.SizeChanged += OnHeatmapScrollViewerSizeChanged;
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayouts(e.NewSize.Width);
        ScheduleScrollToEndIfNeeded();
    }

    private void OnHeatmapScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ScheduleScrollToEndIfNeeded();
    }

    private void OnHeatmapHorizontalViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (!e.IsIntermediate && _pendingScrollToEnd)
        {
            _pendingScrollToEnd = false;
        }
        else if (!e.IsIntermediate)
        {
            _userHasScrolledHeatmap = true;
        }
    }

    private void ScheduleScrollToEndIfNeeded()
    {
        if (_userHasScrolledHeatmap) return;

        _pendingScrollToEnd = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_userHasScrolledHeatmap && HeatmapHorizontalScrollViewer.ScrollableWidth > 0)
            {
                HeatmapHorizontalScrollViewer.ChangeView(
                    HeatmapHorizontalScrollViewer.ScrollableWidth,
                    null,
                    null,
                    disableAnimation: true);
            }
        });
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        ViewModel.RefreshHeatmapTheme(this.ActualTheme);
        ScheduleScrollToEndIfNeeded();
    }

    private void OnHeatmapCellPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: HeatmapDayCell cell } && cell.IsInSelectedYear)
        {
            ViewModel.SelectHeatmapDay(cell.Date, isFromHover: true);
        }
    }

    private void OnHeatmapCellTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: HeatmapDayCell cell } && cell.IsInSelectedYear)
        {
            ViewModel.SelectHeatmapDay(cell.Date, isFromHover: false);
        }
    }

    private void OnResetToTodayClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectHeatmapDay(DateTime.Today, isFromHover: false);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _userHasScrolledHeatmap = false;
        _pendingScrollToEnd = true;
        UpdateResponsiveLayouts(this.ActualWidth);
        await ViewModel.InitializeAsync(this.ActualTheme);
        ScheduleScrollToEndIfNeeded();
    }

    private void UpdateResponsiveLayouts(double width)
    {
        if (width <= 0) return;

        bool isWide = width >= WideLayoutMinWidth;
        SummaryCardsWideGrid.Visibility = isWide ? Visibility.Visible : Visibility.Collapsed;
        SummaryCardsNarrowStack.Visibility = isWide ? Visibility.Collapsed : Visibility.Visible;

        bool isHeatmapSideBySide = width >= HeatmapSideBySideMinWidth;
        if (isHeatmapSideBySide)
        {
            HeatmapRightCol.Width = new GridLength(320);
            Grid.SetRow(DayDetailsCard, 0);
            Grid.SetColumn(DayDetailsCard, 1);
            Grid.SetColumnSpan(HeatmapCard, 1);
            Grid.SetColumnSpan(DayDetailsCard, 1);
        }
        else
        {
            HeatmapRightCol.Width = new GridLength(0);
            Grid.SetColumnSpan(HeatmapCard, 2);
            Grid.SetRow(DayDetailsCard, 1);
            Grid.SetColumn(DayDetailsCard, 0);
            Grid.SetColumnSpan(DayDetailsCard, 2);
        }
    }
}