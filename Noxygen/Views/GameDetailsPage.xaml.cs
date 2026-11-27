using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Noxygen.Models;
using Noxygen.ViewModels;
using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Windows.UI;

namespace Noxygen.Views;

public sealed partial class GameDetailsPage : Page
{
    public Game? SelectedGame { get; private set; }
    public GameDetailViewModel ViewModel { get; } = new();

    private SpriteVisual? _heroVisual;
    private LoadedImageSurface? _heroSurface;

    public GameDetailsPage()
    {
        this.InitializeComponent();
        this.DataContext = ViewModel;

        HeroBackgroundHost.SizeChanged += (_, _) =>
        {
            if (_heroVisual != null)
            {
                _heroVisual.Size = new Vector2(
                    (float)Math.Max(1, HeroBackgroundHost.ActualWidth),
                    (float)Math.Max(1, HeroBackgroundHost.ActualHeight));
            }
        };

        DetailsScrollViewer.ViewChanged += (_, _) => UpdateHeroScrollOffset();
    }

    private void UpdateHeroScrollOffset()
    {
        if (_heroVisual == null) return;
        float scrollOffset = (float)DetailsScrollViewer.VerticalOffset;
        _heroVisual.Offset = new Vector3(0f, -scrollOffset * 0.45f, 0f);
    }

    private void SetHeroBackground(string? url)
    {
        _heroSurface?.Dispose();
        _heroSurface = null;

        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri))
        {
            ElementCompositionPreview.SetElementChildVisual(HeroBackgroundHost, null);
            return;
        }

        var hostVisual = ElementCompositionPreview.GetElementVisual(HeroBackgroundHost);
        var compositor = hostVisual.Compositor;

        _heroSurface = LoadedImageSurface.StartLoadFromUri(uri);
        var surfaceBrush = compositor.CreateSurfaceBrush(_heroSurface);
        surfaceBrush.Stretch = CompositionStretch.UniformToFill;
        surfaceBrush.HorizontalAlignmentRatio = 0.5f;
        surfaceBrush.VerticalAlignmentRatio = 0.0f;

        // Vertical alpha mask: visible at top -> transparent at bottom edge
        var verticalMask = compositor.CreateLinearGradientBrush();
        verticalMask.MappingMode = CompositionMappingMode.Relative;
        verticalMask.StartPoint = new Vector2(0.5f, 0.0f);
        verticalMask.EndPoint = new Vector2(0.5f, 1.0f);
        verticalMask.ColorStops.Add(compositor.CreateColorGradientStop(0.0f, Color.FromArgb(220, 255, 255, 255)));
        verticalMask.ColorStops.Add(compositor.CreateColorGradientStop(0.45f, Color.FromArgb(190, 255, 255, 255)));
        verticalMask.ColorStops.Add(compositor.CreateColorGradientStop(0.80f, Color.FromArgb(65, 255, 255, 255)));
        verticalMask.ColorStops.Add(compositor.CreateColorGradientStop(1.0f, Color.FromArgb(0, 255, 255, 255)));

        var maskBrush = compositor.CreateMaskBrush();
        maskBrush.Source = surfaceBrush;
        maskBrush.Mask = verticalMask;

        _heroVisual = compositor.CreateSpriteVisual();
        _heroVisual.Brush = maskBrush;
        _heroVisual.Size = new Vector2(
            (float)Math.Max(1, HeroBackgroundHost.ActualWidth),
            (float)Math.Max(1, HeroBackgroundHost.ActualHeight));

        ElementCompositionPreview.SetElementChildVisual(HeroBackgroundHost, _heroVisual);
        UpdateHeroScrollOffset();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is Game game)
        {
            SelectedGame = game;
            SetHeroBackground(game.BackgroundUrl);
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