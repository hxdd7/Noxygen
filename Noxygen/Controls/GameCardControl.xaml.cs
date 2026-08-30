using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Noxygen.Helpers;
using Noxygen.Models;
using System;
using System.Diagnostics;
using System.IO;

namespace Noxygen.Controls;

public sealed partial class GameCardControl : UserControl
{
    public static readonly DependencyProperty GameProperty =
        DependencyProperty.Register(
            nameof(Game),
            typeof(Game),
            typeof(GameCardControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty GameTitleProperty =
        DependencyProperty.Register(
            nameof(GameTitle),
            typeof(string),
            typeof(GameCardControl),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty LauncherNameProperty =
        DependencyProperty.Register(
            nameof(LauncherName),
            typeof(string),
            typeof(GameCardControl),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty InstalledSizeGbProperty =
        DependencyProperty.Register(
            nameof(InstalledSizeGb),
            typeof(double),
            typeof(GameCardControl),
            new PropertyMetadata(0.0));

    public Game Game
    {
        get => (Game)GetValue(GameProperty);
        set => SetValue(GameProperty, value);
    }

    public string GameTitle
    {
        get => (string)GetValue(GameTitleProperty);
        set => SetValue(GameTitleProperty, value);
    }

    public string LauncherName
    {
        get => (string)GetValue(LauncherNameProperty);
        set => SetValue(LauncherNameProperty, value);
    }

    public double InstalledSizeGb
    {
        get => (double)GetValue(InstalledSizeGbProperty);
        set => SetValue(InstalledSizeGbProperty, value);
    }

    public string SizeFormatted => $"{InstalledSizeGb:F2} GB";

    public GameCardControl()
    {
        this.InitializeComponent();
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        var targetGame = Game ?? new Game
        {
            Title = GameTitle,
            Launcher = LauncherName,
            InstalledSizeGb = InstalledSizeGb
        };

        LaunchGame(targetGame);
    }

    public static void LaunchGame(Game game)
    {
        if (game == null) return;

        try
        {
            // 1. Launch via Steam Protocol URI if SteamId exists
            if (string.Equals(game.Launcher, "Steam", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(game.SteamId))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"steam://run/{game.SteamId}",
                    UseShellExecute = true
                });
                return;
            }

            // 2. Fall back to ExecutablePath or Dynamic Resolution
            string launchPath = game.ExecutablePath;

            if (string.IsNullOrEmpty(launchPath) || !File.Exists(launchPath))
            {
                if (!string.IsNullOrEmpty(game.InstallPath) && Directory.Exists(game.InstallPath))
                {
                    launchPath = GameScanner.FindPrimaryExecutable(game.InstallPath, game.Title);
                }
            }

            // 3. Execute target binary directly
            if (!string.IsNullOrEmpty(launchPath) && File.Exists(launchPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = launchPath,
                    WorkingDirectory = Path.GetDirectoryName(launchPath),
                    UseShellExecute = true
                });
            }
            else if (!string.IsNullOrEmpty(game.InstallPath) && Directory.Exists(game.InstallPath))
            {
                // Safety net fallback to folder
                Process.Start(new ProcessStartInfo
                {
                    FileName = game.InstallPath,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LAUNCH ERROR] Could not start '{game.Title}': {ex.Message}");
        }
    }
}