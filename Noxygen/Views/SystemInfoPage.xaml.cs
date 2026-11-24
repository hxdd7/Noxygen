using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Noxygen.ViewModels;

namespace Noxygen.Views;

public sealed partial class SystemInfoPage : Page
{
    public SystemInfoViewModel ViewModel { get; } = new();

    public SystemInfoPage()
    {
        this.InitializeComponent();
        this.DataContext = ViewModel;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
        SyncBarWidths();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.StopPolling();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemInfoViewModel.Info)
            or nameof(SystemInfoViewModel.CpuBarWidth)
            or nameof(SystemInfoViewModel.MemoryBarWidth)
            or nameof(SystemInfoViewModel.GpuBarWidth))
        {
            SyncBarWidths();
        }
    }

    private void MonitorTrack_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        SyncBarWidths();
    }

    private void SyncBarWidths()
    {
        ApplyBarWidth(CpuTrackGrid, CpuFillBorder, ViewModel.Info.CpuUsagePercent);
        ApplyBarWidth(MemoryTrackGrid, MemoryFillBorder, ViewModel.Info.MemoryUsagePercent);
        ApplyBarWidth(IntegratedGpuTrackGrid, IntegratedGpuFillBorder, ViewModel.Info.IntegratedGpuUsagePercent);
        ApplyBarWidth(DedicatedGpuTrackGrid, DedicatedGpuFillBorder, ViewModel.Info.DedicatedGpuUsagePercent);
    }

    private static void ApplyBarWidth(Grid track, Border fill, double percent)
    {
        if (track == null || fill == null || track.ActualWidth <= 0)
        {
            return;
        }

        double clamped = Math.Clamp(percent, 0.0, 100.0);
        fill.Width = (clamped / 100.0) * track.ActualWidth;
    }
}