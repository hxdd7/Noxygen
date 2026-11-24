using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Noxygen.Helpers;
using Noxygen.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Noxygen.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private const string SelectedThemeKey = "AppSelectedTheme";
    private const string JumpListModeKey = "JumpListDisplayMode";
    private readonly LauncherDataService _launcherService = new();
    private readonly PlaytimeTrackerService _playtimeTrackerService = new();

    [ObservableProperty]
    private int _selectedThemeIndex;

    [ObservableProperty]
    private int _jumpListModeIndex;

    public SettingsViewModel()
    {
        var localSettings = ApplicationData.Current.LocalSettings;

        // Load saved theme preference (0 = Default, 1 = Light, 2 = Dark)
        if (localSettings.Values.TryGetValue(SelectedThemeKey, out var savedTheme))
        {
            _selectedThemeIndex = (int)savedTheme;
        }
        else
        {
            _selectedThemeIndex = 0;
        }

        // Load saved Jump List mode preference (0 = Pinned Games, 1 = Recent Games)
        if (localSettings.Values.TryGetValue(JumpListModeKey, out var savedJumpListMode))
        {
            _jumpListModeIndex = (int)savedJumpListMode;
        }
        else
        {
            _jumpListModeIndex = 0; // Default to Pinned Games
        }
    }

    partial void OnSelectedThemeIndexChanged(int value)
    {
        // Save choice to local settings
        ApplicationData.Current.LocalSettings.Values[SelectedThemeKey] = value;

        if (App.MainWindow is MainWindow mainWindow && mainWindow.Content is FrameworkElement rootElement)
        {
            ElementTheme newTheme = value switch
            {
                1 => ElementTheme.Light,
                2 => ElementTheme.Dark,
                _ => ElementTheme.Default
            };

            rootElement.RequestedTheme = newTheme;

            // Update the native caption buttons to match the new theme
            mainWindow.UpdateTitleBarTheme(rootElement.ActualTheme);
        }
    }

    partial void OnJumpListModeIndexChanged(int value)
    {
        // Save preference to local settings
        ApplicationData.Current.LocalSettings.Values[JumpListModeKey] = value;

        // Instantly update the taskbar Jump List when the setting changes
        if (App.MainWindow is MainWindow mainWindow)
        {
            mainWindow.UpdateTaskbarJumpList();
        }
    }

    public static void ApplyTheme(int themeIndex)
    {
        if (App.MainWindow?.Content is FrameworkElement rootElement)
        {
            rootElement.RequestedTheme = themeIndex switch
            {
                1 => ElementTheme.Light,
                2 => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }
    }

    [RelayCommand]
    private async Task ScanLaunchersAsync()
    {
        try
        {
            var detectedLaunchers = await Task.Run(() => LauncherScanner.ScanInstalledLaunchers());
            await _launcherService.SaveLaunchersAsync(detectedLaunchers);

            if (App.MainWindow is MainWindow mainWindow)
            {
                mainWindow.SyncLauncherSidebarItems();
            }

            await ShowMessageDialogAsync(
                "Launcher Scan Complete",
                $"Found and pinned {detectedLaunchers.Count} installed game launcher(s) to the sidebar.");
        }
        catch (Exception ex)
        {
            await ShowMessageDialogAsync(
                "Scan Failed",
                $"An error occurred while scanning launchers:\n{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ExportPlaytimeAsync()
    {
        try
        {
            if (App.MainWindow is not Window mainWindow)
            {
                return;
            }

            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = $"noxygen-playtime-{DateTime.Today:yyyy-MM-dd}"
            };
            picker.FileTypeChoices.Add("JSON Backup", new List<string> { ".json" });

            IntPtr hwnd = WindowNative.GetWindowHandle(mainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            StorageFile? file = await picker.PickSaveFileAsync();
            if (file == null) return;

            string json = await _playtimeTrackerService.ExportSessionsJsonAsync();
            await FileIO.WriteTextAsync(file, json);

            await ShowMessageDialogAsync(
                "Export Complete",
                $"Your playtime history has been exported to:\n{file.Name}");
        }
        catch (Exception ex)
        {
            await ShowMessageDialogAsync(
                "Export Failed",
                $"Could not export playtime data:\n{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ImportPlaytimeAsync()
    {
        try
        {
            if (App.MainWindow is not Window mainWindow)
            {
                return;
            }

            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
            picker.FileTypeFilter.Add(".json");

            IntPtr hwnd = WindowNative.GetWindowHandle(mainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            StorageFile? file = await picker.PickSingleFileAsync();
            if (file == null) return;

            string json = await FileIO.ReadTextAsync(file);
            var (importedCount, totalCount) = await _playtimeTrackerService.ImportSessionsJsonAsync(json, mergeWithExisting: true);

            if (App.MainWindow is MainWindow mw)
            {
                mw.UpdateTaskbarJumpList();
            }

            await ShowMessageDialogAsync(
                "Import Complete",
                $"Added {importedCount} new play session(s) from {file.Name}.\nTotal recorded sessions: {totalCount}.");
        }
        catch (Exception ex)
        {
            await ShowMessageDialogAsync(
                "Import Failed",
                $"Could not import playtime data:\n{ex.Message}");
        }
    }

    private static async Task ShowMessageDialogAsync(string title, string content)
    {
        if (App.MainWindow?.Content is FrameworkElement root)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                CloseButtonText = "OK",
                XamlRoot = root.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }
}