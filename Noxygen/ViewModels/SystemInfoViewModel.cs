using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Noxygen.Models;
using Noxygen.Services;

namespace Noxygen.ViewModels;

public partial class SystemInfoViewModel : ObservableObject
{
    private readonly SystemInfoService _systemInfoService = new();
    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private CancellationTokenSource? _pollingCts;

    [ObservableProperty]
    private PcSystemInfo _info = new();

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _lastUpdatedText = "Live monitoring";

    [ObservableProperty]
    private double _cpuBarWidth;

    [ObservableProperty]
    private double _memoryBarWidth;

    [ObservableProperty]
    private double _gpuBarWidth;

    public async Task LoadAsync()
    {
        if (IsRefreshing) return;

        try
        {
            IsRefreshing = true;
            var loaded = await _systemInfoService.GetSystemInfoAsync();
            Info = loaded;
            UpdateBars(loaded);
            LastUpdatedText = $"Updated {DateTime.Now:HH:mm:ss}";
            StartLivePolling();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    public void StopPolling()
    {
        _pollingCts?.Cancel();
        _pollingCts?.Dispose();
        _pollingCts = null;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadAsync();
    }

    private void StartLivePolling()
    {
        StopPolling();
        _pollingCts = new CancellationTokenSource();
        var token = _pollingCts.Token;

        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            try
            {
                while (await timer.WaitForNextTickAsync(token))
                {
                    await _systemInfoService.UpdateLiveMonitoringAsync(Info);
                    _dispatcherQueue?.TryEnqueue(() =>
                    {
                        if (token.IsCancellationRequested) return;
                        OnPropertyChanged(nameof(Info));
                        UpdateBars(Info);
                        LastUpdatedText = $"Updated {DateTime.Now:HH:mm:ss}";
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when leaving page
            }
        }, token);
    }

    private void UpdateBars(PcSystemInfo current)
    {
        // Track width is ~520px on wide layouts; scale cleanly from 0..100%
        const double maxWidth = 520.0;
        CpuBarWidth = Math.Clamp((current.CpuUsagePercent / 100.0) * maxWidth, 0, maxWidth);
        MemoryBarWidth = Math.Clamp((current.MemoryUsagePercent / 100.0) * maxWidth, 0, maxWidth);
        GpuBarWidth = Math.Clamp((current.GpuUsagePercent / 100.0) * maxWidth, 0, maxWidth);
    }
}