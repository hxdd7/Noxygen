using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Noxygen.Models;

namespace Noxygen.Services;

public class LocalStorageService
{
    private readonly string _filePath;

    public LocalStorageService()
    {
        string appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Noxygen"
        );

        Directory.CreateDirectory(appDataDir);
        _filePath = Path.Combine(appDataDir, "library.json");
    }

    public async Task<List<Game>> LoadGamesAsync()
    {
        if (!File.Exists(_filePath)) return new List<Game>();

        try
        {
            using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<List<Game>>(stream) ?? new List<Game>();
        }
        catch
        {
            return new List<Game>();
        }
    }

    public async Task SaveGamesAsync(List<Game> games)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, games, options);
    }
}