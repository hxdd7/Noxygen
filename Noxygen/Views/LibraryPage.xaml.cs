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
using System.Linq;
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

            // 2. Initialize Playtime Tracker
            var playtimeTracker = new PlaytimeTrackerService();
            var sessionStartTime = DateTime.Now;

            Process? launchedProcess = null;
            bool isRiotLaunch = IsRiotGame(game);
            string? targetExeName = ResolveTrackedProcessName(game, isRiotLaunch);

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
            // 4b. Handle Riot Client Launch (RiotClientServices exits immediately after handing off)
            else if (isRiotLaunch)
            {
                var riotStartInfo = BuildRiotClientStartInfo(game);
                if (riotStartInfo == null)
                {
                    await ShowErrorDialogAsync("Riot Client Not Found", "Could not locate RiotClientServices.exe to launch this game.");
                    return;
                }

                Process.Start(riotStartInfo);
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

            // 7. Track actual game process rather than bootstrapper/launcher exit
            if (launchedProcess != null && !isRiotLaunch)
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
                _ = Task.Run(async () =>
                {
                    // Wait for Riot Client / launcher handoff and user to press Play
                    await Task.Delay(isRiotLaunch ? 8000 : 4000);

                    Process? gameProc = null;
                    while (true)
                    {
                        var procs = Process.GetProcessesByName(targetExeName);
                        if (procs.Length > 0)
                        {
                            gameProc = procs[0];
                            break;
                        }
                        await Task.Delay(2500);
                    }

                    // Start counting playtime once the real game process is active
                    var activeStartTime = DateTime.Now;

                    gameProc.EnableRaisingEvents = true;
                    var tcs = new TaskCompletionSource();
                    gameProc.Exited += (s, e) => tcs.SetResult();
                    await tcs.Task;

                    var activeEndTime = DateTime.Now;
                    await playtimeTracker.LogSessionAsync(game.Title, activeStartTime, activeEndTime);
                    RestoreWindow();
                });
            }
            else if (isRiotLaunch)
            {
                // Fallback if process name couldn't be determined: restore after a short grace period
                _ = Task.Run(async () =>
                {
                    await Task.Delay(15000);
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

    private static string? ResolveTrackedProcessName(Game game, bool isRiotLaunch)
    {
        if (isRiotLaunch)
        {
            string combined = $"{game.Title} {game.ExecutablePath} {game.AppId}";
            if (combined.Contains("valorant", StringComparison.OrdinalIgnoreCase))
            {
                return "VALORANT-Win64-Shipping";
            }
            if (combined.Contains("league", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("lol", StringComparison.OrdinalIgnoreCase))
            {
                return "League of Legends";
            }
        }

        return !string.IsNullOrEmpty(game.ExecutablePath)
            ? Path.GetFileNameWithoutExtension(game.ExecutablePath)
            : null;
    }

    private static bool IsRiotGame(Game game) =>
        string.Equals(game.Launcher, "Riot", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(game.Launcher, "Riot Client", StringComparison.OrdinalIgnoreCase) ||
        (!string.IsNullOrWhiteSpace(game.ExecutablePath) &&
         game.ExecutablePath.Contains("Riot Games", StringComparison.OrdinalIgnoreCase) &&
         !game.ExecutablePath.EndsWith("RiotClientServices.exe", StringComparison.OrdinalIgnoreCase));

    private static ProcessStartInfo? BuildRiotClientStartInfo(Game game)
    {
        string? riotClientPath = null;

        try
        {
            var launcherService = new LauncherDataService();
            var launchers = launcherService.LoadLaunchersSync();
            var riotLauncher = launchers?.FirstOrDefault(l =>
                l != null &&
                (l.Name?.Contains("Riot", StringComparison.OrdinalIgnoreCase) == true ||
                 l.ExecutablePath?.Contains("RiotClient", StringComparison.OrdinalIgnoreCase) == true));

            if (!string.IsNullOrWhiteSpace(riotLauncher?.ExecutablePath) && File.Exists(riotLauncher.ExecutablePath))
            {
                riotClientPath = riotLauncher.ExecutablePath;
            }
        }
        catch
        {
            // Fall through to standard paths
        }

        if (string.IsNullOrWhiteSpace(riotClientPath))
        {
            string commonAppDataCandidate = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Riot Games", "RiotClientServices.exe");

            if (File.Exists(commonAppDataCandidate))
            {
                riotClientPath = commonAppDataCandidate;
            }
            else if (File.Exists(@"C:\Riot Games\RiotClient\RiotClientServices.exe"))
            {
                riotClientPath = @"C:\Riot Games\RiotClient\RiotClientServices.exe";
            }
        }

        if (string.IsNullOrWhiteSpace(riotClientPath) || !File.Exists(riotClientPath))
        {
            return null;
        }

        string combined = $"{game.Title} {game.ExecutablePath} {game.AppId}";
        string product = combined.Contains("valorant", StringComparison.OrdinalIgnoreCase)
            ? "valorant"
            : combined.Contains("league", StringComparison.OrdinalIgnoreCase) ||
              combined.Contains("lol", StringComparison.OrdinalIgnoreCase)
                ? "league_of_legends"
                : !string.IsNullOrWhiteSpace(game.AppId) ? game.AppId.Trim() : "valorant";

        return new ProcessStartInfo
        {
            FileName = riotClientPath,
            Arguments = $"--launch-product={product} --launch-patchline=live",
            UseShellExecute = true
        };
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