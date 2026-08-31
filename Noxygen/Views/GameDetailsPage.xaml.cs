using System;
using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Noxygen.Models;

namespace Noxygen.Views;

public sealed partial class GameDetailsPage : Page
{
    public Game SelectedGame { get; private set; }

    public GameDetailsPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is Game game)
        {
            SelectedGame = game;
        }
    }

    private void LaunchGame_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (SelectedGame == null || string.IsNullOrWhiteSpace(SelectedGame.ExecutablePath))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = SelectedGame.ExecutablePath,
                UseShellExecute = true,
                WorkingDirectory = System.IO.Path.GetDirectoryName(SelectedGame.ExecutablePath)
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to launch game: {ex.Message}");
        }
    }
}