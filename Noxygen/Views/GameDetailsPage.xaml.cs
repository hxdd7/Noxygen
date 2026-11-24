using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Noxygen.Models;
using Noxygen.ViewModels;
using System;
using System.Diagnostics;
using System.IO;

namespace Noxygen.Views;

public sealed partial class GameDetailsPage : Page
{
    public Game? SelectedGame { get; private set; }
    public GameDetailViewModel ViewModel { get; } = new();

    public GameDetailsPage()
    {
        this.InitializeComponent();
        this.DataContext = ViewModel;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is Game game)
        {
            SelectedGame = game;
            await ViewModel.LoadGameAsync(game);
        }
    }

    private void LaunchGame_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedGame == null || string.IsNullOrWhiteSpace(SelectedGame.ExecutablePath))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = SelectedGame.ExecutablePath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(SelectedGame.ExecutablePath)
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to launch game: {ex.Message}");
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedGame == null || string.IsNullOrWhiteSpace(SelectedGame.ExecutablePath))
            return;

        try
        {
            string? folder = Path.GetDirectoryName(SelectedGame.ExecutablePath);
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open game folder: {ex.Message}");
        }
    }

    private void ToggleSessions_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleSessionHistoryLimit();
    }
}