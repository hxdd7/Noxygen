using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Noxygen.Helpers;
using Noxygen.Models;

namespace Noxygen.Services;

public class GameDataService
{
    // Changed from LocalApplicationData to ApplicationData (Roaming AppData) 
    // so user libraries persist across MSIX uninstalls and updates.
    private static readonly string FolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Noxygen");

    private static readonly string ImageCacheFolder = Path.Combine(FolderPath, "ImageCache");
    private static readonly string FilePath = Path.Combine(FolderPath, "games_cache.json");

    private static readonly HttpClient _httpClient = new();

    public GameDataService()
    {
        if (!Directory.Exists(FolderPath))
            Directory.CreateDirectory(FolderPath);

        if (!Directory.Exists(ImageCacheFolder))
            Directory.CreateDirectory(ImageCacheFolder);

        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 NoxygenApp/1.0");
        }
    }

    /// <summary>
    /// Loads saved local games, scans for newly installed launcher games, 
    /// merges them without deleting manual entries, and saves back to cache.
    /// </summary>
    public async Task<List<Game>> LoadAndSyncGamesAsync()
    {
        // 1. Load existing saved games (including manual additions)
        var savedGames = await LoadGamesAsync();
        var savedMap = savedGames.ToDictionary(g => g.Title, StringComparer.OrdinalIgnoreCase);

        // 2. Scan system for launcher installations
        var scanner = new GameScanner();
        var scannedGames = await Task.Run(() => scanner.ScanAll());

        // 3. Merge scanned games into saved games
        foreach (var scanned in scannedGames)
        {
            if (!savedMap.ContainsKey(scanned.Title))
            {
                // New game found on disk -> add to list
                savedGames.Add(scanned);
            }
            else
            {
                // Existing game -> update install/exe paths in case they moved, keep custom URLs
                var existing = savedMap[scanned.Title];
                existing.InstallPath = scanned.InstallPath;
                existing.ExecutablePath = string.IsNullOrWhiteSpace(existing.ExecutablePath) ? scanned.ExecutablePath : existing.ExecutablePath;
                existing.InstalledSizeGb = scanned.InstalledSizeGb;

                if (string.IsNullOrWhiteSpace(existing.SteamId))
                    existing.SteamId = scanned.SteamId;
            }
        }

        // 4. Cache remote images & save merged list
        await SaveGamesAsync(savedGames);

        return savedGames;
    }

    public List<Game> LoadGamesSync()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<Game>();
            string json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<Game>>(json) ?? new List<Game>();
        }
        catch
        {
            return new List<Game>();
        }
    }

    public async Task<List<Game>> LoadGamesAsync()
    {
        return await Task.Run(LoadGamesSync);
    }

    public async Task SaveGamesAsync(List<Game> games)
    {
        try
        {
            if (!Directory.Exists(FolderPath)) Directory.CreateDirectory(FolderPath);
            if (!Directory.Exists(ImageCacheFolder)) Directory.CreateDirectory(ImageCacheFolder);

            foreach (var game in games)
            {
                game.CoverArtUrl = await CacheImageAsync(game.CoverArtUrl, $"{game.Title}_cover");
                game.BackgroundUrl = await CacheImageAsync(game.BackgroundUrl, $"{game.Title}_bg");
                game.IconUrl = await CacheImageAsync(game.IconUrl, $"{game.Title}_icon");
            }

            string json = JsonSerializer.Serialize(games, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(FilePath, json);
        }
        catch { }
    }

    private async Task<string> CacheImageAsync(string rawUrl, string filePrefix)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
            return string.Empty;

        if (!rawUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !rawUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return rawUrl;
        }

        try
        {
            string safePrefix = string.Join("_", filePrefix.Split(Path.GetInvalidFileNameChars()));
            string urlHash = GetMd5Hash(rawUrl);
            string extension = GetImageExtension(rawUrl);
            string localFilePath = Path.Combine(ImageCacheFolder, $"{safePrefix}_{urlHash}{extension}");

            if (File.Exists(localFilePath))
            {
                return localFilePath;
            }

            byte[] imageBytes = await _httpClient.GetByteArrayAsync(rawUrl);
            await File.WriteAllBytesAsync(localFilePath, imageBytes);

            return localFilePath;
        }
        catch
        {
            return rawUrl;
        }
    }

    private string GetImageExtension(string url)
    {
        string cleanUrl = url.Split('?')[0].Split('#')[0];
        if (cleanUrl.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return ".png";
        if (cleanUrl.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) return ".webp";
        if (cleanUrl.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)) return ".gif";
        if (cleanUrl.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)) return ".jpeg";
        return ".jpg";
    }

    private string GetMd5Hash(string input)
    {
        byte[] inputBytes = Encoding.UTF8.GetBytes(input);
        byte[] hashBytes = MD5.HashData(inputBytes);
        return Convert.ToHexString(hashBytes)[..8];
    }
}