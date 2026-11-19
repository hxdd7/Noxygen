using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Noxygen.Models;
using Noxygen.ViewModels;
using Noxygen.Services;
using System;
using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Noxygen.Views;

public sealed partial class LibraryPage : Page
{
    public LibraryViewModel ViewModel { get; } = new();

    public LibraryPage()
    {
        this.InitializeComponent();
        this.DataContext = ViewModel;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.InitializeAsync();
    }

    private async void ContextEditGame_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem menuItem && menuItem.DataContext is Game game)
        {
            await ViewModel.EditGameCommand.ExecuteAsync(game);
        }
    }

    private async void ContextDeleteGame_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem menuItem && menuItem.DataContext is Game game)
        {
            await ViewModel.DeleteGameCommand.ExecuteAsync(game);
        }
    }

    private async void ContextToggleRunAsAdmin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem menuItem && menuItem.DataContext is Game game)
        {
            await ViewModel.ToggleRunAsAdminAsync(game);
        }
    }

    public async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button playButton || playButton.DataContext is not Game game)
        {
            return;
        }

        try
        {
            // 1. Record to Recent Games history
            Noxygen.Services.RecentGamesService.AddRecentGame(game.Title);

            // 2. Initialize Playtime Tracker & Start Timer
            var playtimeTracker = new PlaytimeTrackerService();
            var sessionStartTime = DateTime.Now;

            Process? launchedProcess = null;
            string? targetExeName = !string.IsNullOrEmpty(game.ExecutablePath)
                ? Path.GetFileNameWithoutExtension(game.ExecutablePath)
                : null;

            // 3. Handle Steam Protocol Launch
            if (string.Equals(game.Launcher, "Steam", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(game.AppId))
            {
                var uri = new Uri($"steam://run/{game.AppId}");
                await Windows.System.Launcher.LaunchUriAsync(uri);
            }
            // 4. Handle Epic Games Protocol Launch
            else if (string.Equals(game.Launcher, "Epic", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(game.AppId))
            {
                var uri = new Uri($"com.epicgames.launcher://apps/{game.AppId}?action=launch");
                await Windows.System.Launcher.LaunchUriAsync(uri);
            }
            // 5. Fallback to Direct Executable Execution
            else
            {
                if (string.IsNullOrWhiteSpace(game.ExecutablePath) || !File.Exists(game.ExecutablePath))
                {
                    await ShowErrorDialogAsync("Executable Not Found", $"Could not locate file at:\n{game.ExecutablePath}");
                    return;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = game.ExecutablePath,
                    WorkingDirectory = Path.GetDirectoryName(game.ExecutablePath) ?? string.Empty,
                    UseShellExecute = game.RunAsAdmin,
                    Verb = game.RunAsAdmin ? "runas" : string.Empty
                };

                launchedProcess = Process.Start(startInfo);
            }

            // 6. Hide Window to Tray
            if (App.MainWindow != null)
            {
                var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
                Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId)?.Hide();
            }

            // 7. Handle Window Restoration & Playtime Logging (Direct Process or Process Name Polling for URIs)
            if (launchedProcess != null)
            {
                launchedProcess.EnableRaisingEvents = true;
                launchedProcess.Exited += async (s, args) =>
                {
                    var sessionEndTime = DateTime.Now;
                    await playtimeTracker.LogSessionAsync(game.Title, sessionStartTime, sessionEndTime);
                    RestoreWindow();
                };
            }
            else if (!string.IsNullOrEmpty(targetExeName))
            {
                // Background monitor for Steam/Epic games spawned via protocol URI
                _ = Task.Run(async () =>
                {
                    // Give the launcher a few seconds to spawn the game process
                    await Task.Delay(4000);

                    Process? gameProc = null;
                    // Wait for the game process to appear
                    while (true)
                    {
                        var procs = Process.GetProcessesByName(targetExeName);
                        if (procs.Length > 0)
                        {
                            gameProc = procs[0];
                            break;
                        }
                        await Task.Delay(2000);
                    }

                    // Re-anchor start time right when the actual game process is detected running
                    var activeStartTime = DateTime.Now;

                    // Wait until the game process exits
                    gameProc.EnableRaisingEvents = true;
                    var tcs = new TaskCompletionSource();
                    gameProc.Exited += (s, e) => tcs.SetResult();
                    await tcs.Task;

                    var activeEndTime = DateTime.Now;
                    await playtimeTracker.LogSessionAsync(game.Title, activeStartTime, activeEndTime);
                    RestoreWindow();
                });
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // User cancelled UAC prompt
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync("Failed to Launch Game", ex.Message);
        }
    }

    private void RestoreWindow()
    {
        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            if (App.MainWindow != null)
            {
                var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
                Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId)?.Show();
                App.MainWindow.Activate();
            }
        });
    }

    private async System.Threading.Tasks.Task ShowErrorDialogAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = this.XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style
        };

        await dialog.ShowAsync();
    }

    private void GameCard_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is Game selectedGame)
        {
            NavigateToDetails(selectedGame);
        }
    }

    private void ViewDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is Game selectedGame)
        {
            NavigateToDetails(selectedGame);
        }
    }

    private void NavigateToDetails(Game game)
    {
        if (game == null) return;

        Frame.Navigate(typeof(GameDetailsPage), game);
    }

    private void GamesGridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Game selectedGame)
        {
            NavigateToDetails(selectedGame);
        }
    }

    private async void ContextPinGame_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem menuItem && menuItem.DataContext is Game game)
        {
            await ViewModel.TogglePinGameCommand.ExecuteAsync(game);
        }
    }
}