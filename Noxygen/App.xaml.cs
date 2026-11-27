using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;
using Noxygen.Models;
using Noxygen.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.Activation;
using Windows.Storage;

namespace Noxygen;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }

    public App()
    {
        this.InitializeComponent();

        // Catch ALL unhandled background thread and XAML exceptions
        this.UnhandledException += async (sender, e) =>
        {
            e.Handled = true;

            Debug.WriteLine("==================================================");
            Debug.WriteLine($"[EXACT ERROR MESSAGE]: {e.Exception?.Message}");
            Debug.WriteLine($"[EXCEPTION TYPE]: {e.Exception?.GetType().FullName}");
            Debug.WriteLine($"[STACK TRACE]:\n{e.Exception?.StackTrace}");
            Debug.WriteLine("==================================================");

            if (MainWindow?.Content is FrameworkElement root)
            {
                var dialog = new ContentDialog
                {
                    Title = "Crash Caught!",
                    Content = $"Error: {e.Exception?.Message}\n\nType: {e.Exception?.GetType().Name}",
                    CloseButtonText = "OK",
                    XamlRoot = root.XamlRoot
                };

                try
                {
                    await dialog.ShowAsync();
                }
                catch
                {
                    // Fallback if XamlRoot is not ready
                }
            }
        };
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // 1. Enforce Single-Instance so clicking the taskbar doesn't spawn duplicate windows
        var mainInstance = AppInstance.FindOrRegisterForKey("NoxygenMainInstance");
        if (!mainInstance.IsCurrent)
        {
            var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            await mainInstance.RedirectActivationToAsync(activatedArgs);
            Process.GetCurrentProcess().Kill();
            return;
        }

        // Listen for Jump List clicks while Noxygen is already running
        mainInstance.Activated += OnAppInstanceActivated;

        // 2. Extract Jump List arguments on cold start
        string? gameToLaunch = ExtractGameTitleFromArgs(args.Arguments)
                               ?? ExtractGameTitleFromArgs(Environment.GetCommandLineArgs());

        // 3. COLD START: If launched via Jump List while app was closed, quick-launch the game and exit without opening UI (No playtime logged)
        if (!string.IsNullOrEmpty(gameToLaunch))
        {
            await QuickLaunchGameOnlyAsync(gameToLaunch);
            Process.GetCurrentProcess().Kill();
            return;
        }

        // 4. NORMAL START: Initialize MainWindow normally
        MainWindow = new MainWindow();
        ApplyThemeOnStartup();
        MainWindow.Activate();
    }

    private async void OnAppInstanceActivated(object? sender, AppActivationArguments e)
    {
        if (e.Kind == ExtendedActivationKind.Launch && e.Data is ILaunchActivatedEventArgs launchArgs)
        {
            string? gameTitle = ExtractGameTitleFromArgs(launchArgs.Arguments);
            if (!string.IsNullOrEmpty(gameTitle))
            {
                // WARM START: App is already open, so hide it, launch game, and restore when game closes
                MainWindow?.DispatcherQueue.TryEnqueue(async () =>
                {
                    await LaunchGameWhenAppIsOpenAsync(gameTitle);
                });
            }
        }
    }

    private string? ExtractGameTitleFromArgs(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--launch-game")
            {
                return args[i + 1].Trim('"');
            }
        }
        return null;
    }

    private string? ExtractGameTitleFromArgs(string argString)
    {
        if (string.IsNullOrWhiteSpace(argString)) return null;

        const string prefix = "--launch-game ";
        int index = argString.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            return argString.Substring(index + prefix.Length).Trim().Trim('"');
        }
        return null;
    }

    /// <summary>
    /// Used when the app was CLOSED (Cold Start). Quickly triggers the game launcher and terminates without opening UI or tracking playtime.
    /// </summary>
    private async Task QuickLaunchGameOnlyAsync(string title)
    {
        try
        {
            var dataService = new GameDataService();
            var games = dataService.LoadGamesSync();
            var game = games?.FirstOrDefault(g => string.Equals(g.Title, title, StringComparison.OrdinalIgnoreCase));

            if (game == null) return;

            Noxygen.Services.RecentGamesService.AddRecentGame(game.Title);

            if (string.Equals(game.Launcher, "Steam", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(game.AppId))
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri($"steam://run/{game.AppId}"));
            }
            else if (string.Equals(game.Launcher, "Epic", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(game.AppId))
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri($"com.epicgames.launcher://apps/{game.AppId}?action=launch"));
            }
            else if (IsRiotGame(game))
            {
                var riotStartInfo = BuildRiotClientStartInfo(game);
                if (riotStartInfo != null)
                {
                    Process.Start(riotStartInfo);
                }
            }
            else if (!string.IsNullOrWhiteSpace(game.ExecutablePath) && File.Exists(game.ExecutablePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = game.ExecutablePath,
                    WorkingDirectory = Path.GetDirectoryName(game.ExecutablePath) ?? string.Empty,
                    UseShellExecute = game.RunAsAdmin,
                    Verb = game.RunAsAdmin ? "runas" : string.Empty
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Quick Launch Error]: {ex.Message}");
        }
    }

    /// <summary>
    /// Used when the app is ALREADY OPEN (Warm Start via Jump List). Hides the app, launches the game, tracks playtime, and restores the window when closed.
    /// </summary>
    private async Task LaunchGameWhenAppIsOpenAsync(string title)
    {
        try
        {
            var dataService = new GameDataService();
            var games = dataService.LoadGamesSync();
            var game = games?.FirstOrDefault(g => string.Equals(g.Title, title, StringComparison.OrdinalIgnoreCase));

            if (game == null) return;

            Noxygen.Services.RecentGamesService.AddRecentGame(game.Title);

            var playtimeTracker = new PlaytimeTrackerService();
            var sessionStartTime = DateTime.Now;

            Process? launchedProcess = null;
            bool isRiotLaunch = IsRiotGame(game);
            string? targetExeName = ResolveTrackedProcessName(game, isRiotLaunch);

            if (string.Equals(game.Launcher, "Steam", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(game.AppId))
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri($"steam://run/{game.AppId}"));
            }
            else if (string.Equals(game.Launcher, "Epic", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(game.AppId))
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri($"com.epicgames.launcher://apps/{game.AppId}?action=launch"));
            }
            else if (isRiotLaunch)
            {
                var riotStartInfo = BuildRiotClientStartInfo(game);
                if (riotStartInfo == null) return;

                Process.Start(riotStartInfo);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(game.ExecutablePath) || !File.Exists(game.ExecutablePath)) return;

                launchedProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = game.ExecutablePath,
                    WorkingDirectory = Path.GetDirectoryName(game.ExecutablePath) ?? string.Empty,
                    UseShellExecute = false
                });
            }

            // Hide Noxygen window
            MainWindow?.DispatcherQueue.TryEnqueue(() =>
            {
                if (MainWindow != null)
                {
                    var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow);
                    var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
                    Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId)?.Hide();
                }
            });

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
                _ = Task.Run(async () =>
                {
                    await Task.Delay(15000);
                    RestoreWindow();
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[JumpList Launch Error]: {ex.Message}");
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
        MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            if (MainWindow != null)
            {
                var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
                Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId)?.Show();
                MainWindow.Activate();
            }
        });
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int SW_RESTORE = 9;

    public void BringWindowToFront()
    {
        MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            if (MainWindow is MainWindow mainWindow)
            {
                mainWindow.Activate();

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);
                if (hwnd != IntPtr.Zero)
                {
                    ShowWindow(hwnd, SW_RESTORE);
                    SetForegroundWindow(hwnd);
                }
            }
        });
    }

    private void ApplyThemeOnStartup()
    {
        var localSettings = ApplicationData.Current.LocalSettings;
        if (localSettings.Values.TryGetValue("AppSelectedTheme", out var savedTheme))
        {
            int themeIndex = (int)savedTheme;
            if (MainWindow?.Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = themeIndex switch
                {
                    1 => ElementTheme.Light,
                    2 => ElementTheme.Dark,
                    _ => ElementTheme.Default
                };
            }
        }
    }
}