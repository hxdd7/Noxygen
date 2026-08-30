using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Noxygen.Helpers;
using Noxygen.Models;
using Noxygen.Services;
using Noxygen.Services.Contracts;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;

namespace Noxygen.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private const string SortOptionKey = "AppGameSortOption";

    private readonly GameScanner _scanner = new();
    private readonly GameDataService _dataService = new();
    private readonly SteamMetadataService _metadataService = new();

    // Internal master list to preserve un-sorted state for quick filtering/sorting
    private readonly List<Game> _masterGameList = new();

    public ObservableCollection<Game> DetectedGames { get; } = new();

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private GameSortOption _selectedSortOption;

    public LibraryViewModel()
    {
        // Load saved sort option (defaults to TitleAscending / 0)
        var localSettings = ApplicationData.Current.LocalSettings;
        if (localSettings.Values.TryGetValue(SortOptionKey, out var savedSort) && savedSort is int sortValue)
        {
            _selectedSortOption = (GameSortOption)sortValue;
        }
        else
        {
            _selectedSortOption = GameSortOption.TitleAscending;
        }
    }

    partial void OnSelectedSortOptionChanged(GameSortOption value)
    {
        // 1. Save setting to disk
        ApplicationData.Current.LocalSettings.Values[SortOptionKey] = (int)value;

        // 2. Re-apply sorting to the visible collection
        ApplySorting();
    }

    public async Task InitializeAsync()
    {
        if (DetectedGames.Count > 0) return;

        var cachedGames = await _dataService.LoadGamesAsync();

        _masterGameList.Clear();
        _masterGameList.AddRange(cachedGames);

        ApplySorting();
    }

    [RelayCommand]
    private async Task AddCustomGameAsync()
    {
        if (App.MainWindow?.Content is FrameworkElement root)
        {
            var dialog = new Views.AddGameDialog
            {
                XamlRoot = root.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && dialog.CreatedGame != null)
            {
                var newGame = dialog.CreatedGame;

                // Calculate total directory size if size wasn't set during creation
                if (newGame.InstalledSizeGb == 0 && !string.IsNullOrEmpty(newGame.InstallPath) && Directory.Exists(newGame.InstallPath))
                {
                    newGame.InstalledSizeGb = await Task.Run(() => CalculateDirectorySizeGb(newGame.InstallPath));
                }

                // Fetch metadata for custom added game if cover art is missing
                if (string.IsNullOrWhiteSpace(newGame.CoverArtUrl))
                {
                    await _metadataService.FetchMetadataForGameAsync(newGame);
                }

                _masterGameList.Add(newGame);
                ApplySorting();
                await SaveAndSyncCacheAsync();
            }
        }
    }

    [RelayCommand]
    public async Task DeleteGameAsync(Game game)
    {
        if (game == null) return;

        if (App.MainWindow?.Content is FrameworkElement root)
        {
            var confirmDialog = new ContentDialog
            {
                Title = "Remove Game?",
                Content = $"Are you sure you want to remove '{game.Title}' from your Noxygen library?",
                PrimaryButtonText = "Remove",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = root.XamlRoot
            };

            var result = await confirmDialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                _masterGameList.Remove(game);
                DetectedGames.Remove(game);
                await SaveAndSyncCacheAsync();
            }
        }
    }

    [RelayCommand]
    public async Task EditGameAsync(Game game)
    {
        if (game == null) return;

        if (App.MainWindow?.Content is FrameworkElement root)
        {
            var dialog = new Views.EditGameDialog(game)
            {
                XamlRoot = root.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                // Re-sort in case Title or Launcher was modified
                ApplySorting();
                await SaveAndSyncCacheAsync();
            }
        }
    }

    [RelayCommand]
    public async Task ScanForGamesAsync()
    {
        if (IsScanning) return;
        IsScanning = true;

        // 1. Load existing games from cache
        var existingGames = await _dataService.LoadGamesAsync();

        // 2. Map existing games by Title + Launcher
        var existingMap = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in existingGames)
        {
            string key = $"{game.Title}_{game.Launcher}";
            if (!existingMap.ContainsKey(key))
            {
                existingMap[key] = game;
            }
        }

        // 3. Perform local scanner execution
        var scannedGames = await Task.Run(() => _scanner.ScanAll());
        var finalGameList = new List<Game>();

        foreach (var scannedGame in scannedGames)
        {
            string key = $"{scannedGame.Title}_{scannedGame.Launcher}";

            Game targetGame = existingMap.TryGetValue(key, out var existing) ? existing : scannedGame;

            // Force fetch 2:3 vertical cover art
            await _metadataService.FetchMetadataForGameAsync(targetGame);

            finalGameList.Add(targetGame);
            existingMap.Remove(key);
        }

        // 4. Preserve custom games
        finalGameList.AddRange(existingMap.Values);

        // 5. Download 2:3 vertical images, overwrite old disk files, and save JSON
        await _dataService.SaveGamesAsync(finalGameList);

        // 6. Update master list & active view collection with active sort applied
        _masterGameList.Clear();
        _masterGameList.AddRange(finalGameList);

        ApplySorting();

        IsScanning = false;
    }

    private void ApplySorting()
    {
        if (_masterGameList == null || !_masterGameList.Any())
        {
            DetectedGames.Clear();
            return;
        }

        IEnumerable<Game> sorted = SelectedSortOption switch
        {
            GameSortOption.TitleAscending => _masterGameList.OrderBy(g => g.Title),
            GameSortOption.TitleDescending => _masterGameList.OrderByDescending(g => g.Title),
            GameSortOption.SizeDescending => _masterGameList.OrderByDescending(g => g.InstalledSizeGb),
            GameSortOption.SizeAscending => _masterGameList.OrderBy(g => g.InstalledSizeGb),
            GameSortOption.Launcher => _masterGameList.OrderBy(g => g.Launcher).ThenBy(g => g.Title),
            _ => _masterGameList
        };

        DetectedGames.Clear();
        foreach (var game in sorted)
        {
            DetectedGames.Add(game);
        }
    }

    private async Task SaveAndSyncCacheAsync()
    {
        await _dataService.SaveGamesAsync(_masterGameList);

        if (App.MainWindow is MainWindow mainWin)
        {
            mainWin.RefreshGlobalGamesCache();
        }
    }

    private double CalculateDirectorySizeGb(string path)
    {
        try
        {
            var dirInfo = new DirectoryInfo(path);
            long totalBytes = 0;
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                // Fully specify System.IO.FileAttributes to resolve namespace ambiguity
                AttributesToSkip = System.IO.FileAttributes.ReparsePoint | System.IO.FileAttributes.System
            };

            foreach (var file in dirInfo.EnumerateFiles("*", options))
            {
                totalBytes += file.Length;
            }

            return Math.Round(totalBytes / (1024.0 * 1024.0 * 1024.0), 2);
        }
        catch
        {
            return 0.0;
        }
    }
}