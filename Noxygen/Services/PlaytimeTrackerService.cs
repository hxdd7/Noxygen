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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

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

    public async Task SaveSessionsAsync(IEnumerable<PlaytimeSession> sessions)
    {
        if (!Directory.Exists(FolderPath))
        {
            Directory.CreateDirectory(FolderPath);
        }

        var ordered = sessions
            .Where(s => s != null && !string.IsNullOrWhiteSpace(s.GameTitle) && s.Duration.TotalMinutes >= 1)
            .OrderBy(s => s.StartTime)
            .ToList();

        string json = JsonSerializer.Serialize(ordered, JsonOptions);
        await File.WriteAllTextAsync(FilePath, json);
    }

    public async Task LogSessionAsync(string gameTitle, DateTime startTime, DateTime endTime)
    {
        if (string.IsNullOrWhiteSpace(gameTitle) || endTime <= startTime) return;

        var duration = endTime - startTime;

        // RULE: If playtime is less than 1 minute, don't save it
        if (duration.TotalMinutes < 1) return;

        try
        {
            var sessions = await LoadSessionsAsync();
            sessions.Add(new PlaytimeSession
            {
                GameTitle = gameTitle,
                StartTime = startTime,
                EndTime = endTime
            });

            await SaveSessionsAsync(sessions);
        }
        catch { }
    }

    public async Task<string> ExportSessionsJsonAsync()
    {
        var sessions = await LoadSessionsAsync();
        return JsonSerializer.Serialize(sessions, JsonOptions);
    }

    public async Task<(int ImportedCount, int TotalCount)> ImportSessionsJsonAsync(string json, bool mergeWithExisting = true)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("The selected playtime file is empty.");
        }

        List<PlaytimeSession>? imported;
        try
        {
            imported = JsonSerializer.Deserialize<List<PlaytimeSession>>(json);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("The selected file is not a valid Noxygen playtime JSON backup.");
        }

        if (imported == null)
        {
            throw new InvalidOperationException("No playtime sessions were found in the selected file.");
        }

        var validImported = imported
            .Where(s => s != null && !string.IsNullOrWhiteSpace(s.GameTitle) && s.Duration.TotalMinutes >= 1)
            .ToList();

        var existing = mergeWithExisting
            ? await LoadSessionsAsync()
            : new List<PlaytimeSession>();

        var seenKeys = new HashSet<string>(
            existing.Select(BuildSessionKey),
            StringComparer.OrdinalIgnoreCase);

        int added = 0;
        foreach (var session in validImported)
        {
            if (seenKeys.Add(BuildSessionKey(session)))
            {
                existing.Add(session);
                added++;
            }
        }

        await SaveSessionsAsync(existing);
        return (added, existing.Count);
    }

    private static string BuildSessionKey(PlaytimeSession s) =>
        $"{s.GameTitle.Trim()}|{s.StartTime.ToUniversalTime():O}|{s.EndTime.ToUniversalTime():O}";
}