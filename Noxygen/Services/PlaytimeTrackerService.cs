using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Noxygen.Models;

namespace Noxygen.Services;

public class PlaytimeTrackerService
{
    private static readonly string FolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Noxygen");

    private static readonly string FilePath = Path.Combine(FolderPath, "playtime_history.json");

    public async Task<List<PlaytimeSession>> LoadSessionsAsync()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<PlaytimeSession>();
            string json = await File.ReadAllTextAsync(FilePath);
            return JsonSerializer.Deserialize<List<PlaytimeSession>>(json) ?? new List<PlaytimeSession>();
        }
        catch
        {
            return new List<PlaytimeSession>();
        }
    }

    public async Task LogSessionAsync(string gameTitle, DateTime startTime, DateTime endTime)
    {
        if (string.IsNullOrWhiteSpace(gameTitle) || endTime <= startTime) return;

        try
        {
            if (!Directory.Exists(FolderPath))
                Directory.CreateDirectory(FolderPath);

            var sessions = await LoadSessionsAsync();
            sessions.Add(new PlaytimeSession
            {
                GameTitle = gameTitle,
                StartTime = startTime,
                EndTime = endTime
            });

            string json = JsonSerializer.Serialize(sessions, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(FilePath, json);
        }
        catch { }
    }
}