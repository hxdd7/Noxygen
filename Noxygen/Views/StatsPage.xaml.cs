using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Noxygen.ViewModels;
using System;
using System.Linq;

namespace Noxygen.Views;

public sealed partial class StatsPage : Page
{
    private const double WideLayoutMinWidth = 620;
    private const double SplitPanelsMinWidth = 960;

    private bool _userHasScrolledHeatmap;
    private bool _pendingScrollToEnd = true;
    private bool _isSyncingScrollBar;

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
        SyncHeatmapScrollBar();
        ScheduleScrollToEndIfNeeded();
    }

    private void OnHeatmapScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        SyncHeatmapScrollBar();
        ScheduleScrollToEndIfNeeded();
    }

    private void OnHeatmapHorizontalViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (!_isSyncingScrollBar && HeatmapHorizontalScrollBar != null)
        {
            _isSyncingScrollBar = true;
            try
            {
                HeatmapHorizontalScrollBar.Value = HeatmapHorizontalScrollViewer.HorizontalOffset;
            }
            finally
            {
                _isSyncingScrollBar = false;
            }
        }

        if (!e.IsIntermediate && _pendingScrollToEnd)
        {
            _pendingScrollToEnd = false;
        }
        else if (!e.IsIntermediate)
        {
            _userHasScrolledHeatmap = true;
        }
    }

    private void OnHeatmapHorizontalScrollBarScroll(object sender, ScrollEventArgs e)
    {
        if (_isSyncingScrollBar || HeatmapHorizontalScrollViewer == null) return;

        _userHasScrolledHeatmap = true;
        _isSyncingScrollBar = true;
        try
        {
            HeatmapHorizontalScrollViewer.ChangeView(e.NewValue, null, null, disableAnimation: true);
        }
        finally
        {
            _isSyncingScrollBar = false;
        }
    }

    private void SyncHeatmapScrollBar()
    {
        if (HeatmapHorizontalScrollBar == null || HeatmapHorizontalScrollViewer == null) return;

        double scrollableWidth = Math.Max(0, HeatmapHorizontalScrollViewer.ScrollableWidth);
        _isSyncingScrollBar = true;
        try
        {
            HeatmapHorizontalScrollBar.Maximum = scrollableWidth;
            HeatmapHorizontalScrollBar.ViewportSize = HeatmapHorizontalScrollViewer.ViewportWidth;
            HeatmapHorizontalScrollBar.Value = Math.Clamp(HeatmapHorizontalScrollViewer.HorizontalOffset, 0, scrollableWidth);
            HeatmapHorizontalScrollBar.Visibility = scrollableWidth > 1 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _isSyncingScrollBar = false;
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
                SyncHeatmapScrollBar();
            }
        });
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        ViewModel.RefreshHeatmapTheme(this.ActualTheme);
        SyncHeatmapScrollBar();
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

    private void OnShareBarTrackLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Grid trackGrid)
        {
            UpdateShareBarFillWidth(trackGrid);
        }
    }

    private void OnShareBarTrackSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Grid trackGrid)
        {
            UpdateShareBarFillWidth(trackGrid);
        }
    }

    private static void UpdateShareBarFillWidth(Grid trackGrid)
    {
        if (trackGrid.Children.FirstOrDefault() is not Border fillBorder)
            return;

        GamePlayStatItem? item = trackGrid.Tag as GamePlayStatItem ?? trackGrid.DataContext as GamePlayStatItem;
        if (item == null || item.ShareRatio <= 0)
        {
            fillBorder.Width = 0;
            return;
        }

        double trackWidth = trackGrid.ActualWidth;
        if (trackWidth <= 0) return;

        double targetWidth = trackWidth * item.ShareRatio;
        fillBorder.Width = Math.Clamp(targetWidth, 4.0, trackWidth);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _userHasScrolledHeatmap = false;
        _pendingScrollToEnd = true;
        UpdateResponsiveLayouts(this.ActualWidth);
        await ViewModel.InitializeAsync(this.ActualTheme);
        SyncHeatmapScrollBar();
        ScheduleScrollToEndIfNeeded();
    }

    private void UpdateResponsiveLayouts(double width)
    {
        if (width <= 0) return;

        bool isWide = width >= WideLayoutMinWidth;
        SummaryCardsWideGrid.Visibility = isWide ? Visibility.Visible : Visibility.Collapsed;
        SummaryCardsNarrowStack.Visibility = isWide ? Visibility.Collapsed : Visibility.Visible;

        bool isSideBySide = width >= SplitPanelsMinWidth;

        ConfigureSplitPanel(
            ActivityRightCol,
            WeekdayActivityCard,
            SessionInsightsCard,
            isSideBySide,
            rightWidth: 320);

        ConfigureSplitPanel(
            HeatmapRightCol,
            HeatmapCard,
            DayDetailsCard,
            isSideBySide,
            rightWidth: 320);

        ConfigureSplitPanel(
            RecentSessionsCol,
            LeaderboardCard,
            RecentSessionsCard,
            isSideBySide,
            rightWidth: 320);
    }

    private static void ConfigureSplitPanel(
        ColumnDefinition rightColumn,
        FrameworkElement leftElement,
        FrameworkElement rightElement,
        bool isSideBySide,
        double rightWidth)
    {
        if (isSideBySide)
        {
            rightColumn.Width = new GridLength(rightWidth);
            Grid.SetRow(leftElement, 0);
            Grid.SetColumn(leftElement, 0);
            Grid.SetColumnSpan(leftElement, 1);

            Grid.SetRow(rightElement, 0);
            Grid.SetColumn(rightElement, 1);
            Grid.SetColumnSpan(rightElement, 1);
        }
        else
        {
            rightColumn.Width = new GridLength(0);
            Grid.SetRow(leftElement, 0);
            Grid.SetColumn(leftElement, 0);
            Grid.SetColumnSpan(leftElement, 2);

            Grid.SetRow(rightElement, 1);
            Grid.SetColumn(rightElement, 0);
            Grid.SetColumnSpan(rightElement, 2);
        }
    }
}