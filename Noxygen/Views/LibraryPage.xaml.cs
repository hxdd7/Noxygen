using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Noxygen.Models;
using Noxygen.ViewModels;
using System;
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
        if (sender is not Button playButton || playButton.Tag is not string exePath || string.IsNullOrWhiteSpace(exePath))
        {
            return;
        }

        // Find the loading ring in the same container grid
        ProgressRing? loadingRing = null;
        if (playButton.Parent is Grid parentGrid)
        {
            foreach (var child in parentGrid.Children)
            {
                if (child is ProgressRing ring)
                {
                    loadingRing = ring;
                    break;
                }
            }
        }

        // Show Loading Ring & Hide Play Button
        playButton.Visibility = Visibility.Collapsed;
        if (loadingRing != null)
        {
            loadingRing.Visibility = Visibility.Visible;
            loadingRing.IsActive = true;
        }

        try
        {
            if (!System.IO.File.Exists(exePath))
            {
                await ShowErrorDialogAsync("Executable Not Found", $"Could not locate file at:\n{exePath}");
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = System.IO.Path.GetDirectoryName(exePath) ?? string.Empty,
                UseShellExecute = true
            };

            // Start the process on a background thread
            await Task.Run(() =>
            {
                using var process = Process.Start(startInfo);

                // Optional brief delay to ensure process window initializes smoothly
                Task.Delay(1500).Wait();
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // User cancelled UAC prompt — ignore gracefully
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync("Failed to Launch Game", ex.Message);
        }
        finally
        {
            // Restore Play Button & Hide Loading Ring
            if (loadingRing != null)
            {
                loadingRing.IsActive = false;
                loadingRing.Visibility = Visibility.Collapsed;
            }
            playButton.Visibility = Visibility.Visible;
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
}