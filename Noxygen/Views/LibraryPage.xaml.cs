using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Noxygen.Models;
using Noxygen.ViewModels;
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
    public async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button playButton || playButton.DataContext is not Game game)
        {
            return;
        }

        try
        {
            // 1. Handle Steam Protocol Launch
            if (string.Equals(game.Launcher, "Steam", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(game.AppId))
            {
                var uri = new Uri($"steam://run/{game.AppId}");
                await Windows.System.Launcher.LaunchUriAsync(uri);
            }
            // 2. Handle Epic Games Protocol Launch
            else if (string.Equals(game.Launcher, "Epic", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(game.AppId))
            {
                var uri = new Uri($"com.epicgames.launcher://apps/{game.AppId}?action=launch");
                await Windows.System.Launcher.LaunchUriAsync(uri);
            }
            // 3. Fallback to Direct Executable Execution
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
                    UseShellExecute = true
                };

                Process.Start(startInfo);
            }

            // 4. Safely Hide Window to Tray
            if (App.MainWindow != null)
            {
                var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
                Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId)?.Hide();
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

        // Navigates using the current page frame (or App.MainWindow Frame)
        Frame.Navigate(typeof(GameDetailsPage), game);
    }

    private void GamesGridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Game selectedGame)
        {
            NavigateToDetails(selectedGame);
        }
    }
}