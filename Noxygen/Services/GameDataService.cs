using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Noxygen.Models;

namespace Noxygen.Services;

public class GameDataService
{
    private static readonly string FolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Noxygen");

    private static readonly string ImageCacheFolder = Path.Combine(FolderPath, "ImageCache");
    private static readonly string FilePath = Path.Combine(FolderPath, "games_cache.json");

    private static readonly HttpClient _httpClient = new();

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

    public GameDataService()
    {
        // Guarantees the directories exist as soon as GameDataService is instantiated
        if (!Directory.Exists(FolderPath))
            Directory.CreateDirectory(FolderPath);

        if (!Directory.Exists(ImageCacheFolder))
            Directory.CreateDirectory(ImageCacheFolder);
    }

    public async Task SaveGamesAsync(List<Game> games)
    {
        try
        {
            if (!Directory.Exists(FolderPath)) Directory.CreateDirectory(FolderPath);
            if (!Directory.Exists(ImageCacheFolder)) Directory.CreateDirectory(ImageCacheFolder);

            // Download & cache remote images locally to prevent UI lag
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

    public async Task<List<Game>> LoadGamesAsync()
    {
        return await Task.Run(LoadGamesSync);
    }

    private async Task<string> CacheImageAsync(string rawUrl, string filePrefix)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
            return string.Empty;

        // If it's already a local file path or non-HTTP, skip caching
        if (!rawUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !rawUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return rawUrl;
        }

        try
        {
            // Create a safe, unique filename per image
            string safeName = string.Join("_", filePrefix.Split(Path.GetInvalidFileNameChars()));
            string extension = GetImageExtension(rawUrl);
            string localFilePath = Path.Combine(ImageCacheFolder, $"{safeName}{extension}");

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
            // If download fails, retain the original URL as a fallback
            return rawUrl;
        }
    }

    private string GetImageExtension(string url)
    {
        if (url.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return ".png";
        if (url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) return ".webp";
        if (url.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)) return ".gif";
        return ".jpg";
    }
}