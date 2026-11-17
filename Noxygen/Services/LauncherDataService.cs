using Noxygen.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Noxygen.Services;

public class LauncherDataService
{
    private static readonly string FolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Noxygen");

    private static readonly string FilePath = Path.Combine(FolderPath, "launchers_cache.json");

    public List<Launcher> LoadLaunchersSync()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<Launcher>();
            string json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<Launcher>>(json) ?? new List<Launcher>();
        }
        catch
        {
            return new List<Launcher>();
        }
    }

    public async Task SaveLaunchersAsync(List<Launcher> launchers)
    {
        try
        {
            if (!Directory.Exists(FolderPath)) Directory.CreateDirectory(FolderPath);
            string json = JsonSerializer.Serialize(launchers, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(FilePath, json);
        }
        catch { }
    }
}