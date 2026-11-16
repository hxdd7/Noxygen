using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Windows.Storage;

namespace Noxygen.Services;

public static class RecentGamesService
{
    private const string RecentKey = "RecentGameTitles";

    public static List<string> GetRecentGames()
    {
        var settings = ApplicationData.Current.LocalSettings;
        if (settings.Values.TryGetValue(RecentKey, out var obj) && obj is string json)
        {
            try
            {
                return JsonSerializer.Deserialize<List<string>>(json) ?? new();
            }
            catch
            {
                // Fallback on corrupt json
            }
        }
        return new();
    }

    public static void AddRecentGame(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return;

        var recents = GetRecentGames();
        // Remove duplicate if already exists so it bubbles to the top
        recents.RemoveAll(t => string.Equals(t, title, StringComparison.OrdinalIgnoreCase));

        // Insert at the beginning
        recents.Insert(0, title);

        // Keep only the last 3
        if (recents.Count > 3)
        {
            recents = recents.Take(3).ToList();
        }

        ApplicationData.Current.LocalSettings.Values[RecentKey] = JsonSerializer.Serialize(recents);
    }
}