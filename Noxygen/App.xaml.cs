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
            // 1. Prevent the immediate crash so you can read the output
            e.Handled = true;

            // 2. Print exact error details to Visual Studio Output Window (Ctrl + Alt + O)
            Debug.WriteLine("==================================================");
            Debug.WriteLine($"[EXACT ERROR MESSAGE]: {e.Exception?.Message}");
            Debug.WriteLine($"[EXCEPTION TYPE]: {e.Exception?.GetType().FullName}");
            Debug.WriteLine($"[STACK TRACE]:\n{e.Exception?.StackTrace}");
            Debug.WriteLine("==================================================");

            // 3. Show a UI Dialog Box with the exact error details
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
        // 1. Check both LaunchActivatedEventArgs (Packaged) and CommandLineArgs for a Jump List click
        string? gameToLaunch = ExtractGameTitleFromArgs(args.Arguments)
                               ?? ExtractGameTitleFromArgs(Environment.GetCommandLineArgs());

        // 2. Enforce Single-Instance so clicking the taskbar doesn't spawn duplicate windows
        var mainInstance = AppInstance.FindOrRegisterForKey("NoxygenMainInstance");
        if (!mainInstance.IsCurrent)
        {
            // Forward the Jump List click to the already-running instance and close this process
            var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            await mainInstance.RedirectActivationToAsync(activatedArgs);
            Process.GetCurrentProcess().Kill();
            return;
        }

        // Listen for Jump List clicks while Noxygen is already running
        mainInstance.Activated += OnAppInstanceActivated;

        // 3. Initialize MainWindow and apply saved theme
        MainWindow = new MainWindow();
        ApplyThemeOnStartup();
        MainWindow.Activate();

        // 4. Launch the game immediately if triggered from the taskbar on cold boot
        if (!string.IsNullOrEmpty(gameToLaunch))
        {
            await LaunchGameByTitleAsync(gameToLaunch);
        }
    }

    private async void OnAppInstanceActivated(object? sender, AppActivationArguments e)
    {
        if (e.Kind == ExtendedActivationKind.Launch && e.Data is ILaunchActivatedEventArgs launchArgs)
        {
            string? gameTitle = ExtractGameTitleFromArgs(launchArgs.Arguments);
            if (!string.IsNullOrEmpty(gameTitle))
            {
                await LaunchGameByTitleAsync(gameTitle);
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

    private async Task LaunchGameByTitleAsync(string title)
    {
        var dataService = new GameDataService();
        var games = await dataService.LoadGamesAsync();
        var game = games?.FirstOrDefault(g => string.Equals(g.Title, title, StringComparison.OrdinalIgnoreCase));

        if (game == null) return;

        // 1. Record to Recent Games history
        Noxygen.Services.RecentGamesService.AddRecentGame(game.Title);

        // 2. Handle Steam, Epic, and Direct EXE launches cleanly
        if (string.Equals(game.Launcher, "Steam", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(game.AppId))
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri($"steam://run/{game.AppId}"));
        }
        else if (string.Equals(game.Launcher, "Epic", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(game.AppId))
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri($"com.epicgames.launcher://apps/{game.AppId}?action=launch"));
        }
        else if (!string.IsNullOrWhiteSpace(game.ExecutablePath) && File.Exists(game.ExecutablePath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = game.ExecutablePath,
                WorkingDirectory = Path.GetDirectoryName(game.ExecutablePath) ?? string.Empty,
                UseShellExecute = true
            });
        }

        // Hide Noxygen window to tray after launching from the taskbar
        MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            if (MainWindow != null)
            {
                var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
                Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId)?.Hide();
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